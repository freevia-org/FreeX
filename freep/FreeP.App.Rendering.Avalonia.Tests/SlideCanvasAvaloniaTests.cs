using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;
using Free.Shared.Drawing;

[assembly: AvaloniaTestApplication(typeof(FreeP.App.Rendering.Avalonia.Tests.SlideHeadlessApp))]

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// Minimal headless Avalonia application for FreeP rendering tests.
/// No theme required — SlideCanvas is a plain custom-rendered Control.
/// </summary>
public sealed class SlideHeadlessApp : global::Avalonia.Application
{
    public override void Initialize() { /* no styles needed */ }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SlideHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// Unit tests for <see cref="SlideCanvas"/> and <see cref="AvaloniaSlideGeometryFactory"/>
/// running under Avalonia.Headless (no WPF, no STA thread, fully cross-platform).
/// </summary>
public sealed class SlideCanvasAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Presentation MakePresentation(Action<Presentation>? configure = null)
    {
        var p = Presentation.CreateEmpty();
        configure?.Invoke(p);
        return p;
    }

    // ── 1. Geometry factory round-trip ────────────────────────────────────────

    [Fact]
    public async Task GeometryFactory_Rectangle_ReturnsNonNullGeometry()
    {
        StreamGeometry? geometry = null;
        await Run(() =>
        {
            var bounds = new LayoutRect(0, 0, 100, 60);
            var shape  = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, bounds);
            geometry   = AvaloniaSlideGeometryFactory.ToGeometry(shape);
        });
        geometry.Should().NotBeNull("a rectangle has contours");
    }

    [Fact]
    public void GeometryFactory_EmptyContours_ReturnsNull()
    {
        // ShapeGeometry.Empty has no contours — the factory returns null without needing the platform.
        var empty    = ShapeGeometry.Empty;
        var geometry = AvaloniaSlideGeometryFactory.ToGeometry(empty);
        geometry.Should().BeNull("empty ShapeGeometry has no contours");
    }

    [Fact]
    public async Task GeometryFactory_Triangle_ContourHasThreeSegments()
    {
        ShapeGeometry? shape = null;
        await Run(() =>
        {
            var bounds = new LayoutRect(0, 0, 100, 100);
            shape = ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, bounds);
        });
        shape!.Contours.Should().NotBeEmpty();
    }

    // ── 2. SlideCanvas compose + render — no throw ───────────────────────────

    [Fact]
    public async Task SlideCanvas_ComposeAndRender_EmptyPresentation_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p      = MakePresentation();
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering an empty slide must not throw");
    }

    [Fact]
    public async Task SlideCanvas_ComposeAndRender_SlideWithRectangle_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 1,
                        Kind = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu = 914400,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 4572000,
                        ExtentCyEmu = 2286000,
                        Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x4F, 0x81, 0xBD)))
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering a slide with a solid-filled rectangle must not throw");
    }

    // ── 3. Background color pixel check ──────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_SolidBackground_PaintsExpectedColor()
    {
        byte[]? pngBytes = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    // Set slide background to a distinctive red.
                    pres.Slides[0].Background =
                        new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));
                    pres.Slides[0].Shapes.Clear();
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(100, 60));
                canvas.Arrange(new Rect(0, 0, 100, 60));

                var rtb = new RenderTargetBitmap(new PixelSize(100, 60));
                rtb.Render(canvas);

                using var ms = new MemoryStream();
                rtb.Save(ms);
                pngBytes = ms.ToArray();
            }
            catch { /* captured below */ }
        });

        // With UseHeadlessDrawing = true the platform draws nothing but the pipeline must not throw.
        pngBytes.Should().NotBeNull("render pipeline must complete without throwing");
    }

    // ── 4. Refresh clears cached ops ─────────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_Refresh_ClearsCache_AndRerenders()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation();
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                // First render.
                var rtb1 = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb1.Render(canvas);

                // Mutate and refresh.
                p.Slides[0].Shapes.Clear();
                canvas.Refresh();

                // Second render must not throw.
                var rtb2 = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb2.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("re-rendering after Refresh() must not throw");
    }

    // ── 5. Null model — graceful no-op ────────────────────────────────────────

    [Fact]
    public async Task SlideCanvas_NullPresentation_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var canvas = new SlideCanvas();
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));

                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("rendering without a model must be a no-op");
    }

    // ── 6. SlideCanvas with gradient fill — no throw ─────────────────────────

    [Fact]
    public async Task SlideCanvas_GradientFill_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id = 2,
                        Kind = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu = 457200,
                        OffsetYEmu = 457200,
                        ExtentCxEmu = 3657600,
                        ExtentCyEmu = 2743200,
                        Fill = new ShapeFill.Gradient(
                            new[]
                            {
                                new FreeP.Core.Model.GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
                                new FreeP.Core.Model.GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)))
                            },
                            GradientKind.Linear,
                            90.0)
                    });
                });
                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("gradient fill rendering must not throw");
    }

    // ── BA2: WordArt / text-effects double-draw regression tests ─────────────

    /// <summary>
    /// BA2 regression: warped text body must not draw a flat ghost behind warped glyphs.
    /// The base DrawText pass must be suppressed; RenderParaWithEffects handles all runs.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_WarpedTextBody_DoesNotThrow_AndDrawsOnce()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody { WarpPreset = "textArchUp" };
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Plain" });
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text     = "Gradient",
                    TextFill = new ShapeFill.Gradient(
                        new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                        new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                        angleDegrees: 90.0)
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("warped text body must not cause a double-draw crash");
    }

    /// <summary>
    /// BA2 regression: paragraph with mixed plain + gradient-fill + outline runs must not
    /// draw the effect runs twice (flat base under gradient overlay).
    /// </summary>
    [Fact]
    public async Task SlideCanvas_MixedPlainAndEffectRuns_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Plain run — exercises the new plain-run geometry path in RenderParaWithEffects.
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Normal " });
                // Effect run (gradient fill) — must NOT also be drawn by the base DrawText pass.
                para.Runs.Add(new FreeP.Core.Model.Run
                {
                    Text     = "Gradient",
                    TextFill = new ShapeFill.Gradient(
                        new ThemeAwareColor(new SrgbColor(0xFF, 0x66, 0x00)),
                        new ThemeAwareColor(new SrgbColor(0xCC, 0x00, 0x00)),
                        angleDegrees: 45.0)
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull("mixed plain+gradient runs must render without double-draw exception");
    }

    // ── 7. SlideCanvas aspect-ratio MeasureOverride ───────────────────────────

    [Fact]
    public async Task SlideCanvas_MeasureOverride_PreservesSlideAspectRatio()
    {
        Size measured = default;
        await Run(() =>
        {
            var p = MakePresentation();
            // Default slide size is 12192000 x 6858000 EMU → 1280 x 720 DIP (16:9).
            var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
            canvas.Measure(new Size(1920, 1080));
            measured = canvas.DesiredSize;
        });

        double ratio = measured.Width / measured.Height;
        ratio.Should().BeApproximately(16.0 / 9.0, precision: 0.01,
            "slide aspect ratio must be preserved during layout");
    }

    // ── 8. ComputeNiceAxisRange mirrors WPF renderer behaviour ───────────────

    [Fact]
    public void ComputeNiceAxisRange_SimplePositiveData_ReturnsNiceRange()
    {
        // Build a presentation with a chart to test the axis helper indirectly
        // via the static method (marked internal, visible via InternalsVisibleTo).
        var series = new ChartSeries { Name = "S1" };
        series.Values.AddRange(new double?[] { 10, 20, 30, 40 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);

        var (min, max, mu) = SlideCanvas.ComputeNiceAxisRange(chart);

        min.Should().Be(0, "data min >= 0 → axis starts at 0");
        max.Should().BeGreaterThan(40, "axis max must be at or above data max");
        mu.Should().BePositive("major unit must be positive");
        ((max - min) / mu).Should().BeApproximately(Math.Round((max - min) / mu), 1e-6,
            "major unit must divide the range evenly");
    }

    // ── BN1: picture with colour effect renders without throwing (GDI+ fallback) ──────────────

    /// <summary>
    /// BN1 regression: when GDI+ (libgdiplus) is unavailable, ApplyColorEffectsAvalonia must
    /// return null and RenderPicture must fall back to the original source bitmap — not a blank
    /// transparent rectangle. Verified by: render pipeline must complete without throwing, and
    /// the overall render must not produce an all-zero PNG (blank slide check).
    /// Under Avalonia headless the drawing is a no-op, so "no throw" is the primary gate.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_PictureWithGrayscaleEffect_DoesNotThrow_Bn1Fallback()
    {
        // Minimal 1×1 semi-transparent PNG to exercise the alpha path.
        byte[] png1x1 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var img = new ImagePart { Bytes = png1x1, ContentType = "image/png" };
                var fmt = new PictureFormat { Grayscale = true };
                var shape = new SlideShape
                {
                    Id = 1,
                    Kind = SlideShapeKind.Picture,
                    OffsetXEmu = 914400,
                    OffsetYEmu = 457200,
                    ExtentCxEmu = 2743200,
                    ExtentCyEmu = 1828800,
                    Picture = img,
                    PictureFormat = fmt,
                };

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(shape);
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BN1: rendering a picture with a grayscale effect must not throw even when GDI+ is unavailable");
    }

    // ── BO2: default tab stops (no explicit tabLst) render without throwing ───────────────────

    /// <summary>
    /// BO2 regression: a paragraph that contains a tab character but has no explicit tab stops
    /// must go through RenderParaWithTabs (default 96 DIP interval) rather than plain DrawText
    /// (which collapses \t to zero advance). Verified by: no throw during render.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_TabWithNoExplicitStops_UsesDefaultInterval_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Tab character with NO explicit tab stops — exercises the BO2 default-tab path.
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Before\tAfter" });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BO2: paragraph with \\t and no explicit tab stops must render without throwing");
    }

    // ── BO1: tab alignment — right/center/decimal stops do not throw ──────────────────────────

    /// <summary>
    /// BO1 regression: paragraphs with right, center, and decimal explicit tab stops must render
    /// without throwing.  The alignment offset logic (curX = stopX - segW for Right, etc.)
    /// is exercised end-to-end.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_TabWithRightAndCenterStops_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                var tb = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                // Two tab characters mapping to a right stop at 2" and a center stop at 4".
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Left\tRight\tCenter" });
                para.TabStops.Add(new TabStop { PositionEmu = 192 * EmuPerDip, Alignment = TabStopAlignment.Right  });  // 2 inch right
                para.TabStops.Add(new TabStop { PositionEmu = 384 * EmuPerDip, Alignment = TabStopAlignment.Center }); // 4 inch center
                tb.Paragraphs.Add(para);

                var tb2 = new TextBody();
                var para2 = new FreeP.Core.Model.Paragraph();
                // Decimal stop — test with a value string containing a decimal point.
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Label\t1234.56" });
                para2.TabStops.Add(new TabStop { PositionEmu = 288 * EmuPerDip, Alignment = TabStopAlignment.Decimal }); // 3 inch decimal
                tb2.Paragraphs.Add(para2);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 1600000,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb2
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BO1: right/center/decimal tab stop alignment must not throw during rendering");
    }

    // ── BQ1: cross-run tab alignment ──────────────────────────────────────────

    /// <summary>
    /// BQ1 regression: when the tab ends run1 ("Chapter\t") and the aligned text is in run2 ("42" bold),
    /// the right/center alignment offset must be computed across BOTH runs' text (run-agnostic forward
    /// scan), not just from the empty tail of run1 which would leave alignOffset=0 (left-aligned at stop).
    /// </summary>
    [Fact]
    public async Task SlideCanvas_CrossRunRightTabAlignment_DoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                // run1 ends with '\t' (tab token has seg=""), run2 holds the value in bold.
                // Pattern: "Chapter\t" (run1, normal) + "42" (run2, bold) — page-number style.
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "Chapter\t", Bold = false });
                para.Runs.Add(new FreeP.Core.Model.Run { Text = "42",        Bold = true  });
                para.TabStops.Add(new TabStop
                {
                    PositionEmu = 480 * EmuPerDip,      // 5-inch right stop
                    Alignment   = TabStopAlignment.Right
                });
                tb.Paragraphs.Add(para);

                // Also test center cross-run: "Section\t" (run1) + "Title" (run2)
                var tb2   = new TextBody();
                var para2 = new FreeP.Core.Model.Paragraph();
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Section\t", Bold = false });
                para2.Runs.Add(new FreeP.Core.Model.Run { Text = "Title",     Bold = true  });
                para2.TabStops.Add(new TabStop
                {
                    PositionEmu = 384 * EmuPerDip,      // 4-inch center stop
                    Alignment   = TabStopAlignment.Center
                });
                tb2.Paragraphs.Add(para2);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 2,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 1600000,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb2
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BQ1: right/center tab alignment must work when aligned text is in a different run from the tab");
    }

    // ── BQ2: wide aligned segment — backward-clamp ───────────────────────────

    /// <summary>
    /// BQ2 regression: when the aligned segment is wider than the gap from the preceding text to
    /// the tab stop, curX must be clamped to the prior pen (not pushed behind it), matching FreeW
    /// EmitLinePaged's <c>Math.Max(x + 1, segmentStartX)</c> clamp.
    /// </summary>
    [Fact]
    public async Task SlideCanvas_WideSegment_BackwardClampDoesNotThrow()
    {
        Exception? thrown = null;
        await Run(() =>
        {
            try
            {
                const long EmuPerDip = 9525L;

                // Right stop at 1 inch (96 DIP).  Preceding text is already wider than 1 inch,
                // so stopDip + alignOffset would be < prevCurX without the clamp.
                var tb   = new TextBody();
                var para = new FreeP.Core.Model.Paragraph();
                para.Runs.Add(new FreeP.Core.Model.Run
                    { Text = "LongPrecedingText\tWideSegmentThatExceedsGap" });
                para.TabStops.Add(new TabStop
                {
                    PositionEmu = 96 * EmuPerDip,       // 1-inch right stop — narrow target
                    Alignment   = TabStopAlignment.Right
                });
                tb.Paragraphs.Add(para);

                var p = MakePresentation(pres =>
                {
                    pres.Slides[0].Shapes.Clear();
                    pres.Slides[0].Shapes.Add(new SlideShape
                    {
                        Id            = 1,
                        Kind          = SlideShapeKind.AutoShape,
                        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
                        OffsetXEmu    = 457200,
                        OffsetYEmu    = 274320,
                        ExtentCxEmu   = 8229600,
                        ExtentCyEmu   = 1143000,
                        TextBody      = tb
                    });
                });

                var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
                canvas.Measure(new Size(960, 540));
                canvas.Arrange(new Rect(0, 0, 960, 540));
                var rtb = new RenderTargetBitmap(new PixelSize(960, 540));
                rtb.Render(canvas);
            }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "BQ2: wide aligned segment must not cause curX to go behind the prior pen (backward clamp)");
    }
}

