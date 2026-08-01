using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasTransformPreviewComposerTests
{
    [Fact]
    public void Compose_ShapeClone_RescalesGeometryAndKeepsResolvedPaint()
    {
        var sourceBounds = new LayoutRect(10, 20, 100, 50);
        var source = new DrawOp.Shape
        {
            ShapeId = 7,
            BoundsDip = sourceBounds,
            Geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, sourceBounds),
            Fill = new ResolvedFill.Solid(new SrgbColor(0x11, 0x22, 0x33), 200),
            Outline = new ResolvedOutline.Visible(new SrgbColor(0xAA, 0xBB, 0xCC), 2, OutlineDash.Dash),
            RotationDeg = 4,
            Text = new ResolvedTextLayout(),
        };
        var plan = Plan(7, 30, 40, 200, 100, 24);

        var preview = CanvasTransformPreviewComposer.Compose([source], plan)[7]
            .Should().BeOfType<DrawOp.Shape>().Subject;

        preview.BoundsDip.Should().Be(new LayoutRect(30, 40, 200, 100));
        preview.RotationDeg.Should().Be(24);
        preview.Fill.Should().BeSameAs(source.Fill);
        preview.Outline.Should().BeSameAs(source.Outline);
        preview.Text.Should().BeSameAs(source.Text);
        preview.Geometry.Contours.Single().Start.Should().Be(new LayoutPoint(30, 40));
        preview.Geometry.Contours.Single().Segments[^1].End.Should().Be(new LayoutPoint(30, 140));
    }

    [Fact]
    public void Compose_PictureClone_ResizesFrameAndPreservesPictureEffects()
    {
        var source = new DrawOp.Picture
        {
            ShapeId = 11,
            Bytes = [1, 2, 3],
            ContentType = "image/png",
            DestDip = new LayoutRect(5, 6, 20, 30),
            RotationDeg = 8,
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.3,
            CropBottom = 0.4,
            PictureFrameGeometry = "roundRect",
        };
        var plan = Plan(11, 50, 60, 80, 90, 38);

        var preview = CanvasTransformPreviewComposer.Compose([source], plan)[11]
            .Should().BeOfType<DrawOp.Picture>().Subject;

        preview.DestDip.Should().Be(new LayoutRect(50, 60, 80, 90));
        preview.RotationDeg.Should().Be(38);
        preview.Bytes.Should().BeSameAs(source.Bytes);
        preview.CropLeft.Should().Be(source.CropLeft);
        preview.CropBottom.Should().Be(source.CropBottom);
        preview.PictureFrameGeometry.Should().Be(source.PictureFrameGeometry);
    }

    [Fact]
    public void Compose_UsesShapeIdAndSkipsUnsupportedOps()
    {
        var source = new DrawOp.Shape
        {
            ShapeId = 2,
            BoundsDip = new LayoutRect(0, 0, 10, 10),
            Geometry = ShapeGeometry.Empty,
        };
        var unrelated = new DrawOp.Background
        {
            BoundsDip = new LayoutRect(0, 0, 100, 100),
        };

        var previews = CanvasTransformPreviewComposer.Compose(
            [unrelated, source],
            Plan(2, 10, 20, 30, 40, 0));

        previews.Keys.Should().ContainSingle().Which.Should().Be(2);
        CanvasTransformPreviewComposer.TryGetShapeId(source, out var shapeId).Should().BeTrue();
        shapeId.Should().Be(2);
        CanvasTransformPreviewComposer.TryGetShapeId(unrelated, out _).Should().BeFalse();
    }

    private static CanvasMultiTransformPlan Plan(
        uint shapeId,
        double x,
        double y,
        double width,
        double height,
        double rotation) =>
        new(
            [new CanvasShapeTransform(
                shapeId,
                SlideTransformCore.DipToEmu(x),
                SlideTransformCore.DipToEmu(y),
                SlideTransformCore.DipToEmu(width),
                SlideTransformCore.DipToEmu(height),
                rotation)],
            [new CanvasShapeTransformPreview(
                shapeId,
                new SlideScreenRect(x, y, width, height),
                rotation)],
            new SlideScreenRect(x, y, width, height),
            rotation);
}
