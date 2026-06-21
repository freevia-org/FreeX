using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

internal static class PageSetupCommandBuilder
{
    public static IWorkbookCommand Build(SheetId sheetId, PageSetupDialog dialog) =>
        PageSetupCommandFactory.Build(sheetId, CreateRequest(dialog)).ToComposite();

    private static PageSetupCommandRequest CreateRequest(PageSetupDialog dialog) =>
        new()
        {
            PrintArea = dialog.PrintArea,
            Orientation = dialog.Orientation,
            PaperSize = dialog.PaperSize,
            Margins = dialog.Margins,
            PrintGridlines = dialog.PrintGridlines,
            PrintHeadings = dialog.PrintHeadings,
            ScaleToFit = dialog.ScaleToFit,
            PrintTitleRows = dialog.PrintTitleRows,
            PrintTitleColumns = dialog.PrintTitleColumns,
            CenterHorizontally = dialog.CenterHorizontally,
            CenterVertically = dialog.CenterVertically,
            PageOrder = dialog.PageOrder,
            FirstPageNumber = dialog.FirstPageNumber,
            HeaderMargin = dialog.HeaderMargin,
            FooterMargin = dialog.FooterMargin,
            PrintBlackAndWhite = dialog.PrintBlackAndWhite,
            PrintDraftQuality = dialog.PrintDraftQuality,
            PrintQualityDpi = dialog.PrintQualityDpi,
            PrintErrorValue = dialog.PrintErrorValue,
            PrintComments = dialog.PrintComments,
            HeaderFooter = new PageSetupHeaderFooterRequest
            {
                Header = dialog.Header,
                Footer = dialog.Footer,
                FirstPageHeader = dialog.FirstPageHeader,
                FirstPageFooter = dialog.FirstPageFooter,
                EvenPageHeader = dialog.EvenPageHeader,
                EvenPageFooter = dialog.EvenPageFooter,
                DifferentFirstPage = dialog.DifferentFirstPage,
                DifferentOddEvenPages = dialog.DifferentOddEvenPages,
                ScaleHeaderFooterWithDocument = dialog.ScaleHeaderFooterWithDocument,
                AlignHeaderFooterWithMargins = dialog.AlignHeaderFooterWithMargins,
                HeaderPictures = dialog.HeaderPictures,
                FooterPictures = dialog.FooterPictures,
                FirstPageHeaderPictures = dialog.FirstPageHeaderPictures,
                FirstPageFooterPictures = dialog.FirstPageFooterPictures,
                EvenPageHeaderPictures = dialog.EvenPageHeaderPictures,
                EvenPageFooterPictures = dialog.EvenPageFooterPictures,
            }
        };
}
