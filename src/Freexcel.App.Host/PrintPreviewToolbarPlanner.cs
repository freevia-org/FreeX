using System.Globalization;
using System.Printing;
using System.Windows.Documents;

namespace Freexcel.App.Host;

internal static class PrintPreviewToolbarPlanner
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

    public static PrintPreviewSidesMode ResolveSelectedSidesMode(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PrintPreviewSidesMode.TwoSidedLongEdge,
            2 => PrintPreviewSidesMode.TwoSidedShortEdge,
            _ => PrintPreviewSidesMode.OneSided
        };

    public static string CreateStatusText(string? printerName, int? copies, int totalPages)
    {
        var copyText = copies is { } count
            ? count == 1 ? "1 copy" : $"{count.ToString(CultureInfo.InvariantCulture)} copies"
            : "invalid copies";
        var pages = totalPages == 1
            ? "1 page"
            : $"{totalPages.ToString(CultureInfo.InvariantCulture)} pages";
        var name = string.IsNullOrWhiteSpace(printerName)
            ? "Windows print dialog"
            : printerName;

        return $"Ready: {name}; {copyText}; {pages}";
    }
}
