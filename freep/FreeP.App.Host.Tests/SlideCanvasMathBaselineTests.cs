using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

/// <summary>
/// HB4 tests: math and adjacent inline text must share a common baseline, not
/// be top-aligned. Covers <see cref="SlideCanvas.ComputeBaselineY"/> /
/// <see cref="SlideCanvas.ComputeRunTopY"/> (the pure baseline arithmetic) and
/// a smoke test that <see cref="SlideCanvas.RenderParaWithMath"/> draws
/// without throwing for a mixed text+math paragraph.
/// STA is required because the WPF DrawingContext/FormattedText types need it.
/// </summary>
public sealed class SlideCanvasMathBaselineTests
{
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static MathNode ParseOmml(string oMathInner)
    {
        var xml = $"<m:oMath xmlns:m=\"{M}\">{oMathInner}</m:oMath>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    // ── Pure baseline arithmetic (HB4) ──────────────────────────────────────

    [Fact]
    public void ComputeBaselineY_AddsLineAscentToParagraphTop()
    {
        double baselineY = SlideCanvas.ComputeBaselineY(startY: 100, lineAscent: 24);
        baselineY.Should().Be(124);
    }

    [Fact]
    public void ComputeRunTopY_PlacesRunSoItsAscentLandsOnBaseline()
    {
        double runTopY = SlideCanvas.ComputeRunTopY(baselineY: 124, runAscent: 24);
        runTopY.Should().Be(100);
    }

    [Fact]
    public void MixedTextAndMath_ShareCommonBaseline_NotTopAligned()
    {
        // A paragraph with a plain-text run (ascent ~ font-based) and a math
        // run whose box Ascent is taller (e.g. a fraction) must NOT draw both
        // at the same top Y (startY) — their computed top Y's should differ
        // by exactly the difference in their ascents, and both baselines
        // (top + ascent) must be equal.
        const double startY = 50;
        const double textAscent = 18.0;   // a representative FormattedText.Baseline value
        double mathAscent = 40.0;         // taller math box (e.g. a fraction numerator + bar)

        double lineAscent = System.Math.Max(textAscent, mathAscent);
        double baselineY = SlideCanvas.ComputeBaselineY(startY, lineAscent);

        double textTopY = SlideCanvas.ComputeRunTopY(baselineY, textAscent);
        double mathTopY = SlideCanvas.ComputeRunTopY(baselineY, mathAscent);

        // Top-aligned (the HB4 bug) would mean textTopY == mathTopY == startY.
        (textTopY == mathTopY).Should().BeFalse(
            "text and math runs with different ascents must NOT be drawn at the same top Y (that was the HB4 bug)");

        (textTopY + textAscent).Should().BeApproximately(mathTopY + mathAscent, 0.0001,
            "both runs must share exactly one baseline");

        (textTopY + textAscent).Should().BeApproximately(baselineY, 0.0001);
    }

    // ── Live-renderer smoke test (real DrawingContext, no throw) ───────────

    [StaFact]
    public void RenderParaWithMath_MixedTextAndFraction_DoesNotThrow()
    {
        var mathNode = new MathNode.Frac(new MathNode.Run("1"), new MathNode.Run("x"));
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "f(x) = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_Matrix_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        var matrix = new MathNode.Matrix(
            new[]
            {
                new MathNode[] { new MathNode.Run("wide"), new MathNode.Run("wide") },
                new MathNode[] { new MathNode.Run("x"), new MathNode.Run("yy") }
            },
            new[]
            {
                MathNode.Matrix.MatrixColumnAlignment.Left,
                MathNode.Matrix.MatrixColumnAlignment.Right
            });
        var mathBox = MathLayoutEngine.Layout(matrix, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "wide", "x", "yy" },
            "matrix glyphs must come from the shared MathBox plan before WPF draws them");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "M = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_EqArrayAlignment_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        var mathNode = new MathNode.EqArray(
            new MathNode[]
            {
                new MathNode.Row(new MathNode[] { new MathNode.Run("mmmm"), new MathNode.Run("=1") }),
                new MathNode.Row(new MathNode[] { new MathNode.Run("x"), new MathNode.Run("=22") })
            },
            new int?[] { 1, 1 });
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "mmmm", "x", "=22" },
            "equation-array alignment must be resolved in the shared MathBox plan before WPF draws it");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "E = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_PreSubSup_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        var mathNode = new MathNode.PreSubSup(
            new MathNode.Run("x"),
            new MathNode.Rad(null, new MathNode.Run("i")),
            new MathNode.Run("2"));
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "x", "i", "2" },
            "m:sPre glyphs must come from the shared MathBox recursion before WPF draws them");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "P = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_FunctionApply_UsesSharedUprightNamePlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:func>" +
            "<m:fName><m:r><m:t>sin</m:t></m:r></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        ops.Should().HaveCount(2, "m:func should resolve to function-name and argument glyphs in the shared draw plan");
        ops[0].Text.Should().Be("sin");
        ops[0].IsItalic.Should().BeFalse("m:fName is an upright function operator before WPF draws it");
        ops[1].Text.Should().Be("x");
        ops[1].IsItalic.Should().BeTrue("the function argument keeps ordinary math-run styling");
        ops[1].X.Should().BeGreaterThan(ops[0].X + ops[0].Text.Length,
            "the shared function layout must carry visible name-to-argument spacing");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "F = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_RunWithMultipleTextChildren_UsesSharedFullTextPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml("<m:r><m:t>sin</m:t><m:t>^2</m:t><m:t>x</m:t></m:r>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single()
            .Text.Should().Be("sin^2x",
                "split m:t children must be joined in the shared OMML plan before WPF draws");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "R = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_BorderBoxNestedMath_UsesSharedLinePlan_DoesNotThrow()
    {
        var mathNode = new MathNode.BorderBox(
            new MathNode.Frac(
                new MathNode.Run("1"),
                new MathNode.Rad(null, new MathNode.Run("x"))),
            strikeHorizontal: true,
            strikeVertical: true,
            strikeBottomLeftToTopRight: true,
            strikeTopLeftToBottomRight: true);
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(8,
            "borderBox side and strike selection must be resolved in the shared math plan before WPF draws it");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "B = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_BorderBoxHiddenEdgesAndDiagonalStrike_UsesSharedLinePlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:borderBox>" +
            "<m:borderBoxPr><m:hideTop/><m:hideBot/><m:strikeTLBR/></m:borderBoxPr>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:borderBox>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var lines = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawLine>()
            .ToList();
        lines.Should().HaveCount(3,
            "hidden borderBox top/bottom edges and TLBR strike geometry must be shared before WPF draws");
        lines.Should().ContainSingle(line => Math.Abs(line.X1 - line.X2) > 0.01 && line.Y2 > line.Y1);
        lines.Should().NotContain(line => Math.Abs(line.Y1 - line.Y2) < 0.01);

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "B = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_BoxOperatorEmulator_UsesSharedSpacingPlan_DoesNotThrow()
    {
        var ordinaryRow = new MathNode.Row(new MathNode[]
        {
            new MathNode.Run("a"),
            new MathNode.Box(new MathNode.Run("==", isItalic: false)),
            new MathNode.Run("b")
        });
        var emulatorRow = new MathNode.Row(new MathNode[]
        {
            new MathNode.Run("a"),
            new MathNode.Box(new MathNode.Run("==", isItalic: false), operatorEmulator: true),
            new MathNode.Run("b")
        });

        var ordinaryBox = MathLayoutEngine.Layout(ordinaryRow, "Cambria Math", 18.0);
        var mathBox = MathLayoutEngine.Layout(emulatorRow, "Cambria Math", 18.0);
        mathBox.Metrics.Width.Should().BeGreaterThan(ordinaryBox.Metrics.Width,
            "m:boxPr/m:opEmu spacing must be resolved in the shared math plan before WPF draws it");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "E = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_TransparentPhantomMultiGlyphRelation_UsesSharedSpacingPlan_DoesNotThrow()
    {
        var packedRow = new MathNode.Row(new MathNode[]
        {
            new MathNode.Run("x"),
            new MathNode.Phantom(new MathNode.Run("->", isItalic: false), show: false, zeroWidth: true),
            new MathNode.Run("y")
        });
        var transparentRow = new MathNode.Row(new MathNode[]
        {
            new MathNode.Run("x"),
            new MathNode.Phantom(new MathNode.Run("->", isItalic: false), show: false, zeroWidth: true, transparentSpacing: true),
            new MathNode.Run("y")
        });

        var packedBox = MathLayoutEngine.Layout(packedRow, "Cambria Math", 18.0);
        var mathBox = MathLayoutEngine.Layout(transparentRow, "Cambria Math", 18.0);
        mathBox.Metrics.Width.Should().BeGreaterThan(packedBox.Metrics.Width,
            "m:phantPr/m:transp multi-glyph relation spacing must be resolved in the shared math plan before WPF draws it");

        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x", "y" },
            "hidden transparent phantom relation glyphs must not reach the WPF renderer");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "P = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_HiddenPhantom_UsesSharedMetricOnlyPlan_DoesNotThrow()
    {
        var mathNode = new MathNode.Phantom(
            new MathNode.Frac(
                new MathNode.Run("1"),
                new MathNode.Rad(null, new MathNode.Run("x"))),
            show: false);
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Should().BeEmpty(
            "hidden m:phant glyphs must be removed in the shared MathBox plan before WPF draws");
        ops.Should().BeEmpty("this hidden phantom only contains glyph/radical descendants, so no draw ops should remain");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "P = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_RadicalHiddenDegree_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:rad>" +
            "<m:radPr><m:degHide/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x" },
            "m:radPr/m:degHide must be resolved in the shared MathBox plan before WPF draws");
        ops.OfType<MathDrawOp.DrawRadical>().Should().ContainSingle();

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "R = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_GroupChr_UsesSharedGlyphPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:groupChr>" +
            "<m:groupChrPr><m:pos m:val=\"bot\"/></m:groupChrPr>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:groupChr>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain("\u23DF",
            "missing bottom m:groupChrPr/m:chr should resolve to a shared underbrace glyph before WPF draws");

        var wideMathNode = ParseOmml(
            "<m:groupChr>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>y</m:t></m:r></m:e>" +
            "</m:groupChr>");
        var wideMathBox = MathLayoutEngine.Layout(wideMathNode, "Cambria Math", 18.0);
        MathBoxRenderPlanner.Plan(wideMathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single(g => g.Text == "\u23DE")
            .FontSizePt.Should().BeGreaterThan(18.0 * 0.75,
                "wide m:groupChr braces must grow in the shared plan before WPF draws them");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "G = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_NaryGrow_UsesSharedScaledOperatorPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:nary>" +
            "<m:naryPr><m:chr m:val=\"S\"/><m:grow/></m:naryPr>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:nary>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "S").FontSizePt.Should().BeGreaterThan(27.0,
            "m:naryPr/m:grow must be resolved in the shared MathBox plan before WPF draws");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "N = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_OneSidedDelimiters_UseSingleSharedBracketPlan_DoesNotThrow()
    {
        var openSuppressed = MathLayoutEngine.Layout(
            ParseOmml(
                "<m:d>" +
                "<m:dPr><m:begChr m:val=\"\"/><m:endChr m:val=\")\"/></m:dPr>" +
                "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                "</m:d>"),
            "Cambria Math",
            18.0);
        var closeSuppressed = MathLayoutEngine.Layout(
            ParseOmml(
                "<m:d>" +
                "<m:dPr><m:begChr m:val=\"(\"/><m:endChr m:val=\"\"/></m:dPr>" +
                "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                "</m:d>"),
            "Cambria Math",
            18.0);

        MathBoxRenderPlanner.Plan(openSuppressed, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawBracket>()
            .Should().ContainSingle().Which.Character.Should().Be(")");
        MathBoxRenderPlanner.Plan(closeSuppressed, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawBracket>()
            .Should().ContainSingle().Which.Character.Should().Be("(");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "D = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = openSuppressed },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_MathAlphabetStyleVariants_UseSharedUnicodeGlyphPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:r><m:rPr><m:scr m:val=\"script\"/><m:sty m:val=\"b\"/></m:rPr><m:t>Aa</m:t></m:r>" +
            "<m:r><m:rPr><m:scr m:val=\"sans-serif\"/><m:sty m:val=\"bi\"/></m:rPr><m:t>Zz</m:t></m:r>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);

        MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should().Equal(new[] { "\U0001D4D0\U0001D4EA", "\U0001D655\U0001D66F" },
                "styled m:scr variants must be resolved in the shared MathBox plan before WPF draws them");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "S = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_SubSupAlignScripts_UsesSharedRightAlignedScriptPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:sSubSup>" +
            "<m:sSubSupPr><m:alnScr/></m:sSubSupPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sub><m:r><m:t>wide</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSubSup>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "x", "2", "wide" },
            "m:sSubSupPr/m:alnScr glyph ordering must be resolved in the shared MathBox plan before WPF draws it");
        glyphs.Single(g => g.Text == "2").X.Should().BeGreaterThan(glyphs.Single(g => g.Text == "wide").X,
            "the shorter superscript should be shifted right by the shared alignment plan before WPF draws");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "S = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_LimitUpperAndLower_UseSharedCenteredLimitPlan_DoesNotThrow()
    {
        var lowerNode = ParseOmml(
            "<m:limLow><m:e><m:r><m:t>lim</m:t></m:r></m:e><m:lim><m:r><m:t>x->0</m:t></m:r></m:lim></m:limLow>");
        var upperNode = ParseOmml(
            "<m:limUpp><m:e><m:r><m:t>max</m:t></m:r></m:e><m:lim><m:r><m:t>S</m:t></m:r></m:lim></m:limUpp>");
        var lowerBox = MathLayoutEngine.Layout(lowerNode, "Cambria Math", 18.0);
        var upperBox = MathLayoutEngine.Layout(upperNode, "Cambria Math", 18.0);

        MathBoxRenderPlanner.Plan(lowerBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should().Equal(new[] { "lim", "x->0" },
                "m:limLow base and limit ordering must be resolved in the shared MathBox plan before WPF draws it");
        MathBoxRenderPlanner.Plan(upperBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should().Equal(new[] { "S", "max" },
                "m:limUpp limit and base ordering must be resolved in the shared MathBox plan before WPF draws it");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "L = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = lowerBox },
                new ResolvedRun { Text = " U = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = upperBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void RenderParaWithMath_ArgumentSize_UsesSharedScaledGlyphPlan_DoesNotThrow()
    {
        var mathNode = ParseOmml(
            "<m:sSup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sup><m:argPr><m:argSz m:val=\"1\"/></m:argPr><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSup>");
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "x", "2" },
            "m:argPr/m:argSz glyph ordering must be resolved in the shared MathBox plan before WPF draws it");
        glyphs.Single(g => g.Text == "2").FontSizePt.Should().BeApproximately(18.0, 0.001,
            "the shared plan should carry the superscript argument's +1 script-size adjustment to WPF");

        var para = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "A = ", FontFamily = "Calibri", FontSizePt = 18.0, Color = SrgbColor.Black },
                new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
            }
        };

        var visual = new DrawingVisual();
        var act = () =>
        {
            using var dc = visual.RenderOpen();
            SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
        };

        act.Should().NotThrow();
    }
}
