using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfFiguresPageTextResolverPlannerTests
{
    [Fact]
    public void Build_WithoutPhysicalPageResolver_ReturnsNull()
    {
        var resolver = TableOfFiguresPageTextResolverPlanner.Build(TextDocument.CreateEmpty(), null);

        resolver.Should().BeNull();
    }

    [Fact]
    public void Build_UsesKnownPageCountForSectionAwareVisiblePageText()
    {
        var document = TextDocument.CreateEmpty();
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 1;

        var resolver = TableOfFiguresPageTextResolverPlanner.Build(
            document,
            _ => 1,
            minimumPageCount: 4);

        resolver.Should().NotBeNull();
        resolver!(0, null).Should().Be("I");
    }

    [Fact]
    public void Build_OffsetsTableParagraphToItsPlannedPhysicalPage()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var tableIndex = document.Blocks.FindIndex(block => block is Table);
        var pagination = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document).Single().Pagination;
        var secondPageRow = pagination.Pages[1].SourceRowIndexes[0];

        var resolver = TableOfFiguresPageTextResolverPlanner.Build(document, _ => 4);

        resolver.Should().NotBeNull();
        resolver!(tableIndex, new TableParagraphAddress(secondPageRow, 0, 0)).Should().Be("5");
    }
}
