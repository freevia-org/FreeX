using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfAuthoritiesPageResolverPlannerTests
{
    [Fact]
    public void Build_UsesCitationRunOffsetAndObservedCharacterPage()
    {
        var citation = new Citation("Brown v. Board", CitationCategory.Cases);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Prefix "));
        paragraph.Runs.Add(Run.CitationMark(citation));
        var document = DocumentWith(paragraph);
        var requestedOffset = -1;
        var resolver = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 1,
            observedPhysicalPageOfBlockOffset: (_, offset) =>
            {
                requestedOffset = offset;
                return 3;
            },
            minimumPageCount: 4);

        var reference = resolver(document, 0, null, 1, citation);

        requestedOffset.Should().Be("Prefix ".Length);
        reference.Should().Be(new ToaCitationPageReference(3, "3"));
    }

    [Fact]
    public void Build_FallsBackToKnownBlockPageAndFormatsVisibleNumber()
    {
        var citation = new Citation("Roe v. Wade", CitationCategory.Cases);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CitationMark(citation));
        var document = DocumentWith(paragraph);
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        var resolver = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 2,
            observedPhysicalPageOfBlockOffset: (_, _) => null,
            minimumPageCount: 2);

        var reference = resolver(document, 0, null, 0, citation);

        reference.Should().Be(new ToaCitationPageReference(2, "II"));
    }

    [Fact]
    public void Build_never_places_an_observed_block_before_its_authored_page_boundary()
    {
        var first = new Paragraph("First");
        first.Runs.Add(Run.PageBreak());
        var citation = new Citation("Case", CitationCategory.Cases);
        var marked = new Paragraph { Runs = { Run.CitationMark(citation) } };
        var document = DocumentWith(first, marked);
        var resolver = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 1,
            observedPhysicalPageOfBlockOffset: (_, _) => null,
            minimumPageCount: 2);

        resolver(document, 1, null, 0, citation)
            .Should().Be(new ToaCitationPageReference(2, "2"));
    }

    [Fact]
    public void Build_OffsetsTableCitationToPlannedSpilloverPage()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var tableIndex = Enumerable.Range(0, document.Blocks.Count)
            .Single(index => document.Blocks[index] is Table);
        var pagination = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document).Single().Pagination;
        var secondPageRow = pagination.Pages[1].SourceRowIndexes[0];
        var citation = new Citation("Case", CitationCategory.Cases);
        var resolver = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 4,
            observedPhysicalPageOfBlockOffset: null);

        var reference = resolver(
            document,
            tableIndex,
            new TableParagraphAddress(secondPageRow, 0, 0),
            0,
            citation);

        reference.Should().Be(new ToaCitationPageReference(5, "5"));
    }

    [Fact]
    public void Build_RejectsNonCitationRunAndUnsafeUnknownSinglePageFallback()
    {
        var document = DocumentWith(new Paragraph("Body"), new Paragraph("Other"));
        var citation = new Citation("Case", CitationCategory.Cases);
        var resolver = TableOfAuthoritiesPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: null,
            observedPhysicalPageOfBlockOffset: null,
            allowSinglePageFallback: false);

        resolver(document, 0, null, 0, citation).Should().BeNull();

        ((Paragraph)document.Blocks[1]).Runs.Add(Run.CitationMark(citation));
        resolver(document, 1, null, 1, citation).Should().BeNull();
    }

    [Fact]
    public void Both_renderers_delegate_generated_authority_page_policy_to_the_shared_planner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("TableOfAuthoritiesPageResolverPlanner.Build(")
                .And.NotContain("private bool IsModelCitationRun(")
                .And.NotContain("private ToaCitationPageReference? ResolveTableOfAuthoritiesCitationPage(");
        }

        avalonia.Should().Contain("DocumentReferenceBlockPageResolverPlanner.Build(")
            .And.NotContain("TableOfAuthoritiesPageResolverPlanner.HasExplicitPageBoundary(_doc)")
            .And.NotContain("private static bool HasExplicitPageBoundary(TextDocument document)");
    }

    private static TextDocument DocumentWith(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).ReplaceLineEndings("\n");
}