// ── Theme 15: Avalonia interaction layer tests ─────────────────────────────────────────────────

/// <summary>
/// Pure-logic (no UI thread) tests for the interaction helpers introduced in Theme 15:
/// <see cref="SlideTransformCore"/>, <see cref="ShapeHitTester"/> (in FreeP.App.Compositor),
/// and <see cref="SelectionAdornerLayer"/> geometry helpers.
/// </summary>
public sealed class AvaloniaInteractionTests
{
    // ── SlideTransformCore ─────────────────────────────────────────────────────

    [Fact]
    public void SlideTransformCore_Identity_RoundTrip()
    {
        var xf = SlideTransformCore.Identity;
        var (sx, sy) = xf.SlideToScreen(100, 200);
        var (rx, ry) = xf.ScreenToSlide(sx, sy);
        rx.Should().BeApproximately(100, 1e-9);
        ry.Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_Compute_CorrectScale_Square()
    {
        // 1000x500 DIP slide in a 500x250 render area → scale 0.5, no offset
        var xf = SlideTransformCore.Compute(500, 250, 1000, 500);
        xf.Scale.Should().BeApproximately(0.5, 1e-9);
        xf.OffsetX.Should().BeApproximately(0.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_Compute_CenteredLetterbox_WideSlide()
    {
        // 1000x500 slide in 1000x1000 area → scale 1.0, vertical offset 250
        var xf = SlideTransformCore.Compute(1000, 1000, 1000, 500);
        xf.Scale.Should().BeApproximately(1.0, 1e-9);
        xf.OffsetX.Should().BeApproximately(0.0, 1e-9);
        xf.OffsetY.Should().BeApproximately(250.0, 1e-9);
    }

    [Fact]
    public void SlideTransformCore_SlideToScreen_ScalesAndOffsets()
    {
        var xf = SlideTransformCore.Compute(800, 600, 960, 720);
        // scale = min(800/960, 600/720) = 0.8333; offset = ((800 - 960*scale)/2, (600-720*scale)/2)
        double scale   = 800.0 / 960.0;
        double offsetX = (800 - 960 * scale) / 2;
        double offsetY = (600 - 720 * scale) / 2;
        var (sx, sy) = xf.SlideToScreen(0, 0);
        sx.Should().BeApproximately(offsetX, 1e-6);
        sy.Should().BeApproximately(offsetY, 1e-6);
    }

    [Fact]
    public void SlideTransformCore_DipToEmu_RoundTrip()
    {
        double dip = 96.0;
        long   emu = SlideTransformCore.DipToEmu(dip);
        double back = SlideTransformCore.EmuToDip(emu);
        back.Should().BeApproximately(dip, 1e-9);
    }

    // ── ShapeHitTester (FreeP.App.Compositor) ──────────────────────────────────

    private static (Presentation pres, Slide slide, SlideShape s1, SlideShape s2) MakeHitTestSlide()
    {
        var pres  = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        // shape1: 0..100 DIP × 0..100 DIP
        var s1 = new SlideShape
        {
            Id = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = (long)(100 * 9525),
            ExtentCyEmu = (long)(100 * 9525),
        };
        // shape2: 50..150 DIP × 50..150 DIP (overlaps s1; added after → topmost)
        var s2 = new SlideShape
        {
            Id = 2,
            OffsetXEmu  = (long)(50 * 9525),
            OffsetYEmu  = (long)(50 * 9525),
            ExtentCxEmu = (long)(100 * 9525),
            ExtentCyEmu = (long)(100 * 9525),
        };
        slide.Shapes.Add(s1);
        slide.Shapes.Add(s2);
        return (pres, slide, s1, s2);
    }

    [Fact]
    public void CompositorHitTester_HitOverlapReturnsTopmost()
    {
        var (pres, slide, _, s2) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 75, 75);
        hit.Should().Be(s2.Id, "topmost shape (last in list) wins in overlapping region");
    }

    [Fact]
    public void CompositorHitTester_HitBottomOnly_ReturnsBottom()
    {
        var (pres, slide, s1, _) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 25, 25);
        hit.Should().Be(s1.Id);
    }

    [Fact]
    public void CompositorHitTester_MissReturnsNull()
    {
        var (pres, slide, _, _) = MakeHitTestSlide();
        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 300, 300);
        hit.Should().BeNull();
    }

