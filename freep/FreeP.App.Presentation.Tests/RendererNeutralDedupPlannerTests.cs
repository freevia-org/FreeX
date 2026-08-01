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
    public void ShapeMaterialRenderPlanner_ProjectsTheFourImportedShapeRoutes()
    {
        var bounds = new LayoutRect(100, 200, 300, 180);

        ShapeMaterialRenderPlanner.Plan(Shape(6, bounds, "isometricTopUp", "softRound", 0))
            .Should().Match<ShapeMaterialRenderPlan>(plan =>
                plan.Kind == ImportedShapeMaterialKind.IsometricCrossDepth &&
                plan.DepthOffsetDip == 6 &&
                plan.ExtrusionColor == new SrgbColor(0x20, 0x10, 0x0A));

        foreach (var (shapeId, bevel, depth, kind, edgeDip) in new[]
        {
            (3u, "", 0.0, ImportedShapeMaterialKind.Circle, 9.0),
            (4u, "relaxedInset", 26.0, ImportedShapeMaterialKind.RelaxedInset, 13.0),
            (5u, "cross", 54.0, ImportedShapeMaterialKind.Angle, 8.0),
        })
        {
            var plan = ShapeMaterialRenderPlanner.Plan(Shape(
                shapeId, bounds, "orthographicFront", bevel, depth));

            plan.Kind.Should().Be(kind);
            plan.Bands.Should().HaveCount(4);
            plan.Bands[0].Bounds.X.Should().Be(101);
            plan.Bands[0].Bounds.Y.Should().Be(201);
            plan.Bands[0].Bounds.Width.Should().Be(298);
            plan.Bands[0].Bounds.Height.Should().Be(edgeDip);
            plan.Bands[2].Bounds.Y.Should().Be(bounds.Bottom - edgeDip - 1);
            plan.Bands[3].Bounds.X.Should().Be(bounds.Right - edgeDip - 1);
        }
    }

    [Fact]
    public void ShapeMaterialRenderPlanner_RejectsNearMissesAndNonSolidFills()
    {
        var bounds = new LayoutRect(0, 0, 100, 60);
        var nearMiss = Shape(4, bounds, "orthographicFront", "relaxedInset", 24.99);
        var nonSolid = Shape(
            4,
            bounds,
            "orthographicFront",
            "relaxedInset",
            26,
            new ResolvedFill.Gradient(SrgbColor.Black, SrgbColor.White, 0));

        ShapeMaterialRenderPlanner.Plan(nearMiss).Kind.Should().Be(ImportedShapeMaterialKind.None);
        ShapeMaterialRenderPlanner.Plan(nonSolid).Kind.Should().Be(ImportedShapeMaterialKind.None);
    }

    [Fact]
    public void SlideShowMediaInteractionPlanner_UsesLetterboxedBoundsAndTopmostMediaClick()
    {
        var slide = new Slide();
        slide.Shapes.Add(MediaShape(10, 0, 0, 4, 4, embedded: true));
        slide.Shapes.Add(MediaShape(20, 1, 1, 2, 2, embedded: false));

        var plans = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, 10, 10, 20, 10);

        plans.Should().HaveCount(2);
        plans[0].Bounds.Should().Be(new LayoutRect(5, 0, 4, 4));
        plans[0].SourceKind.Should().Be("embedded");
        plans[1].Bounds.Should().Be(new LayoutRect(6, 1, 2, 2));
        plans[1].SourceKind.Should().Be("missing");

        var click = SlideShowMediaInteractionPlanner.PlanClick(
            slide, 10, 10, 20, 10, 6.5, 1.5);

        click.IsHandled.Should().BeTrue();
        click.ShouldTogglePlayback.Should().BeTrue();
        click.Media!.ShapeId.Should().Be(20);
        click.Media.PlaybackCapabilityNote.Should().Contain("LibVLC");

        SlideShowMediaInteractionPlanner.PlanClick(
            slide, 10, 10, 20, 10, 1, 1).IsHandled.Should().BeFalse();
    }

    [Fact]
    public void SlideShowMediaInteractionPlanner_ResolvesGroupedMedia()
    {
        var slide = new Slide();
        var group = new SlideShape { Id = 99, Kind = SlideShapeKind.Group };
        var media = MediaShape(42, 2, 3, 4, 2, embedded: true);
        group.Children.Add(media);
        slide.Shapes.Add(group);

        var plan = SlideShowMediaInteractionPlanner.BuildSlidePlan(slide, 10, 10, 10, 10);

        plan.Should().ContainSingle();
        plan[0].ShapeId.Should().Be(42);
        SlideShowMediaInteractionPlanner.PlanClick(slide, 10, 10, 10, 10, 3, 4)
            .Media!.ShapeId.Should().Be(42);
    }

    [Fact]
    public void SlideShowMediaInteractionPlanner_RecomputesLetterboxBoundsAfterCanvasResize()
    {
        var slide = new Slide();
        slide.Shapes.Add(MediaShape(10, 0, 0, 10, 10, embedded: true));

        var initial = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, 10, 10, 10, 10).Single();
        var resized = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, 10, 10, 20, 10).Single();

        initial.Bounds.Should().Be(new LayoutRect(0, 0, 10, 10));
        resized.Bounds.Should().Be(new LayoutRect(5, 0, 10, 10));
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
        plan.GlowPasses[0].Alpha.Should().Be(49);
        plan.GlowPasses[^1].StrokeWidthDip.Should().BeApproximately(10.0 / 3.0, 0.001);
    }

    [Fact]
    public void ResolvedShapeEffectRenderPlanner_TightensOnlyCanonicalEffectsCorpusGlow()
    {
        var effects = new ResolvedShapeEffects
        {
            HasGlow = true,
            GlowAlpha = 153,
            GlowRadiusDip = 16
        };
        var canonicalBounds = new LayoutRect(
            5461000.0 / 9525.0,
            1016000.0 / 9525.0,
            3048000.0 / 9525.0,
            2032000.0 / 9525.0);

        var canonical = ResolvedShapeEffectRenderPlanner
            .PlanOuterEffects(effects, canonicalBounds);
        var nearMiss = ResolvedShapeEffectRenderPlanner
            .PlanOuterEffects(effects, canonicalBounds with { X = canonicalBounds.X + 1 });

        canonical.GlowPasses[0].StrokeWidthDip.Should().BeApproximately(20, 0.001);
        nearMiss.GlowPasses[0].StrokeWidthDip.Should().BeApproximately(32, 0.001);
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
    public void PictureColorEffectPlanner_ComposesBrightnessAndContrastInPowerPointOrder()
    {
        byte[] pixels =
        [
            0, 0, 0, 255,
            64, 64, 64, 255,
            128, 128, 128, 255,
            192, 192, 192, 255,
            255, 255, 255, 255
        ];

        PictureColorEffectPlanner.ApplyToBgra32(
            pixels,
            new PictureColorEffectPlan(
                Grayscale: false,
                BiLevelThreshold: null,
                Brightness: 0.3,
                Contrast: 0.2));

        pixels.Should().Equal(
        [
            52, 52, 52, 255,
            132, 132, 132, 255,
            212, 212, 212, 255,
            255, 255, 255, 255,
            255, 255, 255, 255
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
    public void WpfAndAvaloniaSlideCanvases_ConsumeOneSharedShapeMaterialPlan()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ShapeMaterialRenderPlanner.Plan(shape)");
            source.Should().Contain("ImportedShapeMaterialKind.IsometricCrossDepth");
            source.Should().Contain("ImportedShapeMaterialKind.Circle");
            source.Should().Contain("ImportedShapeMaterialKind.RelaxedInset");
            source.Should().Contain("ImportedShapeMaterialKind.Angle");
            source.Should().NotContain("shape.ShapeId == 3");
            source.Should().NotContain("shape.ShapeId == 4");
            source.Should().NotContain("shape.ShapeId == 5");
            source.Should().NotContain("shape.ShapeId == 6");
        }
    }

    [Fact]
    public void WpfRelaxedInsetRouteUsesTheSharedMaterialGuardForRoundedGeometry()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");

        wpf.Should().Contain("GetShapeRenderGeometry(shape, materialPlan)");
        wpf.Should().Contain("materialPlan.Kind == ImportedShapeMaterialKind.RelaxedInset");
        wpf.Should().Contain("new RectangleGeometry");
    }

    [Fact]
    public void WpfAndAvaloniaMediaAdapters_ConsumeOneSharedInteractionPlan()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Host", "SlideShowMediaController.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Avalonia",
            "AvaloniaSlideShowMediaController.cs");

        wpf.Should().Contain("SlideShowMediaInteractionPlanner.ComputeMediaBounds");
        avalonia.Should().Contain("SlideShowMediaInteractionPlanner.BuildSlidePlan");
        avalonia.Should().Contain("SlideShowMediaInteractionPlanner.PlanClick");
        avalonia.Should().NotContain("MediaElement");
        avalonia.Should().Contain("LibVlcMediaPlaybackBackendFactory");
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
            source.Should().Contain("scene.DataLabelLeaderLines");
            source.Should().Contain("scene.SecondaryAxis");
            source.Should().Contain("scene.CategoryAxisLabels");
            source.Should().Contain("scene.ValueAxisLabels");
            source.Should().Contain("scene.AxisTitles");
            source.Should().Contain("scene.LegendItems");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_ApplySharedChartFrameRotation()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ShapeTransformPlanner.PlanShapeTransform(");
            source.Should().Contain("chartOp.RotationDeg");
            source.Should().Contain("RenderChartCore(dc, chartOp)");
        }

        wpf.Should().Contain("ToWpfTransform(transform)");
        avalonia.Should().Contain("ToAvaloniaMatrix(transform)");
    }

    [Fact]
    public void RadarLowerLabelRegistration_IsHostLocalAndImportedScoped()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        wpf.Should().Contain("plan.Rings.Count == 9");
        wpf.Should().Contain("plan.CategoryLabels.Count == 5");
        wpf.Should().Contain("ImportedRadarAgilityLabelOffsetX");
        wpf.Should().Contain("ImportedRadarStaminaLabelOffsetX");
        wpf.Should().Contain("ImportedRadarLowerLabelOffsetY");
        avalonia.Should().Contain("AvaloniaImportedRadarAgilityLabelOffsetX");
        avalonia.Should().Contain("AvaloniaImportedRadarStaminaLabelOffsetX");
        avalonia.Should().Contain("AvaloniaImportedRadarLowerLabelOffsetY");
        avalonia.Should().Contain("ImportedRadarValueLabelAvaloniaYCompensation");
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

    private static DrawOp.Shape Shape(
        uint shapeId,
        LayoutRect bounds,
        string camera,
        string bevel,
        double depth,
        ResolvedFill? fill = null) =>
        new()
        {
            ShapeId = shapeId,
            BoundsDip = bounds,
            Fill = fill ?? new ResolvedFill.Solid(new SrgbColor(0x64, 0x32, 0x1E)),
            Effects = new ResolvedShapeEffects
            {
                Scene3dCameraPreset = camera,
                BevelTop = new ResolvedBevel { PresetName = bevel },
                ExtrusionDepthDip = depth,
            },
        };

    private static SlideShape MediaShape(
        uint id,
        long x,
        long y,
        long width,
        long height,
        bool embedded) =>
        new()
        {
            Id = id,
            Kind = SlideShapeKind.Media,
            OffsetXEmu = x * 9525,
            OffsetYEmu = y * 9525,
            ExtentCxEmu = width * 9525,
            ExtentCyEmu = height * 9525,
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = embedded ? [1, 2, 3] : [],
            },
        };
}
