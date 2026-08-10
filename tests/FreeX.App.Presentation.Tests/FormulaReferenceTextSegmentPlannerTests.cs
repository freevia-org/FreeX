using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class FormulaReferenceTextSegmentPlannerTests
{
    [Fact]
    public void CreateSegments_OrdersHighlightsAndPreservesPlainText()
    {
        const string text = "=A1+B2";
        FormulaReferenceHighlight[] highlights =
        [
            new(4, 2, 1, "B2", null, null),
            new(1, 2, 0, "A1", null, null)
        ];

        FormulaReferenceTextSegmentPlanner.CreateSegments(text, highlights)
            .Should()
            .Equal(
                new FormulaReferenceTextSegment("=", null),
                new FormulaReferenceTextSegment("A1", 0),
                new FormulaReferenceTextSegment("+", null),
                new FormulaReferenceTextSegment("B2", 1));
    }

    [Fact]
    public void CreateSegments_IgnoresOverlapsAndClipsFinalHighlight()
    {
        const string text = "=A1+B234";
        FormulaReferenceHighlight[] highlights =
        [
            new(1, 2, 0, "A1", null, null),
            new(2, 2, 4, "1+", null, null),
            new(4, 100, 1, "B234", null, null)
        ];

        FormulaReferenceTextSegmentPlanner.CreateSegments(text, highlights)
            .Should()
            .Equal(
                new FormulaReferenceTextSegment("=", null),
                new FormulaReferenceTextSegment("A1", 0),
                new FormulaReferenceTextSegment("+", null),
                new FormulaReferenceTextSegment("B234", 1));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("=SUM(")]
    public void CreateSegments_ReturnsEmptyWithoutActiveFormulaHighlights(string text)
    {
        FormulaReferenceTextSegmentPlanner.CreateSegments(text, [])
            .Should()
            .BeEmpty();
    }
}
