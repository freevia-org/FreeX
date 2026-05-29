using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class PageSetupRangeSelectionFormatter
{
    public static string Format(
        PageSetupRangeSelectionTarget target,
        GridRange selectedRange,
        bool useR1C1ReferenceStyle) =>
        target switch
        {
            PageSetupRangeSelectionTarget.RepeatRows => FormatRows(selectedRange, useR1C1ReferenceStyle),
            PageSetupRangeSelectionTarget.RepeatColumns => FormatColumns(selectedRange, useR1C1ReferenceStyle),
            _ => FormatCells(selectedRange, useR1C1ReferenceStyle)
        };

    private static string FormatRows(GridRange selectedRange, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? $"R{selectedRange.Start.Row}:R{selectedRange.End.Row}"
            : $"${selectedRange.Start.Row}:${selectedRange.End.Row}";

    private static string FormatColumns(GridRange selectedRange, bool useR1C1ReferenceStyle)
    {
        var start = FormatColumn(selectedRange.Start.Col, useR1C1ReferenceStyle);
        var end = FormatColumn(selectedRange.End.Col, useR1C1ReferenceStyle);
        return $"{start}:{end}";
    }

    private static string FormatColumn(uint column, bool useR1C1ReferenceStyle)
    {
        var reference = SpreadsheetDisplayFormatter.FormatColumnReference(column, useR1C1ReferenceStyle);
        return useR1C1ReferenceStyle ? reference : $"${reference}";
    }

    private static string FormatCells(GridRange selectedRange, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? SpreadsheetDisplayFormatter.FormatRangeReference(selectedRange.Start, selectedRange.End, useR1C1ReferenceStyle)
            : FormatAbsoluteRange(selectedRange);

    private static string FormatAbsoluteRange(GridRange selectedRange)
    {
        var start = FormatAbsoluteCell(selectedRange.Start);
        var end = FormatAbsoluteCell(selectedRange.End);
        return selectedRange.Start == selectedRange.End ? start : $"{start}:{end}";
    }

    private static string FormatAbsoluteCell(CellAddress address) =>
        $"${CellAddress.NumberToColumnName(address.Col)}${address.Row}";
}
