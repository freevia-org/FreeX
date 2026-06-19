using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingInteraction;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class DrawingObjectHitTestPlannerTests
{
    private static DrawingObjectHitCandidate<string> Candidate(string id, double x, double y, double w, double h, int z) =>
        new(id, new LayoutRect(x, y, w, h), z);

    [Fact]
    public void HitTest_ReturnsNull_WhenPointMissesEveryObject()
    {
        var candidates = new[] { Candidate("a", 0, 0, 50, 50, 0) };

        var hit = DrawingObjectHitTestPlanner.HitTest(candidates, new LayoutPoint(200, 200));

        hit.Should().BeNull();
    }

    [Fact]
    public void HitTest_ReturnsObject_WhenPointInsideBody()
    {
        var candidates = new[] { Candidate("a", 10, 10, 80, 40, 0) };

        var hit = DrawingObjectHitTestPlanner.HitTest(candidates, new LayoutPoint(40, 20));

        hit.Should().NotBeNull();
        hit!.Value.Id.Should().Be("a");
        hit.Value.Part.Should().Be(ObjectDragKind.Move);
    }

    [Fact]
    public void HitTest_PrefersHigherZOrder_WhenObjectsOverlap()
    {
        var candidates = new[]
        {
            Candidate("under", 0, 0, 100, 100, 0),
            Candidate("over", 20, 20, 100, 100, 5),
        };

        var hit = DrawingObjectHitTestPlanner.HitTest(candidates, new LayoutPoint(50, 50));

        hit!.Value.Id.Should().Be("over");
    }

    [Fact]
    public void HitTest_PrefersLastPainted_WhenZOrderTies()
    {
        var candidates = new[]
        {
            Candidate("first", 0, 0, 100, 100, 0),
            Candidate("second", 0, 0, 100, 100, 0),
        };

        var hit = DrawingObjectHitTestPlanner.HitTest(candidates, new LayoutPoint(50, 50));

        hit!.Value.Id.Should().Be("second");
    }

    [Fact]
    public void HitTestWithSelection_ReturnsResizeHandle_OverNeighborBody()
    {
        // The selected object's SE corner sits on top of a neighbor that starts where the corner is.
        var selectedBounds = new LayoutRect(0, 0, 100, 100);
        var candidates = new[]
        {
            new DrawingObjectHitCandidate<string>("selected", selectedBounds, 0),
            Candidate("neighbor", 100, 100, 100, 100, 1),
        };

        var hit = DrawingObjectHitTestPlanner.HitTestWithSelection(
            candidates,
            new LayoutPoint(100, 100),
            "selected",
            selectedBounds);

        hit!.Value.Id.Should().Be("selected");
        hit.Value.Part.Should().Be(ObjectDragKind.ResizeSE);
    }

    [Fact]
    public void HitTestWithSelection_FallsBackToBody_WhenNotOnAHandle()
    {
        var selectedBounds = new LayoutRect(0, 0, 100, 100);
        var candidates = new[]
        {
            new DrawingObjectHitCandidate<string>("selected", selectedBounds, 0),
            Candidate("neighbor", 200, 200, 100, 100, 1),
        };

        var hit = DrawingObjectHitTestPlanner.HitTestWithSelection(
            candidates,
            new LayoutPoint(250, 250),
            "selected",
            selectedBounds);

        hit!.Value.Id.Should().Be("neighbor");
        hit.Value.Part.Should().Be(ObjectDragKind.Move);
    }

    [Fact]
    public void HitTestWithSelection_ReturnsNull_OnEmptyCanvasClick()
    {
        var selectedBounds = new LayoutRect(0, 0, 100, 100);
        var candidates = new[]
        {
            new DrawingObjectHitCandidate<string>("selected", selectedBounds, 0),
        };

        var hit = DrawingObjectHitTestPlanner.HitTestWithSelection(
            candidates,
            new LayoutPoint(500, 500),
            "selected",
            selectedBounds);

        hit.Should().BeNull();
    }
}
