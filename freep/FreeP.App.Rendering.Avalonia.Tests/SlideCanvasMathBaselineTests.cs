using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.App.Compositor.MathLayout;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// HB4 tests: math and adjacent inline text must share a common baseline, not
/// be top-aligned. Covers <see cref="SlideCanvas.ComputeBaselineY"/> /
/// <see cref="SlideCanvas.ComputeRunTopY"/> (the pure baseline arithmetic,
/// mirrored from FreeP.App.Rendering.Wpf for parity) and a smoke test that
/// <see cref="SlideCanvas.RenderParaWithMath"/> draws without throwing for a
/// mixed text+math paragraph using a real headless DrawingContext.
/// </summary>
public sealed class SlideCanvasMathBaselineTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(System.Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    // ── Pure baseline arithmetic (HB4) — mirrors the WPF-side test for parity ──

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
        const double startY = 50;
        const double textAscent = 18.0;
        double mathAscent = 40.0; // taller math box (e.g. a fraction numerator + bar)

        double lineAscent = System.Math.Max(textAscent, mathAscent);
        double baselineY = SlideCanvas.ComputeBaselineY(startY, lineAscent);

        double textTopY = SlideCanvas.ComputeRunTopY(baselineY, textAscent);
        double mathTopY = SlideCanvas.ComputeRunTopY(baselineY, mathAscent);

        (textTopY == mathTopY).Should().BeFalse(
            "text and math runs with different ascents must NOT be drawn at the same top Y (that was the HB4 bug)");

        (textTopY + textAscent).Should().BeApproximately(mathTopY + mathAscent, 0.0001,
            "both runs must share exactly one baseline");

        (textTopY + textAscent).Should().BeApproximately(baselineY, 0.0001);
    }

    // ── Live-renderer smoke test (real headless DrawingContext, no throw) ──

    [Fact]
    public async Task RenderParaWithMath_MixedTextAndFraction_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = new MathNode.Frac(new MathNode.Run("1"), new MathNode.Run("x"));
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "f(x) = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(200, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("rendering a mixed text+math paragraph must not throw");
    }

    [Fact]
    public async Task RenderParaWithMath_Matrix_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "matrix glyphs must come from the shared MathBox plan before Avalonia draws them");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "M = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(240, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render matrix math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_BorderBoxNestedMath_UsesSharedLinePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = new MathNode.BorderBox(
                    new MathNode.Frac(
                        new MathNode.Run("1"),
                        new MathNode.Rad(null, new MathNode.Run("x"))));
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(4,
                    "borderBox side selection must be resolved in the shared math plan before Avalonia draws it");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "B = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render borderBox math from the shared MathBox line plan without host-specific layout branching");
    }
}
