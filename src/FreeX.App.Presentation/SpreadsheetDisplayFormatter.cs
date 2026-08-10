using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public enum SpreadsheetScalarFormatProfile
{
    CellDisplay,
    InvariantScalar,
    InvariantContent,
    DefinedNameLabel
}

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

    public static string FormatRangeReference(GridRange range, bool useR1C1ReferenceStyle) =>
        FormatRangeReference(range.Start, range.End, useR1C1ReferenceStyle);

    public static string FormatFormulaBarText(Cell? cell, CellAddress address, bool useR1C1ReferenceStyle) =>
        FormatFormulaBarText(cell, address, useR1C1ReferenceStyle, sheet: null, workbook: null);

    /// <summary>
    /// Formats the formula-bar text for a cell, honoring Excel's "Hidden" protection option: when the
    /// containing <paramref name="sheet"/> is protected and the cell's effective style has
    /// <see cref="CellStyle.Hidden"/> set, the formula text is suppressed and only the computed value
    /// is shown (matching Excel's Format Cells &gt; Protection &gt; Hidden behavior).
    /// </summary>
    public static string FormatFormulaBarText(
        Cell? cell,
        CellAddress address,
        bool useR1C1ReferenceStyle,
        Sheet? sheet,
        Workbook? workbook)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
        {
            if (sheet is { IsProtected: true } && workbook is not null && IsHidden(cell, address, sheet, workbook))
                return FormatCellValue(cell.Value);

            var formula = useR1C1ReferenceStyle
                ? FormulaReferenceStyleService.ToR1C1(cell.FormulaText, address)
                : cell.FormulaText;
            return "=" + formula;
        }

        return FormatCellValue(cell?.Value);
    }

    private static bool IsHidden(Cell cell, CellAddress address, Sheet sheet, Workbook workbook)
    {
        var styleId = cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        return workbook.GetStyle(styleId).Hidden;
    }

    public static string FormatCellValue(ScalarValue? value) =>
        FormatScalarValue(value, SpreadsheetScalarFormatProfile.CellDisplay);

    public static string FormatScalarValue(
        ScalarValue? value,
        SpreadsheetScalarFormatProfile profile = SpreadsheetScalarFormatProfile.CellDisplay) =>
        value switch
        {
            null or BlankValue => "",
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TextValue text => text.Value,
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => FormatDateTimeValue(dateTime, profile),
            ErrorValue error => profile == SpreadsheetScalarFormatProfile.DefinedNameLabel ? "" : error.Code,
            RangeValue range when profile == SpreadsheetScalarFormatProfile.InvariantContent =>
                FormatRangeValue(range, profile),
            _ when profile == SpreadsheetScalarFormatProfile.InvariantContent => value.ToString() ?? "",
            _ => ""
        };

    private static string FormatDateTimeValue(
        DateTimeValue value,
        SpreadsheetScalarFormatProfile profile) =>
        profile switch
        {
            SpreadsheetScalarFormatProfile.CellDisplay => FormatDateTimeCellValue(value),
            SpreadsheetScalarFormatProfile.InvariantScalar or SpreadsheetScalarFormatProfile.InvariantContent =>
                value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => ""
        };

    private static string FormatRangeValue(
        RangeValue range,
        SpreadsheetScalarFormatProfile profile)
    {
        var rowTexts = new List<string>(range.RowCount);
        for (var row = 1; row <= range.RowCount; row++)
        {
            var cellTexts = new List<string>(range.ColCount);
            for (var col = 1; col <= range.ColCount; col++)
                cellTexts.Add(FormatScalarValue(range.At(row, col), profile));
            rowTexts.Add(string.Join(",", cellTexts));
        }

        return "{" + string.Join(";", rowTexts) + "}";
    }

    private static string FormatDateTimeCellValue(DateTimeValue value)
    {
        try { return value.ToDateTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static string FormatA1CellReference(CellAddress address) => address.ToA1();

    private static string FormatA1RangeReference(CellAddress start, CellAddress end) =>
        $"{start.ToA1()}:{end.ToA1()}";

    private static string FormatColumnName(uint column) => CellAddress.NumberToColumnName(column);

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

    private static int WriteR1C1CellReference(Span<char> span, CellAddress address)
    {
        span[0] = 'R';
        var offset = 1 + WriteUInt32(span[1..], address.Row);
        span[offset++] = 'C';
        return offset + WriteUInt32(span[offset..], address.Col);
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
