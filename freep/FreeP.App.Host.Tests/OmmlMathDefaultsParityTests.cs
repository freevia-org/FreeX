using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 100 WPF-host proof that package defaults reach the shared math plan and
/// that local OMML properties override them before WPF draws.
/// </summary>
public sealed class OmmlMathDefaultsParityTests
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    [StaFact]
    public void Wpf_RendersSharedMathPlanWithPackageFallbackAndLocalOverride()
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
        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(drawingContext, paragraph, 10, 20);
    }

    [StaFact]
    public void Wpf_DefJc_UsesSharedInheritedParagraphPlan()
    {
        var node = OmmlParser.ParsePowerPoint(
            "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "x",
            new MathNode.MathProperties(
                DefaultJustification: MathNode.MathParagraphJustification.Right,
                DisplayDefaults: true));
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
        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(drawingContext, paragraph, 10, 20);
    }

    [StaFact]
    public void Wpf_AbsentDispDef_IgnoresDefJcAndKeepsParagraphDefaults()
    {
        var node = OmmlParser.ParsePowerPoint(
            "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
            "<m:mathPr><m:defJc m:val=\"right\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "x");
        var natural = MathLayoutEngine.Layout(
            ((MathNode.MathParagraph)node).Content, "Cambria Math", 18.0);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", 18.0, paragraphWidthDip: 180);
        var glyph = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        glyph.X.Should().BeApproximately(10 + (180 - natural.Metrics.Width) / 2.0, 0.01);

        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(drawingContext, new ResolvedParagraph
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
        }, 10, 20);
    }

    [StaFact]
    public void Wpf_UsesDocumentIntegralLimitDefaultInVisibleSharedPlacement()
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

        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(
            drawingContext,
            new ResolvedParagraph { Runs = new[] { run } },
            10,
            20);
    }

    [StaFact]
    public void Wpf_DocumentMathMarginsChangeSharedLayoutAndRender()
    {
        var withoutMargins = ComposeMathRun(
            new OmmlMathProperties(DisplayDefaults: true),
            "<m:mathPr><m:defJc m:val=\"left\"/></m:mathPr>");
        var withMargins = ComposeMathRun(
            new OmmlMathProperties(DisplayDefaults: true, LeftMargin: "720", RightMargin: "360"),
            "<m:mathPr><m:defJc m:val=\"left\"/></m:mathPr>");

        var withoutGlyph = MathBoxRenderPlanner.Plan(
                withoutMargins.MathLayout!, 0, 0, SrgbColor.Black, withoutMargins.FontFamily)
            .OfType<MathDrawOp.DrawGlyph>().Single();
        var withGlyph = MathBoxRenderPlanner.Plan(
                withMargins.MathLayout!, 0, 0, SrgbColor.Black, withMargins.FontFamily)
            .OfType<MathDrawOp.DrawGlyph>().Single();

        withGlyph.X.Should().BeApproximately(withoutGlyph.X + 48, 0.01);
        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(
            drawingContext,
            new ResolvedParagraph { Runs = new[] { withMargins } },
            10,
            20);
    }

    [StaFact]
    public void Wpf_DocumentMathWrapPropertiesUseSharedContinuationPlan()
    {
        var run = ComposeMathRun(
            new OmmlMathProperties(DisplayDefaults: true, WrapIndent: "15", WrapRight: true),
            "<m:mathPr><m:defJc m:val=\"left\"/></m:mathPr>",
            "<m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r>" +
            "<m:r><m:t>y</m:t></m:r><m:r><m:t>+</m:t></m:r>" +
            "<m:r><m:t>z</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(run.MathLayout is not null
                ? OmmlParser.ParsePowerPoint(
                    "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:mathPr><m:dispDef/><m:defJc m:val=\"left\"/></m:mathPr>" +
                    "<m:oMath><m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>y</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>z</m:t></m:r></m:oMath></m:oMathPara>",
                    "x",
                    new MathNode.MathProperties(DisplayDefaults: true, WrapIndentTwips: 15, WrapRight: true))
                : throw new InvalidOperationException(),
            "Cambria Math",
            18.0,
            paragraphWidthDip: 60);
        var wrapped = Assert.IsType<MathBox.Container>(
            Assert.IsType<MathBox.Container>(layout.Children[0]).Children[0]);
        wrapped.Children.Should().HaveCount(2);
        wrapped.Children[1].X.Should().BeApproximately(60 - wrapped.Children[1].Metrics.Width, 0.01);

        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(
            drawingContext,
            new ResolvedParagraph { Runs = new[] { run } },
            10,
            20);
    }

    [StaFact]
    public void Wpf_MultipleDisplayEquationsUseSharedRunAlignmentPlan()
    {
        var node = OmmlParser.ParsePowerPoint(
            "<m:oMathPara xmlns:m=\"" + MathNamespace + "\">" +
            "<m:oMath><m:r><m:t>mmmm</m:t></m:r>" +
            "<m:r><m:rPr><m:aln/></m:rPr><m:t>=1</m:t></m:r></m:oMath>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r>" +
            "<m:r><m:rPr><m:aln/></m:rPr><m:t>=22</m:t></m:r></m:oMath>" +
            "</m:oMathPara>",
            "x");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", 18.0, paragraphWidthDip: 240);
        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToArray();

        glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(
            glyphs.Single(g => g.Text == "=22").X,
            0.01,
            "WPF must consume the shared run-alignment draw plan");

        var visual = new DrawingVisual();
        using var drawingContext = visual.RenderOpen();
        SlideCanvas.RenderParaWithMath(
            drawingContext,
            new ResolvedParagraph
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
            },
            10,
            20);
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
            Id = 101,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
            TextBody = body
        });

        return SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Single(shape => shape.ShapeId == 101)
            .Text!.Paragraphs.Single()
            .Runs.Single();
    }
}