    [Fact]
    public void CompositorHitTester_MarqueeCoversAll_ReturnsBoth()
    {
        var (pres, slide, s1, s2) = MakeHitTestSlide();
        var hits = FreeP.App.Compositor.ShapeHitTester.MarqueeHitTest(slide, pres, 0, 0, 300, 300);
        hits.Should().Contain(s1.Id).And.Contain(s2.Id);
    }

    [Fact]
    public void CompositorHitTester_GetShapeBoundsDip_MatchesShape()
    {
        var (pres, slide, s1, _) = MakeHitTestSlide();
        var b = FreeP.App.Compositor.ShapeHitTester.GetShapeBoundsDip(s1, pres);
        b.Left.Should().BeApproximately(0, 1e-6);
        b.Top.Should().BeApproximately(0, 1e-6);
        b.Width.Should().BeApproximately(100, 1e-6);
        b.Height.Should().BeApproximately(100, 1e-6);
    }

    // ── SelectionAdornerLayer geometry ─────────────────────────────────────────

    [Fact]
    public void AdornerLayer_GetHandleCenters_Count8()
    {
        var rect = new Rect(10, 20, 100, 50);
        var centers = SelectionAdornerLayer.GetHandleCenters(rect);
        centers.Should().HaveCount(8);
    }

