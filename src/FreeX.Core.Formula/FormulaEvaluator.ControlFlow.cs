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

    private ScalarValue EvaluateIf(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 2 or > 3) return ErrorValue.Value;
        var cond = EvaluateArrayOperand(node.Arguments[0], context);
        if (cond is ErrorValue e) return e;
        if (cond is RangeValue conditionRange) return EvaluateIfConditionRange(node, context, conditionRange);
        bool? taken = cond switch
        {
            BoolValue b     => b.Value,
            NumberValue n   => n.Value != 0,
            DateTimeValue d => d.Value != 0,
            BlankValue      => false,
            _               => null   // text condition is #VALUE! in Excel
        };
        if (taken is null) return ErrorValue.Value;
        if (taken.Value)  return EvaluateArrayOperand(node.Arguments[1], context);
        if (node.Arguments.Count == 3) return EvaluateArrayOperand(node.Arguments[2], context);
        return FalseValue;
    }

    private ScalarValue EvaluateIfConditionRange(FunctionCallNode node, IEvalContext context, RangeValue conditionRange)
    {
        ScalarValue? trueBranch = null;
        ScalarValue? falseBranch = null;
        var cells = new ScalarValue[conditionRange.RowCount, conditionRange.ColCount];

        for (int r = 0; r < conditionRange.RowCount; r++)
            for (int c = 0; c < conditionRange.ColCount; c++)
            {
                var condition = conditionRange.Cells[r, c];
                if (condition is ErrorValue error)
                {
                    cells[r, c] = error;
                    continue;
                }

                bool? taken = condition switch
                {
                    BoolValue b     => b.Value,
                    NumberValue n   => n.Value != 0,
                    DateTimeValue d => d.Value != 0,
                    BlankValue      => false,
                    _               => null
                };
                if (taken is null)
                {
                    cells[r, c] = ErrorValue.Value;
                    continue;
                }

                var selected = taken.Value
                    ? trueBranch ??= EvaluateArrayOperand(node.Arguments[1], context)
                    : falseBranch ??= node.Arguments.Count == 3
                        ? EvaluateArrayOperand(node.Arguments[2], context)
                        : FalseValue;

                cells[r, c] = selected is RangeValue selectedRange
                    ? PickRangeElementForArrayResult(selectedRange, r, c, conditionRange.RowCount, conditionRange.ColCount)
                    : selected;
            }

        return new RangeValue(cells, conditionRange.StartRow, conditionRange.StartCol) { SheetName = conditionRange.SheetName };
    }

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
        RangeValue? fallbackRange = fallback as RangeValue;
        if (fallbackRange is not null && (fallbackRange.RowCount != range.RowCount || fallbackRange.ColCount != range.ColCount))
            return ErrorValue.Value;

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue error && catches(error)
                    ? fallbackRange?.Cells[r, c] ?? fallback
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
        var branchCache = new Dictionary<int, ScalarValue>();
        var cells = new ScalarValue[indexRange.RowCount, indexRange.ColCount];

        for (int r = 0; r < indexRange.RowCount; r++)
            for (int c = 0; c < indexRange.ColCount; c++)
            {
                var indexValue = indexRange.Cells[r, c];
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

                if (!branchCache.TryGetValue(index.Value, out var selected))
                {
                    selected = EvaluateArrayOperand(node.Arguments[index.Value], context);
                    branchCache[index.Value] = selected;
                }

                cells[r, c] = selected is RangeValue selectedRange
                    ? PickRangeElementForArrayResult(selectedRange, r, c, indexRange.RowCount, indexRange.ColCount)
                    : selected;
            }

        return new RangeValue(cells, indexRange.StartRow, indexRange.StartCol) { SheetName = indexRange.SheetName };
    }

    private static ScalarValue PickRangeElementForArrayResult(RangeValue range, int row, int col, int targetRows, int targetCols)
    {
        if (range.RowCount == targetRows && range.ColCount == targetCols)
            return range.Cells[row, col];

        if (range.RowCount == 1 && range.ColCount == 1)
            return range.Cells[0, 0];

        return ErrorValue.Value;
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
            if (cond is RangeValue conditionRange) return EvaluateIfsConditionRange(node, context, conditionRange);
            bool? taken = cond switch
            {
                BoolValue b     => b.Value,
                NumberValue n   => n.Value != 0,
                DateTimeValue d => d.Value != 0,
                BlankValue      => false,
                _               => null
            };
            if (taken is null) return ErrorValue.Value;
            if (taken.Value) return EvaluateArrayOperand(node.Arguments[i + 1], context);
        }
        return ErrorValue.NA;
    }

    private ScalarValue EvaluateIfsConditionRange(FunctionCallNode node, IEvalContext context, RangeValue firstConditionRange)
    {
        var conditionCache = new Dictionary<int, ScalarValue> { [0] = firstConditionRange };
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
            bool? taken = conditionElement switch
            {
                BoolValue b     => b.Value,
                NumberValue n   => n.Value != 0,
                DateTimeValue d => d.Value != 0,
                BlankValue      => false,
                _               => null
            };
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
        for (int i = 0; i < pairCount; i++)
        {
            var val = EvaluateNode(node.Arguments[1 + i * 2], context);
            if (val is ErrorValue ve) return ve;
            if (BuiltInFunctions.ScalarEquals(expr, val))
                return EvaluateArrayOperand(node.Arguments[1 + i * 2 + 1], context);
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
                cells[r, c] = EvaluateSwitchElement(node, context, valueCache, resultCache, exprRange, r, c);

        return new RangeValue(cells, exprRange.StartRow, exprRange.StartCol) { SheetName = exprRange.SheetName };
    }

    private ScalarValue EvaluateSwitchElement(
        FunctionCallNode node,
        IEvalContext context,
        Dictionary<int, ScalarValue> valueCache,
        Dictionary<int, ScalarValue> resultCache,
        RangeValue exprRange,
        int row,
        int col)
    {
        var expr = exprRange.Cells[row, col];
        if (expr is ErrorValue error) return error;

        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            int valueIndex = 1 + i * 2;
            if (!valueCache.TryGetValue(valueIndex, out var value))
            {
                value = EvaluateArrayOperand(node.Arguments[valueIndex], context);
                valueCache[valueIndex] = value;
            }

            var valueElement = value is RangeValue valueRange
                ? PickRangeElementForArrayResult(valueRange, row, col, exprRange.RowCount, exprRange.ColCount)
                : value;

            if (valueElement is ErrorValue valueError) return valueError;
            if (!BuiltInFunctions.ScalarEquals(expr, valueElement)) continue;

            int resultIndex = valueIndex + 1;
            if (!resultCache.TryGetValue(resultIndex, out var result))
            {
                result = EvaluateArrayOperand(node.Arguments[resultIndex], context);
                resultCache[resultIndex] = result;
            }

            return result is RangeValue resultRange
                ? PickRangeElementForArrayResult(resultRange, row, col, exprRange.RowCount, exprRange.ColCount)
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
            ? PickRangeElementForArrayResult(defaultRange, row, col, exprRange.RowCount, exprRange.ColCount)
            : defaultResult;
    }

}
