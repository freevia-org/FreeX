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

        var numeric = new DirectRangeNumericAccumulator();
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
            return false;

        var ignoreErrors = options is 2 or 3 or 6 or 7;
        var ignoreHiddenRows = options is 1 or 3 or 5 or 7;
        var ignoreNestedAggregates = options <= 3;
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

        var numeric = new DirectRangeNumericAccumulator();
        long countA = 0;

        foreach (var range in ranges)
        {
            for (var row = range.StartRow; row <= range.EndRow; row++)
            {
                if (ignoreHiddenRows && IsFastAggregateRowHidden(context, range, row))
                    continue;

                for (var col = range.StartCol; col <= range.EndCol; col++)
                {
                    if (ignoreNestedAggregates && IsFastNestedSubtotalOrAggregateCell(context, range, row, col))
                        continue;

                    var value = GetFastRangeCellValue(context, range, row, col);
                    if (value is ErrorValue error)
                    {
                        if (ignoreErrors)
                            continue;

                        result = error;
                        return true;
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
        }

        result = funcNum == 3
            ? new NumberValue(countA)
            : EvaluateSubtotalAggregateNumericResult(funcNum, numeric, countA: 0);
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
                value = new NumberValue(number.Value);
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

            var resolved = context.TryResolveNamedRange(named.Name);
            if (resolved is null)
            {
                range = default;
                result = ErrorValue.Name;
                return DirectRangeFastPathState.Error;
            }

            var gridRange = resolved.Value;
            return TryCreateDirectRangeArgument(
                context.TryGetSheetName(gridRange.Start.Sheet),
                gridRange.Start.Row,
                gridRange.Start.Col,
                gridRange.End.Row,
                gridRange.End.Col,
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
        DirectRangeNumericAccumulator numeric,
        long countA)
    {
        return functionNumber switch
        {
            1 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(numeric.Average),
            2 => new NumberValue(numeric.Count),
            3 => new NumberValue(countA),
            4 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(numeric.Max),
            5 => numeric.Count == 0 ? ErrorValue.DivByZero : FastNumberResult(numeric.Min),
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
        double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

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

        return IsFastSubtotalOrAggregateFormula(cell?.FormulaText);
    }

    private static bool IsFastSubtotalOrAggregateFormula(string? formulaText)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        var text = formulaText.TrimStart();
        if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].TrimStart();

        return text.StartsWith("SUBTOTAL(", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("AGGREGATE(", StringComparison.OrdinalIgnoreCase);
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

    private struct DirectRangeNumericAccumulator
    {
        private double _varianceMean;

        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Product { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }
        public double VarianceM2 { get; private set; }
        public double Average => Sum / Count;
        public double SampleVariance => VarianceM2 / (Count - 1);
        public double PopulationVariance => VarianceM2 / Count;

        public void Add(double value, int functionNumber)
        {
            Count++;
            switch (functionNumber)
            {
                case 1:
                case 9:
                    Sum += value;
                    break;
                case 4:
                    Max = Count == 1 ? value : Math.Max(Max, value);
                    break;
                case 5:
                    Min = Count == 1 ? value : Math.Min(Min, value);
                    break;
                case 6:
                    Product = Count == 1 ? value : Product * value;
                    break;
                case 7:
                case 8:
                case 10:
                case 11:
                    var delta = value - _varianceMean;
                    _varianceMean += delta / Count;
                    VarianceM2 += delta * (value - _varianceMean);
                    break;
            }
        }
    }
}
