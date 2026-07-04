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
    public void RenderParaWithMath_BorderBoxNestedMath_UsesSharedLinePlan_DoesNotThrow()
    {
        var mathNode = new MathNode.BorderBox(
            new MathNode.Frac(
                new MathNode.Run("1"),
                new MathNode.Rad(null, new MathNode.Run("x"))));
        var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
        var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(4,
            "borderBox side selection must be resolved in the shared math plan before WPF draws it");

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
}