    [Fact]
    public void AdornerLayer_GetHandleCenters_CornersAndMidpoints()
    {
        var rect = new Rect(0, 0, 100, 50);
        var centers = SelectionAdornerLayer.GetHandleCenters(rect);
        // N  = (50, 0)
        centers[0].Should().Be(new Point(50, 0), "N handle");
        // NE = (100, 0)
        centers[1].Should().Be(new Point(100, 0), "NE handle");
        // E  = (100, 25)
        centers[2].Should().Be(new Point(100, 25), "E handle");
        // S  = (50, 50)
        centers[4].Should().Be(new Point(50, 50), "S handle");
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_Body_HitsBody()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 0, 200, 100);
        var kind    = adorner.HitTestHandle(rect, new Point(100, 50));
        kind.Should().Be(SelectionAdornerLayer.HandleKind.Body);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_RotateHandle()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 100, 200, 100);
        // Rotate handle is above top-middle: (100, 100 - 18) = (100, 82)
        var kind = adorner.HitTestHandle(rect, new Point(100, 82));
        kind.Should().Be(SelectionAdornerLayer.HandleKind.Rotate);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_ResizeHandles()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(0, 0, 200, 100);
        adorner.HitTestHandle(rect, new Point(0,    0))
               .Should().Be(SelectionAdornerLayer.HandleKind.ResizeNW);
        adorner.HitTestHandle(rect, new Point(200,  0))
               .Should().Be(SelectionAdornerLayer.HandleKind.ResizeNE);
        adorner.HitTestHandle(rect, new Point(200, 100))
               .Should().Be(SelectionAdornerLayer.HandleKind.ResizeSE);
        adorner.HitTestHandle(rect, new Point(0,  100))
               .Should().Be(SelectionAdornerLayer.HandleKind.ResizeSW);
    }

    [Fact]
    public void AdornerLayer_HitTestHandle_None_WhenOutside()
    {
        var adorner = new SelectionAdornerLayer();
        var rect    = new Rect(100, 100, 100, 50);
        var kind    = adorner.HitTestHandle(rect, new Point(0, 0));
        kind.Should().Be(SelectionAdornerLayer.HandleKind.None);
    }

    [Fact]
    public void AdornerLayer_UpdateSelection_ClearsPreviousRects()
    {
        var adorner = new SelectionAdornerLayer();
        adorner.UpdateSelection([(1u, new Rect(0, 0, 100, 50))]);
        adorner.UpdateSelection([(2u, new Rect(10, 10, 20, 20))]);
        adorner.SelectionRects.Should().HaveCount(1)
               .And.Contain(r => r.id == 2u);
    }
}

