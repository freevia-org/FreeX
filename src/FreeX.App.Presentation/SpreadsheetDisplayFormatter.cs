using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

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
        Workbook? workbook,
        CultureInfo? culture = null)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
        {
            if (sheet is { IsProtected: true } && workbook is not null && IsHidden(cell, address, sheet, workbook))
                return FormatFormulaBarValue(cell.Value, cell, address, sheet, workbook);

            var formula = useR1C1ReferenceStyle
                ? FormulaReferenceStyleService.ToR1C1(cell.FormulaText, address)
                : cell.FormulaText;
            return "=" + formula;
        }

        if (TryFormatBuiltInShortDateForFormulaBar(cell, address, sheet, workbook, culture, out var dateText))
            return dateText;

        return FormatFormulaBarValue(cell?.Value, cell, address, sheet, workbook);
    }

    /// <summary>
    /// R162-formulabar-spill-readback: resolves the <see cref="Cell"/> to hand to
    /// <see cref="FormatFormulaBarText(Cell?, CellAddress, bool, Sheet?, Workbook?)"/> for
    /// <paramref name="address"/>. <c>Sheet.GetCell</c> returns null for a non-anchor dynamic-array
    /// spill member (its value lives only in the spill overlay, never in cell storage), so passing
    /// that null straight through makes the formula bar go blank for a cell the grid is visibly
    /// painting a value into (the grid reads via <c>Sheet.GetValue</c>, which does consult the
    /// overlay). When <paramref name="cell"/> is null, this falls back to a value-only
    /// <see cref="Cell"/> synthesized from <c>Sheet.GetValue</c>, so the formula bar shows the
    /// spilled value (matching Excel) instead of nothing. For a genuinely blank address this
    /// synthesizes a cell wrapping <see cref="BlankValue"/>, which formats identically to the null
    /// it replaces, so ordinary blank cells are unaffected.
    /// Shared by both shells (WPF host and Avalonia) so the spill-member formula-bar resolution rule
    /// exists in exactly one place -- callers that also need the raw, possibly-null cell for
    /// style/alignment/reading-order must keep using the original cell, since a spill member has no
    /// style/formula of its own to borrow from a synthetic cell.
    /// </summary>
    public static Cell? ResolveFormulaBarDisplayCell(Sheet? sheet, Cell? cell, CellAddress address) =>
        cell ?? (sheet is null ? null : Cell.FromValue(sheet.GetValue(address)));

    private static bool IsHidden(Cell cell, CellAddress address, Sheet sheet, Workbook workbook)
    {
        return GetEffectiveStyle(cell, address, sheet, workbook).Hidden;
    }

    /// <summary>
    /// Excel displays a literal percentage in the formula bar rather than the stored decimal
    /// value: a cell containing <c>0.1234</c> with a percentage number format reads
    /// <c>12.34%</c>. This affects only non-formula numeric values; formula text remains editable
    /// formula text, while General and date/time readback retain their established behavior.
    /// </summary>
    private static string FormatFormulaBarValue(
        ScalarValue? value,
        Cell? cell,
        CellAddress address,
        Sheet? sheet,
        Workbook? workbook)
    {
        if (value is NumberValue number &&
            cell is not null &&
            sheet is not null &&
            workbook is not null &&
            IsPercentageNumberFormat(GetEffectiveStyle(cell, address, sheet, workbook).NumberFormat))
        {
            // Do not use the cell's display code here. Ctrl+Shift+5 applies "0%", which rounds
            // the grid display to 12%, but Excel preserves the underlying percentage precision in
            // the formula bar (12.34%). Formatting the scaled value as General gives that editable
            // Excel-style text without changing the stored NumberValue.
            return FormatCellValue(new NumberValue(number.Value * 100d)) + "%";
        }

        return FormatCellValue(value);
    }

    private static CellStyle GetEffectiveStyle(Cell cell, CellAddress address, Sheet sheet, Workbook workbook)
    {
        var styleId = cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private static bool IsPercentageNumberFormat(string format)
    {
        var inQuote = false;
        for (var index = 0; index < format.Length; index++)
        {
            var character = format[index];
            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (character == '\\' && index + 1 < format.Length)
            {
                index++;
                continue;
            }

            if (!inQuote && character == '%')
                return true;
        }

        return false;
    }

    /// <summary>
    /// The formula bar exposes a Ctrl+;-entered built-in short date using the user's short-date
    /// pattern, rather than the invariant ISO diagnostic text used for unformatted DateTimeValue
    /// literals. Excel stores that entry as a numeric serial with built-in numFmtId 14, so both
    /// the style and the serial are required before taking this display path.
    /// </summary>
    private static bool TryFormatBuiltInShortDateForFormulaBar(
        Cell? cell,
        CellAddress address,
        Sheet? sheet,
        Workbook? workbook,
        CultureInfo? culture,
        out string text)
    {
        text = "";
        if (cell?.Value is not NumberValue number || sheet is null || workbook is null)
            return false;

        var styleId = cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        if (!string.Equals(
                workbook.GetStyle(styleId).NumberFormat,
                DateTimeEntryService.CurrentDateNumberFormat,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!new DateTimeValue(number.Value).TryToDateTime(out var date))
            return false;

        text = date.ToString("d", culture ?? CultureInfo.CurrentCulture);
        return true;
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
