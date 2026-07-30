using FreeP.App.Compositor;

namespace FreeP.App.Presentation.Tests;

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
            caret: newline + 4,
            text.Length);

        range.Should().Be((8, newline + 4));
        text[range.Start..range.End].Should().Be("wrapped visual lines\npara");
    }

    [Fact]
    public void Plan_RejectsNegativeDocumentLength()
    {
        var action = () => InCanvasRichTextPointerSelectionPlanner.Plan(0, 0, -1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