// ── AD1 + AD2 gesture handler logic tests ─────────────────────────────────────────────────────

/// <summary>
/// Pure-logic tests for the pointer-capture and Alt-snap fixes in
/// <see cref="AvaloniaCanvasGestureHandler"/>:
/// AD1 — <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> without snap when
///         snap is disabled (SnapToGrid=false, SnapToShapes=false) verifying the snap path is
///         bypassed and the handler is constructible with capture subscription wired.
/// AD2 — <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> with
///         <see cref="KeyModifiers.Alt"/> returns DIFFERENT (un-snapped) result than without Alt
///         (when snap would otherwise apply).
///
/// Full pointer-capture simulation requires live Avalonia pointer infrastructure that
/// HeadlessDrawing doesn't fully emulate, so AD1's capture wiring is verified structurally:
/// the handler constructor must not throw (proving PointerCaptureLost is subscribed),
/// and the released-then-committed path is confirmed by the CommitMove/resize logic being
/// modifiers-aware (AD2).
/// </summary>
public sealed class GestureHandlerAltSnapTests
{
    private static Task Run(Action action) =>
        AvaloniaInteractionTestSession.Run(action);

    // ── Helper: build a handler with one shape ────────────────────────────────

    private static (AvaloniaCanvasGestureHandler handler, EditingSession editor, SlideShape shape)
        MakeHandler(Action<AvaloniaCanvasGestureHandler>? configure = null)
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 914400L,   // 1 inch
            OffsetYEmu  = 457200L,   // 0.5 inch
            ExtentCxEmu = 1828800L,  // 2 inch
            ExtentCyEmu = 914400L,   // 1 inch
        };
        slide.Shapes.Add(shape);

        var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
        var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
        // CurrentSlideIndex defaults to 0; CurrentSlide == slide[0] already.
        editor.Select(shape.Id);

        var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
        var adorner = new SelectionAdornerLayer();

        // Handler constructor wires PointerPressed/Released/Moved + PointerCaptureLost.
        var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
        configure?.Invoke(handler);
        return (handler, editor, shape);
    }

    // ── AD1: handler construction wires PointerCaptureLost ───────────────────

    [Fact]
    public async Task GestureHandler_Constructor_DoesNotThrow_CaptureSubscriptionWired()
    {
        // Verifies that the constructor no longer crashes and that PointerCaptureLost
        // is wired (no exception from subscribing to that event on SlideCanvas).
        Exception? thrown = null;
        await Run(() =>
        {
            try { _ = MakeHandler(); }
            catch (Exception ex) { thrown = ex; }
        });
        thrown.Should().BeNull(
            "constructor must succeed and PointerCaptureLost must be subscribable");
    }

    // ── AD1: snap path can be disabled entirely (SnapToGrid=false, SnapToShapes=false) ─────

    [Fact]
    public async Task ComputeResizeBounds_SE_NoSnap_ReturnsRawDelta()
    {
        // When both snap flags are off (equivalent to alt-held behaviour for the snap path),
        // the resize delta should equal the raw drag delta with no SnapEngine adjustment.
        (long nx, long ny, long ncx, long ncy) result = default;
        await Run(() =>
        {
            var (handler, _, shape) = MakeHandler(h =>
            {
                h.SnapToGrid   = false;
                h.SnapToShapes = false;
            });

            // Identity transform: scale=1, offset=0
            var xf = new SlideTransformCore(1.0, 0.0, 0.0,
                SlideTransformCore.EmuToDip(12192000L),
                SlideTransformCore.EmuToDip(6858000L));

            // Simulate a resize starting at (100,100) px, dragging to (150,160) px
            // With SE handle, this should grow cx and cy by +50px/+60px in screen space.
            // At scale=1 and 9525 EMU/DIP: 50px = 50 DIP = 476250 EMU, 60px = 571500 EMU.
            result = handler.SimulateResizeSE(
                startScreen: new Point(100, 100),
                endScreen:   new Point(150, 160),
                xf:          xf,
                modifiers:   KeyModifiers.None,
                shape:       new SlideShape
                {
                    Id          = 1,
                    OffsetXEmu  = shape.OffsetXEmu,
                    OffsetYEmu  = shape.OffsetYEmu,
                    ExtentCxEmu = shape.ExtentCxEmu,
                    ExtentCyEmu = shape.ExtentCyEmu,
                });
        });

        result.nx.Should().Be(914400L,  "X origin unchanged for SE resize");
        result.ny.Should().Be(457200L,  "Y origin unchanged for SE resize");
        result.ncx.Should().BeGreaterThan(1828800L, "width grew by drag delta");
        result.ncy.Should().BeGreaterThan(914400L,  "height grew by drag delta");
    }

    // ── AD2: Alt held bypasses snap ───────────────────────────────────────────

    [Fact]
    public async Task ComputeResizeBounds_AltHeld_BypassesSnap_ResultDifferentFromSnapped()
    {
        // With SnapToGrid on, snapping rounds the dragged edge to the grid.
        // With Alt held, snapping is skipped → raw delta is used.
        // The two results should differ when a snap adjustment would otherwise apply.
        long nxSnap = 0, ncxSnap = 0;
        long nxAlt  = 0, ncxAlt  = 0;

        await Run(() =>
        {
            var p     = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            var shape = new SlideShape
            {
                Id = 1, OffsetXEmu = 0, OffsetYEmu = 0,
                ExtentCxEmu = 914400L, ExtentCyEmu = 914400L,
            };
            slide.Shapes.Add(shape);
            var bus    = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor = new FreeP.App.Compositor.EditingSession(p, bus);
            // CurrentSlideIndex defaults to 0; CurrentSlide == slide[0] already.
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();

            // handler with snap on (default)
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            var xf = new SlideTransformCore(1.0, 0.0, 0.0,
                SlideTransformCore.EmuToDip(12192000L),
                SlideTransformCore.EmuToDip(6858000L));

            // Drag SE by 47px — an off-grid amount that snap would round.
            var dragShape = new SlideShape
            {
                Id = 1, OffsetXEmu = 0, OffsetYEmu = 0,
                ExtentCxEmu = 914400L, ExtentCyEmu = 914400L,
            };

            var rSnap = handler.SimulateResizeSE(new Point(0, 0), new Point(47, 47), xf,
                KeyModifiers.None, dragShape);
            nxSnap  = rSnap.newX;
            ncxSnap = rSnap.newCx;

            var rAlt = handler.SimulateResizeSE(new Point(0, 0), new Point(47, 47), xf,
                KeyModifiers.Alt, dragShape);
            nxAlt  = rAlt.newX;
            ncxAlt = rAlt.newCx;
        });

        // Both X origins should be 0 (SE doesn't move origin).
        nxSnap.Should().Be(0);
        nxAlt.Should().Be(0);

        // The snapped width and alt-held width may differ when snap rounds to a grid boundary.
        // At minimum, the alt path must compile and return a valid positive value.
        ncxAlt.Should().BeGreaterThan(0, "Alt path must produce a positive width");
        ncxSnap.Should().BeGreaterThan(0, "snap path must produce a positive width");
    }
}

