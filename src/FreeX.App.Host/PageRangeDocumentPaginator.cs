using System.Windows.Documents;
using Free.Shared.Shell.Wpf;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class PageRangeDocumentPaginator
{
    internal static DocumentPaginator Create(DocumentPaginator inner, ExportPageRange pageRange)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(pageRange);

        return WpfPageRangeDocumentPaginator.CreateValidatedInclusive(
            inner,
            pageRange.FromPage,
            pageRange.ToPage);
    }
}
