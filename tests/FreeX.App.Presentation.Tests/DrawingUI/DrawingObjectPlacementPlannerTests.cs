using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectPlacementPlannerTests
{
    [Fact]
    public void PlanDrag_ClickUsesDefaultSizeAndStartAnchor()
    {
        var start = new LayoutPoint(10, 20);

        var plan = DrawingObjectPlacementPlanner.PlanDrag(
            start,
            new LayoutPoint(12, 23),
            defaultWidth: 120,
            defaultHeight: 70);

        plan.IsMeaningfulDrag.Should().BeFalse();
        plan.AnchorPoint.Should().Be(start);
        plan.Width.Should().Be(120);
        plan.Height.Should().Be(70);
        plan.PreviewRect.Should().Be(new LayoutRect(10, 20, DrawingObjectPlacementPlanner.MinimumObjectSize, DrawingObjectPlacementPlanner.MinimumObjectSize));
    }

    [Fact]
    public void PlanDrag_ReverseDragUsesTopLeftAnchorAndClampedSize()
    {
        var plan = DrawingObjectPlacementPlanner.PlanDrag(
            new LayoutPoint(100, 80),
            new LayoutPoint(96, 30),
            defaultWidth: 120,
            defaultHeight: 70);

        plan.IsMeaningfulDrag.Should().BeTrue();
        plan.PreviewRect.Should().Be(new LayoutRect(96, 30, DrawingObjectPlacementPlanner.MinimumObjectSize, 50));
        plan.AnchorPoint.Should().Be(new LayoutPoint(96, 30));
        plan.Width.Should().Be(DrawingObjectPlacementPlanner.MinimumObjectSize);
        plan.Height.Should().Be(50);
    }
}
