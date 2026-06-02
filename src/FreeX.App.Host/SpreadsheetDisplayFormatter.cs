using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SpreadsheetDisplayFormatter
{
    public static string FormatCellReference(CellAddress address, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? FormatR1C1CellReference(address)
            : FormatA1CellReference(address);

    public static string FormatColumnReference(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? FormatR1C1ColumnReference(column)
            : FormatColumnName(column);

    public static string FormatRangeReference(CellAddress start, CellAddress end, bool useR1C1ReferenceStyle) =>
        start == end
            ? FormatCellReference(start, useR1C1ReferenceStyle)
            : useR1C1ReferenceStyle
                ? FormatR1C1RangeReference(start, end)
                : FormatA1RangeReference(start, end);

    public static string FormatFormulaBarText(Cell? cell, CellAddress address, bool useR1C1ReferenceStyle)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
        {
            var formula = useR1C1ReferenceStyle
                ? FormulaReferenceStyleService.ToR1C1(cell.FormulaText, address)
                : cell.FormulaText;
            return "=" + formula;
        }

        return FormatCellValue(cell?.Value);
    }

    public static string FormatCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => FormatDateTimeCellValue(dt),
        ErrorValue err => err.Code,
        _ => ""
    };

    private static string FormatDateTimeCellValue(DateTimeValue value)
    {
        try { return value.ToDateTime().ToString("yyyy-MM-dd"); }
        catch { return value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static string FormatA1CellReference(CellAddress address)
    {
        var columnLength = CountColumnNameChars(address.Col);
        var rowLength = CountDecimalDigits(address.Row);
        return string.Create(columnLength + rowLength, address, static (span, address) =>
        {
            var columnLength = CountColumnNameChars(address.Col);
            WriteColumnName(span[..columnLength], address.Col);
            WriteUInt32(span[columnLength..], address.Row);
        });
    }

    private static string FormatA1RangeReference(CellAddress start, CellAddress end)
    {
        var startColumnLength = CountColumnNameChars(start.Col);
        var startRowLength = CountDecimalDigits(start.Row);
        var endColumnLength = CountColumnNameChars(end.Col);
        var endRowLength = CountDecimalDigits(end.Row);
        return string.Create(
            startColumnLength + startRowLength + 1 + endColumnLength + endRowLength,
            (start, end),
            static (span, state) =>
            {
                var offset = WriteA1CellReference(span, state.start);
                span[offset++] = ':';
                WriteA1CellReference(span[offset..], state.end);
            });
    }

    private static string FormatColumnName(uint column)
    {
        var length = CountColumnNameChars(column);
        return string.Create(length, column, static (span, column) => WriteColumnName(span, column));
    }

    private static string FormatR1C1CellReference(CellAddress address)
    {
        var rowLength = CountDecimalDigits(address.Row);
        var columnLength = CountDecimalDigits(address.Col);
        return string.Create(rowLength + columnLength + 2, address, static (span, address) =>
        {
            span[0] = 'R';
            var offset = 1 + WriteUInt32(span[1..], address.Row);
            span[offset++] = 'C';
            WriteUInt32(span[offset..], address.Col);
        });
    }

    private static string FormatR1C1ColumnReference(uint column)
    {
        var columnLength = CountDecimalDigits(column);
        return string.Create(columnLength + 1, column, static (span, column) =>
        {
            span[0] = 'C';
            WriteUInt32(span[1..], column);
        });
    }

    private static string FormatR1C1RangeReference(CellAddress start, CellAddress end)
    {
        var startLength = CountDecimalDigits(start.Row) + CountDecimalDigits(start.Col) + 2;
        var endLength = CountDecimalDigits(end.Row) + CountDecimalDigits(end.Col) + 2;
        return string.Create(startLength + 1 + endLength, (start, end), static (span, state) =>
        {
            var offset = WriteR1C1CellReference(span, state.start);
            span[offset++] = ':';
            WriteR1C1CellReference(span[offset..], state.end);
        });
    }

    private static int WriteA1CellReference(Span<char> span, CellAddress address)
    {
        var columnLength = CountColumnNameChars(address.Col);
        WriteColumnName(span[..columnLength], address.Col);
        return columnLength + WriteUInt32(span[columnLength..], address.Row);
    }

    private static int WriteR1C1CellReference(Span<char> span, CellAddress address)
    {
        span[0] = 'R';
        var offset = 1 + WriteUInt32(span[1..], address.Row);
        span[offset++] = 'C';
        return offset + WriteUInt32(span[offset..], address.Col);
    }

    private static void WriteColumnName(Span<char> span, uint column)
    {
        for (var i = span.Length - 1; i >= 0; i--)
        {
            column--;
            span[i] = (char)('A' + column % 26);
            column /= 26;
        }
    }

    private static int WriteUInt32(Span<char> span, uint value)
    {
        value.TryFormat(
            span,
            out var charsWritten,
            format: default,
            provider: System.Globalization.CultureInfo.InvariantCulture);
        return charsWritten;
    }

    private static int CountColumnNameChars(uint column)
    {
        var length = 0;
        do
        {
            length++;
            column = (column - 1) / 26;
        }
        while (column > 0);

        return length;
    }

    private static int CountDecimalDigits(uint value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }
}
