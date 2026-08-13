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
}
