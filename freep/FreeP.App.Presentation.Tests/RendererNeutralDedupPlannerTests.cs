using System.IO;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class RendererNeutralDedupPlannerTests
{
    [Fact]
    public void WordArtWarpPlanner_ComputesKnownOffsetsAndRejectsUnknownPresets()
    {
        var bounds = new LayoutRect(0, 0, 200, 100);

        WordArtWarpPlanner.ComputeYOffset("textArchUp", 0.5, bounds)
            .Should().BeApproximately(-35, 0.001);
        WordArtWarpPlanner.ComputeYOffset("textSlantDown", 0.25, bounds)
            .Should().BeApproximately(7.5, 0.001);
        WordArtWarpPlanner.ComputeYOffset("not-a-preset", 0.5, bounds)
            .Should().BeNull();
    }

    [Fact]
    public void ShapeTransformPlanner_PlansFlipAndRotationMatrices()
    {
        var bounds = new LayoutRect(10, 20, 100, 60);

        var flip = ShapeTransformPlanner.PlanShapeTransform(bounds, 0, flipH: true, flipV: false);
        flip.Should().Be(new ShapeAffineTransform(-1, 0, 0, 1, 120, 0));

        var rotation = ShapeTransformPlanner.PlanShapeTransform(bounds, 90, flipH: false, flipV: false);
        rotation.M11.Should().BeApproximately(0, 0.001);
        rotation.M12.Should().BeApproximately(1, 0.001);
        rotation.M21.Should().BeApproximately(-1, 0.001);
        rotation.M22.Should().BeApproximately(0, 0.001);
        rotation.OffsetX.Should().BeApproximately(110, 0.001);
        rotation.OffsetY.Should().BeApproximately(-10, 0.001);
    }

    [Fact]
    public void Scene3dProjectionPlanner_ProjectsIsometricTopUpCamera()
    {
        var projection = Scene3dProjectionPlanner.Plan(
            new LayoutRect(80, 320, 266, 186),
            "isometricTopUp");

        projection.M11.Should().BeApproximately(0.505, 0.0001);
        projection.M12.Should().BeApproximately(0.2925, 0.0001);
        projection.M21.Should().BeApproximately(-1.015, 0.0001);
        projection.M22.Should().BeApproximately(0.588, 0.0001);
        projection.IsIdentity.Should().BeFalse();

        Scene3dProjectionPlanner.Plan(new LayoutRect(0, 0, 100, 60), "orthographicFront")
            .Should().Be(ShapeAffineTransform.Identity);
    }

    [Fact]
    public void BevelGeometryHelper_MapsSurfaceDimensionsToVisibleFootprint()
    {
        var dimensions = BevelGeometryHelper.GetRenderDimensions(
            new LayoutRect(0, 0, 100, 80),
            bevelWidthDip: 20,
            bevelHeightDip: 15);

        dimensions.WidthDip.Should().BeApproximately(8, 0.001);
        dimensions.HeightDip.Should().BeApproximately(6, 0.001);
    }

    [Fact]
    public void ResolvedShapeEffectRenderPlanner_ExpandsShadowAndGlowPasses()
    {
        var effects = new ResolvedShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowColor = new SrgbColor(1, 2, 3),
            OuterShadowAlpha = 100,
            OuterShadowBlurDip = 4,
            OuterShadowDistDip = 10,
            OuterShadowDirDeg = 0,
            HasGlow = true,
            GlowColor = new SrgbColor(4, 5, 6),
            GlowAlpha = 120,
            GlowRadiusDip = 5
        };

        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(effects);

        plan.ShadowPasses.Should().HaveCount(17);
        plan.ShadowPasses[0].Should().Be(new ShapeShadowPass(6, -4, new SrgbColor(1, 2, 3), 33));
        plan.ShadowPasses[^1].Should().Be(new ShapeShadowPass(10, 0, new SrgbColor(1, 2, 3), 100));
        plan.GlowPasses.Should().HaveCount(3);
        plan.GlowPasses[0].StrokeWidthDip.Should().BeApproximately(10, 0.001);
        plan.GlowPasses[0].Alpha.Should().Be(30);
        plan.GlowPasses[^1].StrokeWidthDip.Should().BeApproximately(10.0 / 3.0, 0.001);
    }

    [Fact]
    public void PictureColorEffectPlanner_AppliesGrayscaleAndPreservesAlpha()
    {
        byte[] pixels =
        [
            0, 0, 255, 7,
            0, 255, 0, 8,
            255, 0, 0, 9
        ];

        PictureColorEffectPlanner.ApplyToBgra32(
            pixels,
            new PictureColorEffectPlan(
                Grayscale: true,
                BiLevelThreshold: null,
                Brightness: null,
                Contrast: null));

        pixels.Should().Equal(
        [
            54, 54, 54, 7,
            182, 182, 182, 8,
            18, 18, 18, 9
        ]);
    }

    [Fact]
    public void PictureColorEffectPlanner_AppliesBrightnessContrastAndBiLevelInRendererOrder()
    {
        byte[] pixels =
        [
            0, 0, 0, 77,
            128, 128, 128, 88,
            255, 255, 255, 99
        ];

        PictureColorEffectPlanner.ApplyToBgra32(
            pixels,
            new PictureColorEffectPlan(
                Grayscale: false,
                BiLevelThreshold: 0.5,
                Brightness: 0.25,
                Contrast: -0.5));

        pixels.Should().Equal(
        [
            0, 0, 0, 77,
            255, 255, 255, 88,
            255, 255, 255, 99
        ]);
    }

    [Fact]
    public void PictureColorEffectPlanner_PixelPlanIgnoresAlphaOnlyOpacity()
    {
        var alphaOnly = PictureColorEffectPlanner.Plan(new DrawOp.Picture { AlphaModPct = 0.5 });
        alphaOnly.HasPixelEffects.Should().BeFalse();

        var withBrightness = PictureColorEffectPlanner.Plan(new DrawOp.Picture { Brightness = 0.1 });
        withBrightness.HasPixelEffects.Should().BeTrue();
    }

    [Fact]
    public void PictureRenderPlanner_NoCropUsesFullSourceAndDestinationBounds()
    {
        var picture = new DrawOp.Picture
        {
            DestDip = new LayoutRect(10, 20, 300, 200)
        };

        var plan = PictureRenderPlanner.Plan(picture, pixelWidth: 640, pixelHeight: 480);

        plan.DestinationDip.Should().Be(new LayoutRect(10, 20, 300, 200));
        plan.SourceRectPixels.Should().Be(new PictureSourceRectPixels(0, 0, 640, 480));
        plan.HasCrop.Should().BeFalse();
        plan.HasPixelEffects.Should().BeFalse();
        plan.HasAlphaOpacity.Should().BeFalse();
        plan.HasOuterEffects.Should().BeFalse();
    }

    [Fact]
    public void PictureRenderPlanner_CropSourceRectangleRoundsAndClamps()
    {
        var picture = new DrawOp.Picture
        {
            CropLeft = 1.5,
            CropTop = -0.2,
            CropRight = 0.9,
            CropBottom = 1.5
        };

        var plan = PictureRenderPlanner.Plan(picture, pixelWidth: 20, pixelHeight: 10);

        plan.SourceRectPixels.Should().Be(new PictureSourceRectPixels(19, 0, 1, 1));
        plan.HasCrop.Should().BeTrue();
    }

    [Fact]
    public void PictureRenderPlanner_PlansColorEffectsAlphaAndOuterEffectOrder()
    {
        var picture = new DrawOp.Picture
        {
            Brightness = 0.2,
            Contrast = -0.1,
            AlphaModPct = 0.42,
            Effects = new ResolvedShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = new SrgbColor(1, 2, 3),
                OuterShadowAlpha = 128,
                OuterShadowDistDip = 4,
                OuterShadowDirDeg = 0
            }
        };

        var plan = PictureRenderPlanner.Plan(picture, pixelWidth: 100, pixelHeight: 50);

        plan.ColorEffects.HasPixelEffects.Should().BeTrue();
        plan.AlphaOpacity.Should().BeApproximately(0.42, 0.0001);
        plan.HasAlphaOpacity.Should().BeTrue();
        plan.HasOuterEffects.Should().BeTrue();
        plan.OuterEffects.ShadowPasses.Should().NotBeEmpty();
        plan.PhaseOrder.Should().Equal(
            PictureRenderPhase.OuterEffects,
            PictureRenderPhase.PixelColorEffects,
            PictureRenderPhase.AlphaOpacity,
            PictureRenderPhase.ImageBody);
        plan.AlphaAppliesToImageBody.Should().BeTrue();
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralShapeAndWarpPlanners()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");
        var textEffectPlanner = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "TextRunEffectRenderPlanner.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ShapeTransformPlanner.PlanShapeRenderTransform");
            source.Should().Contain("ResolvedShapeEffectRenderPlanner.PlanOuterEffects");
            source.Should().Contain("TextRunEffectRenderPlanner");
            source.Should().NotContain("BuildWarpYFunc");
            source.Should().NotContain("OuterShadowDirDeg * Math.PI");
            source.Should().NotContain("OuterShadowBlurDip / 2");
            source.Should().NotContain("GlowRadiusDip / 2");
        }

        textEffectPlanner.Should().Contain("WordArtWarpPlanner.ComputeYOffset");
        textEffectPlanner.Should().Contain("ResolvedRunShadow");

        wpf.Should().NotContain("BuildShapeTransform");
        avalonia.Should().NotContain("BuildShapeMatrix");
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralPictureRenderPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PictureRenderPlanner.Plan(pic");
            source.Should().Contain("PictureColorEffectPlanner.ApplyToBgra32");
            source.Should().NotContain("0.2126 * r + 0.7152 * g + 0.0722 * b");
            source.Should().NotContain("Math.Round(pic.CropLeft");
            source.Should().NotContain("visW = 1.0 - pic.CropLeft");
            source.Should().NotContain("pic.Brightness ?? 0");
            source.Should().NotContain("pic.Contrast  ?? 0");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseSharedTextParagraphRoutePlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TextLayoutPlanner.PlanParagraphRenderRoute");
            source.Should().Contain("TextParagraphRenderRoute.Effects");
            source.Should().Contain("TextParagraphRenderRoute.Tabs");
            source.Should().NotContain("ParaHasTextEffects(para) || text.WarpPreset");
            source.Should().NotContain("bool hasTabs");
            source.Should().NotContain("para.Runs.Any(r => r.Text.Contains('\\t'))");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralPieSlicePlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildPieSlicePrimitives");
            source.Should().Contain("ChartRenderPlanner.BuildDoughnutSlicePrimitives");
            source.Should().Contain("primitive.OuterRadiusY");
            source.Should().Contain("primitive.InnerRadiusY");
            source.Should().Contain("primitive.HasThreeDDepth");
            source.Should().Contain("primitive.DepthOffsetY");
            source.Should().Contain("ChartRenderPlanner.ThreeDPieDepthFillAlpha");
            source.Should().Contain("ToPieSliceGeometry(primitive, primitive.DepthOffsetY)");
            source.Should().NotContain("chart.DoughnutHolePercent, 0, 90");
            source.Should().NotContain("ringGap =");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralAreaScatterBubbleRadarAndStockPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildAreaSeriesPrimitives");
            source.Should().Contain("ChartRenderPlanner.BuildScatterPrimitivePlan");
            source.Should().Contain("ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("primitive.LinePaths");
            source.Should().Contain("primitive.Markers");
            source.Should().Contain("plan.DataLabels");
            source.Should().Contain("ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("ChartRenderPlanner.BuildStockPrimitivePlan(chart, plot)");
            source.Should().Contain("ChartRenderPlanner.BuildStockVolumePrimitives(chart, plot, seriesColors)");
            source.Should().Contain("ChartRenderPlanner.BuildSurfaceGeometryPlan(chart, plot, seriesColors)");
            source.Should().Contain("plan.Facets");
            source.Should().Contain("plan.WireframeSegments");
            source.Should().Contain("plan.ContourSegments");
            source.Should().Contain("plan.GridLineStroke");
            source.Should().Contain("plan.SpokeStroke");
            source.Should().Contain("tick.Segment.Stroke");
            source.Should().Contain("ChartType.Surface");
            source.Should().Contain("ChartType.Surface3D");
            source.Should().Contain("ToGeometry(ring.Path)");
            source.Should().Contain("primitive.Paths");
            source.Should().Contain("ToGeometry(path)");
            source.Should().NotContain("ComputeNiceScatterAxisRange(chart, useX: true)");
            source.Should().NotContain("ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot);");
            source.Should().NotContain("ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot);");
            source.Should().NotContain("ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot);");
            source.Should().NotContain("RenderScatterSeriesPrimitive");
            source.Should().NotContain("primitive.LineSegments");
            source.Should().NotContain("double maxBubble =");
            source.Should().NotContain("chart.ScatterStyle is");
            source.Should().NotContain("catCount = Math.Max(3");
            source.Should().NotContain("ringR =");
            source.Should().NotContain("Color.FromArgb(180, color.R");
            source.Should().NotContain("Color.FromArgb(80, color.R");
            source.Should().NotContain("primitive.IsFilled");
            source.Should().NotContain("markerBrush");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralChartSeriesStylePlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildColumnPrimitives(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("ChartRenderPlanner.BuildBarPrimitives(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("ChartRenderPlanner.BuildLineSeriesPrimitives(chart, plot, withMarkers, seriesColors, fillPlans)");
            source.Should().Contain("ChartRenderPlanner.BuildComboOverrideLineSeriesPrimitives(chart, plot, seriesColors, fillPlans)");
            source.Should().Contain("ToRect(primitive.Bounds)");
            source.Should().Contain("primitive.Fill");
            source.Should().Contain("primitive.Stroke");
            source.Should().Contain("primitive.LinePaths");
            source.Should().Contain("primitive.Markers");
            source.Should().Contain("DrawChartMarker(dc, marker)");
            source.Should().Contain("marker.Symbol");
            source.Should().NotContain("BuildColumnPrimitives(chart, plot)");
            source.Should().NotContain("BuildBarPrimitives(chart, plot)");
            source.Should().NotContain("BuildLineSeriesPrimitives(chart, plot, withMarkers)");
            source.Should().NotContain("BuildComboOverrideLineSeriesPrimitives(chart, plot)");
            source.Should().NotContain("dc.DrawEllipse(brush, null, point, 3, 3)");
            source.Should().NotContain("var pen = new Pen(brush, 1.5)");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralChartLegendPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildLegendItemPlans(chart, frame, chartOp.SeriesColors, chartOp.FillPlans)");
            source.Should().Contain("item.SwatchBounds");
            source.Should().Contain("item.Label.Bounds");
            source.Should().NotContain("legendAreaH");
            source.Should().NotContain("legendH");
            source.Should().NotContain("legendRight ? plotH / itemH");
            source.Should().NotContain("Point {ci + 1}");
            source.Should().NotContain("chart.Series[si].Name");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralSecondaryValueAxisPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame)");
            source.Should().Contain("CreateChartSecondaryAxisTickPen(secondaryAxisPlan)");
            source.Should().Contain("CreateChartSecondaryAxisTickPen(ChartSecondaryValueAxisPrimitivePlan plan)");
            source.Should().Contain("secondaryAxisPlan.Ticks");
            source.Should().Contain("secondaryAxisPlan.Labels");
            source.Should().Contain("secondaryAxisPlan.Title");
            source.Should().NotContain("BuildSecondaryValueAxisLabelPlans(");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralChartDataTablePlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame, chartOp.SeriesColors, chartOp.FillPlans)");
            source.Should().Contain("RenderChartDataTable(dc, dataTablePlan)");
            source.Should().Contain("ChartDataTablePrimitivePlan plan");
            source.Should().Contain("cell.CellBounds");
            source.Should().Contain("cell.LegendKeyBounds");
            source.Should().Contain("cell.LegendKeyFill");
            source.Should().Contain("plan.HorizontalBorders");
            source.Should().Contain("plan.VerticalBorders");
            source.Should().Contain("plan.OutlineBorders");
            source.Should().Contain("ToPen(plan.BorderStroke)");
            source.Should().Contain("Fill = stroke.Fill");
            source.Should().Contain("stroke.Dash");
            source.Should().Contain("cell.IsItalic");
            source.Should().Contain("cell.TextColor");
            source.Should().Contain("cell.FontFamily");
            source.Should().NotContain("chart.DataTable?.ShowLegendKeys");
            source.Should().NotContain("chart.DataTable?.TextStyle");
            source.Should().NotContain("chart.DataTable?.BorderOutline");
            source.Should().NotContain("ShapeOutline.GradientVisible");
            source.Should().NotContain("DataTableHeaderHeight + chart.Series.Count");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_RouteChartPatternFillPlansThroughPatternBrushes()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartFillPlan fill");
            source.Should().Contain("fill.Fill switch");
            source.Should().Contain("ResolvedFill.PatternFill pattern => MakePatternBrush(pattern)");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseOrderedDitherForPct40PatternFills()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        wpf.Should().Contain("\"pct40\" => BuildCheckerPatternBrush(bg, fg)");
        wpf.Should().Contain("private static DrawingBrush BuildCheckerPatternBrush");
        avalonia.Should().Contain("case \"pct40\":");
        avalonia.Should().Contain("if ((x + y) % 2 == 0) SetPixel(x, y, fg)");
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralChartAxisTitlePlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildAxisTitlePlans(chart, frame)");
            source.Should().Contain("DrawChartAxisTitle");
            source.Should().Contain("ChartAxisTitleOrientation.Horizontal");
            source.Should().NotContain("CategoryAxis.Title");
            source.Should().NotContain("ValueAxis.Title");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralMajorGridlineStrokePlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame)");
            source.Should().Contain("CreateChartGridLinePen(gridLinePlan)");
            source.Should().Contain("CreateChartGridLinePen(ChartMajorGridLinePrimitivePlan plan)");
            source.Should().Contain("ToPen(plan.Stroke)");
            source.Should().Contain("gridLinePlan.GridLines");
            source.Should().NotContain("BuildMajorGridLinePlans(chart, frame)");
            source.Should().NotContain("Color.FromRgb(0xD9, 0xD9, 0xD9)), 0.5");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralMajorAxisTickPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame)");
            source.Should().Contain("CreateChartAxisTickPen(tickPlan)");
            source.Should().Contain("CreateChartAxisTickPen(ChartMajorAxisTickPrimitivePlan plan)");
            source.Should().Contain("tickPlan.CategoryTicks");
            source.Should().Contain("tickPlan.ValueTicks");
            source.Should().Contain("ToPen(plan.Stroke)");
            source.Should().NotContain("AxisMajorTickLength");
            source.Should().NotContain("Color.FromRgb(0x7F, 0x7F, 0x7F)), 0.75");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralAxisLabelPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame)");
            source.Should().Contain("ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame)");
            source.Should().Contain("ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame)");
            source.Should().Contain("label.Text");
            source.Should().NotContain("NumberFormatCode");
            source.Should().NotContain("AxisLabelFormat");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideShowWindows_UseRendererNeutralPlaybackPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Host", "SlideShowWindow.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "SlideShowWindow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SlideShowPlaybackPlanner.PlanTransition");
            source.Should().Contain("SlideShowPlaybackPlanner.PlanAnimationStep");
            source.Should().Contain("SlideShowPlaybackPlanner.PlanFallbackAnimation");
            source.Should().NotContain("SlideShowTransitionPlanner.Plan(t)");
            source.Should().NotContain("switch (anim.Preset)");
        }
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
