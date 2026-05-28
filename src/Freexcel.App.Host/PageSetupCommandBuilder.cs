using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class PageSetupCommandBuilder
{
    public static IWorkbookCommand Build(SheetId sheetId, PageSetupDialog dialog) =>
        new CompositeWorkbookCommand(
            "Page Setup",
            [
                CreatePrintAreaCommand(sheetId, dialog.PrintArea),
                new SetPageSetupCommand(
                    sheetId,
                    dialog.Orientation,
                    dialog.PaperSize,
                    dialog.Margins,
                    dialog.PrintGridlines,
                    dialog.PrintHeadings,
                    dialog.ScaleToFit,
                    dialog.PrintTitleRows,
                    dialog.PrintTitleColumns,
                    dialog.CenterHorizontally,
                    dialog.CenterVertically,
                    dialog.PageOrder,
                    dialog.FirstPageNumber,
                    dialog.HeaderMargin,
                    dialog.FooterMargin,
                    dialog.PrintBlackAndWhite,
                    dialog.PrintDraftQuality,
                    dialog.PrintQualityDpi,
                    dialog.PrintErrorValue,
                    dialog.PrintComments),
                new SetHeaderFooterCommand(
                    sheetId,
                    dialog.Header,
                    dialog.Footer,
                    dialog.FirstPageHeader,
                    dialog.FirstPageFooter,
                    dialog.EvenPageHeader,
                    dialog.EvenPageFooter,
                    dialog.DifferentFirstPage,
                    dialog.DifferentOddEvenPages,
                    dialog.ScaleHeaderFooterWithDocument,
                    dialog.AlignHeaderFooterWithMargins,
                    dialog.HeaderPictures,
                    dialog.FooterPictures,
                    dialog.FirstPageHeaderPictures,
                    dialog.FirstPageFooterPictures,
                    dialog.EvenPageHeaderPictures,
                    dialog.EvenPageFooterPictures)
            ]);

    public static IWorkbookCommand CreatePrintAreaCommand(SheetId sheetId, GridRange? printArea) =>
        printArea is { } range
            ? new SetPrintAreaCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))
            : new ClearPrintAreaCommand(sheetId);
}
