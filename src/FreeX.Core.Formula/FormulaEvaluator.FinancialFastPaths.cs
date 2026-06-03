using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
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

            var yearFraction = (ExcelDateSystem.SerialToDate(dateSerial) - firstDate).TotalDays / 365.0;
            result += cashFlow / Math.Pow(1 + rate, yearFraction);
        }

        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
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
