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
    public void Compose_ChartClone_ResizesFrameAndCarriesRotationAndResolvedPaint()
    {
        var source = new DrawOp.Chart
        {
            ShapeId = 17,
            BoundsDip = new LayoutRect(5, 6, 40, 30),
            RotationDeg = 8,
            ChartShape = new ChartShape { ChartType = ChartType.LineMarkers },
            SeriesColors = [new SrgbColor(0x11, 0x22, 0x33)],
            FillPlans = new ChartFillPlanSet(),
            ChartAreaFill = new ChartFillPlan(new SrgbColor(0x44, 0x55, 0x66), 200),
            PlotAreaOutline = new ChartStrokePlan(new SrgbColor(0xAA, 0xBB, 0xCC), 255, 1),
        };
        var plan = Plan(17, 50, 60, 80, 90, 38);

        var preview = CanvasTransformPreviewComposer.Compose([source], plan)[17]
            .Should().BeOfType<DrawOp.Chart>().Subject;

        preview.BoundsDip.Should().Be(new LayoutRect(50, 60, 80, 90));
        preview.RotationDeg.Should().Be(38);
        preview.ChartShape.Should().BeSameAs(source.ChartShape);
        preview.SeriesColors.Should().BeSameAs(source.SeriesColors);
        preview.FillPlans.Should().BeSameAs(source.FillPlans);
        preview.ChartAreaFill.Should().Be(source.ChartAreaFill);
        preview.PlotAreaOutline.Should().Be(source.PlotAreaOutline);
    }

    [Fact]
    public void Compose_RealOmmlShapeClone_TransformsFrameAndPreservesResolvedMathLayout()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = new TextBody { Wrap = false };
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "x+1",
                    FontFamily = "Cambria Math",
                    FontSizePt = 24,
                    Math = new MathRunInfo
                    {
                        RawXml = "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>1</m:t></m:r></m:oMath>"
                    }
                }
            }
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 31,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100 * 9525L,
            OffsetYEmu = 80 * 9525L,
            ExtentCxEmu = 180 * 9525L,
            ExtentCyEmu = 90 * 9525L,
            RotationDeg = 7,
            TextBody = body,
        });

        var source = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Single(shape => shape.ShapeId == 31);
        var sourceRun = source.Text!.Paragraphs.Single().Runs.Single();
        sourceRun.IsMathRun.Should().BeTrue();
        sourceRun.MathLayout!.Metrics.Width.Should().BeGreaterThan(0);
        sourceRun.MathLayout.Metrics.Height.Should().BeGreaterThan(0);

        var plan = Plan(31, 320, 210, 300, 150, 35);
        var preview = CanvasTransformPreviewComposer.Compose([source], plan)[31]
            .Should().BeOfType<DrawOp.Shape>().Subject;

        preview.BoundsDip.Should().Be(new LayoutRect(320, 210, 300, 150));
        preview.RotationDeg.Should().Be(35);
        preview.Text.Should().BeSameAs(source.Text);
        preview.Text.Paragraphs.Single().Runs.Single().MathLayout.Should()
            .BeSameAs(sourceRun.MathLayout);
        preview.Geometry.Contours.Single().Start.Should().Be(new LayoutPoint(320, 210));
        preview.Geometry.Contours.Single().Segments[^1].End.Should().Be(new LayoutPoint(320, 360));
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
