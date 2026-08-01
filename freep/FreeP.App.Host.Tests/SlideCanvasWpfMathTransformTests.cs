using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class SlideCanvasWpfMathTransformTests
{
    [StaFact]
    public void RealOmmlShapePreview_ResizesRotatesAndClears()
    {
        var presentation = MakePresentation();
        var slide = presentation.Slides[0];
        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = slide,
        };

        var sourceShape = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Single(shape => shape.ShapeId == 31);
        var sourceRun = sourceShape.Text!.Paragraphs.Single().Runs.Single();
        sourceRun.IsMathRun.Should().BeTrue();
        sourceRun.MathLayout!.Metrics.Width.Should().BeGreaterThan(0);
        sourceRun.MathLayout.Metrics.Height.Should().BeGreaterThan(0);

        var plan = new CanvasMultiTransformPlan(
            [new CanvasShapeTransform(31, 300 * 9525L, 180 * 9525L, 300 * 9525L, 150 * 9525L, 35)],
            [new CanvasShapeTransformPreview(31, new SlideScreenRect(300, 180, 300, 150), 35)],
            new SlideScreenRect(300, 180, 300, 150),
            35);
        var previewShape = CanvasTransformPreviewComposer.Compose([sourceShape], plan)[31]
            .Should().BeOfType<DrawOp.Shape>().Subject;
        previewShape.BoundsDip.Should().Be(new LayoutRect(300, 180, 300, 150));
        previewShape.RotationDeg.Should().Be(35);
        previewShape.Text.Should().BeSameAs(sourceShape.Text);
        previewShape.Text!.Paragraphs.Single().Runs.Single().MathLayout.Should().BeSameAs(sourceRun.MathLayout);
        previewShape.Geometry.Contours.Single().Start.Should().Be(new LayoutPoint(300, 180));
        previewShape.Geometry.Contours.Single().Segments[^1].End.Should().Be(new LayoutPoint(300, 330));

        var baseline = RenderPixels(canvas);
        canvas.UpdateTransformPreview(plan);
        var transformed = RenderPixels(canvas, refresh: false);
        canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        var cleared = RenderPixels(canvas, refresh: false);

        CountPixelDifferences(baseline, transformed, 960, 80, 80, 460, 380)
            .Should().BeGreaterThan(0, "the real math shape must move, resize, and rotate during preview");
        CountPixelDifferences(baseline, transformed, 960, 150, 90, 520, 350)
            .Should().BeGreaterThan(0, "the transformed math glyphs must be visible in the preview frame");
        CountPixelDifferences(baseline, cleared, 960, 0, 0, 960, 540)
            .Should().Be(0, "clearing the transient math preview should restore the composed slide");
    }

    private static Presentation MakePresentation()
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
            OffsetXEmu = 120 * 9525L,
            OffsetYEmu = 120 * 9525L,
            ExtentCxEmu = 180 * 9525L,
            ExtentCyEmu = 90 * 9525L,
            TextBody = body,
        });

        return presentation;
    }

    private static byte[] RenderPixels(SlideCanvas canvas, bool refresh = true)
    {
        const int width = 960;
        const int height = 540;
        if (refresh)
            canvas.Refresh();
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int CountPixelDifferences(byte[] first, byte[] second, int stridePixels, int left, int top, int right, int bottom)
    {
        var differences = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * stridePixels + x) * 4;
                if (first[offset] != second[offset]
                    || first[offset + 1] != second[offset + 1]
                    || first[offset + 2] != second[offset + 2]
                    || first[offset + 3] != second[offset + 3])
                {
                    differences++;
                }
            }
        }

        return differences;
    }
}
