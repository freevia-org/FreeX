using System.Windows;
using System.Windows.Documents;

namespace FreeW.App.Host;

/// <summary>
/// Exposes one inclusive physical-page range from an already composed paginator. The adapter does
/// not repaginate content, so section blanks, note continuations, headers, and page geometry retain
/// the exact ownership established by the print-preview path.
/// </summary>
internal sealed class PageRangeDocumentPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _inner;
    private readonly int _firstPageIndex;
    private readonly int _pageCount;
    private readonly IDocumentPaginatorSource _source;

    private PageRangeDocumentPaginator(
        DocumentPaginator inner,
        int firstPageIndex,
        int pageCount)
    {
        _inner = inner;
        _firstPageIndex = firstPageIndex;
        _pageCount = pageCount;
        _source = new PaginatorSource(this);
    }

    internal static DocumentPaginator Create(
        DocumentPaginator inner,
        int firstPageNumber,
        int lastPageNumber)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (firstPageNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstPageNumber));
        if (lastPageNumber < firstPageNumber)
            throw new ArgumentOutOfRangeException(nameof(lastPageNumber));

        inner.ComputePageCount();
        var innerPageCount = Math.Max(1, inner.PageCount);
        var first = Math.Clamp(firstPageNumber, 1, innerPageCount);
        var last = Math.Clamp(lastPageNumber, first, innerPageCount);
        if (first == 1 && last == innerPageCount)
            return inner;

        return new PageRangeDocumentPaginator(
            inner,
            first - 1,
            last - first + 1);
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _pageCount;

    public override Size PageSize
    {
        get => _inner.PageSize;
        set => _inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => _source;

    public override DocumentPage GetPage(int pageNumber) =>
        pageNumber >= 0 && pageNumber < _pageCount
            ? _inner.GetPage(_firstPageIndex + pageNumber)
            : DocumentPage.Missing;

    private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
    {
        public DocumentPaginator DocumentPaginator { get; } = paginator;
    }
}
