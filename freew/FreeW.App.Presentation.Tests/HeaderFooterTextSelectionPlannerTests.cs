using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterTextSelectionPlannerTests
{
    [Fact]
    public void Clamp_normalizes_paragraph_and_offset_boundaries()
    {
        var story = Story("Alpha", "Beta");

        HeaderFooterTextSelectionPlanner.Clamp(story, new HeaderFooterTextPosition(-2, 40))
            .Should().Be(new HeaderFooterTextPosition(0, 5));
        HeaderFooterTextSelectionPlanner.Clamp(story, new HeaderFooterTextPosition(9, -3))
            .Should().Be(new HeaderFooterTextPosition(1, 0));
    }

    [Fact]
    public void Normalize_orders_cross_paragraph_endpoints_and_ignores_empty_ranges()
    {
        var story = Story("Alpha", "Beta");

        HeaderFooterTextSelectionPlanner.Normalize(
                story,
                new HeaderFooterTextPosition(0, 2),
                new HeaderFooterTextPosition(1, 3))
            .Should().Be(new HeaderFooterTextRange(
                new HeaderFooterTextPosition(0, 2),
                new HeaderFooterTextPosition(1, 3)));
        HeaderFooterTextSelectionPlanner.Normalize(
                story,
                new HeaderFooterTextPosition(0, 2),
                new HeaderFooterTextPosition(0, 2))
            .Should().BeNull();
    }

    [Fact]
    public void MoveHorizontal_crosses_paragraph_boundaries_in_both_directions()
    {
        var story = Story("Alpha", "Beta");

        HeaderFooterTextSelectionPlanner.MoveHorizontal(
                story,
                new HeaderFooterTextPosition(0, 5),
                1)
            .Should().Be(new HeaderFooterTextPosition(1, 0));
        HeaderFooterTextSelectionPlanner.MoveHorizontal(
                story,
                new HeaderFooterTextPosition(1, 0),
                -1)
            .Should().Be(new HeaderFooterTextPosition(0, 5));
    }

    [Fact]
    public void MoveHorizontal_clamps_at_story_edges_and_supports_multiple_steps()
    {
        var story = Story("A", "BC");

        HeaderFooterTextSelectionPlanner.MoveHorizontal(
                story,
                new HeaderFooterTextPosition(0, 0),
                -5)
            .Should().Be(new HeaderFooterTextPosition(0, 0));
        HeaderFooterTextSelectionPlanner.MoveHorizontal(
                story,
                new HeaderFooterTextPosition(0, 0),
                4)
            .Should().Be(new HeaderFooterTextPosition(1, 2));
    }

    [Fact]
    public void MoveToParagraphEdge_uses_the_active_paragraph()
    {
        var story = Story("Alpha", "Beta");
        var caret = new HeaderFooterTextPosition(1, 2);

        HeaderFooterTextSelectionPlanner.MoveToParagraphEdge(story, caret, toStart: true)
            .Should().Be(new HeaderFooterTextPosition(1, 0));
        HeaderFooterTextSelectionPlanner.MoveToParagraphEdge(story, caret, toStart: false)
            .Should().Be(new HeaderFooterTextPosition(1, 4));
    }

    [Fact]
    public void MoveVertical_preserves_the_preferred_offset_and_clamps_shorter_paragraphs()
    {
        var story = Story("Long line", "Two", "Another line");

        HeaderFooterTextSelectionPlanner.MoveVertical(
                story,
                new HeaderFooterTextPosition(0, 7),
                1)
            .Should().Be(new HeaderFooterTextPosition(1, 3));
        HeaderFooterTextSelectionPlanner.MoveVertical(
                story,
                new HeaderFooterTextPosition(1, 2),
                1)
            .Should().Be(new HeaderFooterTextPosition(2, 2));
    }

    [Fact]
    public void GetText_preserves_paragraph_breaks_and_endpoint_order()
    {
        var story = Story("Alpha", "Beta", "Gamma");
        var reverseRange = new HeaderFooterTextRange(
            new HeaderFooterTextPosition(2, 2),
            new HeaderFooterTextPosition(0, 2));

        HeaderFooterTextSelectionPlanner.GetText(story, reverseRange)
            .Should().Be("pha\nBeta\nGa");
    }

    private static HeaderFooter Story(params string[] paragraphs)
    {
        var story = new HeaderFooter();
        foreach (var text in paragraphs)
            story.Paragraphs.Add(new Paragraph(text));
        return story;
    }
}
