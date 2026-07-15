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
    public void ResolvedShapeEffectRenderPlanner_ExpandsShadowGlowAndSoftEdgePasses()
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
            GlowRadiusDip = 5,
            HasSoftEdge = true,
            SoftEdgeRadiusDip = 6
        };

        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(effects);

        plan.ShadowPasses.Should().HaveCount(17);
        plan.ShadowPasses[0].Should().Be(new ShapeShadowPass(6, -4, new SrgbColor(1, 2, 3), 33));
        plan.ShadowPasses[^1].Should().Be(new ShapeShadowPass(10, 0, new SrgbColor(1, 2, 3), 100));
        plan.GlowPasses.Should().HaveCount(3);
        plan.SoftEdgePasses.Should().HaveCount(3);
        plan.SoftEdgePasses[0].StrokeWidthDip.Should().BeApproximately(12, 0.001);
        plan.SoftEdgePasses[^1].StrokeWidthDip.Should().BeApproximately(4, 0.001);
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
    public void WpfAndAvaloniaSlideCanvases_ConsumeOneSharedChartScenePlan()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildScenePlan(");
            source.Should().Contain("scene.GeometryKind");
            source.Should().Contain("scene.Frame.Plot");
            source.Should().Contain("scene.Rectangles");
            source.Should().Contain("scene.LineSeries");
            source.Should().Contain("scene.ComboLineSeries");
            source.Should().Contain("scene.Surface");
            source.Should().Contain("scene.Scatter");
            source.Should().Contain("scene.Bubble");
            source.Should().Contain("scene.Radar");
            source.Should().Contain("scene.Stock");
            source.Should().Contain("scene.AreaSeries");
            source.Should().Contain("scene.PieSlices");
            source.Should().Contain("scene.DoughnutSlices");
            source.Should().Contain("scene.AxisTicks");
            source.Should().Contain("scene.DataLabels");
            source.Should().Contain("scene.SecondaryAxis");
            source.Should().Contain("scene.CategoryAxisLabels");
            source.Should().Contain("scene.ValueAxisLabels");
            source.Should().Contain("scene.AxisTitles");
            source.Should().Contain("scene.LegendItems");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_KeepChartMathOutOfPlatformSources()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().NotContain("ChartRenderPlanner.BuildFramePlan(");
            source.Should().NotContain("BuildColumnPrimitives(");
            source.Should().NotContain("BuildBarPrimitives(");
            source.Should().NotContain("BuildLineSeriesPrimitives(");
            source.Should().NotContain("BuildComboOverrideLineSeriesPrimitives(");
            source.Should().NotContain("BuildAreaSeriesPrimitives(");
            source.Should().NotContain("BuildSurfaceGeometryPlan(");
            source.Should().NotContain("BuildStockPrimitivePlan(");
            source.Should().NotContain("BuildStockVolumePrimitives(");
            source.Should().NotContain("BuildPieSlicePrimitives(");
            source.Should().NotContain("BuildDoughnutSlicePrimitives(");
            source.Should().NotContain("BuildScatterPrimitivePlan(");
            source.Should().NotContain("BuildBubblePrimitivePlan(");
            source.Should().NotContain("BuildRadarPrimitivePlan(");
            source.Should().NotContain("ComputePrimaryValueAxisRange(");
            source.Should().NotContain("ComputeSecondaryValueAxisRange(");
            source.Should().NotContain("ComputeScatterAxisRange(");
            source.Should().NotContain("FormatAxisValue(");
            source.Should().NotContain("new ChartPlanRect(plot");
            source.Should().NotContain("chart.ChartType");
            source.Should().NotContain("chart.Series.Any");
            source.Should().NotContain("chart.Categories.Count");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_KeepNativePaintingAndTextMeasurementBoundaries()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DrawChartLabel");
            source.Should().Contain("DrawChartMarker");
            source.Should().Contain("ToPieSliceGeometry");
            source.Should().Contain("ToGeometry(path)");
            source.Should().NotContain("ChartRenderPlanner.ThreeDPieDepthFillAlpha");
            source.Should().NotContain("ChartRenderPlanner.ResolveSeriesColor");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralPatternPaintPlans()
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
