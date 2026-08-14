namespace FreeP.App.Compositor.Tests;

public sealed class SelectionAdornerControllerTests
{
    [Fact]
    public void ControllerConvertsNativeGeometryUpdatesAndInvalidatesOncePerMutation()
    {
        var invalidations = 0;
        var controller = new SelectionAdornerController<NativeRect, NativePoint>(
            rect => new SelectionAdornerRect(rect.X, rect.Y, rect.Width, rect.Height),
            point => new CanvasGesturePoint(point.X, point.Y),
            () => invalidations++);

        controller.UpdateSelection([(7, new NativeRect(1, 2, 30, 40))]);
        controller.UpdateGeometryHandles([("adjust", new NativePoint(8, 9))]);
        controller.UpdateGeometryPreview("adjust", new NativePoint(10, 11));
        controller.UpdatePreview(new NativeRect(3, 4, 50, 60), 15);
        controller.UpdateMarquee(new NativeRect(5, 6, 70, 80));
        controller.UpdateSnapGuides([], SlideTransformCore.Identity);

        var projection = new SelectionAdornerProjectionPlan(
            [new SelectionAdornerSelectionPlan(9, new SelectionAdornerRect(9, 8, 7, 6))],
            [new SelectionAdornerGeometryHandlePlan("projected", new CanvasGesturePoint(4, 3))]);
        controller.UpdateProjection(projection);

        invalidations.Should().Be(7);
        controller.State.Selections.Should().ContainSingle().Which.Should().Be(
            projection.Selections[0]);
        controller.State.GeometryHandles.Should().ContainSingle().Which.Should().Be(
            projection.GeometryHandles[0]);
        controller.State.GeometryPreview.Should().BeNull();
        controller.State.PreviewRect.Should().BeNull();
        controller.State.MarqueeRect.Should().Be(new SelectionAdornerRect(5, 6, 70, 80));
        controller.State.SnapGuides.Should().BeEmpty();
    }

    private readonly record struct NativeRect(double X, double Y, double Width, double Height);

    private readonly record struct NativePoint(double X, double Y);
}
