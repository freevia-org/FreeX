using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DrawingObjectVisualPlannerTests
{
    [Fact]
    public void ShapePlan_RecordsGeometryFillOutlineTextEffectsAndTransform()
    {
        var shape = Shape.TextBoxWith("Planner text", widthPt: 150, heightPt: 72, fillColorHex: "#E2F0D9");
        shape.ExtendedFill = ShapeFill.LinearGradient(
            5400000,
            new GradientStop(0, "#4472C4"),
            new GradientStop(100000, "#FFFFFF"));
        shape.OutlineColorHex = "#548235";
        shape.OutlineWidthPt = 1.5;
        shape.OutlineDash = "dash";
        shape.RotationAngle = 15;
        shape.FlipH = true;
        shape.Effects = new ShapeEffectLst
        {
            HasShadow = true,
            ShadowAlpha = 35000,
            HasGlow = true,
            GlowColorHex = "ED7D31"
        };

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(
            shape,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape,
                BlockIndex: 1,
                RunIndex: 2,
                new DocumentFloatRect(10, 20, 200, 96),
                BehindText: false,
                ZOrderIndex: 7,
                ImageWrapping.Square,
                RotationAngle: shape.RotationAngle,
                FlipH: shape.FlipH,
                FlipV: shape.FlipV));

        plan.Kind.Should().Be(DrawingObjectVisualKind.Shape);
        plan.GeometryKind.Should().Be(DrawingObjectGeometryKind.TextBox);
        plan.Fill.Kind.Should().Be(DrawingObjectFillKind.Gradient);
        plan.Fill.GradientStops.Should().HaveCount(2);
        plan.Outline.IsVisible.Should().BeTrue();
        plan.Outline.ColorHex.Should().Be("#548235");
        plan.Outline.WidthDip.Should().BeApproximately(2.0, 0.01);
        plan.Outline.DashStyle.Should().Be("dash");
        plan.Text.Should().Be(new DrawingObjectTextPlan("Planner text", ShapeTextDirection.Horizontal));
        plan.Effects.HasShadow.Should().BeTrue();
        plan.Effects.HasGlow.Should().BeTrue();
        plan.Effects.Summary.Should().Contain("shadow");
        plan.Effects.Summary.Should().Contain("glow");
        plan.RotationAngle.Should().Be(15);
        plan.FlipH.Should().BeTrue();
        plan.Wrapping.Should().Be(ImageWrapping.Square);
        plan.ZOrderIndex.Should().Be(7);
    }

    [Fact]
    public void ShapePlan_NormalizesEffectIntentForThinHostRenderers()
    {
        var shape = new Shape(ShapeKind.Ellipse, widthPt: 90, heightPt: 45, fillColorHex: "#00AA11")
        {
            Effects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowBlurRad = 25400,
                ShadowDist = 12700,
                ShadowDir = 5400000,
                ShadowColorHex = "112233",
                ShadowAlpha = 50000,
                HasGlow = true,
                GlowRad = 63500,
                GlowColorHex = "#00FFFF",
                GlowAlpha = 25000,
                HasSoftEdge = true,
                HasReflection = true,
                HasBevel = true
            }
        };

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(
            shape,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape,
                BlockIndex: 0,
                RunIndex: 0,
                new DocumentFloatRect(0, 0, 120, 60),
                BehindText: false,
                ZOrderIndex: 1,
                ImageWrapping.InFront));

        plan.Effects.ShadowColorHex.Should().Be("#112233");
        plan.Effects.ShadowBlurDip.Should().BeApproximately(2.67, 0.01);
        plan.Effects.ShadowDistanceDip.Should().BeApproximately(1.33, 0.01);
        plan.Effects.ShadowDirectionDegrees.Should().Be(90);
        plan.Effects.ShadowOpacity.Should().Be(0.5);
        plan.Effects.GlowColorHex.Should().Be("#00FFFF");
        plan.Effects.GlowRadiusDip.Should().BeApproximately(6.67, 0.01);
        plan.Effects.GlowOpacity.Should().Be(0.25);
        plan.Effects.HasSoftEdge.Should().BeTrue();
        plan.Effects.HasReflection.Should().BeTrue();
        plan.Effects.HasBevel.Should().BeTrue();
        plan.Effects.Summary.Should().Be("shadow, glow, soft-edge, reflection, bevel");
    }

    [Fact]
    public void WordArtPlan_RecordsTextStyleWarpAndPlacementMetadata()
    {
        var wordArt = new WordArt("Shared WordArt", WordArtStyle.GlowBlue, fontSizePt: 30)
        {
            Warp = WordArtWarp.Wave1
        };

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(
            wordArt,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.WordArt,
                BlockIndex: 0,
                RunIndex: 1,
                new DocumentFloatRect(40, 60, 240, 64),
                BehindText: false,
                ZOrderIndex: 9,
                ImageWrapping.InFront));

        plan.Kind.Should().Be(DrawingObjectVisualKind.WordArt);
        plan.WordArt.Should().NotBeNull();
        plan.WordArt!.Text.Should().Be("Shared WordArt");
        plan.WordArt.Style.Should().Be(WordArtStyle.GlowBlue);
        plan.WordArt.Warp.Should().Be(WordArtWarp.Wave1);
        plan.WordArt.FontSizeDip.Should().BeApproximately(40, 0.01);
        plan.WordArt.Fill.Kind.Should().Be(DrawingObjectFillKind.Solid);
        plan.WordArt.FillColorHex.Should().Be("#242424");
        plan.WordArt.WarpHint.Should().Be("wave");
        plan.Effects.HasGlow.Should().BeTrue();
        plan.Effects.GlowColorHex.Should().Be("#2E75B6");
        plan.Effects.Summary.Should().Be("glow");
        plan.Wrapping.Should().Be(ImageWrapping.InFront);
        plan.ZOrderIndex.Should().Be(9);
    }

    [Theory]
    [InlineData(WordArtStyle.GradientFill, DrawingObjectFillKind.Gradient, "none", "none")]
    [InlineData(WordArtStyle.Outline, DrawingObjectFillKind.Solid, "#2E2E2E", "none")]
    [InlineData(WordArtStyle.Shadow, DrawingObjectFillKind.Solid, "none", "shadow")]
    [InlineData(WordArtStyle.GlowGold, DrawingObjectFillKind.Solid, "none", "glow")]
    [InlineData(WordArtStyle.Reflection, DrawingObjectFillKind.Solid, "none", "reflection")]
    [InlineData(WordArtStyle.Bevel, DrawingObjectFillKind.Solid, "none", "bevel")]
    [InlineData(WordArtStyle.PatternFill, DrawingObjectFillKind.Pattern, "#1F4E79", "none")]
    public void WordArtPlan_ResolvesPresetStyleFactsInSharedPresentation(
        WordArtStyle style,
        DrawingObjectFillKind expectedFill,
        string expectedOutline,
        string expectedEffects)
    {
        var plan = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Preset", style, fontSizePt: 24)
            {
                Warp = WordArtWarp.SlantDown
            });

        plan.WordArt.Fill.Kind.Should().Be(expectedFill);
        (plan.WordArt.OutlineColorHex ?? "none").Should().Be(expectedOutline);
        plan.WordArt.Bold.Should().BeFalse();
        plan.WordArt.WarpHint.Should().Be("slant");
        plan.Effects.Summary.Should().Be(expectedEffects);
        plan.Summary.Should().Contain("style:" + style);
    }

    [Fact]
    public void WordArtPlan_ExposesGradientStopsAndPatternFillWithoutRendererMapping()
    {
        var gradient = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Gradient", WordArtStyle.GradFillMulti, fontSizePt: 24));
        var pattern = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Pattern", WordArtStyle.PatternFill, fontSizePt: 24));

        gradient.WordArt.Fill.Kind.Should().Be(DrawingObjectFillKind.Gradient);
        gradient.WordArt.Fill.GradientAngle.Should().Be(5400000);
        gradient.WordArt.Fill.GradientStops.Select(stop => stop.ColorHex)
            .Should().Equal("#FF6000", "#C00000", "#7030A0");
        pattern.WordArt.HasPatternFill.Should().BeTrue();
        pattern.WordArt.Fill.PatternPreset.Should().Be("diagCross");
        pattern.WordArt.Fill.PatternForegroundColorHex.Should().Be("#1F4E79");
        pattern.WordArt.Fill.PatternBackgroundColorHex.Should().Be("#FFFFFF");
    }

    [Fact]
    public void InlineWordArtPlan_RecordsPresetEffectsWithoutFloatingPlacement()
    {
        var wordArt = new WordArt("Inline Glow", WordArtStyle.GlowGold, fontSizePt: 24)
        {
            Warp = WordArtWarp.ArchUp
        };

        var plan = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(wordArt);

        plan.WordArt.Text.Should().Be("Inline Glow");
        plan.WordArt.Style.Should().Be(WordArtStyle.GlowGold);
        plan.WordArt.Warp.Should().Be(WordArtWarp.ArchUp);
        plan.WordArt.FontSizeDip.Should().BeApproximately(32, 0.01);
        plan.WordArt.Fill.Kind.Should().Be(DrawingObjectFillKind.Solid);
        plan.WordArt.FillColorHex.Should().Be("#242424");
        plan.Effects.HasGlow.Should().BeTrue();
        plan.Effects.GlowColorHex.Should().Be("#C09000");
        plan.Effects.Summary.Should().Be("glow");
    }

    [Fact]
    public void WordArtPlacementPlan_UsesSharedNormalizedArchUpCurveAndTangents()
    {
        var plan = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.ArchUp,
            [30, 30, 30],
            boundsWidthDip: 200,
            boundsHeightDip: 100);

        plan.Glyphs.Should().HaveCount(3);
        plan.Glyphs[0].CenterXNormalized.Should().BeApproximately(0.35, 0.001);
        plan.Glyphs[1].CenterXNormalized.Should().BeApproximately(0.5, 0.001);
        plan.Glyphs[2].CenterXNormalized.Should().BeApproximately(0.65, 0.001);
        plan.Glyphs[1].CenterYNormalized.Should().BeLessThan(plan.Glyphs[0].CenterYNormalized);
        plan.Glyphs[0].RotationRadians.Should().BeLessThan(0);
        plan.Glyphs[1].RotationRadians.Should().BeApproximately(0, 0.001);
        plan.Glyphs[2].RotationRadians.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WordArtPlacementPlan_UsesSharedNormalizedWave1CurveAndTangents()
    {
        var plan = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.Wave1,
            [20, 20, 20, 20],
            boundsWidthDip: 200,
            boundsHeightDip: 100);

        plan.Glyphs.Should().HaveCount(4);
        plan.Glyphs[0].CenterYNormalized.Should().BeGreaterThan(0.5);
        plan.Glyphs[1].CenterYNormalized.Should().BeGreaterThan(0.5);
        plan.Glyphs[2].CenterYNormalized.Should().BeLessThan(0.5);
        plan.Glyphs[3].CenterYNormalized.Should().BeLessThan(0.5);
        plan.Glyphs[0].RotationRadians.Should().BeGreaterThan(0);
        plan.Glyphs[1].RotationRadians.Should().BeLessThan(0);
        plan.Glyphs[2].RotationRadians.Should().BeLessThan(0);
        plan.Glyphs[3].RotationRadians.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WordArtPlacementPlan_LeavesUnsupportedWarpAndInvalidBoundsEmpty()
    {
        DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(WordArtWarp.Circle, [20, 20], 200, 100)
            .Glyphs.Should().BeEmpty();
        DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(WordArtWarp.ArchUp, [20, 20], 0, 100)
            .Glyphs.Should().BeEmpty();
    }

    [Fact]
    public void WordArtPlacementPlan_ClampsNarrowArchUpSpanDenominator()
    {
        var plan = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.ArchUp,
            [0.1, 0.1],
            boundsWidthDip: 200,
            boundsHeightDip: 100);

        plan.Glyphs.Should().HaveCount(2);
        plan.Glyphs.Should().OnlyContain(glyph =>
            double.IsFinite(glyph.CenterXNormalized)
            && double.IsFinite(glyph.CenterYNormalized)
            && double.IsFinite(glyph.RotationRadians));
        plan.Glyphs[0].RotationRadians.Should().BeLessThan(0);
        plan.Glyphs[1].RotationRadians.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WordArtPlacementPlan_ClampsNarrowWave1SpanDenominator()
    {
        var plan = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.Wave1,
            [0.01, 0.01],
            boundsWidthDip: 200,
            boundsHeightDip: 100);

        plan.Glyphs.Should().HaveCount(2);
        plan.Glyphs.Should().OnlyContain(glyph =>
            double.IsFinite(glyph.CenterXNormalized)
            && double.IsFinite(glyph.CenterYNormalized)
            && double.IsFinite(glyph.RotationRadians)
            && Math.Abs(glyph.RotationRadians) < 1.5);
    }

    [Fact]
    public void GroupPlan_RecordsMixedChildrenWithLocalOffsetsAndTypedPlans()
    {
        var group = new DrawingGroup
        {
            WidthPt = 240,
            HeightPt = 140,
            RotationAngle = 18,
            FlipH = true
        };
        var image = new InlineImage([1, 2, 3, 4], widthPt: 24, heightPt: 18)
        {
            CropLeft = 0.1,
            RotationAngle = 12,
            FlipH = true
        };
        group.Children.Add(image);
        group.ChildOffsets.Add((3, 4));
        group.Children.Add(new Shape(ShapeKind.Ellipse, widthPt: 72, heightPt: 36, fillColorHex: "#CFE2F3")
        {
            Effects = new ShapeEffectLst
            {
                HasGlow = true,
                GlowColorHex = "70AD47",
                GlowRad = 63500
            }
        });
        group.ChildOffsets.Add((9, 6));
        var chart = Chart.Create(
            ChartKind.Line,
            ["A", "B"],
            [1.0, 2.0],
            seriesName: "Series",
            title: "Grouped chart");
        chart.WidthPt = 90;
        chart.HeightPt = 54;
        chart.StyleId = 4;
        chart.ColorSchemeId = "colorful2";
        chart.QuickLayoutId = 5;
        chart.ShowLegend = true;
        group.Children.Add(chart);
        group.ChildOffsets.Add((84, 0));
        group.Children.Add(new WordArt("Group", WordArtStyle.GlowGold, fontSizePt: 20));
        group.ChildOffsets.Add((72, 12));
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Plan", "Ship", "Review", "Launch"]);
        smartArt.WidthPt = 120;
        smartArt.HeightPt = 44;
        smartArt.LayoutId = "matrix1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "moderate1";
        group.Children.Add(smartArt);
        group.ChildOffsets.Add((24, 84));

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(
            group,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Group,
                BlockIndex: 2,
                RunIndex: 3,
                new DocumentFloatRect(100, 200, 240, 120),
                BehindText: false,
                ZOrderIndex: 11,
                ImageWrapping.Square));

        plan.Kind.Should().Be(DrawingObjectVisualKind.Group);
        plan.RotationAngle.Should().Be(18);
        plan.FlipH.Should().BeTrue();
        plan.FlipV.Should().BeFalse();
        plan.GroupChildren.Should().HaveCount(5);
        plan.GroupChildren.Select(child => child.Visual.Kind).Should().Equal(
            DrawingObjectVisualKind.Image,
            DrawingObjectVisualKind.Shape,
            DrawingObjectVisualKind.Chart,
            DrawingObjectVisualKind.WordArt,
            DrawingObjectVisualKind.SmartArt);
        plan.GroupChildren[0].OffsetXDip.Should().BeApproximately(4, 0.01);
        plan.GroupChildren[0].OffsetYDip.Should().BeApproximately(5.33, 0.01);
        var imagePlan = plan.GroupChildren[0].Visual.Image;
        imagePlan.Should().NotBeNull();
        imagePlan!.ByteLength.Should().Be(4);
        imagePlan.HasCrop.Should().BeTrue();
        plan.GroupChildren[0].Visual.RotationAngle.Should().Be(12);
        plan.GroupChildren[0].Visual.FlipH.Should().BeTrue();
        plan.GroupChildren[1].OffsetXDip.Should().BeApproximately(12, 0.01);
        plan.GroupChildren[1].OffsetYDip.Should().BeApproximately(8, 0.01);
        plan.GroupChildren[1].Visual.GeometryKind.Should().Be(DrawingObjectGeometryKind.Ellipse);
        plan.GroupChildren[1].Visual.Rect.XDip.Should().BeApproximately(112, 0.01);
        plan.GroupChildren[1].Visual.Effects.HasGlow.Should().BeTrue();
        plan.GroupChildren[1].Visual.Effects.GlowColorHex.Should().Be("#70AD47");
        var chartPlan = plan.GroupChildren[2].Visual.Chart;
        chartPlan.Should().NotBeNull();
        chartPlan!.Kind.Should().Be(ChartKind.Line);
        chartPlan.StyleId.Should().Be(4);
        chartPlan.ColorSchemeId.Should().Be("colorful2");
        plan.GroupChildren[3].OffsetXDip.Should().BeApproximately(96, 0.01);
        plan.GroupChildren[3].Visual.WordArt!.Text.Should().Be("Group");
        plan.GroupChildren[3].Visual.Effects.HasGlow.Should().BeTrue();
        plan.GroupChildren[3].Visual.Effects.GlowColorHex.Should().Be("#C09000");
        var smartArtPlan = plan.GroupChildren[4].Visual.SmartArt;
        smartArtPlan.Should().NotBeNull();
        smartArtPlan!.Kind.Should().Be(SmartArtKind.List);
        smartArtPlan.LayoutId.Should().Be("matrix1");
        smartArtPlan.Nodes.Should().HaveCount(4);
        smartArtPlan.LayoutGeometry.Should().NotBeNull();
        smartArtPlan.LayoutGeometry!.Kind.Should().Be(SmartArtLayoutGeometryKind.Matrix);
        smartArtPlan.LayoutGeometry.Nodes.Should().HaveCount(4);
    }
}
