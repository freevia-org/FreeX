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

    [Fact]
    public void Build_never_places_an_observed_block_before_its_authored_page_boundary()
    {
        var first = new Paragraph("First");
        first.Runs.Add(Run.PageBreak());
        var caption = new Paragraph("Figure 1: Diagram");
        var document = new TextDocument { Blocks = { first, caption } };
        var resolver = TableOfFiguresPageTextResolverPlanner.Build(
            document,
            physicalPageOfBlock: _ => 1,
            minimumPageCount: 2);

        resolver.Should().NotBeNull();
        resolver!(1, null).Should().Be("2");
    }

    [Fact]
    public void Both_renderers_delegate_figure_page_policy_and_known_page_count_to_the_shared_planner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("TableOfFiguresPageTextResolverPlanner.Build(")
                .And.Contain("minimumPageCount: pages.PageCount ?? 1")
                .And.NotContain("GeneratedReferencePaginationContext.Create(");
        }
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).ReplaceLineEndings("\n");
}
