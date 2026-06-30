using FluentAssertions;
using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class SharedDrawingObjectInteractionPlannerTests
{
    private static readonly LayoutRect Start = new(100, 100, 200, 100);

    [Fact]
    public void CalculateDragTransform_CrossedEdgeReportsFlipAndNormalizedRect()
    {
        var result = DrawingObjectInteractionPlanner.CalculateDragTransform(
            DrawingObjectInteractionKind.ResizeE,
            Start,
            new LayoutPoint(300, 150),
            new LayoutPoint(0, 150));

        result.Rect.Should().Be(new LayoutRect(0, 100, 100, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeFalse();
    }

    [Fact]
    public void GetResizeHandleCenters_ReturnsSharedAdornerOrder()
    {
        DrawingObjectInteractionPlanner.GetResizeHandleCenters(new LayoutRect(10, 20, 100, 40))
            .Should().Equal(
                new LayoutPoint(60, 20),
                new LayoutPoint(110, 20),
                new LayoutPoint(110, 40),
                new LayoutPoint(110, 60),
                new LayoutPoint(60, 60),
                new LayoutPoint(10, 60),
                new LayoutPoint(10, 40),
                new LayoutPoint(10, 20));
    }

    [Fact]
    public void DrawingBoundsHitTester_UnrotatesPointBeforeBoundsCheck()
    {
        var bounds = new LayoutRect(100, 100, 100, 100);
        var rotatedTopLeft = DrawingObjectInteractionPlanner.RotatePointAroundCenter(
            new LayoutPoint(100, 100),
            bounds,
            45);
        var candidates = new[]
        {
            new DrawingBoundsHitCandidate<string>("shape", bounds, ZOrder: 0, RotationDegrees: 45)
        };

        var hit = DrawingBoundsHitTester.HitTest(candidates, rotatedTopLeft);

        hit.Should().NotBeNull();
        hit!.Value.Id.Should().Be("shape");
    }
}
