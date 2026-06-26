using System.Windows;
using FreeP.App.Host;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Smoke tests for <see cref="SlideCanvas"/>: the control constructs without throwing and
/// renders a sample slide using the real <see cref="FreeP.App.Presentation.SlideCompositor"/> pipeline.
/// STA is required because SlideCanvas is a WPF FrameworkElement.
/// </summary>
public sealed class SlideCanvasTests
{
    [StaFact]
    public void SlideCanvas_ConstructsWithNullModel_DoesNotThrow()
    {
        var canvas = new SlideCanvas();
        canvas.Should().NotBeNull();
        canvas.Presentation.Should().BeNull();
        canvas.Slide.Should().BeNull();
    }

    [StaFact]
    public void SlideCanvas_SetPresentationAndSlide_DoesNotThrow()
    {
        var canvas = new SlideCanvas();
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];

        var act = () =>
        {
            canvas.Presentation = p;
            canvas.Slide = slide;
            canvas.Refresh();
        };

        act.Should().NotThrow();
    }

    [StaFact]
    public void SlideCanvas_WithShapes_DoesNotThrow()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];

        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1143000,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC4)),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, 0.75)
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            OffsetXEmu = 1000000,
            OffsetYEmu = 500000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
        });

        var canvas = new SlideCanvas
        {
            Presentation = p,
            Slide = slide
        };

        // Force a measure (simulates layout pass) — should not throw.
        canvas.Measure(new Size(1280, 720));
        canvas.Should().NotBeNull();
    }

    [StaFact]
    public void SlideCanvas_WithTextShape_DoesNotThrow()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 }
        };
        shape.Text = "Hello FreeP!";
        slide.Shapes.Add(shape);

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };

        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow();
    }

    [StaFact]
    public void SlideCanvas_Refresh_DoesNotThrow_WhenCalledMultipleTimes()
    {
        var p = Presentation.CreateEmpty();
        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };

        // Multiple refreshes should be idempotent.
        canvas.Refresh();
        canvas.Refresh();
        canvas.Refresh();

        canvas.Should().NotBeNull();
    }

    [StaFact]
    public void MainWindow_WithSlideCanvas_ConstructsSuccessfully()
    {
        var window = new MainWindow();
        try
        {
            window.Should().NotBeNull();
            window.Title.Should().Contain("FreeP");
            window.Content.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    // ── ComputeNiceAxisRange unit tests ───────────────────────────────────────

    private static ChartShape MakeChart(params double[] values)
    {
        var chart = new ChartShape();
        var series = new ChartSeries();
        series.Values.AddRange(values.Select(v => (double?)v));
        chart.Series.Add(series);
        return chart;
    }

    [Fact]
    public void ComputeNiceAxisRange_ZeroToHundred_NiceTicksAndCoversMax()
    {
        var chart = MakeChart(0, 25, 50, 75, 100);
        var (min, max, unit) = SlideCanvas.ComputeNiceAxisRange(chart);
        min.Should().Be(0);
        max.Should().BeGreaterThanOrEqualTo(100, "must cover data max");
        unit.Should().BeGreaterThan(0);
        // The important invariant: max is exactly divisible by unit
        (max % unit).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void ComputeNiceAxisRange_Values0To200_MaxIsNiceMultiple()
    {
        var chart = MakeChart(120, 200, 150, 180, 130, 170, 160, 190);
        var (min, max, unit) = SlideCanvas.ComputeNiceAxisRange(chart);
        min.Should().Be(0, "data is non-negative so floor is 0");
        max.Should().BeGreaterThanOrEqualTo(200, "max must cover data max");
        (max % unit).Should().BeApproximately(0, 1e-9, "max must be a multiple of majorUnit");
        unit.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeNiceAxisRange_SmallValues_MajorUnitIsPositive()
    {
        var chart = MakeChart(1.2, 3.5, 2.8, 4.1);
        var (_, _, unit) = SlideCanvas.ComputeNiceAxisRange(chart);
        unit.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeNiceAxisRange_LargeValues_MajorUnitIsHumanReadable()
    {
        // Revenue data in thousands: 50, 80, 65, 90, 75, 110
        var chart = MakeChart(50, 80, 65, 90, 75, 110);
        var (min, max, unit) = SlideCanvas.ComputeNiceAxisRange(chart);
        unit.Should().BeOneOf(new double[] { 10, 20, 25, 50 });
        max.Should().BeGreaterThanOrEqualTo(110);
        (max % unit).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void ComputeNiceAxisRange_ExplicitAxisMax_IsRespected()
    {
        var chart = MakeChart(50, 100);
        chart.ValueAxis.Max = 200;
        var (_, max, _) = SlideCanvas.ComputeNiceAxisRange(chart);
        max.Should().BeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public void ComputeNiceAxisRange_MajorUnitDividesRangeCleanly()
    {
        // Typical bar chart data
        var chart = MakeChart(80, 100, 60, 90, 90, 110, 70, 100);
        var (min, max, unit) = SlideCanvas.ComputeNiceAxisRange(chart);
        double range = max - min;
        // Number of intervals should be 3-7
        int intervals = (int)Math.Round(range / unit);
        intervals.Should().BeInRange(3, 7, "nice axis should have 3-7 gridline intervals");
    }

    // ── BA2: WordArt / text-effects double-draw regression tests ─────────────

    /// <summary>
    /// BA2 regression: a warped text body must not cause a flat ghost behind the warped glyphs.
    /// The base DrawText pass must be suppressed when warp is active.
    /// Verified by: Measure must not throw (layout pipeline runs RenderParaWithEffects for all runs).
    /// </summary>
    [StaFact]
    public void SlideCanvas_WarpedTextBody_DoesNotThrow_AndDrawsOnce()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var tb = new TextBody { WarpPreset = "textArchUp" };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Plain" });
        para.Runs.Add(new Run
        {
            Text = "Gradient",
            TextFill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                angleDegrees: 90.0)
        });
        tb.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id            = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu    = 457200,
            OffsetYEmu    = 274320,
            ExtentCxEmu   = 8229600,
            ExtentCyEmu   = 1143000,
            TextBody      = tb
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow("warped text body must not cause a double-draw crash");
    }

    /// <summary>
    /// BA2 regression: a paragraph with a gradient-fill run must not draw a flat base glyph
    /// under the gradient overlay.  The base DrawText pass must be suppressed for effect runs.
    /// </summary>
    [StaFact]
    public void SlideCanvas_GradientFillRun_MixedWithPlainRun_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var tb   = new TextBody();
        var para = new Paragraph();
        // Plain run — must still be drawn via the updated RenderParaWithEffects plain-run path.
        para.Runs.Add(new Run { Text = "Normal " });
        // Effect run — must NOT be drawn by the base DrawText pass.
        para.Runs.Add(new Run
        {
            Text    = "Gradient",
            TextFill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x66, 0x00)),
                new ThemeAwareColor(new SrgbColor(0xCC, 0x00, 0x00)),
                angleDegrees: 45.0)
        });
        para.Runs.Add(new Run
        {
            Text       = " Outline",
            TextOutline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)), widthPt: 1.0, dash: OutlineDash.Solid)
        });
        tb.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id            = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu    = 457200,
            OffsetYEmu    = 274320,
            ExtentCxEmu   = 8229600,
            ExtentCyEmu   = 1143000,
            TextBody      = tb
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act    = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow("mixed plain+effect runs must render without double-draw exception");
    }

    /// <summary>
    /// BA2 regression: text body with shadow on every run plus warp.
    /// All runs go through RenderParaWithEffects — the plain-run geometry path is not exercised,
    /// but the shadow+warp path must not throw.
    /// </summary>
    [StaFact]
    public void SlideCanvas_ShadowAndWarp_AllRunsEffect_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shadow = new RunTextShadow
        {
            Color  = new ThemeAwareColor(new SrgbColor(0x20, 0x20, 0x20)),
            Alpha  = 180,
            BlurPt = 3.0,
            DistPt = 2.5,
            DirDeg = 45.0
        };

        var tb   = new TextBody { WarpPreset = "textWave1" };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Warped & shadowed", TextShadow = shadow });
        tb.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id            = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu    = 457200,
            OffsetYEmu    = 274320,
            ExtentCxEmu   = 8229600,
            ExtentCyEmu   = 1143000,
            TextBody      = tb
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act    = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow("warp+shadow text must not throw");
    }
}
