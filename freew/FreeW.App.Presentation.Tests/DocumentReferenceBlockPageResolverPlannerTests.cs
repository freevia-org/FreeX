using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentReferenceBlockPageResolverPlannerTests
{
    [Fact]
    public void Build_combines_observed_pages_with_authored_boundary_precedence()
    {
        var first = new Paragraph("First");
        first.Runs.Add(Run.PageBreak());
        var document = DocumentWith(first, new Paragraph("Second"));

        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 1,
            pageCount: 2,
            allowUnobservedFirstPageFallback: false);

        resolution.PageCount.Should().Be(2);
        resolution.PageNumberAtBlock!(0).Should().Be(1);
        resolution.PageNumberAtBlock(1).Should().Be(2);
    }

    [Fact]
    public void Build_clamps_observed_pages_and_rejects_invalid_blocks()
    {
        var document = DocumentWith(new Paragraph("Body"));
        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => 99,
            pageCount: 4,
            allowUnobservedFirstPageFallback: false);

        resolution.PageNumberAtBlock!(0).Should().Be(4);
        resolution.PageNumberAtBlock(-1).Should().BeNull();
        resolution.PageNumberAtBlock(1).Should().BeNull();
    }

    [Theory]
    [InlineData(true, 1, 1)]
    [InlineData(true, 3, 1)]
    [InlineData(false, 1, null)]
    public void Build_owns_unobserved_first_page_fallback(
        bool allowFallback,
        int pageCount,
        int? expected)
    {
        var document = DocumentWith(new Paragraph("Body"));
        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => null,
            pageCount,
            allowUnobservedFirstPageFallback: allowFallback);

        resolution.PageNumberAtBlock!(0).Should().Be(expected);
    }

    [Fact]
    public void Build_does_not_invent_an_unobserved_page_in_a_multi_page_document()
    {
        var document = DocumentWith(new Paragraph("Body"));
        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: null,
            pageCount: 3,
            allowUnobservedFirstPageFallback: false);

        resolution.PageNumberAtBlock!(0).Should().BeNull();
    }

    [Fact]
    public void Build_only_falls_back_to_page_one_for_the_first_unobserved_block()
    {
        var document = DocumentWith(new Paragraph("First"), new Paragraph("Second"));
        var resolution = DocumentReferenceBlockPageResolverPlanner.Build(
            document,
            observedPhysicalPageOfBlock: _ => null,
            pageCount: 1,
            allowUnobservedFirstPageFallback: true);

        resolution.PageNumberAtBlock!(0).Should().Be(1);
        resolution.PageNumberAtBlock(1).Should().BeNull();
    }

    [Fact]
    public void Both_renderers_delegate_block_page_policy_to_the_shared_planner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("DocumentReferenceBlockPageResolverPlanner.Build(")
                .And.NotContain("CrossReferences.ExplicitPageNumberAtBlock(");
        }
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
