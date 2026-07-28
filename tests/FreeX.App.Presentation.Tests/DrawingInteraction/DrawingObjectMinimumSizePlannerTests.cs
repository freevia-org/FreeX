using FluentAssertions;
using FreeX.App.Presentation.DrawingInteraction;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class DrawingObjectMinimumSizePlannerTests
{
    [Theory]
    [InlineData(DrawingObjectMinimumSizeKind.Shape, 8, 8)]
    [InlineData(DrawingObjectMinimumSizeKind.Picture, 24, 18)]
    [InlineData(DrawingObjectMinimumSizeKind.TextBox, 24, 18)]
    [InlineData(DrawingObjectMinimumSizeKind.Chart, 24, 18)]
    public void MinimumSize_MatchesWpfDrawingObjectResizeContract(
        DrawingObjectMinimumSizeKind kind,
        double expectedWidth,
        double expectedHeight)
    {
        DrawingObjectMinimumSizePlanner.MinimumWidth(kind).Should().Be(expectedWidth);
        DrawingObjectMinimumSizePlanner.MinimumHeight(kind).Should().Be(expectedHeight);
    }
}
