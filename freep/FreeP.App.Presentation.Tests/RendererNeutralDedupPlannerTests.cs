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
    public void ShapeEffectRenderPlanner_ExpandsShadowAndGlowPasses()
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

        var plan = ShapeEffectRenderPlanner.PlanOuterEffects(effects);

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
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralShapeAndWarpPlanners()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ShapeTransformPlanner.PlanShapeTransform");
            source.Should().Contain("ShapeEffectRenderPlanner.PlanOuterEffects");
            source.Should().Contain("WordArtWarpPlanner.ComputeYOffset");
            source.Should().NotContain("BuildWarpYFunc");
            source.Should().NotContain("OuterShadowDirDeg * Math.PI");
            source.Should().NotContain("OuterShadowBlurDip / 2");
            source.Should().NotContain("GlowRadiusDip / 2");
        }

        wpf.Should().NotContain("BuildShapeTransform");
        avalonia.Should().NotContain("BuildShapeMatrix");
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralPictureColorEffectPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PictureColorEffectPlanner.Plan");
            source.Should().Contain("PictureColorEffectPlanner.ApplyToBgra32");
            source.Should().NotContain("0.2126 * r + 0.7152 * g + 0.0722 * b");
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
            source.Should().NotContain("chart.DoughnutHolePercent, 0, 90");
            source.Should().NotContain("ringGap =");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralAreaScatterBubbleAndRadarPlanning()
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
            source.Should().Contain("ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot, seriesColors)");
            source.Should().Contain("primitive.LineSegments");
            source.Should().Contain("primitive.Markers");
            source.Should().Contain("plan.DataLabels");
            source.Should().Contain("ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot, seriesColors)");
            source.Should().Contain("ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot, seriesColors)");
            source.Should().Contain("plan.GridLineStroke");
            source.Should().Contain("plan.SpokeStroke");
            source.Should().Contain("ToGeometry(ring.Path)");
            source.Should().Contain("ToGeometry(primitive.Path)");
            source.Should().NotContain("ComputeNiceScatterAxisRange(chart, useX: true)");
            source.Should().NotContain("ChartRenderPlanner.BuildScatterPrimitivePlan(chart, plot);");
            source.Should().NotContain("ChartRenderPlanner.BuildBubblePrimitivePlan(chart, plot);");
            source.Should().NotContain("ChartRenderPlanner.BuildRadarPrimitivePlan(chart, plot);");
            source.Should().NotContain("RenderScatterSeriesPrimitive");
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
    public void WpfAndAvaloniaSlideCanvases_UseRendererNeutralChartLegendPlanning()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderPlanner.BuildLegendItemPlans(chart, frame, chartOp.SeriesColors)");
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
