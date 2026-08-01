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

    private static MathNode ParseOmmlParagraph(string oMathParaInner)
    {
        var xml = $"<m:oMathPara xmlns:m=\"{M}\">{oMathParaInner}</m:oMathPara>";
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
    public async Task RenderParaWithBaseline_WrapsAndPreservesOffsets_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "long ", FontFamily = "Arial", FontSizePt = 18, Color = SrgbColor.Black },
                        new ResolvedRun { Text = "script", FontFamily = "Arial", FontSizePt = 18, BaselineOffset = -25000, Color = SrgbColor.Black },
                        new ResolvedRun { Text = " text that wraps", FontFamily = "Arial", FontSizePt = 18, Color = SrgbColor.Black }
                    }
                };
                var rtb = new RenderTargetBitmap(new PixelSize(160, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithBaseline(dc, para, startX: 10, startY: 20, maxWidth: 70);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("wrapped baseline runs must render through the baseline-aware line owner");
    }

    [Fact]
    public async Task MeasureBaselineTextWidth_PreservesTrailingWhitespaceAdvance()
    {
        double withSpace = 0;
        double withoutSpace = 0;
        await Run(() =>
        {
            var run = new ResolvedRun
            {
                FontFamily = "Arial",
                FontSizePt = 18,
                Color = SrgbColor.Black
            };
            withoutSpace = SlideCanvas.MeasureBaselineTextWidth(run, "long", 1.0);
            withSpace = SlideCanvas.MeasureBaselineTextWidth(run, "long ", 1.0);
        });

        withSpace.Should().BeGreaterThan(
            withoutSpace,
            "baseline runs must retain the separator advance between independently drawn runs");
    }

    [Fact]
    public async Task RenderParaWithMath_FractionTypes_UseSharedDrawPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>" +
                    "<m:f><m:fPr><m:type m:val=\"noBar\"/></m:fPr><m:num><m:r><m:t>n</m:t></m:r></m:num><m:den><m:r><m:t>k</m:t></m:r></m:den></m:f>" +
                    "<m:f><m:fPr><m:type m:val=\"lin\"/></m:fPr><m:num><m:r><m:t>a</m:t></m:r></m:num><m:den><m:r><m:t>b</m:t></m:r></m:den></m:f>" +
                    "<m:f><m:fPr><m:type m:val=\"skw\"/></m:fPr><m:num><m:r><m:t>p</m:t></m:r></m:num><m:den><m:r><m:t>q</m:t></m:r></m:den></m:f>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");

                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "1", "2", "n", "k", "a", "/", "b", "p", "q" },
                    "all m:fPr/m:type glyphs must be resolved in the shared MathBox plan before Avalonia draws them");
                ops.OfType<MathDrawOp.DrawHRule>().Should().ContainSingle(
                    "only the default bar fraction should contribute a horizontal fraction rule");
                ops.OfType<MathDrawOp.DrawLine>().Should().ContainSingle(
                    "the skewed fraction should contribute one renderer-neutral diagonal line");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "F = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(340, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render OMML fraction type variants from the shared MathBox plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_MatrixColumnAlignmentCount_UsesSharedRepeatedAlignmentPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var matrixNode = ParseOmml(
                    "<m:m>" +
                    "<m:mPr><m:mcs>" +
                    "<m:mc><m:mcPr><m:count m:val=\"2\"/><m:aln m:val=\"left\"/></m:mcPr></m:mc>" +
                    "<m:mc><m:mcPr><m:aln m:val=\"right\"/></m:mcPr></m:mc>" +
                    "</m:mcs></m:mPr>" +
                    "<m:mr><m:e><m:r><m:t>wide</m:t></m:r></m:e><m:e><m:r><m:t>wide</m:t></m:r></m:e><m:e><m:r><m:t>wide</m:t></m:r></m:e></m:mr>" +
                    "<m:mr><m:e><m:r><m:t>x</m:t></m:r></m:e><m:e><m:r><m:t>y</m:t></m:r></m:e><m:e><m:r><m:t>z</m:t></m:r></m:e></m:mr>" +
                    "</m:m>");
                var mathBox = MathLayoutEngine.Layout(matrixNode, "Cambria Math", 18.0);
                var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();
                glyphs.Single(g => g.Text == "y").X.Should().BeApproximately(glyphs[1].X,
                    0.01,
                    "m:mcPr/m:count should repeat the left alignment into the second matrix column before Avalonia draws it");
                glyphs.Single(g => g.Text == "z").X.Should().BeGreaterThan(glyphs[2].X,
                    "the right-aligned repeated-count successor column should shift its short cell toward the right edge");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "M = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(280, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render repeated-count matrix alignment from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_MatrixPlaceholder_UsesSharedPlcHidePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var visibleNode = ParseOmml(
                    "<m:m>" +
                    "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e/></m:mr>" +
                    "</m:m>");
                var hiddenNode = ParseOmml(
                    "<m:m>" +
                    "<m:mPr><m:plcHide/></m:mPr>" +
                    "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e/></m:mr>" +
                    "</m:m>");
                var visibleBox = MathLayoutEngine.Layout(visibleNode, "Cambria Math", 18.0);
                var hiddenBox = MathLayoutEngine.Layout(hiddenNode, "Cambria Math", 18.0);

                MathBoxRenderPlanner.Plan(visibleBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "a", "\u25A1" },
                        "visible empty matrix cells must be resolved in the shared MathBox plan before Avalonia draws them");
                MathBoxRenderPlanner.Plan(hiddenBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "a" },
                        "m:plcHide must suppress placeholders in shared math layout, not in Avalonia renderer code");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "M = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = visibleBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(240, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render matrix placeholders from the shared MathBox plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_ManualBreakAlignment_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:r><m:t>title</m:t></m:r><m:r><m:rPr><m:brk m:alnAt=\"1\"/></m:rPr><m:t>mmmm</m:t></m:r><m:r><m:t>=1</m:t></m:r>" +
                    "<m:r><m:rPr><m:brk m:alnAt=\"1\"/></m:rPr><m:t>x</m:t></m:r><m:r><m:t>=22</m:t></m:r>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();

                glyphs.Select(g => g.Text).Should().Equal(new[] { "title", "mmmm", "=1", "x", "=22" },
                    "m:brk rows must be normalized to a shared equation-array draw plan before Avalonia draws them");
                glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(glyphs.Single(g => g.Text == "=22").X, 0.01,
                    "manual-break m:alnAt rows should share the same draw-plan X coordinate before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "B = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(300, 130));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render manually broken aligned OMML from the shared MathBox plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_FunctionApply_UsesSharedUprightNamePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                ops[0].IsItalic.Should().BeFalse("m:fName is an upright function operator before Avalonia draws it");
                ops[1].Text.Should().Be("x");
                ops[1].IsItalic.Should().BeTrue("the function argument keeps ordinary math-run styling");
                ops[1].X.Should().BeGreaterThan(ops[0].X + ops[0].Text.Length,
                    "the shared function layout must carry visible name-to-argument spacing");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "F = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(220, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:func math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_ScriptedFunctionApply_UsesSharedUprightNamePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:func>" +
                    "<m:fName><m:sSup><m:e><m:r><m:t>sin</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:fName>" +
                    "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                    "</m:func>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();

                ops.Select(g => g.Text).Should().Equal(new[] { "sin", "2", "x" },
                    "scripted m:func names should be resolved in the shared draw plan before Avalonia draws");
                ops[0].IsItalic.Should().BeFalse("the scripted function-name base is an upright operator");
                ops[2].IsItalic.Should().BeTrue("the function argument keeps ordinary math-run styling");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "F = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(240, 110));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render scripted m:func math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_RunWithMultipleTextChildren_UsesSharedFullTextPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml("<m:r><m:t>sin</m:t><m:t>^2</m:t><m:t>x</m:t></m:r>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single()
                    .Text.Should().Be("sin^2x",
                        "split m:t children must be joined in the shared OMML plan before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "R = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render split-text OMML runs from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_LiteralRun_UsesSharedUprightGlyphPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml("<m:r><m:rPr><m:lit/></m:rPr><m:t>x</m:t></m:r>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var op = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();

                op.Text.Should().Be("x");
                op.IsItalic.Should().BeFalse("m:lit literal style must be resolved in the shared draw plan before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "L = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(220, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:lit math from the shared MathBox plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_AccentBarOverline_UsesSharedHRulePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:acc>" +
                    "<m:accPr><m:chr m:val=\"&#x0305;\"/></m:accPr>" +
                    "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                    "</m:acc>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");

                ops.OfType<MathDrawOp.DrawHRule>().Should().ContainSingle(
                    "PowerPoint-authored accent bars must resolve to shared horizontal-rule ops before Avalonia draws");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x" },
                    because: "the accent bar itself should not depend on Avalonia combining-glyph shaping");

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

        thrown.Should().BeNull("Avalonia must render accent-bar math from the shared HRule plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_BorderBoxHiddenEdgesAndDiagonalStrike_UsesSharedLinePlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "hidden borderBox top/bottom edges and TLBR strike geometry must be shared before Avalonia draws");
                lines.Should().ContainSingle(line => Math.Abs(line.X1 - line.X2) > 0.01 && line.Y2 > line.Y1);
                lines.Should().NotContain(line => Math.Abs(line.Y1 - line.Y2) < 0.01);

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

        thrown.Should().BeNull("Avalonia must render hidden-edge borderBox strike geometry from the shared MathBox line plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_TransparentPhantomMultiGlyphRelation_UsesSharedSpacingPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "m:phantPr/m:transp multi-glyph relation spacing must be resolved in the shared math plan before Avalonia draws it");

                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x", "y" },
                    "hidden transparent phantom relation glyphs must not reach the Avalonia renderer");

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

        thrown.Should().BeNull("Avalonia must render m:phantPr/m:transp relation spacing from the shared MathBox plan without host-specific layout branching");
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
    public async Task RenderParaWithMath_RadicalVisibleDegree_UsesSharedMathBoxPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:rad>" +
                    "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
                    "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                    "</m:rad>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x", "3" },
                    "visible radical degrees must be preserved in the shared MathBox plan before Avalonia draws");
                var radical = ops.OfType<MathDrawOp.DrawRadical>().Single();
                var radicand = ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "x");
                var degree = ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "3");
                degree.X.Should().BeLessThan(radical.X,
                    "Avalonia consumes the shared degree position to the left of the radical sign");
                degree.Y.Should().BeLessThan(radicand.Y,
                    "Avalonia consumes the shared degree position above the radicand");

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

        thrown.Should().BeNull("Avalonia must render visible-degree radical math from the shared MathBox plan without host-specific layout branching");
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
                    "<m:groupChrPr><m:pos m:val=\"bot\"/><m:vertJc m:val=\"bot\"/></m:groupChrPr>" +
                    "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                    "</m:groupChr>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                mathBox.Metrics.Ascent.Should().BeApproximately(mathBox.Metrics.Height, 0.01,
                    "m:groupChrPr/m:vertJc=bot baseline alignment must be resolved in shared layout before Avalonia draws");
                var ops = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math");
                ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain("\u23DF",
                    "missing bottom m:groupChrPr/m:chr should resolve to a shared underbrace glyph before Avalonia draws");

                var wideMathNode = ParseOmml(
                    "<m:groupChr>" +
                    "<m:e><m:r><m:t>x</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>y</m:t></m:r></m:e>" +
                    "</m:groupChr>");
                var wideMathBox = MathLayoutEngine.Layout(wideMathNode, "Cambria Math", 18.0);
                MathBoxRenderPlanner.Plan(wideMathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single(g => g.Text == "\u23DE")
                    .FontSizePt.Should().BeGreaterThan(18.0 * 0.75,
                        "wide m:groupChr braces must grow in the shared plan before Avalonia draws them");

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

    [Fact]
    public async Task RenderParaWithMath_NaryGrowHiddenLimits_UsesSharedPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:nary>" +
                    "<m:naryPr><m:chr m:val=\"S\"/><m:grow/><m:subHide/><m:supHide/></m:naryPr>" +
                    "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                    "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
                    "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                    "</m:nary>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToArray();
                glyphs.Select(g => g.Text).Should().Equal(new[] { "S", "1", "x" },
                    "hidden n-ary limits must be suppressed in the shared plan before Avalonia draws");
                glyphs.Single(g => g.Text == "S").FontSizePt.Should().BeGreaterThan(27.0,
                    "m:naryPr/m:grow must still be resolved in the shared MathBox plan before Avalonia draws");

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

        thrown.Should().BeNull("Avalonia must render grow-enabled hidden-limit n-ary math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_NaryLimLoc_UsesSharedLimitPlacementPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var underOverBox = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:nary>" +
                        "<m:naryPr><m:chr m:val=\"S\"/><m:limLoc m:val=\"undOvr\"/></m:naryPr>" +
                        "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                        "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
                        "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                        "</m:nary>"),
                    "Cambria Math",
                    18.0);
                var subSupBox = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:nary>" +
                        "<m:naryPr><m:chr m:val=\"S\"/><m:limLoc m:val=\"subSup\"/></m:naryPr>" +
                        "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                        "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
                        "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                        "</m:nary>"),
                    "Cambria Math",
                    18.0);

                MathBoxRenderPlanner.Plan(underOverBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should()
                    .Equal(new[] { "n", "S", "0", "x" },
                        "m:naryPr/m:limLoc=undOvr must be resolved in the shared plan before Avalonia draws");
                MathBoxRenderPlanner.Plan(subSupBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should()
                    .Equal(new[] { "S", "n", "0", "x" },
                        "m:naryPr/m:limLoc=subSup must be resolved in the shared plan before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "N = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = underOverBox },
                        new ResolvedRun { Text = " / ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = subSupBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(320, 160));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render n-ary limit-location math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_OneSidedDelimiters_UseSingleSharedBracketPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                        new ResolvedRun { Text = "D = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = openSuppressed },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(220, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must consume one-sided delimiter brackets from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_DelimiterSeparator_UsesSharedSeparatorPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var customSeparator = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:d>" +
                        "<m:dPr><m:sepChr m:val=\"|\"/></m:dPr>" +
                        "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                        "<m:e><m:r><m:t>y</m:t></m:r></m:e>" +
                        "</m:d>"),
                    "Cambria Math",
                    18.0);
                var emptySeparator = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:d>" +
                        "<m:dPr><m:sepChr m:val=\"\"/></m:dPr>" +
                        "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                        "<m:e><m:r><m:t>y</m:t></m:r></m:e>" +
                        "</m:d>"),
                    "Cambria Math",
                    18.0);

                MathBoxRenderPlanner.Plan(customSeparator, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "x", "|", "y" },
                        "m:dPr/m:sepChr custom separator glyphs must be resolved in the shared plan before Avalonia draws");
                MathBoxRenderPlanner.Plan(emptySeparator, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "x", "y" },
                        "explicit empty m:sepChr suppresses the visible separator before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "D = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = customSeparator },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must consume delimiter separator glyphs from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_CenteredDelimiterShape_UsesSharedOrdinaryBracketPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var matchBox = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:d>" +
                        "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                        "</m:d>"),
                    "Cambria Math",
                    18.0);
                var centeredBox = MathLayoutEngine.Layout(
                    ParseOmml(
                        "<m:d>" +
                        "<m:dPr><m:shp m:val=\"centered\"/></m:dPr>" +
                        "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
                        "</m:d>"),
                    "Cambria Math",
                    18.0);

                var matchBracket = MathBoxRenderPlanner.Plan(matchBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawBracket>()
                    .First();
                var centeredBracket = MathBoxRenderPlanner.Plan(centeredBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawBracket>()
                    .First();

                centeredBracket.ScaledHeight.Should().BeLessThan(matchBracket.ScaledHeight,
                    "m:dPr/m:shp=centered must be resolved in the shared plan before Avalonia draws it");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "D = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = centeredBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 140));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must consume centered delimiter shape from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_MathAlphabetStyleVariants_UseSharedUnicodeGlyphPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:r><m:rPr><m:scr m:val=\"script\"/><m:sty m:val=\"b\"/></m:rPr><m:t>Aa</m:t></m:r>" +
                    "<m:r><m:rPr><m:scr m:val=\"sans-serif\"/><m:sty m:val=\"bi\"/></m:rPr><m:t>Zz</m:t></m:r>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);

                MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "\U0001D4D0\U0001D4EA", "\U0001D655\U0001D66F" },
                        "styled m:scr variants must be resolved in the shared MathBox plan before Avalonia draws them");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "S = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 100));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render styled math alphabet glyphs from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_SubSupAlignScripts_UsesSharedRightAlignedScriptPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "m:sSubSupPr/m:alnScr glyph ordering must be resolved in the shared MathBox plan before Avalonia draws it");
                glyphs.Single(g => g.Text == "2").X.Should().BeGreaterThan(glyphs.Single(g => g.Text == "wide").X,
                    "the shorter superscript should be shifted right by the shared alignment plan before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "S = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render aligned m:sSubSup math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_LimitUpperAndLower_UseSharedCenteredLimitPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                        "m:limLow base and limit ordering must be resolved in the shared MathBox plan before Avalonia draws it");
                MathBoxRenderPlanner.Plan(upperBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Select(g => g.Text)
                    .Should().Equal(new[] { "S", "max" },
                        "m:limUpp limit and base ordering must be resolved in the shared MathBox plan before Avalonia draws it");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "L = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = lowerBox },
                        new ResolvedRun { Text = " U = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = upperBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render centered OMML limit math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_ArgumentSize_UsesSharedScaledGlyphPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
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
                    "m:argPr/m:argSz glyph ordering must be resolved in the shared MathBox plan before Avalonia draws it");
                glyphs.Single(g => g.Text == "2").FontSizePt.Should().BeApproximately(18.0, 0.001,
                    "the shared plan should carry the superscript argument's +1 script-size adjustment to Avalonia");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "A = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(260, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render OMML argument-size math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_EqArrayBoxPropertyAlignment_UsesSharedAlignmentPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:eqArr>" +
                    "<m:e><m:r><m:t>mmmm</m:t></m:r><m:box><m:boxPr><m:aln/></m:boxPr><m:e><m:r><m:t>=1</m:t></m:r></m:e></m:box></m:e>" +
                    "<m:e><m:r><m:t>x</m:t></m:r><m:box><m:boxPr><m:aln/></m:boxPr><m:e><m:r><m:t>=22</m:t></m:r></m:e></m:box></m:e>" +
                    "</m:eqArr>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();

                glyphs.Select(g => g.Text).Should().Equal(new[] { "mmmm", "=1", "x", "=22" },
                    "m:boxPr/m:aln rows must be resolved in the shared MathBox plan before Avalonia draws them");
                glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(glyphs.Single(g => g.Text == "=22").X, 0.01,
                    "boxed alignment terms should share the same draw-plan X coordinate before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "E = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(300, 130));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:boxPr/m:aln equation arrays from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_EqArraySpacingAndBaseJustification_UsesSharedRowPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmml(
                    "<m:eqArr>" +
                    "<m:eqArrPr><m:baseJc m:val=\"bot\"/><m:rSpRule m:val=\"3\"/><m:rSp m:val=\"24\"/></m:eqArrPr>" +
                    "<m:e><m:r><m:t>mmmm</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e>" +
                    "<m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=22</m:t></m:r></m:e>" +
                    "<m:e><m:r><m:t>z</m:t></m:r></m:e>" +
                    "</m:eqArr>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var glyphs = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();

                glyphs.Select(g => g.Text).Should().Equal(new[] { "mmmm", "=1", "x", "=22", "z" },
                    "m:eqArrPr spacing and base justification must be resolved in the shared MathBox plan before Avalonia draws them");
                glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(glyphs.Single(g => g.Text == "=22").X, 0.01,
                    "aligned equation terms should share the same draw-plan X coordinate before Avalonia draws");
                glyphs.Single(g => g.Text == "=22").Y.Should().BeGreaterThan(glyphs.Single(g => g.Text == "=1").Y,
                    "row spacing should reach Avalonia as shared draw-plan Y offsets");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "E = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(300, 160));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:eqArrPr row spacing and base justification from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_OMathParaJustification_UsesSharedAlignedParagraphPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmmlParagraph(
                    "<m:oMathParaPr><m:jc m:val=\"right\"/></m:oMathParaPr>" +
                    "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");
                var natural = MathLayoutEngine.Layout(((MathNode.MathParagraph)mathNode).Content, "Cambria Math", 18.0);
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0, paragraphWidthDip: 180);
                var glyph = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();

                glyph.X.Should().BeApproximately(10 + 180 - natural.Metrics.Width, 0.01,
                    "m:oMathParaPr/m:jc alignment must shift shared draw-plan coordinates before Avalonia draws");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "P = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:oMathParaPr/m:jc math from the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_MathFont_UsesSharedGlyphFontPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var mathNode = ParseOmmlParagraph(
                    "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
                    "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");
                var mathBox = MathLayoutEngine.Layout(mathNode, "Cambria Math", 18.0);
                var glyph = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();

                glyph.FontFamily.Should().Be("Arial",
                    "Avalonia must consume the equation-wide font selected by the shared math layout plan");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "P = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:mathPr/m:mathFont through the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_DocumentMathProperties_UsesInheritedFontPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var xml = $"<a:graphicData xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                          $"xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" xmlns:m=\"{M}\">" +
                          "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
                          "<a14:m><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></a14:m></a:graphicData>";
                var mathBox = MathLayoutEngine.Layout(OmmlParser.Parse(xml, "FALLBACK"), "Cambria Math", 18.0);
                var glyph = MathBoxRenderPlanner.Plan(mathBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .Single();

                glyph.FontFamily.Should().Be("Arial",
                    "Avalonia must consume document-level m:mathPr through the shared layout plan");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "P = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = mathBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(320, 120));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render document-level m:mathPr through the shared MathBox plan without host-specific layout branching");
    }

    [Fact]
    public async Task RenderParaWithMath_MatrixSpacingAndBaseJustification_UsesSharedCellPlan_DoesNotThrow()
    {
        System.Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var defaultBox = MathLayoutEngine.Layout(ParseOmml(MatrixOmml()), "Cambria Math", 18.0);
                var centeredBox = MathLayoutEngine.Layout(ParseOmml(MatrixOmml("<m:baseJc m:val=\"ctr\"/>")), "Cambria Math", 18.0);
                var spacedBox = MathLayoutEngine.Layout(ParseOmml(MatrixOmml(
                    "<m:baseJc m:val=\"bot\"/><m:rSpRule m:val=\"2\"/><m:cGpRule m:val=\"3\"/><m:cGp m:val=\"24\"/><m:cSp m:val=\"240\"/>")),
                    "Cambria Math",
                    18.0);

                var defaultGlyphs = MathBoxRenderPlanner.Plan(defaultBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();
                var spacedGlyphs = MathBoxRenderPlanner.Plan(spacedBox, 10, 20, SrgbColor.Black, "Cambria Math")
                    .OfType<MathDrawOp.DrawGlyph>()
                    .ToList();

                spacedGlyphs.Select(g => g.Text).Should().Equal(new[] { "a", "bb", "ccc", "d" },
                    "matrix spacing and base justification must be resolved in the shared MathBox plan before Avalonia draws them");
                spacedGlyphs.Single(g => g.Text == "ccc").Y.Should().BeGreaterThan(defaultGlyphs.Single(g => g.Text == "ccc").Y,
                    "m:mPr row spacing should reach Avalonia as shared draw-plan Y offsets");
                spacedGlyphs.Single(g => g.Text == "d").X.Should().BeGreaterThan(defaultGlyphs.Single(g => g.Text == "d").X,
                    "m:mPr column gap and minimum column width should reach Avalonia as shared draw-plan X offsets");
                spacedBox.Metrics.Ascent.Should().BeGreaterThan(centeredBox.Metrics.Ascent,
                    "m:mPr/m:baseJc=bot should report a bottom-row baseline contract through shared MathBox metrics");

                var para = new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "M = ", FontFamily = "Arial", FontSizePt = 18.0, Color = SrgbColor.Black },
                        new ResolvedRun { FontFamily = "Cambria Math", FontSizePt = 18.0, Color = SrgbColor.Black, MathLayout = spacedBox },
                    }
                };

                var rtb = new RenderTargetBitmap(new PixelSize(300, 170));
                using DrawingContext dc = rtb.CreateDrawingContext();
                SlideCanvas.RenderParaWithMath(dc, para, startX: 10, startY: 20);
            }
            catch (System.Exception ex) { thrown = ex; }
        });

        thrown.Should().BeNull("Avalonia must render m:mPr matrix spacing and base justification from the shared MathBox plan without host-specific layout branching");
    }

    private static string MatrixOmml(string matrixProperties = "") =>
        "<m:m>" +
        (string.IsNullOrEmpty(matrixProperties) ? "" : $"<m:mPr>{matrixProperties}</m:mPr>") +
        "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>bb</m:t></m:r></m:e></m:mr>" +
        "<m:mr><m:e><m:r><m:t>ccc</m:t></m:r></m:e><m:e><m:r><m:t>d</m:t></m:r></m:e></m:mr>" +
        "</m:m>";
}
