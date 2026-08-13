using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class OutlineCollapseStateTests
{
    [Fact]
    public void Collapse_validates_heading_targets_and_suppresses_duplicates()
    {
        Block[] blocks =
        [
            Heading("Heading", 1),
            new Paragraph("Body"),
            new Table()
        ];
        var state = new OutlineCollapseState();

        state.Collapse(blocks, 0).Should().BeTrue();
        state.Collapse(blocks, 0).Should().BeFalse();
        state.Collapse(blocks, 1).Should().BeFalse();
        state.Collapse(blocks, 2).Should().BeFalse();
        state.Collapse(blocks, -1).Should().BeFalse();
        state.Collapse(blocks, blocks.Length).Should().BeFalse();

        state.Count.Should().Be(1);
        state.IsCollapsed(0).Should().BeTrue();
    }

    [Fact]
    public void Hidden_projection_unions_nested_subtrees_without_losing_nested_state()
    {
        Block[] blocks =
        [
            Heading("Chapter", 1),
            new Paragraph("Chapter body"),
            Heading("Section", 2),
            new Paragraph("Section body"),
            Heading("Next chapter", 1),
            new Paragraph("Next body")
        ];
        var state = new OutlineCollapseState();
        state.Collapse(blocks, 0).Should().BeTrue();
        state.Collapse(blocks, 2).Should().BeTrue();

        state.BuildHiddenBlockIndices(blocks).Should().BeEquivalentTo([1, 2, 3]);
        state.IsCollapsed(0).Should().BeTrue();
        state.IsCollapsed(2).Should().BeTrue();

        state.Expand(0).Should().BeTrue();
        state.BuildHiddenBlockIndices(blocks).Should().BeEquivalentTo([3]);
    }

    [Fact]
    public void Projection_prunes_indices_that_no_longer_identify_headings()
    {
        var heading = Heading("Heading", 1);
        Block[] blocks = [heading, new Paragraph("Body")];
        var state = new OutlineCollapseState();
        state.Collapse(blocks, 0).Should().BeTrue();
        heading.StyleId = "Normal";

        state.BuildHiddenBlockIndices(blocks).Should().BeEmpty();

        state.IsCollapsed(0).Should().BeFalse();
        state.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_resets_document_scoped_view_state()
    {
        Block[] blocks = [Heading("Heading", 1), new Paragraph("Body")];
        var state = new OutlineCollapseState();
        state.Collapse(blocks, 0).Should().BeTrue();

        state.Clear();

        state.Count.Should().Be(0);
        state.IsCollapsed(0).Should().BeFalse();
        state.BuildHiddenBlockIndices(blocks).Should().BeEmpty();
    }

    [Fact]
    public void Final_heading_is_a_valid_collapsed_target_with_an_empty_hidden_projection()
    {
        Block[] blocks = [Heading("Heading", 1)];
        var state = new OutlineCollapseState();

        state.Collapse(blocks, 0).Should().BeTrue();

        state.IsCollapsed(0).Should().BeTrue();
        state.BuildHiddenBlockIndices(blocks).Should().BeEmpty();
    }

    private static Paragraph Heading(string text, int level) =>
        new(text) { StyleId = $"Heading{level}" };
}
