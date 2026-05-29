using System.Globalization;
using System.Printing;
using System.Windows.Documents;

namespace FreeX.App.Host;

internal static class PrintPreviewToolbarPlanner
{
    public static PrintPreviewNavigationState CreateNavigationState(int currentPage, int totalPages)
    {
        var normalizedTotalPages = Math.Max(1, totalPages);
        var normalizedCurrentPage = Math.Clamp(currentPage, 1, normalizedTotalPages);

        return new PrintPreviewNavigationState(
            normalizedCurrentPage,
            normalizedTotalPages,
            CanGoFirst: normalizedCurrentPage > 1,
            CanGoPrevious: normalizedCurrentPage > 1,
            CanGoNext: normalizedCurrentPage < normalizedTotalPages,
            CanGoLast: normalizedCurrentPage < normalizedTotalPages,
            StatusText: $"Page {normalizedCurrentPage.ToString(CultureInfo.InvariantCulture)} of {normalizedTotalPages.ToString(CultureInfo.InvariantCulture)}");
    }

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

internal sealed record PrintPreviewNavigationState(
    int CurrentPage,
    int TotalPages,
    bool CanGoFirst,
    bool CanGoPrevious,
    bool CanGoNext,
    bool CanGoLast,
    string StatusText);
