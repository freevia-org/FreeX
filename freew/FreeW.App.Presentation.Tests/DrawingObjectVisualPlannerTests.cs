using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DrawingObjectVisualPlannerTests
{
    [Fact]
    public void TextLayout_BaselinePositionMovesGlyphAndCaretWithoutChangingAdvance()
    {
        var formatting = new RunFormatting { PositionPt = 3 };
        var plan = new DrawingObjectTextPlan("Raised", ShapeTextDirection.Horizontal)
        {
            Paragraphs =
            [
                new DrawingObjectTextParagraphPlan(
                    TextAlignment.Left,
                    [new DrawingObjectTextRunPlan("Raised", formatting, 0, 0)])
            ]
        };

        var layout = DrawingObjectTextLayoutPlanner.LayoutPlan(
            plan,
            200,
            80,
            (text, _) => text.Length * 10,
            _ => 20);

        layout.Glyphs[0].Y.Should().Be(DrawingObjectTextLayoutPlanner.TextInsetDip - 4);
        layout.CaretStops[0].Y.Should().Be(DrawingObjectTextLayoutPlanner.TextInsetDip - 4);
        layout.Glyphs[1].X.Should().Be(layout.Glyphs[0].X + 10);
        layout.Glyphs[0].Height.Should().Be(20);
    }

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
    public void RichShapeTextLayout_PreservesRunsParagraphBreaksAndCaretStops()
    {
        var shape = new Shape(ShapeKind.TextBox, 120, 80);
        var first = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        first.Runs.Add(new Run("Bold", RunFormatting.Default with
        {
            FontFamily = "Arial",
            FontSizePt = 14,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            ColorHex = "#C00000"
        }));
        first.Runs.Add(new Run(" plain", RunFormatting.Default));
        var second = new Paragraph();
        second.Runs.Add(new Run("next", RunFormatting.Default with { FontSizePt = 10 }));
        shape.TextParagraphs.Add(first);
        shape.TextParagraphs.Add(second);

        var textPlan = DrawingObjectTextLayoutPlanner.BuildTextPlan(shape);
        var layout = DrawingObjectTextLayoutPlanner.LayoutPlan(
            textPlan,
            120,
            80,
            (text, formatting) => (formatting.FontSizePt ?? 9) * (formatting.Bold ? 2 : 1),
            formatting => formatting.FontSizePt ?? 9);

        textPlan.Paragraphs.Should().HaveCount(2);
        layout.Glyphs.Should().Contain(glyph => glyph.Character == 'B'
            && glyph.ParagraphIndex == 0
            && glyph.RunIndex == 0
            && glyph.Formatting.FontFamily == "Arial"
            && glyph.Formatting.Bold
            && glyph.Formatting.Underline
            && glyph.Formatting.Strikethrough
            && glyph.Formatting.ColorHex == "#C00000");
        layout.Glyphs.Should().Contain(glyph => glyph.Character == 'p' && glyph.ParagraphIndex == 0 && glyph.RunIndex == 1);
        layout.Glyphs.Should().Contain(glyph => glyph.Character == 'n' && glyph.ParagraphIndex == 1);
        layout.Glyphs.Single(glyph => glyph.Character == 'n' && glyph.ParagraphIndex == 1)
            .Y.Should().BeGreaterThan(layout.Glyphs.Single(glyph => glyph.Character == 'B').Y);
        layout.CaretStops.Should().Contain(stop => stop.ParagraphIndex == 0 && stop.RunIndex == 0 && stop.Offset == 4);
        layout.CaretStops.Should().Contain(stop => stop.ParagraphIndex == 1 && stop.RunIndex == 0 && stop.Offset == 0);
    }

    [Fact]
    public void ShapeTextPlan_PreservesListKindAndLevelForNumberedTextBoxParagraphs()
    {
        // freew-numbering-restart F2: DocxReader populates Formatting.ListKind/ListLevel on shape
        // (text-box) paragraphs identically to body paragraphs, but the render-plan projection used
        // to drop it because DrawingObjectTextParagraphPlan had no field to carry it.
        var shape = new Shape(ShapeKind.TextBox, 120, 80);
        var firstItem = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 0 }
        };
        firstItem.Runs.Add(new Run("First", RunFormatting.Default));
        var secondItem = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 1 }
        };
        secondItem.Runs.Add(new Run("Second", RunFormatting.Default));
        shape.TextParagraphs.Add(firstItem);
        shape.TextParagraphs.Add(secondItem);

        var textPlan = DrawingObjectTextLayoutPlanner.BuildTextPlan(shape);

        textPlan.Paragraphs.Should().HaveCount(2);
        textPlan.Paragraphs[0].ListKind.Should().Be(ListKind.Number);
        textPlan.Paragraphs[0].ListLevel.Should().Be(0);
        textPlan.Paragraphs[1].ListKind.Should().Be(ListKind.Bullet);
        textPlan.Paragraphs[1].ListLevel.Should().Be(1);
    }

    [Fact]
    public void ShapeTextPlan_NonListParagraphStillDefaultsToNoListKind()
    {
        // Sibling no-regression case: an ordinary (non-list) text-box paragraph must keep
        // reporting ListKind.None/level 0, exactly as before this fix.
        var shape = new Shape(ShapeKind.TextBox, 120, 80);
        var plain = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        plain.Runs.Add(new Run("Plain", RunFormatting.Default));
        shape.TextParagraphs.Add(plain);

        var textPlan = DrawingObjectTextLayoutPlanner.BuildTextPlan(shape);

        textPlan.Paragraphs.Should().HaveCount(1);
        textPlan.Paragraphs[0].ListKind.Should().Be(ListKind.None);
        textPlan.Paragraphs[0].ListLevel.Should().Be(0);
        textPlan.Paragraphs[0].Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void TextLayout_UsesMonotonicLineIndexesAcrossHardBreaks()
    {
        var layout = DrawingObjectTextLayoutPlanner.LayoutPlan(
            new DrawingObjectTextPlan("A\nB", ShapeTextDirection.Horizontal),
            widthDip: 80,
            heightDip: 40,
            (text, _) => 8,
            _ => 10);

        layout.Glyphs.Single(glyph => glyph.Character == 'A').LineIndex.Should().Be(0);
        layout.Glyphs.Single(glyph => glyph.Character == 'B').LineIndex.Should().Be(1);
        layout.Glyphs.Select(glyph => glyph.LineIndex).Distinct().Should().Equal(0, 1);
    }

    [Fact]
    public void TextLayout_TreatsCrLfAsOneHardBreak()
    {
        var layout = DrawingObjectTextLayoutPlanner.LayoutPlan(
            new DrawingObjectTextPlan("A\r\nB", ShapeTextDirection.Horizontal),
            widthDip: 80,
            heightDip: 40,
            (text, _) => 8,
            _ => 10);

        layout.Glyphs.Select(glyph => glyph.Character).Should().Equal('A', 'B');
        layout.Glyphs.Single(glyph => glyph.Character == 'B').LineIndex.Should().Be(1);
        layout.Glyphs.Should().OnlyContain(glyph => glyph.LineIndex == 0 || glyph.LineIndex == 1);
        layout.CaretStops.Should().Contain(stop => stop.Offset == 3 && stop.LineIndex == 1);
    }

    [Fact]
    public void TextLayout_WrapsAtFittingWordBoundariesAndFallsBackToCharacters()
    {
        var wordWrapped = DrawingObjectTextLayoutPlanner.LayoutPlan(
            new DrawingObjectTextPlan("one two", ShapeTextDirection.Horizontal),
            widthDip: 48,
            heightDip: 40,
            (text, _) => 8,
            _ => 10);

        wordWrapped.Glyphs.Where(glyph => glyph.LineIndex == 0)
            .Select(glyph => glyph.Character).Should().Equal('o', 'n', 'e', ' ');
        wordWrapped.Glyphs.Where(glyph => glyph.LineIndex == 1)
            .Select(glyph => glyph.Character).Should().Equal('t', 'w', 'o');

        var characterFallback = DrawingObjectTextLayoutPlanner.LayoutPlan(
            new DrawingObjectTextPlan("abcdef", ShapeTextDirection.Horizontal),
            widthDip: 48,
            heightDip: 40,
            (text, _) => 10,
            _ => 10);

        characterFallback.Glyphs.Where(glyph => glyph.LineIndex == 0)
            .Select(glyph => glyph.Character).Should().Equal('a', 'b', 'c', 'd');
        characterFallback.Glyphs.Where(glyph => glyph.LineIndex == 1)
            .Select(glyph => glyph.Character).Should().Equal('e', 'f');

        var richOverlongWord = new DrawingObjectTextPlan("", ShapeTextDirection.Horizontal)
        {
            Paragraphs =
            [
                new DrawingObjectTextParagraphPlan(
                    TextAlignment.Left,
                    [
                        new DrawingObjectTextRunPlan("fo", RunFormatting.Default with { Bold = true }, 0, 0),
                        new DrawingObjectTextRunPlan("obar", RunFormatting.Default, 0, 1)
                    ])
            ]
        };
        var richFallback = DrawingObjectTextLayoutPlanner.LayoutPlan(
            richOverlongWord,
            widthDip: 40,
            heightDip: 40,
            (text, _) => 8,
            _ => 10);

        richFallback.Glyphs.Where(glyph => glyph.LineIndex == 0)
            .Select(glyph => glyph.Character).Should().Equal('f', 'o', 'o', 'b');
        richFallback.Glyphs.Where(glyph => glyph.LineIndex == 1)
            .Select(glyph => glyph.Character).Should().Equal('a', 'r');
    }

    [Fact]
    public void WordArtPlan_UsesTheWordArtCentreTransform()
    {
        var wordArt = new WordArt("Transform", WordArtStyle.GlowBlue, 36)
        {
            RotationAngle = 30,
            FlipH = true,
            FlipV = true,
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront }
        };

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(
            wordArt,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.WordArt,
                BlockIndex: 0,
                RunIndex: 1,
                new DocumentFloatRect(10, 20, 200, 96),
                BehindText: false,
                ZOrderIndex: 7,
                ImageWrapping.InFront));

        plan.Kind.Should().Be(DrawingObjectVisualKind.WordArt);
        plan.RotationAngle.Should().Be(30);
        plan.FlipH.Should().BeTrue();
        plan.FlipV.Should().BeTrue();
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

    [Fact]
    public void WordArtPlan_PreservesAuthoredFontFamilyAndDefaultsOnlyWhenAbsent()
    {
        var authored = new WordArt("Typeface", WordArtStyle.FillBlue, fontSizePt: 24)
        {
            FontFamily = "Arial"
        };

        DrawingObjectVisualPlanner.BuildInlineWordArtPlan(authored).WordArt.FontFamily.Should().Be("Arial");
        DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Default", WordArtStyle.FillBlue, fontSizePt: 24)).WordArt.FontFamily.Should().Be("Calibri");
    }

    [Fact]
    public void WordArtPlan_PreservesAuthoredBoldFormatting()
    {
        var bold = new WordArt("Bold", WordArtStyle.FillBlue, fontSizePt: 24)
        {
            Bold = true
        };

        DrawingObjectVisualPlanner.BuildInlineWordArtPlan(bold).WordArt.Bold.Should().BeTrue();
        DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Regular", WordArtStyle.FillBlue, fontSizePt: 24)).WordArt.Bold.Should().BeFalse();
    }

    [Fact]
    public void WordArtPlan_AppliesOnlyAuthoredNormalAutoFitFontScale()
    {
        var normalAutoFit = new WordArt("Fit", WordArtStyle.GlowBlue, fontSizePt: 30)
        {
            TextFitMode = WordArtTextFitMode.NormalAutoFit,
            NormalAutoFitFontScale = 85000
        };
        var noAutoFit = new WordArt("Fit", WordArtStyle.GlowBlue, fontSizePt: 30)
        {
            TextFitMode = WordArtTextFitMode.NoAutoFit,
            NormalAutoFitFontScale = 85000
        };

        var normalPlan = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(normalAutoFit);
        var noAutoFitPlan = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(noAutoFit);

        normalPlan.WordArt.FontSizeDip.Should().BeApproximately(34, 0.01);
        noAutoFitPlan.WordArt.FontSizeDip.Should().BeApproximately(40, 0.01);
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

    [Fact]
    public void GroupPlan_RecursesNestedGroupWithLocalTransform()
    {
        var inner = new DrawingGroup { WidthPt = 72, HeightPt = 36, RotationAngle = 20, FlipV = true };
        inner.Children.Add(new Shape(ShapeKind.Ellipse, 24, 24, "#70AD47"));
        inner.ChildOffsets.Add((3, 4));
        inner.Children.Add(new WordArt("Inner", WordArtStyle.GlowGold, 16));
        inner.ChildOffsets.Add((30, 6));

        var outer = new DrawingGroup { WidthPt = 180, HeightPt = 90 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 18));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 36, 24, "#4472C4"));
        outer.ChildOffsets.Add((108, 24));

        var plan = DrawingObjectVisualPlanner.BuildVisualPlan(outer,
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Group, 0, 0,
                new DocumentFloatRect(100, 200, 240, 120), false, 0, ImageWrapping.Square));

        plan.GroupChildren.Should().HaveCount(2);
        var nested = plan.GroupChildren[0].Visual;
        nested.Kind.Should().Be(DrawingObjectVisualKind.Group);
        nested.Rect.XDip.Should().BeApproximately(116, 0.01);
        nested.Rect.YDip.Should().BeApproximately(224, 0.01);
        nested.RotationAngle.Should().Be(20);
        nested.FlipV.Should().BeTrue();
        nested.GroupChildren.Select(child => child.Visual.Kind).Should().Equal(
            DrawingObjectVisualKind.Shape,
            DrawingObjectVisualKind.WordArt);
    }

    [Fact]
    public void SmartArtPlan_UsesUniformDarkAccentForModerateBasicProcess()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Check"]);
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "moderate1";

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.Nodes.Select(node => node.FillHex).Should().Equal("#1F3864", "#1F3864");
    }
}
