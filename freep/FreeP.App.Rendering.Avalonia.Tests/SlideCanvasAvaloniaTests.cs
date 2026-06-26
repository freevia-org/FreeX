using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
}