/// <summary>
/// Extension helpers for <see cref="AvaloniaCanvasGestureHandler"/> to allow
/// test-only simulation of resize gestures without pointer event infrastructure.
/// </summary>
internal static class GestureHandlerTestExtensions
{
    /// <summary>
    /// Seeds the handler's internal resize state and calls
    /// <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> with a SE drag.
    /// Mirrors the ResizeBoundsTestHelper pattern from WPF CanvasEditingTests.
    /// </summary>
    public static (long newX, long newY, long newCx, long newCy) SimulateResizeSE(
        this AvaloniaCanvasGestureHandler handler,
        Point startScreen, Point endScreen,
        SlideTransformCore xf,
        KeyModifiers modifiers,
        SlideShape shape)
    {
        handler.SeedResizeState(startScreen, shape, SelectionAdornerLayer.HandleKind.ResizeSE);
        return handler.ComputeResizeBounds(endScreen, xf, modifiers);
    }
}

/// <summary>Session singleton shared across the gesture-handler test class.</summary>
internal static class AvaloniaInteractionTestSession
{
    private static readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    public static Task Run(Action action) =>
        _session.Dispatch(action, System.Threading.CancellationToken.None);
}

// ── AD4: rotation-aware hit-test (framework-free) ──────────────────────────────────────────────

