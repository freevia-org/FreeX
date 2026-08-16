using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

public readonly record struct DialogRangeSelectionFormatContext(
    string? SourceSheetName,
    string? ActiveSheetName,
    bool UseR1C1ReferenceStyle);

public static class DialogRangeSelectionFormatter
{
    public static string Format(
        GridRange range,
        DialogRangeSelectionFormat format,
        DialogRangeSelectionFormatContext context) =>
        format switch
        {
            DialogRangeSelectionFormat.StartCell =>
                SpreadsheetDisplayFormatter.FormatCellReference(
                    range.Start,
                    useR1C1ReferenceStyle: false),
            DialogRangeSelectionFormat.AbsoluteRange =>
                FormatAbsoluteRange(range, context.UseR1C1ReferenceStyle),
            DialogRangeSelectionFormat.DataValidationFormula =>
                DataValidationService.FormatListSourceRange(
                    range,
                    context.SourceSheetName,
                    context.ActiveSheetName),
            DialogRangeSelectionFormat.PageSetupPrintArea =>
                PageSetupRangeSelectionFormatter.Format(
                    PageSetupRangeSelectionTarget.PrintArea,
                    range,
                    context.UseR1C1ReferenceStyle),
            DialogRangeSelectionFormat.PageSetupRepeatRows =>
                PageSetupRangeSelectionFormatter.Format(
                    PageSetupRangeSelectionTarget.RepeatRows,
                    range,
                    context.UseR1C1ReferenceStyle),
            DialogRangeSelectionFormat.PageSetupRepeatColumns =>
                PageSetupRangeSelectionFormatter.Format(
                    PageSetupRangeSelectionTarget.RepeatColumns,
                    range,
                    context.UseR1C1ReferenceStyle),
            _ => SpreadsheetDisplayFormatter.FormatRangeReference(
                range.Start,
                range.End,
                useR1C1ReferenceStyle: false),
        };

    /// <summary>
    /// A1-style absolute reference ($B$2:$C$3). R1C1 has no $ notation -- an R1C1 reference is already
    /// absolute unless written in brackets -- so that style falls through to the ordinary formatting.
    /// </summary>
    private static string FormatAbsoluteRange(GridRange range, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? SpreadsheetDisplayFormatter.FormatRangeReference(range.Start, range.End, useR1C1ReferenceStyle: true)
            : range.Start == range.End
                ? FormatAbsoluteCell(range.Start)
                : $"{FormatAbsoluteCell(range.Start)}:{FormatAbsoluteCell(range.End)}";

    private static string FormatAbsoluteCell(CellAddress address) =>
        $"${SpreadsheetDisplayFormatter.FormatColumnReference(address.Col, useR1C1ReferenceStyle: false)}${address.Row}";
}
