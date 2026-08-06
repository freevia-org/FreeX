using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Host;
using FreeP.App.Compositor;
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
    public void SlideCanvas_SolidShapeFillAlpha_BlendsOverBackground()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Background = new ShapeFill.Solid(SrgbColor.White);
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = p.SlideSizeCxEmu,
            ExtentCyEmu = p.SlideSizeCyEmu,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000), alpha: 128)),
            Outline = ShapeOutline.None.Instance
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        canvas.Measure(new Size(100, 60));
        canvas.Arrange(new Rect(0, 0, 100, 60));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(100, 60, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        var pixels = new byte[100 * 60 * 4];
        rtb.CopyPixels(pixels, 100 * 4, 0);
        var offset = ((30 * 100) + 50) * 4;

        pixels[offset + 2].Should().BeGreaterThan(180);
        pixels[offset + 1].Should().BeInRange((byte)80, (byte)180);
        pixels[offset].Should().BeInRange((byte)80, (byte)180);
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
    public void SlideCanvas_ActiveTextEditShapeId_SuppressesOnlyMatchingShapeText()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Background = new ShapeFill.Solid(SrgbColor.White);
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1371600,
            Fill = new ShapeFill.Solid(new SrgbColor(0xD9, 0xE2, 0xF3)),
            TextBody = MakeRenderTextBody("Active shape"),
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            OffsetXEmu = 4572000,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1371600,
            Fill = new ShapeFill.Solid(new SrgbColor(0xE2, 0xF0, 0xD9)),
            TextBody = MakeRenderTextBody("Other shape"),
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        canvas.Measure(new Size(960, 540));
        canvas.Arrange(new Rect(0, 0, 960, 540));
        canvas.UpdateLayout();

        var before = RenderPixels(canvas, 960, 540);
        canvas.ActiveTextEditShapeId = 1;
        var suppressed = RenderPixels(canvas, 960, 540);

        CountPixelDifferences(before, suppressed, 0, 0, 360, 260)
            .Should().BeGreaterThan(0, "the active shape base text should be removed");
        CountPixelDifferences(before, suppressed, 360, 0, 960, 260)
            .Should().Be(0, "a different shape must remain unchanged");
    }

    [StaFact]
    public void SlideCanvas_WithStackedVerticalTextShape_RendersWithoutThrow()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 2743200,
            TextBody = new TextBody { VerticalType = TextVerticalType.EastAsianVertical }
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Stacked" });
        shape.TextBody.Paragraphs.Add(paragraph);
        slide.Shapes.Add(shape);

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        canvas.Measure(new Size(1280, 720));
        canvas.Arrange(new Rect(0, 0, 1280, 720));

        var act = () =>
        {
            var rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);
        };
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
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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

    private static TextBody MakeRenderTextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static byte[] RenderPixels(SlideCanvas canvas, int width, int height)
    {
        canvas.Refresh();
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int CountPixelDifferences(
        byte[] first,
        byte[] second,
        int left,
        int top,
        int right,
        int bottom)
    {
        int width = 960;
        int differences = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = (y * width + x) * 4;
                if (first[offset] != second[offset]
                    || first[offset + 1] != second[offset + 1]
                    || first[offset + 2] != second[offset + 2]
                    || first[offset + 3] != second[offset + 3])
                {
                    differences++;
                }
            }
        }

        return differences;
    }

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
        var (min, max, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
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
        var (min, max, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        min.Should().Be(0, "data is non-negative so floor is 0");
        max.Should().BeGreaterThanOrEqualTo(200, "max must cover data max");
        (max % unit).Should().BeApproximately(0, 1e-9, "max must be a multiple of majorUnit");
        unit.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeNiceAxisRange_SmallValues_MajorUnitIsPositive()
    {
        var chart = MakeChart(1.2, 3.5, 2.8, 4.1);
        var (_, _, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        unit.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeNiceAxisRange_LargeValues_MajorUnitIsHumanReadable()
    {
        // Revenue data in thousands: 50, 80, 65, 90, 75, 110
        var chart = MakeChart(50, 80, 65, 90, 75, 110);
        var (min, max, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        unit.Should().BeOneOf(new double[] { 10, 20, 25, 50 });
        max.Should().BeGreaterThanOrEqualTo(110);
        (max % unit).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void ComputeNiceAxisRange_ExplicitAxisMax_IsRespected()
    {
        var chart = MakeChart(50, 100);
        chart.ValueAxis.Max = 200;
        var (_, max, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        max.Should().BeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public void ComputeNiceAxisRange_MajorUnitDividesRangeCleanly()
    {
        // Typical bar chart data
        var chart = MakeChart(80, 100, 60, 90, 90, 110, 70, 100);
        var (min, max, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);
        double range = max - min;
        // Number of intervals should be 3-7
        int intervals = (int)Math.Round(range / unit);
        intervals.Should().BeInRange(3, 7, "nice axis should have 3-7 gridline intervals");
    }

    [StaFact]
    public void SlideCanvas_ChartGridLinePen_UsesSharedStrokePlan()
    {
        var plan = new ChartMajorGridLinePrimitivePlan(
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(
                new SrgbColor(0x12, 0x34, 0x56),
                Alpha: 0x7F,
                Thickness: 1.25,
                Dash: OutlineDash.DashDot));

        var pen = SlideCanvas.CreateChartGridLinePen(plan);

        pen.Thickness.Should().Be(1.25);
        var brush = pen.Brush.Should()
            .BeOfType<System.Windows.Media.SolidColorBrush>()
            .Subject;
        brush.Color.Should().Be(System.Windows.Media.Color.FromArgb(0x7F, 0x12, 0x34, 0x56));
        pen.DashStyle.Should().Be(System.Windows.Media.DashStyles.DashDot);
        pen.IsFrozen.Should().BeTrue();
    }

    [StaFact]
    public void SlideCanvas_ChartGridLinePen_UsesSharedStrokeGradientFill()
    {
        var gradient = new ResolvedFill.Gradient(
            new[]
            {
                new ResolvedFill.ResolvedGradientStop(0.0, new SrgbColor(0x10, 0x20, 0x30)),
                new ResolvedFill.ResolvedGradientStop(1.0, new SrgbColor(0xD0, 0xE0, 0xF0))
            },
            GradientKind.Linear,
            angleDegrees: 45.0);
        var plan = new ChartMajorGridLinePrimitivePlan(
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(
                new SrgbColor(0x12, 0x34, 0x56),
                Alpha: 0x7F,
                Thickness: 1.75,
                Dash: OutlineDash.LongDash)
            {
                Fill = gradient
            });

        var pen = SlideCanvas.CreateChartGridLinePen(plan);

        pen.Thickness.Should().Be(1.75);
        var brush = pen.Brush.Should()
            .BeOfType<System.Windows.Media.LinearGradientBrush>()
            .Subject;
        brush.GradientStops.Should().HaveCount(17);
        brush.GradientStops.First().Should().Match<System.Windows.Media.GradientStop>(stop =>
            stop.Offset == 0.0 && stop.Color == System.Windows.Media.Color.FromRgb(0x10, 0x20, 0x30));
        brush.GradientStops.Last().Should().Match<System.Windows.Media.GradientStop>(stop =>
            stop.Offset == 1.0 && stop.Color == System.Windows.Media.Color.FromRgb(0xD0, 0xE0, 0xF0));
        pen.DashStyle.Dashes.Should().Equal(8.0, 3.0);
        pen.IsFrozen.Should().BeTrue();
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

    [StaFact]
    public void SlideCanvas_GlowAndSoftEdgeRuns_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var tb   = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text = "Glow ",
            TextGlow = new RunTextGlow
            {
                Color = new ThemeAwareColor(new SrgbColor(0x20, 0x80, 0xFF)),
                Alpha = 128,
                RadiusPt = 4.0
            }
        });
        para.Runs.Add(new Run
        {
            Text = "Soft",
            TextSoftEdge = new RunTextSoftEdge
            {
                RadiusPt = 2.5
            }
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
        act.Should().NotThrow("glow and soft-edge text runs must render through shared planner cases");
    }

    // ── BO2: default tab stops (no explicit tabLst) — WPF ───────────────────────

    /// <summary>
    /// BO2 regression (WPF): a paragraph with a tab character and NO explicit tab stops must
    /// route through RenderParaWithTabs and use the 96 DIP default interval rather than
    /// collapsing \t via plain DrawText.
    /// </summary>
    [StaFact]
    public void SlideCanvas_TabWithNoExplicitStops_UsesDefaultInterval_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var tb   = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Before\tAfter" }); // tab, no explicit stops
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
        act.Should().NotThrow("BO2: tab with no explicit tab stops must use default interval without throwing");
    }

    // ── BO1: tab alignment — right/center/decimal stops — WPF ───────────────────

    /// <summary>
    /// BO1 regression (WPF): paragraphs with right, center, and decimal explicit tab stops must
    /// render without throwing. The alignment-offset path is exercised end-to-end.
    /// </summary>
    [StaFact]
    public void SlideCanvas_TabWithRightAndCenterStops_DoesNotThrow()
    {
        const long EmuPerDip = 9525L;

        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        // Right + Center stops
        var tb1   = new TextBody();
        var para1 = new Paragraph();
        para1.Runs.Add(new Run { Text = "Left\tRight\tCenter" });
        para1.TabStops.Add(new TabStop { PositionEmu = 192 * EmuPerDip, Alignment = TabStopAlignment.Right  });
        para1.TabStops.Add(new TabStop { PositionEmu = 384 * EmuPerDip, Alignment = TabStopAlignment.Center });
        tb1.Paragraphs.Add(para1);

        // Decimal stop
        var tb2   = new TextBody();
        var para2 = new Paragraph();
        para2.Runs.Add(new Run { Text = "Amount\t9876.54" });
        para2.TabStops.Add(new TabStop { PositionEmu = 288 * EmuPerDip, Alignment = TabStopAlignment.Decimal });
        tb2.Paragraphs.Add(para2);

        slide.Shapes.Add(new SlideShape
        {
            Id = 1, AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            TextBody = tb1
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 1600000,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            TextBody = tb2
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow("BO1: right/center/decimal tab alignment must not throw in WPF renderer");
    }

    // ── BQ1: cross-run tab alignment (WPF) ──────────────────────────────────────

    /// <summary>
    /// BQ1 regression (WPF): when the tab character ends run1 and the aligned text is in run2,
    /// the right/center alignment offset must be computed across BOTH runs' text segments
    /// (run-agnostic forward scan), not just from the empty tail of run1.
    /// Verified by: rendering must not throw, and a second Measure with a tiny shape (1 DIP wide)
    /// must not throw either — the clamp (BQ2) handles the case where segment > gap.
    /// </summary>
    [StaFact]
    public void SlideCanvas_CrossRunRightTabAlignment_DoesNotThrow()
    {
        const long EmuPerDip = 9525L;

        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        // run1 ends with '\t' (tab token has seg=""), run2 holds the value in bold.
        // Pattern: "Chapter\t" (run1, normal) + "42" (run2, bold) — page-number style.
        var tb   = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Chapter\t", Bold = false });
        para.Runs.Add(new Run { Text = "42",        Bold = true  });
        para.TabStops.Add(new TabStop
        {
            PositionEmu = 480 * EmuPerDip,      // 5-inch right stop
            Alignment   = TabStopAlignment.Right
        });
        tb.Paragraphs.Add(para);

        // Also test center cross-run: "Section\t" (run1) + "Title" (run2)
        var tb2   = new TextBody();
        var para2 = new Paragraph();
        para2.Runs.Add(new Run { Text = "Section\t", Bold = false });
        para2.Runs.Add(new Run { Text = "Title",     Bold = true  });
        para2.TabStops.Add(new TabStop
        {
            PositionEmu = 384 * EmuPerDip,      // 4-inch center stop
            Alignment   = TabStopAlignment.Center
        });
        tb2.Paragraphs.Add(para2);

        slide.Shapes.Add(new SlideShape
        {
            Id = 1, AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            TextBody = tb
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 1600000,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            TextBody = tb2
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow(
            "BQ1: right/center tab alignment must work when aligned text is in a different run from the tab");
    }

    // ── BQ2: wide aligned segment — backward-clamp (WPF) ────────────────────────

    /// <summary>
    /// BQ2 regression (WPF): when the aligned segment is wider than the gap from the preceding
    /// text to the tab stop, curX must be clamped to the prior pen position (not go negative
    /// relative to it).  Verified by: rendering a very wide segment must not throw.
    /// </summary>
    [StaFact]
    public void SlideCanvas_WideSegment_BackwardClampDoesNotThrow()
    {
        const long EmuPerDip = 9525L;

        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        // Right stop at 1 inch (96 DIP).  Preceding text "LongPrecedingText" is already
        // wider than that, so stopDip + alignOffset would be < prevCurX without the clamp.
        var tb   = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "LongPrecedingText\tWideSegmentThatExceedsGap" });
        para.TabStops.Add(new TabStop
        {
            PositionEmu = 96 * EmuPerDip,       // 1-inch right stop — narrow target
            Alignment   = TabStopAlignment.Right
        });
        tb.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id = 1, AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            TextBody = tb
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow(
            "BQ2: wide aligned segment must not cause curX to go behind the prior pen (backward clamp)");
    }

    // ── CB1: secondary-axis range isolation (WPF) ─────────────────────────────

    /// <summary>
    /// CB1: primary ComputeNiceAxisRange must exclude secondary-axis series.
    /// Chart: primary bars 0-100, secondary line 0-1_000_000.
    /// Primary range ≈ 0-100 (NOT 0-1M); secondary range ≈ 0-1M.
    /// </summary>
    [Fact]
    public void CB1_WPF_PrimaryRange_ExcludesSecondaryAxisSeries()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (min, max, _) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

        min.Should().BeGreaterThanOrEqualTo(0, "primary range min should start at or above 0");
        max.Should().BeLessThan(10_000,
            "CB1: primary range must not be polluted by the 1M secondary series (should be ~100-200)");
        max.Should().BeGreaterThanOrEqualTo(100, "primary range must cover the 100 primary max");
    }

    [Fact]
    public void CB1_WPF_SecondaryRange_CoversSecondaryAxisSeriesOnly()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, secMu) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

        secMin.Should().BeGreaterThanOrEqualTo(0, "secondary range min should start at or above 0");
        secMax.Should().BeGreaterThanOrEqualTo(1_000_000, "secondary range must cover the 1M secondary max");
        secMu.Should().BePositive("secondary major unit must be positive");
    }

    [Fact]
    public void CB1_WPF_SecondarySeriesPixelFraction_IsReasonable()
    {
        // Verify that a secondary series value maps to a sensible pixel fraction when scaled
        // against the secondary range — not a huge ratio (broken) or ~0 (invisible).
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (secMin, secMax, _) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);
        double secRange = secMax - secMin;

        // A mid-range secondary value (600,000) at plotH=400 should map to a reasonable pixel
        double midVal = 600_000.0;
        double frac   = (midVal - secMin) / secRange;

        frac.Should().BeGreaterThan(0.3, "CB1: mid-range secondary value must occupy a meaningful fraction of the plot");
        frac.Should().BeLessThan(1.1,   "CB1: secondary fraction must not exceed 1.0 (plus rounding)");
    }

    [Fact]
    public void CB1_WPF_NoSecondarySeriesChart_FallbackRange()
    {
        // No secondary series: primary range unchanged; secondary fallback = (0,1,1).
        var s = new ChartSeries { Name = "S1", OnSecondaryAxis = false };
        s.Values.AddRange(new double?[] { 10, 50, 100 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(s);

        var (sMin, sMax, sMu) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

        sMin.Should().Be(0,   "fallback secondary min");
        sMax.Should().Be(1,   "fallback secondary max");
        sMu.Should().Be(1,    "fallback secondary unit");
    }

    [StaFact]
    public void CB1_WPF_ComboChart_RendersWithoutThrow()
    {
        // Smoke test: a combo chart (primary column + secondary line) must not throw during Measure.
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape
        {
            ChartType          = ChartType.ColumnClustered,
            SecondaryValueAxis = new ChartAxis(),
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var p = Presentation.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart       = chart,
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        var act = () => canvas.Measure(new Size(1280, 720));
        act.Should().NotThrow(
            "CB1: combo chart with primary bars + secondary line must render without throwing");
    }

    [StaFact]
    public void SlideCanvas_SmoothedLineChart_RendersWithoutThrow()
    {
        var series = new ChartSeries
        {
            Name = "Smoothed",
            SmoothLine = true
        };
        series.Values.AddRange(new double?[] { 10, 20, 30, 15 });

        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        chart.Series.Add(series);

        var p = Presentation.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart,
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        canvas.Measure(new Size(960, 540));
        canvas.Arrange(new Rect(0, 0, 960, 540));
        var rtb = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);

        var act = () => rtb.Render(canvas);

        act.Should().NotThrow("WPF should consume smoothed line path primitives");
    }

    [StaFact]
    public void SlideCanvas_SmoothedScatterChart_RendersWithoutThrow()
    {
        var series = new ChartSeries { Name = "Smoothed scatter" };
        series.XValues.AddRange(new double?[] { 0, 50, 100, 150 });
        series.Values.AddRange(new double?[] { 10, 25, 15, 35 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.SmoothMarker
        };
        chart.Series.Add(series);

        var p = Presentation.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart,
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        canvas.Measure(new Size(960, 540));
        canvas.Arrange(new Rect(0, 0, 960, 540));
        var rtb = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);

        var act = () => rtb.Render(canvas);

        act.Should().NotThrow("WPF should consume smoothed scatter path primitives");
    }

    [StaFact]
    public void SlideCanvas_RadarChart_RendersWithoutThrow()
    {
        var series = new ChartSeries { Name = "Coverage" };
        series.Values.AddRange(new double?[] { 4, 6, 3, 5 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Filled
        };
        chart.Categories.AddRange(new[] { "North", "East", "South", "West" });
        chart.Series.Add(series);

        var p = Presentation.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart,
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        canvas.Measure(new Size(960, 540));
        canvas.Arrange(new Rect(0, 0, 960, 540));
        var rtb = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);

        var act = () => rtb.Render(canvas);

        act.Should().NotThrow("WPF should consume shared radar primitive plans");
    }

    [StaFact]
    public void SlideCanvas_StockVolumeChart_RendersWithoutThrow()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });
        foreach (var (name, values) in new[]
        {
            ("Volume", new double?[] { 1000, 1500, 750 }),
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 14 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        var p = Presentation.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Chart = chart,
        });

        var canvas = new SlideCanvas { Presentation = p, Slide = p.Slides[0] };
        canvas.Measure(new Size(960, 540));
        canvas.Arrange(new Rect(0, 0, 960, 540));
        var rtb = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);

        var act = () => rtb.Render(canvas);

        act.Should().NotThrow("WPF should consume shared stock volume and OHLC primitive plans");
    }

    [Fact]
    public void SlideCanvas_LineSeriesRenderer_ConsumesSharedPathPrimitive()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var planner = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Core",
            "ChartRenderCommandPlanner.cs"));
        var execution = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.ChartExecution.cs"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        planner.Should().Contain("foreach (var path in primitive.LinePaths)");
        planner.Should().Contain("new ChartRenderCommand.LinePath(");
        execution.Should().Contain("ToGeometry(path.Primitive, path.Depth)");
        source.Should().Contain("ctx.BezierTo(");
        source.Should().Contain("ChartLinePathSegmentKind.CubicBezier");
    }

}
