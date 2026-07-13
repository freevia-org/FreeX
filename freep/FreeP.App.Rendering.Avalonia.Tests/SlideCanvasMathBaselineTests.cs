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
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(System.Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static MathNode ParseOmml(string oMathInner)
    {
        var xml = $"<m:oMath xmlns:m=\"{M}\">{oMathInner}</m:oMath>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

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
    public async Task RenderParaWithMath_EqArrayAlignment_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "equation-array alignment must be resolved in the shared MathBox plan before Avalonia draws it");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "E = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render aligned m:eqArr math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_PreSubSup_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = new MathNode.PreSubSup(
                    new MathNode.Run("x"),
                    new MathNode.Rad(null, new MathNode.Run("i")),
                    new MathNode.Run("2"));
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "x", "i", "2" },
                    "m:sPre glyphs must come from the shared MathBox recursion before Avalonia draws them");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "P = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(240, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:sPre math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_AccentAndBar_UseSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = new MathNode.Row(new MathNode[]
                {
                    new MathNode.Acc("~", new MathNode.Run("x")),
                    new MathNode.Run("+"),
                    new MathNode.Bar(new MathNode.Run("y"), isOver: false)
                });
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "~", "x", "+", "y" },
                    "m:acc and m:bar glyphs must come from the shared MathBox plan before Avalonia draws them");
                ops.OfType<MathDrawOp.DrawHRule>().Should().ContainSingle(
                    "m:bar must emit a shared horizontal rule consumed by WPF and Avalonia");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "A = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:acc and m:bar math from the shared MathBox plan without host-specific layout branching");
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
                        new MathNode.Rad(null, new MathNode.Run("x"))),
                    strikeHorizontal: true,
                    strikeVertical: true,
                    strikeBottomLeftToTopRight: true,
                    strikeTopLeftToBottomRight: true);
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(8,
                    "borderBox side and strike selection must be resolved in the shared math plan before Avalonia draws it");

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

    [Fact]
    public async Task RenderParaWithMath_BoxOperatorEmulator_UsesSharedSpacingPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "m:boxPr/m:opEmu spacing must be resolved in the shared math plan before Avalonia draws it");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "E = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:box operator-emulator spacing from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_HiddenPhantom_UsesSharedMetricOnlyPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = new MathNode.Phantom(
                    new MathNode.Frac(
                        new MathNode.Run("1"),
                        new MathNode.Rad(null, new MathNode.Run("x"))),
                    show: false);
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Should().BeEmpty(
                    "hidden m:phant glyphs must be removed in the shared MathBox plan before Avalonia draws");
                ops.Should().BeEmpty("this hidden phantom only contains glyph/radical descendants, so no draw ops should remain");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "P = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render hidden m:phant metric-only math without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_RadicalHiddenDegree_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "m:radPr/m:degHide must be resolved in the shared MathBox plan before Avalonia draws");
                ops.OfType<MathDrawOp.DrawRadical>().Should().ContainSingle();

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "R = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render hidden-degree radical math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_GroupChr_UsesSharedGlyphPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:groupChr>" +
                    "<m:groupChrPr><m:pos m:val=\"bot\"/></m:groupChrPr>" +
                    "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                    "</m:groupChr>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain("\u23DF",
                    "missing bottom m:groupChrPr/m:chr should resolve to a shared underbrace glyph before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "G = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render group-character math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_NaryGrow_UsesSharedScaledOperatorPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:nary>" +
                    "<m:naryPr><m:chr m:val=\"S\"/><m:grow/></m:naryPr>" +
                    "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                    "</m:nary>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "S").FontSizePt.Should().BeGreaterThan(27.0,
                    "m:naryPr/m:grow must be resolved in the shared MathBox plan before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "N = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render grow-enabled n-ary math from the shared MathBox plan without host-specific layout branching");
    }
}
