using System.IO;
using System.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TextLayoutPlannerTests
{
    [Fact]
    public void PlanShapeAutoFitBounds_GrowsSingleColumnShapeByAnchor()
    {
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Anchor = VerticalAnchor.Middle,
            InsetTopDip = 4,
            InsetBottomDip = 4,
            ColumnCount = 1
        };
        var bounds = new LayoutRect(10, 20, 100, 50);
        var measures = new[] { new TextParagraphMeasure(0, 80, 2, 2) };

        var planned = TextLayoutPlanner.PlanShapeAutoFitBounds(text, bounds, measures);

        planned.Should().Be(new LayoutRect(10, -1, 100, 92));
    }

    [Fact]
    public void PlanShapeAutoFitBounds_LeavesMultiColumnAndFittingShapesUntouched()
    {
        var multiColumn = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            ColumnCount = 2
        };
        var bounds = new LayoutRect(10, 20, 100, 50);
        var measures = new[] { new TextParagraphMeasure(0, 80, 2, 2) };

        TextLayoutPlanner.PlanShapeAutoFitBounds(multiColumn, bounds, measures)
            .Should().Be(bounds);

        var fitting = new ResolvedTextLayout { AutoFitKind = TextAutoFitKind.Shape };
        TextLayoutPlanner.PlanShapeAutoFitBounds(
                fitting,
                bounds,
                new[] { new TextParagraphMeasure(0, 1, 0, 0) })
            .Should().Be(bounds);
    }

    [Fact]
    public void PlanTableCellText_MiddleAnchor_UsesInsetsAndMeasuredParagraphs()
    {
        var text = new ResolvedTextLayout
        {
            InsetLeftDip = 5,
            InsetTopDip = 6,
            InsetRightDip = 7,
            InsetBottomDip = 8
        };
        var bounds = new LayoutRect(10, 20, 200, 100);
        var measures = new[]
        {
            new TextParagraphMeasure(0, 20, 2, 3),
            new TextParagraphMeasure(2, 10, 1, 0)
        };

        var plan = TextLayoutPlanner.PlanTableCellText(
            text,
            bounds,
            TableCellAnchor.Middle,
            measures);

        plan.Area.Should().Be(new TextLayoutArea(15, 26, 188, 86));
        plan.Paragraphs.Should().Equal(
            new TextParagraphPlacement(0, 0, 15, 53, 188),
            new TextParagraphPlacement(2, 0, 15, 77, 188));
    }

    [Fact]
    public void GetColumnLayout_UsesDefaultSpacingAndLineSpacingScale()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 3,
            ColumnSpacingDip = 0,
            LnSpcReduction = 0.25,
            InsetLeftDip = 6,
            InsetRightDip = 6
        };

        var layout = TextLayoutPlanner.GetColumnLayout(
            text,
            new LayoutRect(0, 0, 396, 100));

        layout.ColumnCount.Should().Be(3);
        layout.ColumnSpacingDip.Should().Be(TextLayoutPlanner.DefaultColumnSpacingDip);
        layout.ColumnWidthDip.Should().BeApproximately((384 - 97) / 3.0, 0.001);
        layout.LineSpacingScale.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void PlanColumns_GreedilyFlowsParagraphsAcrossColumns()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 2,
            ColumnSpacingDip = 20,
            InsetLeftDip = 10,
            InsetTopDip = 5,
            InsetRightDip = 10,
            InsetBottomDip = 5,
            Paragraphs = new[]
            {
                Paragraph(),
                Paragraph(indent: 12),
                Paragraph()
            }
        };
        var layout = TextLayoutPlanner.GetColumnLayout(text, new LayoutRect(0, 0, 300, 100));
        var measures = new[]
        {
            new TextParagraphMeasure(0, 40, 0, 0),
            new TextParagraphMeasure(1, 60, 0, 0),
            new TextParagraphMeasure(2, 20, 0, 0)
        };

        var plan = TextLayoutPlanner.PlanColumns(text, layout, measures);

        layout.ColumnWidthDip.Should().Be(130);
        plan.Paragraphs.Should().Equal(
            new TextParagraphPlacement(0, 0, 10, 5, 130),
            new TextParagraphPlacement(1, 1, 172, 5, 118),
            new TextParagraphPlacement(2, 1, 160, 65, 130));
    }

    [Fact]
    public void PlanColumns_BulletedParagraph_PlansBulletSlotInAssignedColumn()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 2,
            ColumnSpacingDip = 20,
            Paragraphs = new[]
            {
                Paragraph(),
                BulletParagraph(indent: 36, hanging: 18)
            }
        };
        var layout = TextLayoutPlanner.GetColumnLayout(text, new LayoutRect(0, 0, 200, 90));
        var measures = new[]
        {
            new TextParagraphMeasure(0, 80, 0, 0),
            new TextParagraphMeasure(1, 20, 0, 0)
        };

        var plan = TextLayoutPlanner.PlanColumns(text, layout, measures);

        plan.Paragraphs[1].X.Should().Be(146);
        plan.Paragraphs[1].MaxWidthDip.Should().BeApproximately(layout.ColumnWidthDip - 36, 0.001);
        plan.Paragraphs[1].Bullet.HasValue.Should().BeTrue();
        var bullet = plan.Paragraphs[1].Bullet!.Value;
        bullet.Text.Should().Be("\u2022");
        bullet.FontFamily.Should().Be("Aptos");
        bullet.FontSizePt.Should().Be(14);
        bullet.Color.Should().Be(new SrgbColor(0x22, 0x33, 0x44));
        bullet.X.Should().Be(128);
        bullet.Y.Should().BeApproximately(layout.Area.Y, 0.001);
    }

    [Fact]
    public void PlanColumnLines_FlowsTheLastLineAcrossColumnBoundary()
    {
        var text = new ResolvedTextLayout
        {
            ColumnCount = 2,
            ColumnSpacingDip = 20,
            InsetLeftDip = 10,
            InsetTopDip = 5,
            InsetRightDip = 10,
            InsetBottomDip = 5,
            Paragraphs = new[] { Paragraph(), Paragraph() }
        };
        var layout = TextLayoutPlanner.GetColumnLayout(text, new LayoutRect(0, 0, 300, 100));
        var lines = new[]
        {
            new TextColumnLineMeasure(0, 0, 40, 0, 0, true, false),
            new TextColumnLineMeasure(0, 1, 40, 0, 0, false, true),
            new TextColumnLineMeasure(1, 0, 40, 0, 0, true, false),
            new TextColumnLineMeasure(1, 1, 40, 0, 0, false, true)
        };

        var plan = TextLayoutPlanner.PlanColumnLines(text, layout, lines);

        plan.Select(line => (line.ParagraphIndex, line.LineIndex, line.ColumnIndex))
            .Should().Equal(
                (0, 0, 0),
                (0, 1, 0),
                (1, 0, 1),
                (1, 1, 1));
        plan[2].X.Should().BeApproximately(160, 0.001);
    }

    [Fact]
    public void PlanBodyText_BottomAnchor_UsesInsetsIndentAndLineSpacingScale()
    {
        var text = new ResolvedTextLayout
        {
            Anchor = VerticalAnchor.Bottom,
            LnSpcReduction = 0.25,
            InsetLeftDip = 5,
            InsetTopDip = 6,
            InsetRightDip = 7,
            InsetBottomDip = 8,
            Paragraphs = new[]
            {
                Paragraph(),
                Paragraph(indent: 12)
            }
        };
        var measures = new[]
        {
            new TextParagraphMeasure(0, 40, 4, 8),
            new TextParagraphMeasure(1, 20, 2, 6)
        };

        var plan = TextLayoutPlanner.PlanBodyText(
            text,
            new LayoutRect(10, 20, 200, 100),
            measures);

        plan.Area.Should().Be(new TextLayoutArea(15, 26, 188, 86));
        plan.Paragraphs.Should().HaveCount(2);
        plan.Paragraphs[0].Should().Be(new TextParagraphPlacement(0, 0, 15, 55, 188));
        plan.Paragraphs[1].ParagraphIndex.Should().Be(1);
        plan.Paragraphs[1].X.Should().BeApproximately(27, 0.001);
        plan.Paragraphs[1].Y.Should().BeApproximately(92.5, 0.001);
        plan.Paragraphs[1].MaxWidthDip.Should().BeApproximately(176, 0.001);
    }

    [Fact]
    public void PlanBodyText_RuntimeAutoFitLineSpacingReduction_AnchorsFromReducedHeight()
    {
        var text = new ResolvedTextLayout
        {
            Anchor = VerticalAnchor.Middle,
            InsetLeftDip = 0,
            InsetTopDip = 0,
            InsetRightDip = 0,
            InsetBottomDip = 0,
            Paragraphs = new[] { Paragraph("Shrink") }
        };
        var autoFitPlan = new TextAutoFitOverflowPlan(
            TextAutoFitOverflowMode.RuntimeShrink,
            FontScale: 1.0,
            LineSpacingReduction: 0.20);

        var plan = TextLayoutPlanner.PlanBodyText(
            text,
            new LayoutRect(0, 0, 200, 100),
            new[] { new TextParagraphMeasure(0, 100, 0, 0) },
            autoFitPlan);

        plan.Paragraphs.Should().ContainSingle()
            .Which.Y.Should().BeApproximately(10.0, 0.001);
    }

    [Fact]
    public void PlanBodyText_BulletedParagraph_PlansBulletSlotFromIndentAndHanging()
    {
        var text = new ResolvedTextLayout
        {
            InsetLeftDip = 5,
            InsetTopDip = 6,
            InsetRightDip = 7,
            InsetBottomDip = 8,
            Paragraphs = new[]
            {
                BulletParagraph(indent: 48, hanging: 24)
            }
        };

        var plan = TextLayoutPlanner.PlanBodyText(
            text,
            new LayoutRect(10, 20, 200, 100),
            new[] { new TextParagraphMeasure(0, 20, 0, 0) });

        var placement = plan.Paragraphs.Single();
        placement.X.Should().Be(63);
        placement.Y.Should().Be(26);
        placement.MaxWidthDip.Should().Be(140);
        placement.Bullet.Should().Be(new TextBulletPlacement(
            "\u2022",
            "Aptos",
            14,
            new SrgbColor(0x22, 0x33, 0x44),
            null,
            39,
            26));
    }

    [Fact]
    public void PlanBodyText_ImageBullet_PlansImagePlacementFromIndentAndHanging()
    {
        var image = new ImagePart
        {
            Bytes = new byte[] { 1, 2, 3 },
            ContentType = "image/png"
        };
        var text = new ResolvedTextLayout
        {
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[] { new ResolvedRun { Text = "Picture bullet" } },
                    BulletKind = BulletKind.Image,
                    BulletImage = image,
                    BulletFontSizePt = 12,
                    IndentDip = 36,
                    HangingDip = 18
                }
            }
        };

        var plan = TextLayoutPlanner.PlanBodyText(
            text,
            new LayoutRect(10, 20, 200, 100),
            new[] { new TextParagraphMeasure(0, 20, 0, 0) });

        var bullet = plan.Paragraphs.Single().Bullet;
        bullet.Should().NotBeNull();
        bullet!.Value.IsImage.Should().BeTrue();
        bullet.Value.Image.Should().BeSameAs(image);
        bullet.Value.Text.Should().BeEmpty();
        bullet.Value.X.Should().BeApproximately(37.14, 0.001);
        bullet.Value.Y.Should().BeApproximately(24.57, 0.001);
    }

    [Fact]
    public void CreateParagraphMeasure_AppliesPointAndLineSpacingScale()
    {
        var measure = TextLayoutPlanner.CreateParagraphMeasure(
            paragraphIndex: 4,
            heightDip: 24,
            spaceBeforePt: 6,
            spaceAfterPt: 3,
            lineSpacingScale: 0.5);

        measure.Should().Be(new TextParagraphMeasure(4, 12, 4, 2));
    }

    [Theory]
    [InlineData(TextVerticalType.Horizontal, TextVerticalRenderMode.Horizontal, 0.0, false)]
    [InlineData(TextVerticalType.Vertical, TextVerticalRenderMode.Rotated, 90.0, true)]
    [InlineData(TextVerticalType.Vertical270, TextVerticalRenderMode.Rotated, -90.0, true)]
    [InlineData(TextVerticalType.EastAsianVertical, TextVerticalRenderMode.StackedUpright, 0.0, false)]
    [InlineData(TextVerticalType.WordArtVertical, TextVerticalRenderMode.StackedUpright, 0.0, false)]
    [InlineData(TextVerticalType.WordArtVerticalRtl, TextVerticalRenderMode.StackedUpright, 0.0, false)]
    public void PlanTextOrientation_MapsPowerPointVerticalTypesToSharedBoundsAndAngle(
        TextVerticalType verticalType,
        TextVerticalRenderMode expectedMode,
        double expectedAngle,
        bool isRotated)
    {
        var text = new ResolvedTextLayout { VerticalType = verticalType };
        var bounds = new LayoutRect(10, 20, 200, 100);

        var plan = TextLayoutPlanner.PlanTextOrientation(text, bounds);

        plan.VerticalType.Should().Be(verticalType);
        plan.RenderMode.Should().Be(expectedMode);
        plan.RotationAngleDegrees.Should().Be(expectedAngle);
        plan.RotationCenterX.Should().Be(110);
        plan.RotationCenterY.Should().Be(70);
        plan.IsRotated.Should().Be(isRotated);
        plan.TextBounds.Should().Be(isRotated
            ? new LayoutRect(60, -30, 100, 200)
            : bounds);
    }

    [Fact]
    public void PlanStackedVerticalText_EastAsianVertical_PlansUprightGlyphsTopToBottom()
    {
        var text = new ResolvedTextLayout
        {
            VerticalType = TextVerticalType.EastAsianVertical,
            Anchor = VerticalAnchor.Middle,
            InsetLeftDip = 0,
            InsetTopDip = 0,
            InsetRightDip = 0,
            InsetBottomDip = 0,
            Paragraphs = new[]
            {
                ParagraphWithRuns("AB")
            }
        };

        var plan = TextLayoutPlanner.PlanStackedVerticalText(
            text,
            new LayoutRect(0, 0, 100, 100),
            MeasureStackedGlyph);

        plan.RenderMode.Should().Be(TextVerticalRenderMode.StackedUpright);
        plan.Glyphs.Should().Equal(
            new TextStackedGlyphPlacement(0, 0, "A", 45, 26, 10, 8),
            new TextStackedGlyphPlacement(0, 0, "B", 40, 50, 20, 8));
    }

    [Fact]
    public void PlanStackedVerticalText_WordArtVerticalRtl_UsesLogicalTopToBottomOrder()
    {
        var text = new ResolvedTextLayout
        {
            VerticalType = TextVerticalType.WordArtVerticalRtl,
            InsetLeftDip = 0,
            InsetTopDip = 0,
            InsetRightDip = 0,
            InsetBottomDip = 0,
            Paragraphs = new[]
            {
                ParagraphWithRuns("AB")
            }
        };

        var plan = TextLayoutPlanner.PlanStackedVerticalText(
            text,
            new LayoutRect(0, 0, 100, 100),
            MeasureStackedGlyph);

        plan.Glyphs.Select(g => g.Text).Should().Equal(new[] { "A", "B" },
            "without PowerPoint COM baselines this slice keeps single-column RTL WordArt in logical run order");
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_AutoFitFalse_DoesNotShrink()
    {
        var text = new ResolvedTextLayout { AutoFit = false };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.NoAutoFit);
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_StoredFontScaleWinsWithoutRuntimeDoubleScale()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            HasStoredFontScale = true,
            FontScale = 0.625,
            Paragraphs = new[] { Paragraph("Cached") }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.StoredFontScale);
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_OverflowingAutoFitWithoutCacheShrinks()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            Paragraphs = new[] { BulletParagraph(indent: 36, hanging: 18) }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 80,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });
        var scaled = TextLayoutPlanner.ApplyAutoFitPlan(text, plan);

        plan.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        plan.FontScale.Should().BeApproximately(0.8, 0.001);
        plan.FontScale.Should().BeLessThan(1.0);
        plan.FontScale.Should().BeGreaterThanOrEqualTo(TextLayoutPlanner.RuntimeAutoFitMinimumFontScale);
        scaled.Should().NotBeSameAs(text);
        scaled.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(18.0 * 0.8, 0.001);
        scaled.Paragraphs[0].BulletFontSizePt.Should().BeApproximately(14.0 * 0.8, 0.001);
        scaled.FontScale.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_NonOverflowingAutoFitWithoutCacheDoesNotEnlarge()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            Paragraphs = new[] { Paragraph("Fits") }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 120,
            new[] { new TextParagraphMeasure(0, 80, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.Fits);
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_MultiColumnCapacityDoesNotShrinkTextThatFitsAcrossColumns()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            ColumnCount = 2,
            Paragraphs = new[] { Paragraph("Column 1"), Paragraph("Column 2") }
        };
        var layout = TextLayoutPlanner.GetColumnLayout(text, new LayoutRect(0, 0, 300, 100));

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            TextLayoutPlanner.GetAutoFitCapacityHeight(layout),
            new[]
            {
                new TextParagraphMeasure(0, 80, 0, 0),
                new TextParagraphMeasure(1, 80, 0, 0)
            });

        TextLayoutPlanner.GetAutoFitCapacityHeight(layout)
            .Should().BeApproximately(layout.Area.Height * layout.ColumnCount, 0.001);
        plan.Mode.Should().Be(TextAutoFitOverflowMode.Fits);
        plan.FontScale.Should().Be(1.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    [Fact]
    public void PlanNormalAutoFitOverflow_ExtremeOverflowUsesMinimumScaleAndLineReduction()
    {
        var text = new ResolvedTextLayout { AutoFit = true };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 50,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        plan.FontScale.Should().Be(TextLayoutPlanner.RuntimeAutoFitMinimumFontScale);
        plan.LineSpacingReduction.Should().BeGreaterThan(0.0);
        plan.LineSpacingReduction.Should().BeLessThanOrEqualTo(TextLayoutPlanner.RuntimeAutoFitMaximumLineSpacingReduction);
    }

    // ─── LA1: normAutofit (shrink) vs spAutoFit (grow shape) must not conflate ────

    /// <summary>
    /// LA1: a:spAutoFit (AutoFitKind.Shape) means "grow the SHAPE to fit text" — the text
    /// itself must never be runtime-shrunk. Before the fix, any AutoFit=true box without a
    /// cached font scale (including spAutoFit boxes) went through the shrink-to-fit path.
    /// </summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_SpAutoFitShape_OverflowingText_DoesNotShrink()
    {
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = new[] { BulletParagraph(indent: 36, hanging: 18) }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.NoAutoFit,
            "LA1: spAutoFit grows the shape; it must never trigger the text runtime-shrink path");
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text,
            "no shrink should be applied for an spAutoFit (Shape) box even when text overflows");
    }

    /// <summary>LA1 control: a normAutofit (AutoFitKind.Normal) box with overflow still shrinks.</summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_NormAutofitKind_OverflowingText_StillShrinks()
    {
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Normal,
            Paragraphs = new[] { BulletParagraph(indent: 36, hanging: 18) }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 80,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });
        var scaled = TextLayoutPlanner.ApplyAutoFitPlan(text, plan);

        plan.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        plan.FontScale.Should().BeLessThan(1.0);
        scaled.Should().NotBeSameAs(text);
        scaled.Paragraphs[0].Runs[0].FontSizePt.Should().BeLessThan(18.0);
    }

    /// <summary>LA1: AutoFitKind.None (no autofit / a:noAutofit) never shrinks, same as before.</summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_NoneKind_DoesNotShrink()
    {
        var text = new ResolvedTextLayout { AutoFitKind = TextAutoFitKind.None };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[] { new TextParagraphMeasure(0, 100, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.NoAutoFit);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    [Fact]
    public void PlanParagraphRenderRoute_UsesPlainRouteForSimpleParagraph()
    {
        var text = new ResolvedTextLayout();
        var paragraph = Paragraph();

        TextLayoutPlanner.PlanParagraphRenderRoute(paragraph, text)
            .Should().Be(TextParagraphRenderRoute.Plain);
    }

    [Fact]
    public void PlanParagraphRenderRoute_UsesTabsRouteForTabCharactersWithoutEffects()
    {
        var text = new ResolvedTextLayout();
        var paragraph = Paragraph("Before\tAfter");

        TextLayoutPlanner.PlanParagraphRenderRoute(paragraph, text)
            .Should().Be(TextParagraphRenderRoute.Tabs);
    }

    [Fact]
    public void PlanParagraphRenderRoute_UsesBaselineRouteForAuthoredRunOffset()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = new[] { new ResolvedRun { Text = "P", BaselineOffset = 30000 } }
        };

        TextLayoutPlanner.PlanParagraphRenderRoute(paragraph, new ResolvedTextLayout())
            .Should().Be(TextParagraphRenderRoute.Baseline);
    }

    [Fact]
    public void BaselineOffsetToDip_UsesFontSizeAndSignedPercentageUnits()
    {
        TextLayoutPlanner.BaselineOffsetToDip(30000, 12).Should().BeApproximately(4.8, 0.0001);
        TextLayoutPlanner.BaselineOffsetToDip(-25000, 12).Should().BeApproximately(-4.0, 0.0001);
        TextLayoutPlanner.BaselineOffsetToDip(null, 12).Should().Be(0);
    }

    [Fact]
    public void ApplyAutoFitPlan_RetainsAuthoredBaselineToken()
    {
        var text = new ResolvedTextLayout
        {
            Paragraphs = new[] { new ResolvedParagraph
            {
                Runs = new[] { new ResolvedRun { Text = "P", BaselineOffset = -25000 } }
            } }
        };
        var plan = new TextAutoFitOverflowPlan(
            TextAutoFitOverflowMode.RuntimeShrink,
            FontScale: 0.8,
            LineSpacingReduction: 0);

        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Paragraphs[0].Runs[0].BaselineOffset
            .Should().Be(-25000);
    }

    [Fact]
    public void PlanParagraphRenderRoute_UsesEffectsRouteForTextEffectsAndWarpBeforeTabs()
    {
        var effectParagraph = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "Shadow", TextShadow = new ResolvedRunShadow() }
            }
        };
        var tabAndWarpParagraph = Paragraph("Before\tAfter");

        TextLayoutPlanner.PlanParagraphRenderRoute(effectParagraph, new ResolvedTextLayout())
            .Should().Be(TextParagraphRenderRoute.Effects);
        TextLayoutPlanner.PlanParagraphRenderRoute(
                tabAndWarpParagraph,
                new ResolvedTextLayout { WarpPreset = "textArchUp" })
            .Should().Be(TextParagraphRenderRoute.Effects);
    }

    [Fact]
    public void PlanTabStops_UsesExplicitStopThenDefaultFallbackStop()
    {
        var paragraph = Paragraph("A\tB\tC");
        var tabStops = new[]
        {
            new ResolvedTabStop
            {
                PositionDip = 50,
                Alignment = TabStopAlignment.Left
            }
        };

        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX: 100,
            tabStops,
            MeasureTenDipPerCharacter);

        plan.Segments.Should().Equal(
            new TextTabSegmentPlacement(0, "A", 100),
            new TextTabSegmentPlacement(0, "B", 150),
            new TextTabSegmentPlacement(0, "C", 196));
    }

    [Fact]
    public void PlanTabStops_RightAlignedStop_ClampsToCurrentPenWhenSegmentWouldOverlap()
    {
        var paragraph = Paragraph("LongLong\tWide!");
        var tabStops = new[]
        {
            new ResolvedTabStop
            {
                PositionDip = 100,
                Alignment = TabStopAlignment.Right
            }
        };

        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX: 0,
            tabStops,
            MeasureTenDipPerCharacter);

        plan.Segments.Should().Equal(
            new TextTabSegmentPlacement(0, "LongLong", 0),
            new TextTabSegmentPlacement(0, "Wide!", 80));
    }

    [Fact]
    public void PlanTabStops_DecimalAlignedStop_MeasuresAcrossRuns()
    {
        var paragraph = ParagraphWithRuns(
            "Label\t",
            "12",
            ".34");
        var tabStops = new[]
        {
            new ResolvedTabStop
            {
                PositionDip = 100,
                Alignment = TabStopAlignment.Decimal
            }
        };

        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX: 0,
            tabStops,
            MeasureTenDipPerCharacter);

        plan.Segments.Should().Equal(
            new TextTabSegmentPlacement(0, "Label", 0),
            new TextTabSegmentPlacement(1, "12", 70),
            new TextTabSegmentPlacement(2, ".34", 90));
    }

    [Fact]
    public void PlanTabStops_DecimalAlignedStop_UsesEachRunMeasurement()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun { Text = "Label\t", FontSizePt = 12 },
                new ResolvedRun { Text = "12", FontSizePt = 12 },
                new ResolvedRun { Text = ".34", FontSizePt = 24 }
            }
        };
        var tabStops = new[]
        {
            new ResolvedTabStop
            {
                PositionDip = 100,
                Alignment = TabStopAlignment.Decimal
            }
        };

        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX: 0,
            tabStops,
            (run, text) => text.Length * (run.FontSizePt <= 12 ? 10 : 20));

        plan.Segments.Should().Equal(
            new TextTabSegmentPlacement(0, "Label", 0),
            new TextTabSegmentPlacement(1, "12", 60),
            new TextTabSegmentPlacement(2, ".34", 80));
    }

    [Fact]
    public void PlanTabStops_CarriesLeaderOnTheSegmentFollowingItsTab()
    {
        var paragraph = Paragraph("Contents\tPage 1");
        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX: 0,
            new[]
            {
                new ResolvedTabStop
                {
                    PositionDip = 100,
                    Alignment = TabStopAlignment.Right,
                    Leader = TabStopLeader.Dots,
                },
            },
            MeasureTenDipPerCharacter);

        plan.Segments.Should().Equal(
            new TextTabSegmentPlacement(0, "Contents", 0),
            new TextTabSegmentPlacement(0, "Page 1", 80, TabStopLeader.Dots));
    }

    [Theory]
    [InlineData(TabStopLeader.None, '\0')]
    [InlineData(TabStopLeader.Dots, '.')]
    [InlineData(TabStopLeader.Hyphens, '-')]
    [InlineData(TabStopLeader.Underscore, '_')]
    [InlineData(TabStopLeader.ThickLine, '\u2501')]
    [InlineData(TabStopLeader.Equal, '=')]
    public void TabLeaderGlyph_MapsEveryExternalRtfLeader(TabStopLeader leader, char expected)
    {
        TextLayoutPlanner.GetTabLeaderGlyph(leader).Should().Be(expected);
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_DelegateTextLayoutMathToSharedPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        wpf.Should().Contain("TextLayoutPlanner.GetTextArea");
        wpf.Should().Contain("TextLayoutPlanner.PlanTableCellText");
        wpf.Should().Contain("TextLayoutPlanner.PlanBodyText");
        wpf.Should().Contain("TextLayoutPlanner.GetColumnLayout");
        wpf.Should().Contain("TextLayoutPlanner.PlanColumns");
        wpf.Should().Contain("TextLayoutPlanner.PlanNormalAutoFitOverflow");
        wpf.Should().Contain("TextLayoutPlanner.ApplyAutoFitPlan");
        wpf.Should().Contain("TextLayoutPlanner.GetAutoFitCapacityHeight");
        wpf.Should().Contain("TextLayoutPlanner.PlanTabStops");
        wpf.Should().Contain("TextLayoutPlanner.PlanTextOrientation");
        wpf.Should().Contain("TextLayoutPlanner.PlanStackedVerticalText");
        wpf.Should().Contain("placement.Bullet");
        wpf.Should().Contain("DrawBulletPlacementWpf");
        wpf.Should().Contain("bullet.Image");
        wpf.Should().NotContain("bool isVertical = text.VerticalType");
        wpf.Should().NotContain("bool isVert270");
        wpf.Should().NotContain("FontScalePPT");
        wpf.Should().NotContain("placement.X - para.HangingDip");
        wpf.Should().NotContain("const double DefaultSpacingDip");
        wpf.Should().NotContain("const double DefaultTabDip");
        wpf.Should().NotContain("Math.Floor(relX /");
        wpf.Should().NotContain("TableCellAnchor.Middle => bounds.Y");
        wpf.Should().NotContain("VerticalAnchor.Middle => bounds.Y");

        avalonia.Should().Contain("TextLayoutPlanner.GetTextArea");
        avalonia.Should().Contain("TextLayoutPlanner.PlanTableCellText");
        avalonia.Should().Contain("TextLayoutPlanner.PlanBodyText");
        avalonia.Should().Contain("TextLayoutPlanner.GetColumnLayout");
        avalonia.Should().Contain("TextLayoutPlanner.PlanColumns");
        avalonia.Should().Contain("TextLayoutPlanner.PlanNormalAutoFitOverflow");
        avalonia.Should().Contain("TextLayoutPlanner.ApplyAutoFitPlan");
        wpf.Should().Contain("DrawTabLeaderWpf");
        avalonia.Should().Contain("DrawTabLeaderAvalonia");
        avalonia.Should().Contain("TextLayoutPlanner.GetAutoFitCapacityHeight");
        avalonia.Should().Contain("TextLayoutPlanner.PlanTabStops");
        avalonia.Should().Contain("TextLayoutPlanner.PlanTextOrientation");
        avalonia.Should().Contain("TextLayoutPlanner.PlanStackedVerticalText");
        avalonia.Should().Contain("placement.Bullet");
        avalonia.Should().Contain("DrawBulletPlacementAvalonia");
        avalonia.Should().Contain("bullet.Image");
        avalonia.Should().NotContain("bool isVertical = text.VerticalType");
        avalonia.Should().NotContain("bool isVert270");
        avalonia.Should().NotContain("FontScalePPT");
        avalonia.Should().NotContain("placement.X - para.HangingDip");
        avalonia.Should().NotContain("const double DefaultSpacingDip");
        avalonia.Should().NotContain("const double DefaultTabDip");
        avalonia.Should().NotContain("Math.Floor(relX /");
        avalonia.Should().NotContain("TableCellAnchor.Middle => bounds.Y");
        avalonia.Should().NotContain("VerticalAnchor.Middle => bounds.Y");
    }

    [Fact]
    public void ImportedBulletBodyOriginPolicy_RecognizesOnlyTheGuardedBulletBodySignature()
    {
        var paragraphs = Enumerable.Range(0, 6)
            .Select(_ => new ResolvedParagraph
            {
                Runs = new[]
                {
                    new ResolvedRun { Text = "Bullet", FontFamily = "Aptos", FontSizePt = 18.0 }
                },
                BulletKind = BulletKind.Char
            })
            .ToArray();

        var matching = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs
        };
        TextLayoutPlanner.UsesImportedAptosBodyOrigin(matching).Should().BeTrue();
        TextLayoutPlanner.ResolveImportedAptosBodyOriginOffsetY(matching)
            .Should().Be(TextLayoutPlanner.ImportedAptosBodyOriginOffsetY);

        TextLayoutPlanner.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs.Take(5).ToArray()
        }).Should().BeFalse();

        TextLayoutPlanner.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.None,
            Paragraphs = paragraphs
        }).Should().BeFalse();

        TextLayoutPlanner.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs.Skip(1).Append(new ResolvedParagraph
            {
                Runs = new[]
                {
                    new ResolvedRun { Text = "Bullet", FontFamily = "Calibri", FontSizePt = 18.0 }
                },
                BulletKind = BulletKind.Char
            }).ToArray()
        }).Should().BeFalse();
    }

    [Fact]
    public void WpfAndAvaloniaImportedBulletBodyOrigin_ConsumeSharedPolicy()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        wpf.Should().Contain("TextLayoutPlanner.ResolveImportedAptosBodyOriginOffsetY");
        avalonia.Should().Contain("TextLayoutPlanner.ResolveImportedAptosBodyOriginOffsetY");
        wpf.Should().NotContain("ImportedAptosBodyOriginOffsetY = 6.0");
        avalonia.Should().NotContain("ImportedAptosBodyOriginOffsetY = 6.0");
        wpf.Should().NotContain("UsesImportedAptosBodyOrigin");
        avalonia.Should().NotContain("UsesImportedAptosBodyOrigin");
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_DoNotResolvePlaceholderTextInsetsLocally()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");

        wpf.Should().Contain("TextLayoutPlanner.GetTextArea");
        wpf.Should().Contain("TextLayoutPlanner.PlanBodyText");
        wpf.Should().NotContain("InsetLeftPt");
        wpf.Should().NotContain("InsetTopPt");
        wpf.Should().NotContain("InsetRightPt");
        wpf.Should().NotContain("InsetBottomPt");

        avalonia.Should().Contain("TextLayoutPlanner.GetTextArea");
        avalonia.Should().Contain("TextLayoutPlanner.PlanBodyText");
        avalonia.Should().NotContain("InsetLeftPt");
        avalonia.Should().NotContain("InsetTopPt");
        avalonia.Should().NotContain("InsetRightPt");
        avalonia.Should().NotContain("InsetBottomPt");
    }

    private static ResolvedParagraph Paragraph(double indent = 0) =>
        Paragraph("P", indent);

    private static ResolvedParagraph Paragraph(string text) =>
        Paragraph(text, indent: 0);

    private static ResolvedParagraph Paragraph(string text, double indent)
    {
        return new ResolvedParagraph
        {
            IndentDip = indent,
            Runs = new[] { new ResolvedRun { Text = text } }
        };
    }

    private static ResolvedParagraph ParagraphWithRuns(params string[] runs)
    {
        return new ResolvedParagraph
        {
            Runs = runs.Select(text => new ResolvedRun { Text = text }).ToArray()
        };
    }

    private static ResolvedParagraph BulletParagraph(double indent, double hanging)
    {
        return new ResolvedParagraph
        {
            IndentDip = indent,
            HangingDip = hanging,
            BulletText = "\u2022",
            BulletFontFamily = "Aptos",
            BulletFontSizePt = 14,
            BulletColor = new SrgbColor(0x22, 0x33, 0x44),
            Runs = new[] { new ResolvedRun { Text = "Item" } }
        };
    }

    private static double MeasureTenDipPerCharacter(ResolvedRun run, string text) =>
        text.Length * 10;

    private static TextGlyphMeasure MeasureStackedGlyph(ResolvedRun run, string text) =>
        new(text == "B" ? 20 : 10, 8);

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
