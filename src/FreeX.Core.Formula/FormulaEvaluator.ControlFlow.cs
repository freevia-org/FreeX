using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private ScalarValue EvaluateShortCircuit(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "IF"      => EvaluateIf(node, context),
            "IFERROR" => EvaluateIfError(node, context),
            "IFNA"    => EvaluateIfNa(node, context),
            "CHOOSE"  => EvaluateChoose(node, context),
            "IFS"     => EvaluateIfs(node, context),
            "SWITCH"  => EvaluateSwitch(node, context),
            _         => ErrorValue.Value
        };
    }

    // Excel coerces text "TRUE"/"FALSE" (case-insensitive) to booleans in IF/IFS conditions.
    // Any other text returns null (caller maps to #VALUE!).
    private static bool? TryCoerceCondition(ScalarValue cond) => cond switch
    {
        BoolValue b     => b.Value,
        NumberValue n   => n.Value != 0,
        DateTimeValue d => d.Value != 0,
        BlankValue      => false,
        TextValue t when string.Equals(t.Value, "TRUE",  StringComparison.OrdinalIgnoreCase) => true,
        TextValue t when string.Equals(t.Value, "FALSE", StringComparison.OrdinalIgnoreCase) => false,
        _               => null   // any other text → #VALUE! in Excel
    };

    private ScalarValue EvaluateIf(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 2 or > 3) return ErrorValue.Value;
        var cond = EvaluateArrayOperand(node.Arguments[0], context);
        if (cond is ErrorValue e) return e;
        if (cond is RangeValue conditionRange) return EvaluateIfConditionRange(node, context, conditionRange);
        bool? taken = TryCoerceCondition(cond);
        if (taken is null) return ErrorValue.Value;
        if (taken.Value)  return EvaluateArrayOperand(node.Arguments[1], context);
        if (node.Arguments.Count == 3) return EvaluateArrayOperand(node.Arguments[2], context);
        return FalseValue;
    }

    private ScalarValue EvaluateIfConditionRange(FunctionCallNode node, IEvalContext context, RangeValue conditionRange)
    {
        // Excel broadcasts an array condition against vector (1xN / Nx1) branches — e.g.
        // IF(A1:A3>0, B1:D1, 0) broadcasts the 3x1 condition against the 1x3 true branch into a
        // 3x3 result — rather than requiring every branch to already match the condition's shape.
        // Both branches are evaluated eagerly (matching Excel's own array-formula behaviour of
        // computing both sides up front) so the overall result shape — the broadcast of the
        // condition with whichever branches are actually reachable — is known before any cell is
        // written, instead of being fixed to the condition's shape as soon as the first cell picks
        // a branch.
        var trueBranch = EvaluateArrayOperand(node.Arguments[1], context);
        var falseBranch = node.Arguments.Count == 3
            ? EvaluateArrayOperand(node.Arguments[2], context)
            : FalseValue;

        int rowCount = conditionRange.RowCount;
        int colCount = conditionRange.ColCount;
        if (!TryExpandBroadcastShape(trueBranch, ref rowCount, ref colCount) ||
            !TryExpandBroadcastShape(falseBranch, ref rowCount, ref colCount))
            return ErrorValue.Value;

        var cells = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
            {
                var condition = BroadcastElementAt(conditionRange, r, c, rowCount, colCount);
                if (condition is ErrorValue error)
                {
                    cells[r, c] = error;
                    continue;
                }

                bool? taken = TryCoerceCondition(condition);
                if (taken is null)
                {
                    cells[r, c] = ErrorValue.Value;
                    continue;
                }

                var selected = taken.Value ? trueBranch : falseBranch;
                cells[r, c] = selected is RangeValue selectedRange
                    ? BroadcastElementAt(selectedRange, r, c, rowCount, colCount)
                    : selected;
            }

        return new RangeValue(cells, conditionRange.StartRow, conditionRange.StartCol) { SheetName = conditionRange.SheetName };
    }

    /// <summary>
    /// Grows <paramref name="rowCount"/>/<paramref name="colCount"/> (the running broadcast shape) to
    /// cover <paramref name="value"/> when it is a <see cref="RangeValue"/> whose dimensions are
    /// broadcast-compatible (equal, or 1, on each axis) with the running shape. Returns false when
    /// <paramref name="value"/> is a range whose shape cannot be broadcast against the running shape
    /// (Excel's #VALUE! for mismatched array shapes); non-range scalars always succeed as a no-op.
    /// </summary>
    private static bool TryExpandBroadcastShape(ScalarValue value, ref int rowCount, ref int colCount)
    {
        if (value is not RangeValue range) return true;
        if (!CanBroadcast(rowCount, range.RowCount) || !CanBroadcast(colCount, range.ColCount)) return false;
        rowCount = Math.Max(rowCount, range.RowCount);
        colCount = Math.Max(colCount, range.ColCount);
        return true;
    }

    /// <summary>
    /// Reads the element of <paramref name="range"/> that corresponds to broadcast position
    /// (<paramref name="row"/>, <paramref name="col"/>) in a result shaped
    /// <paramref name="targetRows"/> x <paramref name="targetCols"/>: an axis whose extent is 1 is
    /// held fixed (broadcast) rather than indexed, matching Excel's array-broadcasting rules (and the
    /// same convention <see cref="EvaluateChooseIndexRange"/> already uses for CHOOSE's index/branch
    /// broadcasting). A range whose shape is neither an exact match nor broadcastable maps to #VALUE!.
    /// </summary>
    private static ScalarValue BroadcastElementAt(RangeValue range, int row, int col, int targetRows, int targetCols)
    {
        if (!CanBroadcast(targetRows, range.RowCount) || !CanBroadcast(targetCols, range.ColCount))
            return ErrorValue.Value;

        int r = range.RowCount == 1 ? 0 : row;
        int c = range.ColCount == 1 ? 0 : col;
        return range.Cells[r, c];
    }

    /// <summary>Whether a dimension of size <paramref name="source"/> can broadcast to <paramref name="target"/> (equal, or either side is 1).</summary>
    private static bool CanBroadcast(int target, int source) => target == source || target == 1 || source == 1;

    private ScalarValue EvaluateIfError(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateArrayOperand(node.Arguments[0], context);
        if (value is RangeValue range)
        {
            if (!RangeHasMatchingError(range, _ => true)) return value;
            var fallback = EvaluateArrayOperand(node.Arguments[1], context);
            return ReplaceRangeErrors(range, fallback, _ => true);
        }

        return value is ErrorValue ? EvaluateArrayOperand(node.Arguments[1], context) : value;
    }

    private ScalarValue EvaluateIfNa(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateArrayOperand(node.Arguments[0], context);
        if (value is RangeValue range)
        {
            if (!RangeHasMatchingError(range, IsNAError)) return value;
            var fallback = EvaluateArrayOperand(node.Arguments[1], context);
            return ReplaceRangeErrors(range, fallback, IsNAError);
        }

        return value is ErrorValue e && IsNAError(e) ? EvaluateArrayOperand(node.Arguments[1], context) : value;
    }

    private static bool RangeHasMatchingError(RangeValue range, Func<ErrorValue, bool> catches)
    {
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                if (range.Cells[r, c] is ErrorValue error && catches(error))
                    return true;

        return false;
    }

    private static ScalarValue ReplaceRangeErrors(RangeValue range, ScalarValue fallback, Func<ErrorValue, bool> catches)
    {
        // A fallback array need not match the error range's shape exactly — Excel broadcasts a
        // fallback whose row and/or column extent is 1 across every position (e.g. a 1x1 range like
        // B1:B1, or a 1xN / Nx1 vector), the same broadcasting convention IF/CHOOSE already apply via
        // CanBroadcast/BroadcastElementAt. Only a genuinely incompatible shape is #VALUE!.
        RangeValue? fallbackRange = fallback as RangeValue;
        if (fallbackRange is not null && (!CanBroadcast(range.RowCount, fallbackRange.RowCount) || !CanBroadcast(range.ColCount, fallbackRange.ColCount)))
            return ErrorValue.Value;

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue error && catches(error)
                    ? fallbackRange is not null ? BroadcastElementAt(fallbackRange, r, c, range.RowCount, range.ColCount) : fallback
                    : value;
            }

        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static bool IsNAError(ErrorValue error) => error.Code == ErrorValue.NA.Code;

    private ScalarValue EvaluateChoose(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2) return ErrorValue.Value;
        var indexVal = EvaluateArrayOperand(node.Arguments[0], context);
        if (indexVal is ErrorValue e) return e;
        if (indexVal is RangeValue indexRange) return EvaluateChooseIndexRange(node, context, indexRange);
        var coerced = CoerceToNumber(indexVal);
        if (coerced is ErrorValue ec) return ec;
        double rawIdx = ((NumberValue)coerced).Value;
        if (!double.IsFinite(rawIdx)) return ErrorValue.Value;
        int idx = (int)rawIdx;
        if (idx < 1 || idx >= node.Arguments.Count) return ErrorValue.Value;
        return EvaluateArrayOperand(node.Arguments[idx], context);
    }

    private ScalarValue EvaluateChooseIndexRange(FunctionCallNode node, IEvalContext context, RangeValue indexRange)
    {
        // Excel broadcasts an array index_num against the selected branches: a 1xN index over
        // M-row column-vector branches yields an MxN result (the "stack columns" idiom). The result
        // shape is the broadcast of the index dimensions with every selected branch's dimensions.
        var branchCache = new Dictionary<int, ScalarValue>();

        int rowCount = indexRange.RowCount;
        int colCount = indexRange.ColCount;
        foreach (var indexValue in indexRange.Cells)
        {
            if (indexValue is ErrorValue) continue;
            var index = CoerceChooseIndex(indexValue, node.Arguments.Count);
            if (index is null) continue;

            if (!branchCache.TryGetValue(index.Value, out var selected))
            {
                selected = EvaluateArrayOperand(node.Arguments[index.Value], context);
                branchCache[index.Value] = selected;
            }

            if (selected is RangeValue branch)
            {
                if (!CanBroadcast(rowCount, branch.RowCount) || !CanBroadcast(colCount, branch.ColCount))
                    return ErrorValue.Value;
                rowCount = Math.Max(rowCount, branch.RowCount);
                colCount = Math.Max(colCount, branch.ColCount);
            }
        }

        var cells = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
            {
                var indexValue = indexRange.Cells[indexRange.RowCount == 1 ? 0 : r, indexRange.ColCount == 1 ? 0 : c];
                if (indexValue is ErrorValue indexError)
                {
                    cells[r, c] = indexError;
                    continue;
                }

                var index = CoerceChooseIndex(indexValue, node.Arguments.Count);
                if (index is null)
                {
                    cells[r, c] = ErrorValue.Value;
                    continue;
                }

                var selected = branchCache[index.Value];
                cells[r, c] = selected is RangeValue selectedRange
                    ? selectedRange.Cells[selectedRange.RowCount == 1 ? 0 : r, selectedRange.ColCount == 1 ? 0 : c]
                    : selected;
            }

        return new RangeValue(cells, indexRange.StartRow, indexRange.StartCol) { SheetName = indexRange.SheetName };
    }

    private static ScalarValue PickRangeElementForArrayResult(RangeValue range, int row, int col, int targetRows, int targetCols)
    {
        // Delegate to the shared broadcast helper: this accepts an exact-shape match, a fully-1x1
        // range, AND a partially-broadcastable range (row extent 1 with matching column count, or
        // column extent 1 with matching row count) — matching Excel's per-axis broadcasting for
        // IFS/SWITCH condition/value/result arrays, the same rule IF and CHOOSE already apply.
        return BroadcastElementAt(range, row, col, targetRows, targetCols);
    }

    private int? CoerceChooseIndex(ScalarValue value, int argumentCount)
    {
        if (value is ErrorValue) return null;
        var coerced = CoerceToNumber(value);
        if (coerced is not NumberValue number) return null;
        double rawIdx = number.Value;
        if (!double.IsFinite(rawIdx)) return null;
        int idx = (int)rawIdx;
        return idx >= 1 && idx < argumentCount ? idx : null;
    }

    private ScalarValue EvaluateIfs(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2 || node.Arguments.Count % 2 != 0) return ErrorValue.Value;
        for (int i = 0; i < node.Arguments.Count - 1; i += 2)
        {
            var cond = EvaluateArrayOperand(node.Arguments[i], context);
            if (cond is ErrorValue e) return e;
            if (cond is RangeValue conditionRange) return EvaluateIfsConditionRange(node, context, conditionRange, i);
            bool? taken = TryCoerceCondition(cond);
            if (taken is null) return ErrorValue.Value;
            if (taken.Value) return EvaluateArrayOperand(node.Arguments[i + 1], context);
        }
        return ErrorValue.NA;
    }

    private ScalarValue EvaluateIfsConditionRange(FunctionCallNode node, IEvalContext context, RangeValue firstConditionRange, int conditionArgIndex)
    {
        // Seed the cache at the argument index that actually produced this range (not always 0):
        // EvaluateIfsElement's per-cell loop starts scanning from argument 0, and any earlier scalar
        // condition pairs (already known FALSE — that's why the outer EvaluateIfs loop reached this
        // later index at all) must still be genuinely re-evaluated at their OWN index rather than
        // having this later range silently substituted in their place.
        var conditionCache = new Dictionary<int, ScalarValue> { [conditionArgIndex] = firstConditionRange };
        var resultCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[firstConditionRange.RowCount, firstConditionRange.ColCount];

        for (int r = 0; r < firstConditionRange.RowCount; r++)
            for (int c = 0; c < firstConditionRange.ColCount; c++)
                cells[r, c] = EvaluateIfsElement(node, context, conditionCache, resultCache, firstConditionRange, r, c);

        return new RangeValue(cells, firstConditionRange.StartRow, firstConditionRange.StartCol) { SheetName = firstConditionRange.SheetName };
    }

    private ScalarValue EvaluateIfsElement(
        FunctionCallNode node,
        IEvalContext context,
        Dictionary<int, ScalarValue> conditionCache,
        Dictionary<int, ScalarValue> resultCache,
        RangeValue shape,
        int row,
        int col)
    {
        for (int i = 0; i < node.Arguments.Count - 1; i += 2)
        {
            if (!conditionCache.TryGetValue(i, out var condition))
            {
                condition = EvaluateArrayOperand(node.Arguments[i], context);
                conditionCache[i] = condition;
            }

            var conditionElement = condition is RangeValue conditionRange
                ? PickRangeElementForArrayResult(conditionRange, row, col, shape.RowCount, shape.ColCount)
                : condition;

            if (conditionElement is ErrorValue error) return error;
            bool? taken = TryCoerceCondition(conditionElement);
            if (taken is null) return ErrorValue.Value;
            if (!taken.Value) continue;

            int resultIndex = i + 1;
            if (!resultCache.TryGetValue(resultIndex, out var result))
            {
                result = EvaluateArrayOperand(node.Arguments[resultIndex], context);
                resultCache[resultIndex] = result;
            }

            return result is RangeValue resultRange
                ? PickRangeElementForArrayResult(resultRange, row, col, shape.RowCount, shape.ColCount)
                : result;
        }

        return ErrorValue.NA;
    }


    private ScalarValue EvaluateSwitch(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 3) return ErrorValue.Value;
        var expr = EvaluateArrayOperand(node.Arguments[0], context);
        if (expr is ErrorValue e) return e;
        if (expr is RangeValue exprRange) return EvaluateSwitchExpressionRange(node, context, exprRange);

        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        var valueCache = new Dictionary<int, ScalarValue>();
        var resultCache = new Dictionary<int, ScalarValue>();
        for (int i = 0; i < pairCount; i++)
        {
            int valueIndex = 1 + i * 2;
            // Array-aware, matching every sibling short-circuit function's scalar-context argument
            // evaluation (IF's branches, IFERROR/IFNA's fallback, CHOOSE's/IFS's branches all already
            // go through EvaluateArrayOperand here) -- NOT the legacy EvaluateNode this replaced,
            // which implicitly-intersected a multi-cell value_i against the CURRENT FORMULA CELL's
            // row/column (wrong cell off-axis, #VALUE! when there's no current cell to intersect
            // against at all in a nested-call context, and always just the top-left cell when there's
            // no current-cell context whatsoever -- e.g. a direct Evaluate(text, sheet) call).
            var val = EvaluateArrayOperand(node.Arguments[valueIndex], context);
            if (val is ErrorValue ve) return ve;

            // A multi-cell value_i comparand (expr itself being scalar) is exactly Excel's
            // "implicit array behavior" trigger: the whole SWITCH result spills across THIS
            // argument's shape, comparing the (fixed) scalar expr against each of its elements in
            // turn -- the same per-cell PickRangeElementForArrayResult/EvaluateSwitchElement
            // machinery the exprRange-is-an-array path below already uses, just driven by this
            // value_i's shape instead of expr's, and starting the per-cell pair scan at THIS pair
            // (every earlier pair was already confirmed a non-matching scalar by this very loop).
            if (val is RangeValue valueRange)
            {
                valueCache[valueIndex] = valueRange;
                var cells = new ScalarValue[valueRange.RowCount, valueRange.ColCount];
                for (int r = 0; r < valueRange.RowCount; r++)
                    for (int c = 0; c < valueRange.ColCount; c++)
                        cells[r, c] = EvaluateSwitchElement(
                            node, context, valueCache, resultCache,
                            expr, valueRange.RowCount, valueRange.ColCount, r, c, i);

                return new RangeValue(cells, valueRange.StartRow, valueRange.StartCol) { SheetName = valueRange.SheetName };
            }

            valueCache[valueIndex] = val;
            if (BuiltInFunctions.ScalarEquals(expr, val))
                return EvaluateArrayOperand(node.Arguments[valueIndex + 1], context);
        }
        return hasDefault ? EvaluateArrayOperand(node.Arguments[^1], context) : ErrorValue.NA;
    }

    private ScalarValue EvaluateSwitchExpressionRange(FunctionCallNode node, IEvalContext context, RangeValue exprRange)
    {
        var valueCache = new Dictionary<int, ScalarValue>();
        var resultCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[exprRange.RowCount, exprRange.ColCount];

        for (int r = 0; r < exprRange.RowCount; r++)
            for (int c = 0; c < exprRange.ColCount; c++)
            {
                var exprElement = exprRange.Cells[r, c];
                cells[r, c] = exprElement is ErrorValue error
                    ? error
                    : EvaluateSwitchElement(node, context, valueCache, resultCache,
                        exprElement, exprRange.RowCount, exprRange.ColCount, r, c, startPairIndex: 0);
            }

        return new RangeValue(cells, exprRange.StartRow, exprRange.StartCol) { SheetName = exprRange.SheetName };
    }

    /// <summary>
    /// Evaluates one output cell of a spilled SWITCH result -- shared by the two ways SWITCH can end
    /// up producing an array: <paramref name="exprElement"/> came from a per-cell slice of an array
    /// <c>expr</c> (<see cref="EvaluateSwitchExpressionRange"/>, which scans every pair from index 0
    /// since a different cell's expr can match an earlier pair), or it is the single fixed scalar
    /// <c>expr</c> paired with a per-cell slice of an array value_i comparand
    /// (<see cref="EvaluateSwitch"/>, which starts scanning at <paramref name="startPairIndex"/> --
    /// the pair that produced the array -- since every earlier pair was already confirmed scalar and
    /// non-matching before that array was ever reached).
    /// </summary>
    private ScalarValue EvaluateSwitchElement(
        FunctionCallNode node,
        IEvalContext context,
        Dictionary<int, ScalarValue> valueCache,
        Dictionary<int, ScalarValue> resultCache,
        ScalarValue exprElement,
        int shapeRows,
        int shapeCols,
        int row,
        int col,
        int startPairIndex)
    {
        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = startPairIndex; i < pairCount; i++)
        {
            int valueIndex = 1 + i * 2;
            if (!valueCache.TryGetValue(valueIndex, out var value))
            {
                value = EvaluateArrayOperand(node.Arguments[valueIndex], context);
                valueCache[valueIndex] = value;
            }

            var valueElement = value is RangeValue valueRange
                ? PickRangeElementForArrayResult(valueRange, row, col, shapeRows, shapeCols)
                : value;

            if (valueElement is ErrorValue valueError) return valueError;
            if (!BuiltInFunctions.ScalarEquals(exprElement, valueElement)) continue;

            int resultIndex = valueIndex + 1;
            if (!resultCache.TryGetValue(resultIndex, out var result))
            {
                result = EvaluateArrayOperand(node.Arguments[resultIndex], context);
                resultCache[resultIndex] = result;
            }

            return result is RangeValue resultRange
                ? PickRangeElementForArrayResult(resultRange, row, col, shapeRows, shapeCols)
                : result;
        }

        if (!hasDefault) return ErrorValue.NA;

        int defaultIndex = node.Arguments.Count - 1;
        if (!resultCache.TryGetValue(defaultIndex, out var defaultResult))
        {
            defaultResult = EvaluateArrayOperand(node.Arguments[defaultIndex], context);
            resultCache[defaultIndex] = defaultResult;
        }

        return defaultResult is RangeValue defaultRange
            ? PickRangeElementForArrayResult(defaultRange, row, col, shapeRows, shapeCols)
            : defaultResult;
    }

}
