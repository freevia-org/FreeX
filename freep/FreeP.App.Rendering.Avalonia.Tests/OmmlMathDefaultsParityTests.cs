using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// Wave 100 Avalonia-host proof paired with the WPF test: both render the same
/// shared MathBox plan after package/local default precedence is resolved.
/// </summary>
public sealed class OmmlMathDefaultsParityTests
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_RendersSharedMathPlanWithPackageFallbackAndLocalOverride()
    {
        Exception? thrown = null;
        await Session.Dispatch(() =>
        {
            try
            {
                var run = ComposeMathRun(
                    new OmmlMathProperties(MathFontFamily: "Arial"),
                    "<m:mathPr><m:mathFont m:val=\"Times New Roman\"/></m:mathPr>");

                var glyph = MathBoxRenderPlanner.Plan(
                        run.MathLayout!,
                        0,
                        0,
                        SrgbColor.Black,
                        run.FontFamily)
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();
                glyph.FontFamily.Should().Be("Times New Roman");

                var paragraph = new ResolvedParagraph { Runs = new[] { run } };
                var bitmap = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext drawingContext = bitmap.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(drawingContext, paragraph, 10, 20);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        thrown.Should().BeNull();
    }

    [Fact]
    public async Task Avalonia_DefJc_UsesSharedInheritedParagraphPlan()
    {
        Exception? thrown = null;
        await Session.Dispatch(() =>
        {
            try
            {
                var node = OmmlParser.ParsePowerPoint(
                    "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
                    "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
                    "x",
                    new MathNode.MathProperties(
                        DefaultJustification: MathNode.MathParagraphJustification.Right));
                var natural = MathLayoutEngine.Layout(
                    ((MathNode.MathParagraph)node).Content, "Cambria Math", 18.0);
                var layout = MathLayoutEngine.Layout(node, "Cambria Math", 18.0, paragraphWidthDip: 180);
                var glyph = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();

                glyph.X.Should().BeApproximately(10 + 180 - natural.Metrics.Width, 0.01);

                var paragraph = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun
                        {
                            Text = "x",
                            FontFamily = "Cambria Math",
                            MathLayout = layout,
                            Color = SrgbColor.Black,
                        },
                    },
                };
                var bitmap = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext drawingContext = bitmap.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(drawingContext, paragraph, 10, 20);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        thrown.Should().BeNull();
    }

    [Fact]
    public async Task Avalonia_UsesDocumentIntegralLimitDefaultInVisibleSharedPlacement()
    {
        Exception? thrown = null;
        await Session.Dispatch(() =>
        {
            try
            {
                var run = ComposeMathRun(
                    new OmmlMathProperties(IntegralLimitLocation: "undOvr"),
                    string.Empty,
                    "<m:nary><m:naryPr/>" +
                    "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                    "<m:sup><m:r><m:t>1</m:t></m:r></m:sup>" +
                    "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

                var glyphs = MathBoxRenderPlanner.Plan(
                        run.MathLayout!, 0, 0, SrgbColor.Black, run.FontFamily)
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToArray();
                var operatorGlyph = glyphs.Single(g => g.Text == "\u222B");
                glyphs.Single(g => g.Text == "1").Y.Should().BeLessThan(operatorGlyph.Y);
                glyphs.Single(g => g.Text == "0").Y.Should().BeGreaterThan(operatorGlyph.Y);

                var bitmap = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext drawingContext = bitmap.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(
                    drawingContext,
                    new ResolvedParagraph { Runs = new[] { run } },
                    10,
                    20);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        thrown.Should().BeNull();
    }

    private static ResolvedRun ComposeMathRun(
        OmmlMathProperties documentDefaults,
        string localProperties,
        string? mathContent = null)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.DocumentMathProperties = documentDefaults;
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "x",
                    Math = new MathRunInfo
                    {
                        RawXml =
                            "<m:oMathPara xmlns:m=\"" + MathNamespace + "\">" +
                            localProperties +
                            "<m:oMath>" +
                            (mathContent ?? "<m:r><m:t>x</m:t></m:r>") +
                            "</m:oMath></m:oMathPara>"
                    }
                }
            }
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 102,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
            TextBody = body
        });

        return SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Single(shape => shape.ShapeId == 102)
            .Text!.Paragraphs.Single()
            .Runs.Single();
    }
}
