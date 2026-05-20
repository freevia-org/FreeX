using System.Windows;
using System.Windows.Documents;

namespace Freexcel.App.Host;

internal sealed class PageRangeDocumentPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _inner;
    private readonly int _firstPageIndex;
    private readonly int _pageCount;

    public PageRangeDocumentPaginator(DocumentPaginator inner, ExportPageRange pageRange)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(pageRange);

        _inner = inner;
        _firstPageIndex = pageRange.FromPage - 1;
        _pageCount = Math.Max(0, Math.Min(_inner.PageCount, pageRange.ToPage) - _firstPageIndex);
    }

    public override bool IsPageCountValid => _inner.IsPageCountValid;

    public override int PageCount => _pageCount;

    public override Size PageSize
    {
        get => _inner.PageSize;
        set => _inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => _inner.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pageCount)
            return DocumentPage.Missing;

        return _inner.GetPage(_firstPageIndex + pageNumber);
    }
}
