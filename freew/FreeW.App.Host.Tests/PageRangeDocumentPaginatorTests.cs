using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class PageRangeDocumentPaginatorTests
{
    [StaFact]
    public void Create_MapsInclusiveUserRangeWithoutRecomposingPages()
    {
        var inner = new RecordingPaginator(5);

        var range = PageRangeDocumentPaginator.Create(inner, 2, 4);

        Assert.Equal(3, range.PageCount);
        Assert.Equal(2, range.GetPage(0).Size.Width);
        Assert.Equal(3, range.GetPage(1).Size.Width);
        Assert.Equal(4, range.GetPage(2).Size.Width);
        Assert.Equal([1, 2, 3], inner.RequestedPageIndexes);
        Assert.Same(DocumentPage.Missing, range.GetPage(-1));
        Assert.Same(DocumentPage.Missing, range.GetPage(3));
    }

    [StaFact]
    public void Create_ClampsOverflowToLastPhysicalPage()
    {
        var inner = new RecordingPaginator(5);

        var range = PageRangeDocumentPaginator.Create(inner, 8, 12);

        Assert.Equal(1, range.PageCount);
        Assert.Equal(5, range.GetPage(0).Size.Width);
        Assert.Equal([4], inner.RequestedPageIndexes);
    }

    [StaFact]
    public void Create_ReturnsOriginalPaginatorForCompleteRange()
    {
        var inner = new RecordingPaginator(3);

        var range = PageRangeDocumentPaginator.Create(inner, 1, 99);

        Assert.Same(inner, range);
    }

    [StaFact]
    public void Create_RejectsInvalidAscendingContract()
    {
        var inner = new RecordingPaginator(3);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PageRangeDocumentPaginator.Create(inner, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PageRangeDocumentPaginator.Create(inner, 3, 2));
    }

    private sealed class RecordingPaginator : DocumentPaginator
    {
        private readonly int _pageCount;
        private readonly IDocumentPaginatorSource _source;

        internal RecordingPaginator(int pageCount)
        {
            _pageCount = pageCount;
            _source = new PaginatorSource(this);
        }

        internal List<int> RequestedPageIndexes { get; } = [];

        public override bool IsPageCountValid => true;

        public override int PageCount => _pageCount;

        public override Size PageSize { get; set; } = new(816, 1056);

        public override IDocumentPaginatorSource Source => _source;

        public override void ComputePageCount()
        {
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            RequestedPageIndexes.Add(pageNumber);
            return new DocumentPage(
                new DrawingVisual(),
                new Size(pageNumber + 1, 100),
                new Rect(0, 0, pageNumber + 1, 100),
                new Rect(0, 0, pageNumber + 1, 100));
        }

        private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
        {
            public DocumentPaginator DocumentPaginator { get; } = paginator;
        }
    }
}
