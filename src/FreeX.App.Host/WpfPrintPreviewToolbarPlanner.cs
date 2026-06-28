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
        ExportPageRange? pageRange = null)
    {
        var range = PrintPreviewToolbarStatePlanner.ResolvePageRange(
            pageRangeMode,
            currentPage,
            pageRange?.FromPage,
            pageRange?.ToPage);

        return range is { } plan
            ? new PageRangeDocumentPaginator(
                document.DocumentPaginator,
                new ExportPageRange(plan.FromPage, plan.ToPage))
            : document.DocumentPaginator;
    }

    public static Duplexing ResolvePrintTicketDuplexing(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => Duplexing.TwoSidedLongEdge,
            PrintPreviewSidesMode.TwoSidedShortEdge => Duplexing.TwoSidedShortEdge,
            _ => Duplexing.OneSided
        };
}
