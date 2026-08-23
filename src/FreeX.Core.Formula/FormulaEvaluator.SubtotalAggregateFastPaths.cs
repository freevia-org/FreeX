using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private bool TryEvaluateSubtotalDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 2 or > 255)
            return false;

        var funcState = TryEvaluateFastScalarControl(node.Arguments[0], context, out var funcValue);
        if (funcState == DirectRangeFastPathState.Unsupported)
            return false;
        if (funcValue is ErrorValue funcError)
        {
            result = funcError;
            return true;
        }

        var coerced = CoerceToNumber(funcValue);
        if (coerced is ErrorValue coercionError)
        {
            result = coercionError;
            return true;
        }

        var funcNumD = ((NumberValue)coerced).Value;
        if (!double.IsFinite(funcNumD))
        {
            result = ErrorValue.Value;
            return true;
        }

        var ranges = new List<DirectRangeArgument>(node.Arguments.Count - 1);
        for (var index = 1; index < node.Arguments.Count; index++)
        {
            var rangeState = TryCreateDirectRangeArgument(node.Arguments[index], context, out var range, out result);
            if (rangeState == DirectRangeFastPathState.Unsupported)
                return false;
            if (rangeState == DirectRangeFastPathState.Error)
                return true;

            ranges.Add(range);
        }

        var funcNum = (int)funcNumD;
        var skipHidden = funcNum >= 101;
        var baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;
        if (baseFunc is 7 or 8 or 10 or 11)
            return false;

        var numeric = new NumericAggregateAccumulator();
        long countA = 0;

        foreach (var range in ranges)
        {
            for (var row = range.StartRow; row <= range.EndRow; row++)
            {
                if (ShouldSkipFastSubtotalRow(context, range, row, skipHidden))
                    continue;

                for (var col = range.StartCol; col <= range.EndCol; col++)
                {
                    if (IsFastNestedSubtotalOrAggregateCell(context, range, row, col))
                        continue;

                    var value = GetFastRangeCellValue(context, range, row, col);
                    if (value is ErrorValue error)
                    {
                        // COUNT (2) ignores error cells; COUNTA (3) counts them as non-blank.
                        // All other aggregating functions propagate the error immediately.
                        if (baseFunc == 2) continue;
                        if (baseFunc == 3) { countA++; continue; }
                        result = error;
                        return true;
                    }

                    if (TryDirectRangeNumber(value, out var number, out _))
                        numeric.Add(number, baseFunc);

                    if (value is not BlankValue)
                        countA++;
                }
            }
        }

        result = EvaluateSubtotalAggregateNumericResult(baseFunc, numeric, countA);
        return true;
    }

    private bool TryEvaluateAggregateDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 3 or > 255)
            return false;

        var funcState = TryEvaluateFastScalarControl(node.Arguments[0], context, out var funcValue);
        if (funcState == DirectRangeFastPathState.Unsupported)
            return false;
        if (funcValue is ErrorValue funcError)
        {
            result = funcError;
            return true;
        }

        var optionsState = TryEvaluateFastScalarControl(node.Arguments[1], context, out var optionsValue);
        if (optionsState == DirectRangeFastPathState.Unsupported)
            return false;
        if (optionsValue is ErrorValue optionsError)
        {
            result = optionsError;
            return true;
        }

        var funcCoerced = CoerceToNumber(funcValue);
        if (funcCoerced is ErrorValue funcCoercionError)
        {
            result = funcCoercionError;
            return true;
        }

        var optionsCoerced = CoerceToNumber(optionsValue);
        if (optionsCoerced is ErrorValue optionsCoercionError)
        {
            result = optionsCoercionError;
            return true;
        }

        var funcNumD = ((NumberValue)funcCoerced).Value;
        var optionsD = ((NumberValue)optionsCoerced).Value;
        if (!double.IsFinite(funcNumD) || !double.IsFinite(optionsD))
        {
            result = ErrorValue.Value;
            return true;
        }

        var funcNum = (int)funcNumD;
        var options = (int)optionsD;
        if (funcNum < 1 || funcNum > 19 || options < 0 || options > 7)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (funcNum > 11)
            return TryEvaluateAggregateSelectionDirectRanges(node, context, funcNum, options, out result);

        var ignoreErrors = options is 2 or 3 or 6 or 7;
        var ignoreHiddenRows = options is 1 or 3 or 5 or 7;
        var ignoreNestedAggregates = options <= 3;

        // The common single-range form should stay allocation-light after the formula has been
        // parsed. Keep the multi-range list for formulas that genuinely need it, but do not create
        // a list and backing array for =AGGREGATE(...,A1:A100000).
        if (node.Arguments.Count == 3)
        {
            var rangeState = TryCreateDirectRangeArgument(node.Arguments[2], context, out var range, out result);
            if (rangeState == DirectRangeFastPathState.Unsupported)
                return false;
            if (rangeState == DirectRangeFastPathState.Error)
                return true;

            var singleNumeric = new NumericAggregateAccumulator();
            long singleCountA = 0;
            if (!TryAccumulateAggregateDirectRange(
                    context,
                    range,
                    funcNum,
                    ignoreErrors,
                    ignoreHiddenRows,
                    ignoreNestedAggregates,
                    ref singleNumeric,
                    ref singleCountA,
                    out var rangeError))
            {
                result = rangeError!;
                return true;
            }

            result = funcNum == 3
                ? NumberValueFor(singleCountA)
                : EvaluateSubtotalAggregateNumericResult(funcNum, singleNumeric, countA: 0);
            return true;
        }

        var ranges = new List<DirectRangeArgument>(node.Arguments.Count - 2);

        for (var index = 2; index < node.Arguments.Count; index++)
        {
            var rangeState = TryCreateDirectRangeArgument(node.Arguments[index], context, out var range, out result);
            if (rangeState == DirectRangeFastPathState.Unsupported)
                return false;
            if (rangeState == DirectRangeFastPathState.Error)
                return true;

            ranges.Add(range);
        }

        var numeric = new NumericAggregateAccumulator();
        long countA = 0;

        foreach (var range in ranges)
        {
            if (!TryAccumulateAggregateDirectRange(
                    context,
                    range,
                    funcNum,
                    ignoreErrors,
                    ignoreHiddenRows,
                    ignoreNestedAggregates,
                    ref numeric,
                    ref countA,
                    out var rangeError))
            {
                result = rangeError!;
                return true;
            }
        }

        result = funcNum == 3
            ? NumberValueFor(countA)
            : EvaluateSubtotalAggregateNumericResult(funcNum, numeric, countA: 0);
        return true;
    }

    private static bool TryAccumulateAggregateDirectRange(
        IEvalContext context,
        DirectRangeArgument range,
        int funcNum,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates,
        ref NumericAggregateAccumulator numeric,
        ref long countA,
        out ErrorValue? error)
    {
        error = null;
        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            if (ignoreHiddenRows && IsFastAggregateRowHidden(context, range, row))
                continue;

            for (var col = range.StartCol; col <= range.EndCol; col++)
            {
                if (ignoreNestedAggregates && IsFastNestedSubtotalOrAggregateCell(context, range, row, col))
                    continue;

                var value = GetFastRangeCellValue(context, range, row, col);
                if (value is ErrorValue cellError)
                {
                    if (ignoreErrors)
                        continue;

                    error = cellError;
                    return false;
                }

                if (funcNum == 3)
                {
                    if (value is not BlankValue)
                        countA++;
                }
                else if (TryDirectRangeNumber(value, out var number, out _))
                {
                    numeric.Add(number, funcNum);
                }
            }
        }

        return true;
    }

    private DirectRangeFastPathState TryEvaluateFastScalarControl(
        FormulaNode node,
        IEvalContext context,
        out ScalarValue value)
    {
        switch (node)
        {
            case NumberNode number:
                value = NumberValueFor(number.Value);
                return DirectRangeFastPathState.Success;
            case StringNode text:
                value = new TextValue(text.Value);
                return DirectRangeFastPathState.Success;
            case BooleanNode boolean:
                value = boolean.Value ? TrueValue : FalseValue;
                return DirectRangeFastPathState.Success;
            case OmittedArgumentNode:
                value = BlankValue.Instance;
                return DirectRangeFastPathState.Success;
            case ErrorNode error:
                value = error.Error;
                return DirectRangeFastPathState.Success;
            case CellRefNode cell:
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    value = ErrorValue.Ref;
                    return DirectRangeFastPathState.Success;
                }

                value = cell.SheetName is not null
                    ? context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber)
                    : context.GetCellValue(cell.Row, cell.ColumnNumber);
                return DirectRangeFastPathState.Success;
        }

        if (!TryAsRangeRef(node, out var rangeRef))
        {
            value = BlankValue.Instance;
            return DirectRangeFastPathState.Unsupported;
        }

        var rangeState = TryCreateDirectRangeArgument(rangeRef, context, out var range, out value);
        if (rangeState != DirectRangeFastPathState.Success)
            return rangeState;

        if (range.StartRow != range.EndRow || range.StartCol != range.EndCol)
        {
            value = BlankValue.Instance;
            return DirectRangeFastPathState.Unsupported;
        }

        value = GetFastRangeCellValue(context, range, range.StartRow, range.StartCol);
        return DirectRangeFastPathState.Success;
    }

    private static DirectRangeFastPathState TryCreateDirectRangeArgument(
        FormulaNode node,
        IEvalContext context,
        out DirectRangeArgument range,
        out ScalarValue result)
    {
        if (TryAsRangeRef(node, out var rangeRef))
            return TryCreateDirectRangeArgument(rangeRef, context, out range, out result);

        if (node is NamedRangeNode named)
        {
            if (context.TryResolveLambdaBinding(named.Name) is not null)
            {
                range = default;
                result = BlankValue.Instance;
                return DirectRangeFastPathState.Unsupported;
            }

            // Excel scope precedence: a sheet-scoped named FORMULA outranks a same-named
            // workbook-global named RANGE. Defer to the general (slow) argument-expansion path,
            // which resolves that precedence correctly, instead of summing the wrong global range.
            if (IsSheetScopedName(named.Name, context, out var sheetScopedIsFormula) && sheetScopedIsFormula)
            {
                range = default;
                result = BlankValue.Instance;
                return DirectRangeFastPathState.Unsupported;
            }

            var resolved = context.TryResolveNamedRange(named.Name);
            if (resolved is null)
            {
                // Not a resolvable range - it may be a workbook-global named FORMULA
                // (e.g. a dynamic OFFSET/COUNTA range). Defer to the slow path, which
                // evaluates named formulas via TryEvaluateNamedFormula, instead of
                // short-circuiting to #NAME?.
                range = default;
                result = BlankValue.Instance;
                return DirectRangeFastPathState.Unsupported;
            }

            var gridRange = resolved.Value;
            var sheetName = context.TryGetSheetName(gridRange.Start.Sheet);
            var startRow = gridRange.Start.Row;
            var startCol = gridRange.Start.Col;
            var endRow = gridRange.End.Row;
            var endCol = gridRange.End.Col;

            // A named range that spans a full column/row (e.g. Data = Sheet1!$A:$A) resolves to
            // the full 1,048,576-row / 16,384-col grid extent, which exceeds
            // MaxMaterializedRangeCells below and would wrongly return #REF! even though the
            // identical literal range (=COUNTIF(A:A,...)) is clamped and works. Clamp it to the
            // sheet's used range first, mirroring the literal-range clamp just below
            // (ClampOpenEndedRangeToUsed) and the equivalent named-range clamp in
            // FormulaEvaluator.FastAggregates.cs.
            var isFullCol = startRow == 1 && endRow == CellAddress.MaxRow;
            var isFullRow = startCol == 1 && endCol == CellAddress.MaxCol;
            if (isFullCol || isFullRow)
            {
                if (!TryClampFullRangeToUsed(sheetName, context, ref startRow, ref startCol, ref endRow, ref endCol))
                {
                    // Nothing populated on the sheet: zero cells to aggregate rather than the huge nominal range.
                    range = new DirectRangeArgument(sheetName, 1, 1, 0, 0);
                    result = BlankValue.Instance;
                    return DirectRangeFastPathState.Success;
                }
            }

            return TryCreateDirectRangeArgument(
                sheetName,
                startRow,
                startCol,
                endRow,
                endCol,
                out range,
                out result);
        }

        range = default;
        result = BlankValue.Instance;
        return DirectRangeFastPathState.Unsupported;
    }

    private static DirectRangeFastPathState TryCreateDirectRangeArgument(
        RangeRefNode rangeRef,
        IEvalContext context,
        out DirectRangeArgument range,
        out ScalarValue result)
    {
        if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
        {
            range = default;
            result = ErrorValue.Ref;
            return DirectRangeFastPathState.Error;
        }

        rangeRef = ClampOpenEndedRangeToUsed(rangeRef, context);
        return TryCreateDirectRangeArgument(
            rangeRef.SheetName,
            rangeRef.Start.Row,
            rangeRef.Start.ColumnNumber,
            rangeRef.End.Row,
            rangeRef.End.ColumnNumber,
            out range,
            out result);
    }

    private static DirectRangeFastPathState TryCreateDirectRangeArgument(
        string? sheetName,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        out DirectRangeArgument range,
        out ScalarValue result)
    {
        var normalizedStartRow = Math.Min(startRow, endRow);
        var normalizedEndRow = Math.Max(startRow, endRow);
        var normalizedStartCol = Math.Min(startCol, endCol);
        var normalizedEndCol = Math.Max(startCol, endCol);
        var cellCount = FormulaSafetyLimits.GetRangeCellCount(
            normalizedStartRow,
            normalizedStartCol,
            normalizedEndRow,
            normalizedEndCol);

        if (cellCount > FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            range = default;
            result = ErrorValue.Ref;
            return DirectRangeFastPathState.Error;
        }

        range = new DirectRangeArgument(
            sheetName,
            normalizedStartRow,
            normalizedStartCol,
            normalizedEndRow,
            normalizedEndCol);
        result = BlankValue.Instance;
        return DirectRangeFastPathState.Success;
    }

    private static ScalarValue EvaluateSubtotalAggregateNumericResult(
        int functionNumber,
        NumericAggregateAccumulator numeric,
        long countA)
    {
        return functionNumber switch
        {
            1 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(numeric.Average),
            2 => NumberValueFor(numeric.Count),
            3 => NumberValueFor(countA),
            // MAX/MIN return 0 for an all-non-numeric/empty range, matching the plain MAX()/MIN()
            // functions and real Excel (and BuiltInFunctions.Subtotal.cs's SUBTOTAL slow path) —
            // unlike AVERAGE/STDEV/VAR (1,7,8,10,11) which genuinely error (#DIV/0!) on an empty sample.
            4 => FastNumberResult(numeric.Count == 0 ? 0 : numeric.Max),
            5 => FastNumberResult(numeric.Count == 0 ? 0 : numeric.Min),
            6 => FastNumberResult(numeric.Count == 0 ? 0 : numeric.Product),
            7 => numeric.Count < 2 ? ErrorValue.DivByZero : FastNumberResult(Math.Sqrt(numeric.SampleVariance)),
            8 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(Math.Sqrt(numeric.PopulationVariance)),
            9 => FastNumberResult(numeric.Sum),
            10 => numeric.Count < 2 ? ErrorValue.DivByZero : FastNumberResult(numeric.SampleVariance),
            11 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(numeric.PopulationVariance),
            _ => ErrorValue.Value
        };
    }

    private static ScalarValue FastNumberResult(double value) =>
        double.IsFinite(value) ? NumberValueFor(value) : ErrorValue.Num;

    private static bool ShouldSkipFastSubtotalRow(
        IEvalContext context,
        DirectRangeArgument range,
        uint row,
        bool skipHidden)
    {
        return range.SheetName is null
            ? skipHidden ? context.IsRowHidden(row) : context.IsRowFilterHidden(row)
            : skipHidden ? context.IsRowHidden(range.SheetName, row) : context.IsRowFilterHidden(range.SheetName, row);
    }

    private static bool IsFastAggregateRowHidden(
        IEvalContext context,
        DirectRangeArgument range,
        uint row)
    {
        return range.SheetName is null
            ? context.IsRowHidden(row)
            : context.IsRowHidden(range.SheetName, row);
    }

    private static bool IsFastNestedSubtotalOrAggregateCell(
        IEvalContext context,
        DirectRangeArgument range,
        uint row,
        uint col)
    {
        var cell = range.SheetName is null
            ? context.TryGetCell(row, col)
            : context.TryGetCell(range.SheetName, row, col);

        return FormulaFunctionCallScanner.ContainsSubtotalOrAggregateCall(cell?.FormulaText);
    }

    private static ScalarValue GetFastRangeCellValue(
        IEvalContext context,
        DirectRangeArgument range,
        uint row,
        uint col)
    {
        return range.SheetName is null
            ? context.GetCellValue(row, col)
            : context.GetCellValue(range.SheetName, row, col);
    }

    // ── SUBTOTAL with array-producing OFFSET argument ──────────────────────────
    //
    // The classic CSE idiom  SUBTOTAL(3, OFFSET(ref, ROW(ref)-ROW(anchor), 0, 1))
    // relies on OFFSET being "array-called" once per element of its rows argument:
    //   • rows  = RangeValue {0,1,2,...,n-1}  (produced by ROW(range)-ROW(anchor))
    //   • for each rows_i: OFFSET returns a 1-row range at  baseRow + rows_i
    //   • SUBTOTAL(funcNum, that_range) returns a scalar per row
    //   • The outer call returns the n-element array of scalars
    //
    // FreeX's normal OFFSET evaluation calls CoerceToNumber(rowsArg), which
    // returns #VALUE! for a RangeValue.  This special-case path detects the
    // pattern early and produces the expected array result.

    private bool TryEvaluateSubtotalOffsetArrayArg(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;

        // Require exactly 2 arguments: func_num + one OFFSET call.
        if (node.Arguments.Count != 2) return false;

        var funcState = TryEvaluateFastScalarControl(node.Arguments[0], context, out var funcValue);
        if (funcState == DirectRangeFastPathState.Unsupported) return false;
        if (funcValue is ErrorValue funcError) { result = funcError; return true; }

        var coerced = CoerceToNumber(funcValue);
        if (coerced is ErrorValue coercionError) { result = coercionError; return true; }

        var funcNumD = ((NumberValue)coerced).Value;
        if (!double.IsFinite(funcNumD)) { result = ErrorValue.Value; return true; }
        int funcNum = (int)funcNumD;
        if (funcNum is 7 or 8 or 10 or 11) return false; // statistical: need list, not just count/sum
        bool skipHidden = funcNum >= 101;
        int baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;

        // Check that the second argument is an OFFSET call.
        if (node.Arguments[1] is not FunctionCallNode offsetNode ||
            !string.Equals(offsetNode.FunctionName, "OFFSET", StringComparison.OrdinalIgnoreCase))
            return false;

        if (offsetNode.Arguments.Count is < 3 or > 5) return false;

        // Parse the OFFSET base reference (mirrors EvaluateOffsetReference).
        uint baseRow, baseCol; int baseHeight, baseWidth; string? baseSheet = null;
        switch (offsetNode.Arguments[0])
        {
            case CellRefNode cellRef:
                if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                { result = ErrorValue.Ref; return true; }
                baseRow = cellRef.Row; baseCol = cellRef.ColumnNumber;
                baseHeight = 1; baseWidth = 1;
                baseSheet = cellRef.SheetName;
                break;
            case RangeRefNode rangeRef:
                if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                { result = ErrorValue.Ref; return true; }
                uint r0 = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
                uint r1 = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
                uint c0 = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                uint c1 = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                baseRow = r0; baseCol = c0;
                baseHeight = (int)(r1 - r0 + 1);
                baseWidth = (int)(c1 - c0 + 1);
                baseSheet = rangeRef.SheetName;
                break;
            default:
                return false; // named ranges and full-col/row are uncommon here; fall through to slow path
        }

        // Evaluate scalar arguments (rows, cols, height, width).
        var rowsRaw = EvaluateNode(offsetNode.Arguments[1], context);
        if (rowsRaw is ErrorValue re) { result = re; return true; }
        var colsRaw = EvaluateNode(offsetNode.Arguments[2], context);
        if (colsRaw is ErrorValue ce) { result = ce; return true; }

        // Require at least one of rows/cols to be a RangeValue (array); otherwise the
        // normal scalar OFFSET path handles it.
        bool rowsIsArray = rowsRaw is RangeValue;
        bool colsIsArray = colsRaw is RangeValue;
        if (!rowsIsArray && !colsIsArray) return false;

        // Evaluate height and width overrides if present.
        int height = baseHeight;
        int width  = baseWidth;
        if (offsetNode.Arguments.Count >= 4 && offsetNode.Arguments[3] is not OmittedArgumentNode)
        {
            var hRaw = EvaluateNode(offsetNode.Arguments[3], context);
            if (hRaw is ErrorValue he) { result = he; return true; }
            if (hRaw is not BlankValue)
            {
                var hc = CoerceToNumber(hRaw);
                if (hc is ErrorValue hce) { result = hce; return true; }
                double dh = ((NumberValue)hc).Value;
                if (!double.IsFinite(dh)) { result = ErrorValue.Value; return true; }
                height = (int)Math.Truncate(dh);
            }
        }
        if (offsetNode.Arguments.Count == 5 && offsetNode.Arguments[4] is not OmittedArgumentNode)
        {
            var wRaw = EvaluateNode(offsetNode.Arguments[4], context);
            if (wRaw is ErrorValue we) { result = we; return true; }
            if (wRaw is not BlankValue)
            {
                var wc = CoerceToNumber(wRaw);
                if (wc is ErrorValue wce) { result = wce; return true; }
                double dw = ((NumberValue)wc).Value;
                if (!double.IsFinite(dw)) { result = ErrorValue.Value; return true; }
                width = (int)Math.Truncate(dw);
            }
        }
        if (height <= 0 || width <= 0) { result = ErrorValue.Ref; return true; }

        // Determine the shape of the output array from whichever arg is an array.
        var shapeSource = rowsIsArray ? (RangeValue)rowsRaw : (RangeValue)colsRaw;
        int outRows = shapeSource.RowCount;
        int outCols = shapeSource.ColCount;

        var cells = new ScalarValue[outRows, outCols];
        for (int ri = 0; ri < outRows; ri++)
        {
            for (int ci = 0; ci < outCols; ci++)
            {
                // Extract scalar rows offset for this element.
                ScalarValue rowsElem = rowsIsArray
                    ? ((RangeValue)rowsRaw).Cells[ri, ci]
                    : rowsRaw;
                ScalarValue colsElem = colsIsArray
                    ? ((RangeValue)colsRaw).Cells[ri, ci]
                    : colsRaw;

                if (rowsElem is ErrorValue rowsErr) { cells[ri, ci] = rowsErr; continue; }
                if (colsElem is ErrorValue colsErr) { cells[ri, ci] = colsErr; continue; }

                var rowsCo = CoerceToNumber(rowsElem);
                if (rowsCo is ErrorValue rce) { cells[ri, ci] = rce; continue; }
                var colsCo = CoerceToNumber(colsElem);
                if (colsCo is ErrorValue cce) { cells[ri, ci] = cce; continue; }

                double dRows = ((NumberValue)rowsCo).Value;
                double dCols = ((NumberValue)colsCo).Value;
                if (!double.IsFinite(dRows) || !double.IsFinite(dCols))
                { cells[ri, ci] = ErrorValue.Value; continue; }

                long startRow = (long)baseRow + (long)Math.Truncate(dRows);
                long startCol = (long)baseCol + (long)Math.Truncate(dCols);
                long endRow   = startRow + height - 1;
                long endCol   = startCol + width  - 1;
                if (startRow < 1 || startCol < 1 ||
                    endRow > CellAddress.MaxRow || endCol > CellAddress.MaxCol)
                { cells[ri, ci] = ErrorValue.Ref; continue; }

                var rangeArg = new DirectRangeArgument(
                    baseSheet,
                    (uint)startRow, (uint)startCol,
                    (uint)endRow,   (uint)endCol);

                // Run SUBTOTAL(baseFunc) on this single range element.
                var numeric = new NumericAggregateAccumulator();
                long countA = 0;
                bool errorSeen = false;
                ScalarValue errorVal = BlankValue.Instance;

                for (uint row = rangeArg.StartRow; row <= rangeArg.EndRow && !errorSeen; row++)
                {
                    if (ShouldSkipFastSubtotalRow(context, rangeArg, row, skipHidden)) continue;
                    for (uint col = rangeArg.StartCol; col <= rangeArg.EndCol && !errorSeen; col++)
                    {
                        if (IsFastNestedSubtotalOrAggregateCell(context, rangeArg, row, col)) continue;
                        var v = GetFastRangeCellValue(context, rangeArg, row, col);
                        if (v is ErrorValue ev)
                        {
                            // COUNT (2) ignores error cells; COUNTA (3) counts them as non-blank.
                            if (baseFunc == 2) continue;
                            if (baseFunc == 3) { countA++; continue; }
                            errorSeen = true; errorVal = ev; break;
                        }
                        if (TryDirectRangeNumber(v, out var num, out _))
                        {
                            numeric.Add(num, baseFunc);
                        }
                        if (v is not BlankValue) countA++;
                    }
                }

                cells[ri, ci] = errorSeen ? errorVal : EvaluateSubtotalAggregateNumericResult(baseFunc, numeric, countA);
            }
        }

        result = new RangeValue(cells);
        return true;
    }

    private readonly record struct DirectRangeArgument(
        string? SheetName,
        uint StartRow,
        uint StartCol,
        uint EndRow,
        uint EndCol);

    private enum DirectRangeFastPathState
    {
        Unsupported,
        Success,
        Error
    }

}
