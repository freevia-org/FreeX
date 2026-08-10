using System.Windows;
using System.Windows.Documents;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Projects an inclusive one-based page range over an existing WPF paginator without recomposing it.
/// </summary>
public sealed class WpfPageRangeDocumentPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _inner;
    private readonly int _firstPageIndex;
    private readonly int _pageCount;
    private readonly bool _useInnerValidity;
    private readonly bool _useInnerSource;
    private readonly IDocumentPaginatorSource? _rangeSource;

    private WpfPageRangeDocumentPaginator(
        DocumentPaginator inner,
        int firstPageIndex,
        int pageCount,
        bool useInnerValidity,
        bool useInnerSource)
    {
        _inner = inner;
        _firstPageIndex = firstPageIndex;
        _pageCount = pageCount;
        _useInnerValidity = useInnerValidity;
        _useInnerSource = useInnerSource;
        _rangeSource = useInnerSource ? null : new PaginatorSource(this);
    }

    /// <summary>
    /// Creates a range for callers that already validated its one-based inclusive bounds. The result
    /// always remains a distinct paginator and preserves the inner validity and Source contracts.
    /// </summary>
    public static DocumentPaginator CreateValidatedInclusive(
        DocumentPaginator inner,
        int firstPageNumber,
        int lastPageNumber)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var firstPageIndex = firstPageNumber - 1;
        var pageCount = Math.Max(
            0,
            Math.Min(inner.PageCount, lastPageNumber) - firstPageIndex);

        return new WpfPageRangeDocumentPaginator(
            inner,
            firstPageIndex,
            pageCount,
            useInnerValidity: true,
            useInnerSource: true);
    }

    /// <summary>
    /// Validates ascending positive bounds, computes the inner count, and clamps the requested range
    /// to physical pages. A request covering the complete range preserves inner paginator identity.
    /// </summary>
    public static DocumentPaginator CreateClampedInclusive(
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

        return new WpfPageRangeDocumentPaginator(
            inner,
            first - 1,
            last - first + 1,
            useInnerValidity: false,
            useInnerSource: false);
    }

    public override bool IsPageCountValid =>
        _useInnerValidity
            ? _inner.IsPageCountValid
            : true;

    public override int PageCount => _pageCount;

    public override Size PageSize
    {
        get => _inner.PageSize;
        set => _inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source =>
        _useInnerSource
            ? _inner.Source
            : _rangeSource!;

    public override DocumentPage GetPage(int pageNumber) =>
        pageNumber >= 0 && pageNumber < _pageCount
            ? _inner.GetPage(_firstPageIndex + pageNumber)
            : DocumentPage.Missing;

    private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
    {
        public DocumentPaginator DocumentPaginator { get; } = paginator;
    }
}
