using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class PageSetupRangeSelectionFormatter
{
    public static string Format(
        PageSetupRangeSelectionTarget target,
        GridRange selectedRange,
        bool useR1C1ReferenceStyle) =>
        target switch
        {
            PageSetupRangeSelectionTarget.RepeatRows => useR1C1ReferenceStyle
                ? $"R{selectedRange.Start.Row}:R{selectedRange.End.Row}"
                : $"${selectedRange.Start.Row}:${selectedRange.End.Row}",
            PageSetupRangeSelectionTarget.RepeatColumns =>
                useR1C1ReferenceStyle
                    ? $"{SpreadsheetDisplayFormatter.FormatColumnReference(selectedRange.Start.Col, useR1C1ReferenceStyle)}:{SpreadsheetDisplayFormatter.FormatColumnReference(selectedRange.End.Col, useR1C1ReferenceStyle)}"
                    : $"${SpreadsheetDisplayFormatter.FormatColumnReference(selectedRange.Start.Col, useR1C1ReferenceStyle)}:${SpreadsheetDisplayFormatter.FormatColumnReference(selectedRange.End.Col, useR1C1ReferenceStyle)}",
            _ => SpreadsheetDisplayFormatter.FormatRangeReference(selectedRange.Start, selectedRange.End, useR1C1ReferenceStyle)
        };
}
