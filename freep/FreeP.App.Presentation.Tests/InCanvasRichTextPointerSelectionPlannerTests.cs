using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichTextPointerSelectionPlannerTests
{
    [Theory]
    [InlineData(4, 27, 40, 4, 27)]
    [InlineData(27, 4, 40, 27, 4)]
    [InlineData(-5, 99, 40, 0, 40)]
    public void Plan_PreservesDragDirectionAndClampsDocumentBounds(
        int anchor,
        int caret,
        int textLength,
        int expectedAnchor,
        int expectedCaret)
    {
        var plan = InCanvasRichTextPointerSelectionPlanner.Plan(
            anchor,
            caret,
            textLength);

        plan.Start.Should().Be(expectedAnchor);
        plan.End.Should().Be(expectedCaret);
    }

    [Fact]
    public void Normalize_CrossesParagraphBoundaryWithoutDroppingTheModelNewline()
    {
        const string text = "Unequal wrapped visual lines\nparagraph tail";
        int newline = text.IndexOf('\n');

        var range = InCanvasRichTextPointerSelectionPlanner.Normalize(
            anchor: 8,
            caret: newline + 5,
            text.Length);

        range.Should().Be((8, newline + 5));
        text[range.Start..range.End].Should().Be("wrapped visual lines\npara");
    }

    [Theory]
    [InlineData("first\nsecond", 2, 0, 6)]
    [InlineData("first\nsecond", 8, 6, 12)]
    [InlineData("single", 3, 0, 6)]
    public void PlanParagraph_MatchesRichTextBoxParagraphMarkerSemantics(
        string text,
        int logicalPosition,
        int expectedStart,
        int expectedEnd)
    {
        InCanvasRichTextPointerSelectionPlanner.PlanParagraph(text, logicalPosition)
            .Should().Be((expectedStart, expectedEnd));
    }

    [Fact]
    public void Plan_RejectsNegativeDocumentLength()
    {
        var action = () => InCanvasRichTextPointerSelectionPlanner.Plan(0, 0, -1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-20, 100, -1)]
    [InlineData(4, 100, -1)]
    [InlineData(50, 100, 0)]
    [InlineData(96, 100, 1)]
    [InlineData(140, 100, 1)]
    public void ResolveVerticalEdgeDirection_UsesCapturedPointerEdgeBands(
        double pointerY,
        double viewportHeight,
        int expectedDirection)
    {
        InCanvasRichTextPointerSelectionPlanner.ResolveVerticalEdgeDirection(
            pointerY,
            viewportHeight,
            edgeThreshold: 6)
            .Should().Be(expectedDirection);
    }

    [Fact]
    public void AdvanceVerticalScroll_ClampsAtBothContentEdges()
    {
        InCanvasRichTextPointerSelectionPlanner.AdvanceVerticalScroll(
                0, contentExtent: 500, viewportExtent: 100, direction: -1, step: 30)
            .Should().Be(0);
        InCanvasRichTextPointerSelectionPlanner.AdvanceVerticalScroll(
                0, contentExtent: 500, viewportExtent: 100, direction: 1, step: 30)
            .Should().Be(30);
        InCanvasRichTextPointerSelectionPlanner.AdvanceVerticalScroll(
                390, contentExtent: 500, viewportExtent: 100, direction: 1, step: 30)
            .Should().Be(400);
        InCanvasRichTextPointerSelectionPlanner.AdvanceVerticalScroll(
                450, contentExtent: 500, viewportExtent: 100, direction: 0)
            .Should().Be(400);
    }

    [Fact]
    public void EdgePolicy_RejectsInvalidGeometry()
    {
        var act = () => InCanvasRichTextPointerSelectionPlanner
            .ResolveVerticalEdgeDirection(0, double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