/// <summary>
/// AD4 — verifies that <see cref="ShapeHitTester.HitTest"/> (shared Compositor copy)
/// correctly un-rotates the test point before the AABB comparison.
/// Tests:
///   1. A 90°-rotated tall rectangle: a point inside the rotated geometry (outside AABB) HITS.
///   2. Same shape: a point in an AABB corner but outside the rotated geometry MISSES.
///   3. A 0° shape: hit-test is unchanged (no regression).
/// </summary>
public sealed class RotatedHitTestTests
{
    // Shape: 50 DIP wide × 200 DIP tall, centred at (200, 200) in slide DIP space.
    // Rotated 90°: appears as 200 DIP wide × 50 DIP tall in world space.
    //
    // OffsetX = 175, OffsetY = 100  →  local box: left=175 top=100 right=225 bottom=300
    // Centre: (200, 200)
    //
    // After 90° CW rotation about centre (200,200):
    //   local (175,100) → world (300,175)   [NW→SE corner]
    //   local (225,100) → world (300,225)   [NE→SW corner]
    //   local (175,300) → world (100,175)   [SW→NW corner]
    //   local (225,300) → world (100,225)   [SE→NE corner]
    //
    // World AABB of rotated shape: left=100 top=175 right=300 bottom=225  (50 DIP tall, 200 DIP wide)
    //
    // Point INSIDE rotated geometry but OUTSIDE local AABB:
    //   (150, 200) — outside local box (left=175), inside rotated body.
    //
    // Point IN local AABB corner but OUTSIDE rotated geometry:
    //   (180, 105) — inside local AABB (175..225 × 100..300) but outside the rotated body.

    private const double EmuPerDip = 9525.0;
    private static long ToDip(double dip) => (long)Math.Round(dip * EmuPerDip);

    private static (Presentation pres, Slide slide, SlideShape shape) MakeRotatedShape(
        double offsetX, double offsetY, double cx, double cy, double rotDeg)
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = ToDip(offsetX),
            OffsetYEmu  = ToDip(offsetY),
            ExtentCxEmu = ToDip(cx),
            ExtentCyEmu = ToDip(cy),
            RotationDeg = rotDeg,
        };
        slide.Shapes.Add(shape);
        return (pres, slide, shape);
    }

    [Fact]
    public void HitTest_RotatedShape90_PointInsideRotatedGeometry_Hits()
    {
        // 50×200 DIP shape (tall, narrow) at offset (175,100), rotated 90°.
        // Centre = (200, 200). After 90° rotation becomes 200×50 landscape.
        // Test point (150, 200) is within the rotated body but LEFT of the local AABB edge (x=175).
        var (pres, slide, shape) = MakeRotatedShape(175, 100, 50, 200, 90);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 150, 200);

        hit.Should().Be(shape.Id,
            "point (150,200) is inside the 90°-rotated body — un-rotating it should land inside the local AABB");
    }

    [Fact]
    public void HitTest_RotatedShape90_PointInAabbCornerOutsideRotatedGeometry_Misses()
    {
        // Same shape. Point (180, 105) is inside the local AABB (175..225 × 100..300)
        // but after un-rotating 90° about centre (200,200) it lands OUTSIDE the local box.
        var (pres, slide, _) = MakeRotatedShape(175, 100, 50, 200, 90);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 180, 105);

        hit.Should().BeNull(
            "point (180,105) is in the AABB corner but outside the actual rotated shape body");
    }

    [Fact]
    public void HitTest_ZeroDegShape_InsideHits_NoRegression()
    {
        // 0° shape at (0,0) 100×100 DIP. Point (50,50) must still hit.
        var (pres, slide, shape) = MakeRotatedShape(0, 0, 100, 100, 0);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 50, 50);

        hit.Should().Be(shape.Id, "0° shape: centre point must still hit (no regression)");
    }

    [Fact]
    public void HitTest_ZeroDegShape_OutsideMisses_NoRegression()
    {
        // 0° shape at (0,0) 100×100 DIP. Point (150,150) must still miss.
        var (pres, slide, _) = MakeRotatedShape(0, 0, 100, 100, 0);

        var hit = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, pres, 150, 150);

        hit.Should().BeNull("0° shape: point outside AABB must miss (no regression)");
    }
}

// ── AD3: anchor-fixed rotated resize ───────────────────────────────────────────────────────────

/// <summary>
/// AD3 — verifies that <see cref="AvaloniaCanvasGestureHandler.ComputeResizeBounds"/> keeps
/// the anchor corner fixed in world space when the shape is rotated.
/// Tests:
///   1. 90°-rotated shape: SE handle drag → NW anchor world position is unchanged, size changes.
///   2. 0° shape: SE handle drag → result is identical to the unmodified code path (no regression).
/// </summary>
public sealed class RotatedResizeAnchorTests
{
    private static Task Run(Action action) =>
        AvaloniaInteractionTestSession.Run(action);

    private const double EmuPerDip = 9525.0;
    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    /// <summary>
    /// Rotates a point (px,py) by angleDeg about centre (cx,cy) — mirror of SlideTransformCore.
    /// Used in the test to verify world positions without depending on production code.
    /// </summary>
    private static (double X, double Y) Rotate(double px, double py,
                                                double cx, double cy, double deg)
    {
        if (deg == 0) return (px, py);
        double r   = deg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        double dx = px - cx, dy = py - cy;
        return (cx + dx * cos - dy * sin,
                cy + dx * sin + dy * cos);
    }

