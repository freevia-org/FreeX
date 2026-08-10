namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureInteractionPlannerTests
{
    [Fact]
    public void BuildPressRequest_ProjectsSelectionAndGeometryHitsWithoutRendererTypes()
    {
        var selection = new SelectionAdornerProjectionPlan(
            [new SelectionAdornerSelectionPlan(1, new SelectionAdornerRect(10, 20, 100, 50))],
            [new SelectionAdornerGeometryHandlePlan("adj", new CanvasGesturePoint(50, 40))]);
        var transform = new SlideTransformCore(2, 10, 20, 100, 100);

        var resize = CanvasGestureInteractionPlanner.BuildPressRequest(
            new CanvasGesturePoint(110, 70),
            transform,
            selection,
            clickCount: 1,
            CanvasGestureModifiers.Shift,
            editPointsEnabled: true,
            canNotifyChartPointDoubleClick: false);
        var geometry = CanvasGestureInteractionPlanner.BuildPressRequest(
            new CanvasGesturePoint(50, 40),
            transform,
            selection,
            clickCount: 2,
            CanvasGestureModifiers.None,
            editPointsEnabled: true,
            canNotifyChartPointDoubleClick: true);

        resize.SelectionHandle.Should().Be(CanvasGestureHandleKind.ResizeSE);
        resize.SlidePoint.Should().Be(new CanvasGesturePoint(50, 25));
        resize.HasSingleSelectionFrame.Should().BeTrue();
        geometry.GeometryHandle.Should().Be("adj");
        geometry.CanNotifyChartPointDoubleClick.Should().BeTrue();
    }

    [Fact]
    public void PlanCursor_MapsSelectionHandlesAndGeometryToPortableIntents()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        var selection = new SelectionAdornerProjectionPlan(
            [new SelectionAdornerSelectionPlan(1, new SelectionAdornerRect(10, 20, 100, 50))],
            [new SelectionAdornerGeometryHandlePlan("adj", new CanvasGesturePoint(50, 40))]);

        CanvasGestureInteractionPlanner.PlanCursor(
                slide,
                presentation,
                [1],
                SlideTransformCore.Identity,
                selection,
                new CanvasGesturePoint(110, 70),
                editPointsEnabled: true)
            .Should().Be(CanvasGestureCursorKind.ResizeNorthWestSouthEast);
        CanvasGestureInteractionPlanner.PlanCursor(
                slide,
                presentation,
                [1],
                SlideTransformCore.Identity,
                selection,
                new CanvasGesturePoint(50, 40),
                editPointsEnabled: true)
            .Should().Be(CanvasGestureCursorKind.Pointer);
    }
}
