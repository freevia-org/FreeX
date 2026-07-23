using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private bool TryEvaluateNpvDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count < 2 ||
            TryAsRangeRef(node.Arguments[0], out _) ||
            !IsNpvDirectScalarArgument(node.Arguments[0]))
            return false;

        var hasDirectRange = false;
        for (var i = 1; i < node.Arguments.Count; i++)
        {
            var argument = node.Arguments[i];
            if (TryAsRangeRef(argument, out var range))
            {
                hasDirectRange = true;
                if (TryDirectRangeExceedsMaterializationLimit(range, context, out var rangeError))
                {
                    result = rangeError;
                    return true;
                }

                continue;
            }

            if (!IsNpvDirectScalarArgument(argument))
                return false;
        }

        if (!hasDirectRange)
            return false;

        var rateValue = EvaluateNode(node.Arguments[0], context);
        if (rateValue is RangeValue)
            return false;
        if (rateValue is ErrorValue rateError)
        {
            result = rateError;
            return true;
        }

        if (!TryCoerceToNumberValue(rateValue, out var rate))
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!double.IsFinite(rate))
        {
            result = ErrorValue.Num;
            return true;
        }

        double total = 0;
        var valueIndex = 0;
        for (var i = 1; i < node.Arguments.Count; i++)
        {
            var argument = node.Arguments[i];
            if (TryAsRangeRef(argument, out var range))
            {
                if (!TryCreateDirectRangeCursor(range, context, out var cursor, out result))
                    return true;

                var rangeResult = AccumulateNpvDirectRange(ref cursor, context, rate, ref valueIndex, ref total);
                if (rangeResult is not null)
                {
                    result = rangeResult;
                    return true;
                }

                continue;
            }

            var scalarResult = AccumulateNpvDirectArgument(argument, context, rate, ref valueIndex, ref total);
            if (scalarResult is not null)
            {
                result = scalarResult;
                return true;
            }
        }

        result = double.IsFinite(total) ? new NumberValue(total) : ErrorValue.Num;
        return true;
    }

    private static bool IsNpvDirectScalarArgument(FormulaNode argument) =>
        argument is CellRefNode or NumberNode or StringNode or BooleanNode or ErrorNode or OmittedArgumentNode;

    private bool TryEvaluateXnpvDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 3)
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _) ||
            !TryAsRangeRef(node.Arguments[1], out var valueRange) ||
            !TryAsRangeRef(node.Arguments[2], out var dateRange))
            return false;

        var rateValue = EvaluateNode(node.Arguments[0], context);
        if (rateValue is RangeValue)
            return false;
        if (rateValue is ErrorValue rateError)
        {
            result = rateError;
            return true;
        }

        if (!TryCoerceToNumberValue(rateValue, out var rate))
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!double.IsFinite(rate) || rate <= -1)
        {
            result = ErrorValue.Num;
            return true;
        }

        if (!TryCreateDirectRangeCursor(valueRange, context, out var values, out result) ||
            !TryCreateDirectRangeCursor(dateRange, context, out var dates, out result))
            return true;

        if (TryFindDirectRangeError(values, context, out var rangeError) ||
            TryFindDirectRangeError(dates, context, out rangeError))
        {
            result = rangeError;
            return true;
        }

        try
        {
            result = EvaluateXnpvDirectRanges(rate, values, dates, context);
        }
        catch (ArgumentOutOfRangeException)
        {
            result = ErrorValue.Num;
        }
        catch (OverflowException)
        {
            result = ErrorValue.Num;
        }

        return true;
    }

    private bool TryEvaluateIrrDirectRange(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 1 or > 2 ||
            !TryAsRangeRef(node.Arguments[0], out var valueRange))
            return false;

        if (node.Arguments.Count == 2 && TryAsRangeRef(node.Arguments[1], out _))
            return false;

        double guess = 0.1;
        if (node.Arguments.Count == 2)
        {
            var guessValue = EvaluateNode(node.Arguments[1], context);
            if (guessValue is RangeValue)
                return false;
            if (guessValue is ErrorValue guessError)
            {
                result = guessError;
                return true;
            }

            // An explicitly-supplied guess argument that evaluates to blank (e.g. a reference to
            // an empty cell) must coerce to 0.0, not silently keep the omitted-argument default
            // of 0.1 -- TryCoerceToNumberValue already maps BlankValue to 0.0/true, so it must
            // run unconditionally here rather than being skipped for BlankValue (mirrors the
            // slow-path Irr fix in BuiltInFunctions.Financial.CashFlow.cs).
            if (!TryCoerceToNumberValue(guessValue, out guess))
            {
                result = ErrorValue.Value;
                return true;
            }
        }

        if (!double.IsFinite(guess) || guess <= -1)
        {
            result = ErrorValue.Num;
            return true;
        }

        if (!TryCreateDirectRangeCursor(valueRange, context, out var values, out result))
            return true;

        if (!TryCollectDirectRangeNumbers(values, context, out var cashflows, out var rangeError))
        {
            result = rangeError ?? ErrorValue.Num;
            return true;
        }

        result = BuiltInFunctions.IrrCashFlows(cashflows, guess);
        return true;
    }

    private static ScalarValue EvaluateXnpvDirectRanges(
        double rate,
        DirectRangeCursor values,
        DirectRangeCursor dates,
        IEvalContext context)
    {
        if (!TryReadNextDirectRangeNumber(ref values, context, out var firstCashFlow, out var valueError))
            return valueError ?? ErrorValue.Num;
        if (!TryReadNextDirectRangeNumber(ref dates, context, out var firstDateSerial, out var dateError))
            return dateError ?? ErrorValue.Num;

        var firstDate = ExcelDateSystem.SerialToDate(firstDateSerial);
        double result = firstCashFlow;

        while (true)
        {
            var hasValue = TryReadNextDirectRangeNumber(ref values, context, out var cashFlow, out valueError);
            if (valueError is not null)
                return valueError;

            var hasDate = TryReadNextDirectRangeNumber(ref dates, context, out var dateSerial, out dateError);
            if (dateError is not null)
                return dateError;

            if (!hasValue && !hasDate)
                break;
            if (hasValue != hasDate)
                return ErrorValue.Num;

            // Excel's XNPV returns #NUM! when any date precedes the first (anchor) date
            // (mirrors the slow-path guard in BuiltInFunctions.Financial.CashFlow.cs,
            // R32-formula-financial-remaining-1).
            if (dateSerial < firstDateSerial)
                return ErrorValue.Num;

            var yearFraction = (ExcelDateSystem.SerialToDate(dateSerial) - firstDate).TotalDays / 365.0;
            result += cashFlow / Math.Pow(1 + rate, yearFraction);
        }

        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ErrorValue? AccumulateNpvDirectRange(
        ref DirectRangeCursor cursor,
        IEvalContext context,
        double rate,
        ref int valueIndex,
        ref double total)
    {
        while (true)
        {
            var hasNumber = TryReadNextDirectRangeNumber(ref cursor, context, out var value, out var error);
            if (error is not null)
                return error;
            if (!hasNumber)
                return null;

            valueIndex++;
            var denom = Math.Pow(1 + rate, valueIndex);
            // rate == -1 zeroes every discount factor: Excel's plain x/0 #DIV/0! propagation,
            // matching the slow-path Npv in BuiltInFunctions.Financial.CashFlow.cs.
            if (denom == 0) return ErrorValue.DivByZero;
            total += value / denom;
        }
    }

    private static ErrorValue? AccumulateNpvDirectArgument(
        FormulaNode argument,
        IEvalContext context,
        double rate,
        ref int valueIndex,
        ref double total)
    {
        if (!TryGetNpvDirectArgumentNumber(argument, context, out var value, out var hasNumber, out var error))
            return error;

        if (hasNumber)
        {
            valueIndex++;
            var denom = Math.Pow(1 + rate, valueIndex);
            // rate == -1 zeroes every discount factor: Excel's plain x/0 #DIV/0! propagation,
            // matching the slow-path Npv in BuiltInFunctions.Financial.CashFlow.cs.
            if (denom == 0) return ErrorValue.DivByZero;
            total += value / denom;
        }

        return null;
    }

    private static bool TryGetNpvDirectArgumentNumber(
        FormulaNode argument,
        IEvalContext context,
        out double number,
        out bool hasNumber,
        out ErrorValue? error)
    {
        number = 0;
        hasNumber = false;
        error = null;

        switch (argument)
        {
            case NumberNode n:
                number = n.Value;
                hasNumber = true;
                return true;
            case BooleanNode b:
                number = b.Value ? 1.0 : 0.0;
                hasNumber = true;
                return true;
            case StringNode s:
                if (!ExcelTextNumberParser.TryParse(s.Value, out number))
                {
                    error = ErrorValue.Value;
                    return false;
                }

                hasNumber = true;
                return true;
            case ErrorNode e:
                error = e.Error;
                return false;
            case OmittedArgumentNode:
                return true;
            case CellRefNode cell:
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    error = ErrorValue.Ref;
                    return false;
                }

                var cellValue = cell.SheetName is null
                    ? context.GetCellValue(cell.Row, cell.ColumnNumber)
                    : context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber);
                if (TryDirectRangeNumber(cellValue, out number, out error))
                    hasNumber = true;
                return error is null;
            default:
                return true;
        }
    }

    private static bool TryCreateDirectRangeCursor(
        RangeRefNode rawRange,
        IEvalContext context,
        out DirectRangeCursor cursor,
        out ScalarValue error)
    {
        cursor = default;
        error = BlankValue.Instance;

        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
        {
            error = ErrorValue.Ref;
            return false;
        }

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        uint startRow = Math.Min(range.Start.Row, range.End.Row);
        uint endRow = Math.Max(range.Start.Row, range.End.Row);
        uint startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);

        if (FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol) >
            FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            error = ErrorValue.Ref;
            return false;
        }

        cursor = new DirectRangeCursor(range.SheetName, startRow, endRow, startCol, endCol);
        return true;
    }

    private static bool TryDirectRangeExceedsMaterializationLimit(
        RangeRefNode rawRange,
        IEvalContext context,
        out ErrorValue error)
    {
        error = ErrorValue.Ref;
        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
            return false;

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        uint startRow = Math.Min(range.Start.Row, range.End.Row);
        uint endRow = Math.Max(range.Start.Row, range.End.Row);
        uint startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);

        return FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol) >
            FormulaSafetyLimits.MaxMaterializedRangeCells;
    }

    private static bool TryFindDirectRangeError(
        DirectRangeCursor cursor,
        IEvalContext context,
        out ErrorValue error)
    {
        for (var row = cursor.StartRow; row <= cursor.EndRow; row++)
        {
            for (var col = cursor.StartCol; col <= cursor.EndCol; col++)
            {
                var value = cursor.SheetName is null
                    ? context.GetCellValue(row, col)
                    : context.GetCellValue(cursor.SheetName, row, col);
                if (value is ErrorValue cellError)
                {
                    error = cellError;
                    return true;
                }
            }
        }

        error = ErrorValue.Value;
        return false;
    }

    private static bool TryCollectDirectRangeNumbers(
        DirectRangeCursor cursor,
        IEvalContext context,
        out List<double> numbers,
        out ErrorValue? error)
    {
        var count = 0;
        for (var row = cursor.StartRow; row <= cursor.EndRow; row++)
        {
            for (var col = cursor.StartCol; col <= cursor.EndCol; col++)
            {
                var value = cursor.SheetName is null
                    ? context.GetCellValue(row, col)
                    : context.GetCellValue(cursor.SheetName, row, col);
                if (value is ErrorValue cellError)
                {
                    numbers = [];
                    error = cellError;
                    return false;
                }

                if (value is NumberValue or DateTimeValue)
                    count++;
            }
        }

        numbers = new List<double>(count);
        for (var row = cursor.StartRow; row <= cursor.EndRow; row++)
        {
            for (var col = cursor.StartCol; col <= cursor.EndCol; col++)
            {
                var value = cursor.SheetName is null
                    ? context.GetCellValue(row, col)
                    : context.GetCellValue(cursor.SheetName, row, col);
                if (value is NumberValue n)
                    numbers.Add(n.Value);
                else if (value is DateTimeValue d)
                    numbers.Add(d.Value);
            }
        }

        error = null;
        return true;
    }

    private static bool TryReadNextDirectRangeNumber(
        ref DirectRangeCursor cursor,
        IEvalContext context,
        out double number,
        out ErrorValue? error)
    {
        while (cursor.Row <= cursor.EndRow)
        {
            while (cursor.Col <= cursor.EndCol)
            {
                var value = cursor.SheetName is null
                    ? context.GetCellValue(cursor.Row, cursor.Col)
                    : context.GetCellValue(cursor.SheetName, cursor.Row, cursor.Col);
                cursor.Col++;

                switch (value)
                {
                    case ErrorValue cellError:
                        number = 0;
                        error = cellError;
                        return false;
                    case NumberValue n:
                        number = n.Value;
                        error = null;
                        return true;
                    case DateTimeValue d:
                        number = d.Value;
                        error = null;
                        return true;
                }
            }

            cursor.Row++;
            cursor.Col = cursor.StartCol;
        }

        number = 0;
        error = null;
        return false;
    }

    private struct DirectRangeCursor
    {
        public DirectRangeCursor(string? sheetName, uint startRow, uint endRow, uint startCol, uint endCol)
        {
            SheetName = sheetName;
            StartRow = startRow;
            EndRow = endRow;
            StartCol = startCol;
            EndCol = endCol;
            Row = startRow;
            Col = startCol;
        }

        public string? SheetName { get; }
        public uint StartRow { get; }
        public uint EndRow { get; }
        public uint StartCol { get; }
        public uint EndCol { get; }
        public uint Row { get; set; }
        public uint Col { get; set; }
    }
}