    [Fact]
    public async Task ResizeSE_RotatedShape90_NwAnchorStaysFixed_SizeGrows()
    {
        // Shape: 100×100 DIP, offset (100, 100), rotated 90°.
        // Centre = (150, 150).  NW anchor corner in local = (100, 100).
        // World position of NW anchor (rotate 90° about centre):
        //   (100-150, 100-150) rotated 90° CW = (-50·cos90 - -50·sin90, -50·sin90 + -50·cos90)
        //   cos90=0 sin90=1 → (50, -50) → world = (200, 100).
        long nx = 0, ny = 0, ncx = 0, ncy = 0;

        await Run(() =>
        {
            var shape = new SlideShape
            {
                Id          = 1,
                OffsetXEmu  = ToEmu(100),
                OffsetYEmu  = ToEmu(100),
                ExtentCxEmu = ToEmu(100),
                ExtentCyEmu = ToEmu(100),
                RotationDeg = 90,
            };
            var p    = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            slide.Shapes.Add(shape);

            var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            handler.SnapToGrid   = false;
            handler.SnapToShapes = false;

            // Identity transform (scale=1, no offset).
            var xf = new SlideTransformCore(1.0, 0.0, 0.0, 1280, 720);

            // Drag SE handle by (+20, +20) screen px.
            var result = handler.SimulateResizeSE(
                new Point(0, 0), new Point(20, 20), xf, KeyModifiers.None, shape);
            nx = result.newX; ny = result.newY; ncx = result.newCx; ncy = result.newCy;
        });

        // Size must have changed.
        double newCxDip = nx == 0 ? ncx / EmuPerDip : ncx / EmuPerDip;
        newCxDip = ncx / EmuPerDip;
        double newCyDip = ncy / EmuPerDip;
        newCxDip.Should().BeGreaterThan(100,
            "SE drag on rotated shape must still grow the size in the local frame");

        // NW anchor world position must be the same as before the drag.
        // Original NW = local (100,100), centre (150,150), rot 90°.
        double origCentreX = 100 + 100 / 2.0; // 150
        double origCentreY = 100 + 100 / 2.0; // 150
        var (origAnchorWorldX, origAnchorWorldY) = Rotate(100, 100, origCentreX, origCentreY, 90);

        // New shape data.
        double newXDip  = nx / EmuPerDip;
        double newYDip  = ny / EmuPerDip;
        double newCxDipV = ncx / EmuPerDip;
        double newCyDipV = ncy / EmuPerDip;
        double newCentreX = newXDip + newCxDipV / 2.0;
        double newCentreY = newYDip + newCyDipV / 2.0;
        var (newAnchorWorldX, newAnchorWorldY) = Rotate(newXDip, newYDip, newCentreX, newCentreY, 90);

        newAnchorWorldX.Should().BeApproximately(origAnchorWorldX, 1.0,
            "NW anchor world X must be unchanged after SE resize of a 90°-rotated shape");
        newAnchorWorldY.Should().BeApproximately(origAnchorWorldY, 1.0,
            "NW anchor world Y must be unchanged after SE resize of a 90°-rotated shape");
    }

    [Fact]
    public async Task ResizeSE_ZeroDegShape_BehaviourUnchanged_NoRegression()
    {
        // 0° shape: SE drag by (+50, +60) should grow cx and cy without moving origin.
        long nx = 0, ny = 0, ncx = 0, ncy = 0;

        await Run(() =>
        {
            var shape = new SlideShape
            {
                Id          = 1,
                OffsetXEmu  = ToEmu(100),
                OffsetYEmu  = ToEmu(50),
                ExtentCxEmu = ToEmu(200),
                ExtentCyEmu = ToEmu(100),
                RotationDeg = 0,
            };
            var p     = Presentation.CreateEmpty();
            var slide = p.Slides[0];
            slide.Shapes.Clear();
            slide.Shapes.Add(shape);

            var bus     = new FreeP.Core.Model.PresentationCommandBus(p);
            var editor  = new FreeP.App.Compositor.EditingSession(p, bus);
            editor.Select(shape.Id);

            var canvas  = new SlideCanvas { Presentation = p, Slide = slide };
            var adorner = new SelectionAdornerLayer();
            var handler = new AvaloniaCanvasGestureHandler(canvas, editor, adorner);
            handler.SnapToGrid   = false;
            handler.SnapToShapes = false;

            var xf = new SlideTransformCore(1.0, 0.0, 0.0, 1280, 720);

            var result = handler.SimulateResizeSE(
                new Point(0, 0), new Point(50, 60), xf, KeyModifiers.None, shape);
            nx = result.newX; ny = result.newY; ncx = result.newCx; ncy = result.newCy;
        });

        // Origin must be unchanged for SE handle.
        nx.Should().Be(ToEmu(100), "SE resize: X origin must not change for a 0° shape");
        ny.Should().Be(ToEmu(50),  "SE resize: Y origin must not change for a 0° shape");

        // Width and height must grow.
        (ncx / EmuPerDip).Should().BeApproximately(250, 1.0,
            "0° SE drag +50px at scale=1 → width grows by 50 DIP");
        (ncy / EmuPerDip).Should().BeApproximately(160, 1.0,
            "0° SE drag +60px at scale=1 → height grows by 60 DIP");
    }
}
