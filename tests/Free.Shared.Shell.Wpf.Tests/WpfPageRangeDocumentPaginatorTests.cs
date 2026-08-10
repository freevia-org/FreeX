using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfPageRangeDocumentPaginatorTests
{
    [Fact]
    public void ValidatedInclusive_PreservesDistinctProjectionAndInnerContracts()
    {
        var inner = new RecordingPaginator(pageCount: 5, isPageCountValid: false);

        var range = WpfPageRangeDocumentPaginator.CreateValidatedInclusive(inner, 2, 4);

        range.Should().BeOfType<WpfPageRangeDocumentPaginator>();
        range.Should().NotBeSameAs(inner);
        range.PageCount.Should().Be(3);
        range.IsPageCountValid.Should().BeFalse();
        range.Source.Should().BeSameAs(inner.Source);
        inner.ComputePageCountCallCount.Should().Be(0);

        range.GetPage(0).Size.Width.Should().Be(2);
        range.GetPage(1).Size.Width.Should().Be(3);
        range.GetPage(2).Size.Width.Should().Be(4);
        inner.RequestedPageIndexes.Should().Equal(1, 2, 3);
        range.GetPage(-1).Should().BeSameAs(DocumentPage.Missing);
        range.GetPage(3).Should().BeSameAs(DocumentPage.Missing);

        inner.IsPageCountValidValue = true;
        range.IsPageCountValid.Should().BeTrue();

        range.PageSize = new Size(640, 480);
        inner.PageSize.Should().Be(new Size(640, 480));
    }

    [Fact]
    public void ValidatedInclusive_PreservesFullRangeAndEmptyOverflowSemantics()
    {
        var inner = new RecordingPaginator(pageCount: 3, isPageCountValid: true);

        var full = WpfPageRangeDocumentPaginator.CreateValidatedInclusive(inner, 1, 3);
        var overflow = WpfPageRangeDocumentPaginator.CreateValidatedInclusive(inner, 8, 12);

        full.Should().NotBeSameAs(inner);
        full.PageCount.Should().Be(3);
        overflow.Should().NotBeSameAs(inner);
        overflow.PageCount.Should().Be(0);
        overflow.GetPage(0).Should().BeSameAs(DocumentPage.Missing);
        inner.ComputePageCountCallCount.Should().Be(0);
    }

    [Fact]
    public void ClampedInclusive_ComputesClampsAndOwnsRangeSource()
    {
        var inner = new RecordingPaginator(pageCount: 5, isPageCountValid: false);

        var range = WpfPageRangeDocumentPaginator.CreateClampedInclusive(inner, 8, 12);

        range.Should().BeOfType<WpfPageRangeDocumentPaginator>();
        range.PageCount.Should().Be(1);
        range.IsPageCountValid.Should().BeTrue();
        range.Source.Should().NotBeSameAs(inner.Source);
        range.Source.DocumentPaginator.Should().BeSameAs(range);
        inner.ComputePageCountCallCount.Should().Be(1);
        range.GetPage(0).Size.Width.Should().Be(5);
        inner.RequestedPageIndexes.Should().Equal(4);
    }

    [Fact]
    public void ClampedInclusive_PreservesWholeRangeIdentity()
    {
        var inner = new RecordingPaginator(pageCount: 3, isPageCountValid: true);

        var range = WpfPageRangeDocumentPaginator.CreateClampedInclusive(inner, 1, 99);

        range.Should().BeSameAs(inner);
        inner.ComputePageCountCallCount.Should().Be(1);
    }

    [Fact]
    public void Factories_PreserveDistinctValidationContracts()
    {
        var inner = new RecordingPaginator(pageCount: 3, isPageCountValid: true);

        var validatedNull = () => WpfPageRangeDocumentPaginator.CreateValidatedInclusive(null!, 1, 1);
        validatedNull.Should().Throw<ArgumentNullException>().WithParameterName("inner");

        var clampedNull = () => WpfPageRangeDocumentPaginator.CreateClampedInclusive(null!, 1, 1);
        clampedNull.Should().Throw<ArgumentNullException>().WithParameterName("inner");

        var invalidFirst = () => WpfPageRangeDocumentPaginator.CreateClampedInclusive(inner, 0, 1);
        invalidFirst.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("firstPageNumber");

        var invalidLast = () => WpfPageRangeDocumentPaginator.CreateClampedInclusive(inner, 3, 2);
        invalidLast.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lastPageNumber");
    }

    [Fact]
    public void ProductWrappersDelegateRangePolicyToSharedWpfOwner()
    {
        var freeW = File.ReadAllText(RepositoryFile(
            "freew",
            "FreeW.App.Host",
            "PageRangeDocumentPaginator.cs"));
        var freeX = File.ReadAllText(RepositoryFile(
            "src",
            "FreeX.App.Host",
            "PageRangeDocumentPaginator.cs"));

        freeW.Should().Contain("WpfPageRangeDocumentPaginator.CreateClampedInclusive(");
        freeX.Should().Contain("WpfPageRangeDocumentPaginator.CreateValidatedInclusive(");
        freeW.Should().NotContain("override");
        freeX.Should().NotContain("override");
    }

    private sealed class RecordingPaginator : DocumentPaginator
    {
        private readonly int _pageCount;
        private readonly IDocumentPaginatorSource _source;

        internal RecordingPaginator(int pageCount, bool isPageCountValid)
        {
            _pageCount = pageCount;
            IsPageCountValidValue = isPageCountValid;
            _source = new PaginatorSource(this);
        }

        internal bool IsPageCountValidValue { get; set; }

        internal int ComputePageCountCallCount { get; private set; }

        internal List<int> RequestedPageIndexes { get; } = [];

        public override bool IsPageCountValid => IsPageCountValidValue;

        public override int PageCount => _pageCount;

        public override Size PageSize { get; set; } = new(816, 1056);

        public override IDocumentPaginatorSource Source => _source;

        public override void ComputePageCount() => ComputePageCountCallCount++;

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

    private static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return Path.Combine([directory.FullName, .. parts]);
        }

        throw new DirectoryNotFoundException("Could not locate the FreeX repository root.");
    }
}
