using System.Windows.Documents;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// Exposes one inclusive physical-page range from an already composed paginator. The adapter does
/// not repaginate content, so section blanks, note continuations, headers, and page geometry retain
/// the exact ownership established by the print-preview path.
/// </summary>
internal static class PageRangeDocumentPaginator
{
    internal static DocumentPaginator Create(
        DocumentPaginator inner,
        int firstPageNumber,
        int lastPageNumber) =>
        WpfPageRangeDocumentPaginator.CreateClampedInclusive(
            inner,
            firstPageNumber,
            lastPageNumber);
}
