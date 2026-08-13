namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGesturePreviewDispatcherTests
{
    [Fact]
    public void MoveRoutesPreviewAndSnapGuideState()
    {
        var bounds = new SlideScreenRect(10, 20, 30, 40);
        var surface = new RecordingSurface();

        CanvasGesturePreviewDispatcher.Apply(
            new CanvasGestureVisualPreviewPlan(
                CanvasGestureKind.Move,
                bounds,
                [],
                null,
                null,
                null,
                null),
            SlideTransformCore.Identity,
            surface);

        surface.Preview.Should().NotBeNull();
        surface.Preview!.Value.Bounds.Should().Be(bounds);
        surface.Preview.Value.Rotation.Should().Be(0);
        surface.SnapGuidesApplied.Should().BeTrue();
        surface.SnapGuides.Should().BeNull();
    }

    [Theory]
    [InlineData(CanvasGestureKind.Resize)]
    [InlineData(CanvasGestureKind.Rotate)]
    public void MultiTransformRoutesOneSharedSurfaceOperation(CanvasGestureKind kind)
    {
        var surface = new RecordingSurface();
        var transform = CanvasMultiTransformPlan.Empty;

        CanvasGesturePreviewDispatcher.Apply(
            new CanvasGestureVisualPreviewPlan(kind, null, [], transform, null, null, null),
            SlideTransformCore.Identity,
            surface);

        surface.TransformPreview.Should().Be(transform);
        surface.OperationCount.Should().Be(1);
    }

    [Fact]
    public void RotationRoutesBoundsAndAngle()
    {
        var bounds = new SlideScreenRect(1, 2, 3, 4);
        var surface = new RecordingSurface();

        CanvasGesturePreviewDispatcher.Apply(
            new CanvasGestureVisualPreviewPlan(
                CanvasGestureKind.Rotate,
                bounds,
                [],
                null,
                37.5,
                null,
                null),
            SlideTransformCore.Identity,
            surface);

        surface.Preview.Should().NotBeNull();
        surface.Preview!.Value.Bounds.Should().Be(bounds);
        surface.Preview.Value.Rotation.Should().Be(37.5);
    }

    [Fact]
    public void GeometryAndMarqueeRoutePortableCoordinates()
    {
        var surface = new RecordingSurface();
        var point = new CanvasGesturePoint(12, 18);
        var marquee = new SlideScreenRect(4, 5, 6, 7);

        CanvasGesturePreviewDispatcher.Apply(
            new CanvasGestureVisualPreviewPlan(
                CanvasGestureKind.GeometryAdjustment,
                null,
                [],
                null,
                null,
                "adj",
                point),
            SlideTransformCore.Identity,
            surface);
        CanvasGesturePreviewDispatcher.Apply(
            new CanvasGestureVisualPreviewPlan(
                CanvasGestureKind.Marquee,
                marquee,
                [],
                null,
                null,
                null,
                null),
            SlideTransformCore.Identity,
            surface);

        surface.GeometryPreview.Should().Be(("adj", point));
        surface.Marquee.Should().Be(marquee);
    }

    private sealed class RecordingSurface : ICanvasGesturePreviewSurface
    {
        public (SlideScreenRect? Bounds, double Rotation)? Preview { get; private set; }
        public IReadOnlyList<SnapGuideLine>? SnapGuides { get; private set; }
        public bool SnapGuidesApplied { get; private set; }
        public CanvasMultiTransformPlan? TransformPreview { get; private set; }
        public (string Name, CanvasGesturePoint Point)? GeometryPreview { get; private set; }
        public SlideScreenRect? Marquee { get; private set; }
        public int OperationCount { get; private set; }

        public void UpdatePreview(SlideScreenRect? bounds, double rotationDegrees = 0)
        {
            Preview = (bounds, rotationDegrees);
            OperationCount++;
        }

        public void UpdateSnapGuides(
            IReadOnlyList<SnapGuideLine>? guides,
            SlideTransformCore transform)
        {
            SnapGuides = guides;
            SnapGuidesApplied = true;
            OperationCount++;
        }

        public void UpdateTransformPreview(CanvasMultiTransformPlan plan)
        {
            TransformPreview = plan;
            OperationCount++;
        }

        public void UpdateGeometryPreview(string handleName, CanvasGesturePoint screenPoint)
        {
            GeometryPreview = (handleName, screenPoint);
            OperationCount++;
        }

        public void UpdateMarquee(SlideScreenRect bounds)
        {
            Marquee = bounds;
            OperationCount++;
        }
    }
}
