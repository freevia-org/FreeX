namespace FreeP.App.Compositor.Tests;

public sealed class SelectionAdornerSurfaceTests
{
    [Fact]
    public void Extensions_ForwardUpdatesAndInvalidateSurface()
    {
        var surface = new TestSurface();

        surface.UpdateSelection([(7, new TestRect(1, 2, 3, 4))]);
        surface.UpdateGeometryHandles([("adjust", new TestPoint(5, 6))]);
        surface.UpdatePreview(new TestRect(7, 8, 9, 10), 15);
        surface.UpdateMarquee(new TestRect(11, 12, 13, 14));

        surface.Controller.State.Selections.Should().ContainSingle();
        surface.Controller.State.GeometryHandles.Should().ContainSingle();
        surface.Controller.State.PreviewRotationDeg.Should().Be(15);
        surface.Controller.State.MarqueeRect.Should().NotBeNull();
        surface.Invalidations.Should().Be(4);
    }

    private readonly record struct TestRect(double X, double Y, double Width, double Height);
    private readonly record struct TestPoint(double X, double Y);

    private sealed class TestSurface : ISelectionAdornerSurface<TestRect, TestPoint>
    {
        public TestSurface()
        {
            Controller = new(
                rect => new SelectionAdornerRect(rect.X, rect.Y, rect.Width, rect.Height),
                point => new CanvasGesturePoint(point.X, point.Y),
                () => Invalidations++);
        }

        public SelectionAdornerController<TestRect, TestPoint> Controller { get; }
        public int Invalidations { get; private set; }
    }
}
