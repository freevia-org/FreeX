using System.Printing;
using System.Windows.Documents;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class WpfPrintPreviewToolbarPlanner
{
    public static DocumentPaginator ResolvePrintPaginator(
        FixedDocument document,
        PrintPreviewPageRangeMode pageRangeMode,
        int currentPage,
        ExportPageRange? pageRange = null) =>
        pageRangeMode switch
        {
            PrintPreviewPageRangeMode.CurrentPage => new PageRangeDocumentPaginator(
                document.DocumentPaginator,
                new ExportPageRange(currentPage, currentPage)),
            PrintPreviewPageRangeMode.Pages when pageRange is not null => new PageRangeDocumentPaginator(
                document.DocumentPaginator,
                pageRange),
            _ => document.DocumentPaginator
        };

    public static Duplexing ResolvePrintTicketDuplexing(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => Duplexing.TwoSidedLongEdge,
            PrintPreviewSidesMode.TwoSidedShortEdge => Duplexing.TwoSidedShortEdge,
            _ => Duplexing.OneSided
        };
}
