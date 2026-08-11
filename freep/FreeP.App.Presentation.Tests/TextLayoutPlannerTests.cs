using System.Globalization;
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

    /// <summary>
    /// When the box hasn't changed since the cached normAutofit scale was computed, recomputing
    /// from the current (unscaled-back-out) geometry reproduces the same scale the file cached —
    /// so the plan collapses to "keep the cache, no runtime correction" and no new object is
    /// allocated. This is the "still valid" counterpart to the shrink/grow-back tests below.
    /// </summary>
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

        // HeightDip (62.5) is what SlideCompositor would measure for an 18pt run authored at
        // 100 unscaled DIP once the cached 0.625 fontScale is baked into its font size. The box
        // (62.5) is exactly what a 0.625 scale would have been computed for, i.e. no resize.
        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 62.5,
            new[] { new TextParagraphMeasure(0, 62.5, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.StoredFontScale);
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    /// <summary>
    /// R133 fix: a stored normAutofit fontScale must not be trusted forever. When the shape is
    /// resized SMALLER than what the cached scale was computed for, the cached scale under-shrinks
    /// and the text would overflow — PlanNormalAutoFitOverflow must shrink further.
    /// </summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_StoredFontScale_ShapeShrunkSinceCache_ShrinksFurther()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            HasStoredFontScale = true,
            FontScale = 0.625,
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[] { new ResolvedRun { Text = "Cached", FontSizePt = 18.0 * 0.625 } },
                    BulletText = "•",
                    BulletFontSizePt = 14.0 * 0.625
                }
            }
        };

        // The box shrank to 40 DIP -- well below the 62.5 DIP the cached 0.625 scale was
        // computed for, so even the already-shrunk (cache-scaled) 62.5 DIP of measured text
        // overflows it.
        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[] { new TextParagraphMeasure(0, 62.5, 0, 0) });
        var scaled = TextLayoutPlanner.ApplyAutoFitPlan(text, plan);

        plan.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        scaled.FontScale.Should().BeApproximately(0.6, 0.001,
            "the effective font scale must move past the stale 0.625 cache once the box shrinks further");
        scaled.FontScale.Should().BeLessThan(0.625, "the box is now smaller than what produced the cached scale");
        scaled.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(18.0 * 0.6, 0.001);
        scaled.Paragraphs[0].BulletFontSizePt.Should().BeApproximately(14.0 * 0.6, 0.001);
    }

    /// <summary>
    /// R133 fix: when the shape is resized LARGER than what the cached scale was computed for,
    /// the stale cache must not keep the text needlessly shrunk forever -- it should grow back
    /// toward (up to) the authored size.
    /// </summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_StoredFontScale_ShapeGrownSinceCache_GrowsBackToAuthoredSize()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            HasStoredFontScale = true,
            FontScale = 0.625,
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[] { new ResolvedRun { Text = "Cached", FontSizePt = 18.0 * 0.625 } }
                }
            }
        };

        // The box grew to 200 DIP -- far more than the 62.5 DIP the cached 0.625 scale was
        // computed for, so the fully unscaled (100 DIP) text now fits without any shrink at all.
        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 200,
            new[] { new TextParagraphMeasure(0, 62.5, 0, 0) });
        var scaled = TextLayoutPlanner.ApplyAutoFitPlan(text, plan);

        plan.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        scaled.FontScale.Should().BeApproximately(1.0, 0.001,
            "the effective font scale must grow back once the box is large enough that no shrink is needed");
        scaled.FontScale.Should().BeGreaterThan(0.625, "the box is now larger than what produced the cached scale");
        scaled.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(18.0, 0.001,
            "text should return to its full authored size, not stay stuck at the stale cached shrink");
    }

    /// <summary>
    /// Sibling/no-regression: LA1's spAutoFit guard must still hold even when a text body happens
    /// to carry a stored (irrelevant) fontScale and the box has been resized. spAutoFit grows the
    /// SHAPE to fit text -- text itself is never runtime-shrunk or -grown for it, cache or no cache.
    /// </summary>
    [Fact]
    public void PlanNormalAutoFitOverflow_StoredFontScale_ShapeAutoFitKind_UnaffectedByResize()
    {
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            HasStoredFontScale = true,
            FontScale = 0.625,
            Paragraphs = new[] { Paragraph("Cached") }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 10, // far smaller than the cache-implied box; would shrink further under Normal
            new[] { new TextParagraphMeasure(0, 62.5, 0, 0) });

        plan.Mode.Should().Be(TextAutoFitOverflowMode.NoAutoFit,
            "LA1: spAutoFit must never trigger text runtime-shrink/regrow, even with a stored fontScale and a resize");
        plan.FontScale.Should().Be(1.0);
        plan.LineSpacingReduction.Should().Be(0.0);
        TextLayoutPlanner.ApplyAutoFitPlan(text, plan).Should().BeSameAs(text);
    }

    /// <summary>
    /// R133 fix: paragraph spacing (SpaceBeforePt/AfterPt) was never pre-scaled by the cached
    /// fontScale the way run/bullet font sizes were (see SlideCompositor.ResolveTextBody), so it
    /// must land on the recomputed ABSOLUTE target proportion, not the bare correction factor
    /// used for the already-scaled font sizes. Without threading the cached baseline through,
    /// spacing would only move a fraction of the way the fonts moved.
    /// </summary>
    [Fact]
    public void ApplyAutoFitPlan_StoredFontScale_ScalesParagraphSpacingToAbsoluteTarget()
    {
        var text = new ResolvedTextLayout
        {
            AutoFit = true,
            HasStoredFontScale = true,
            FontScale = 0.625,
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[] { new ResolvedRun { Text = "Cached", FontSizePt = 18.0 * 0.625 } },
                    SpaceBeforePt = 12.0,
                    SpaceAfterPt = 6.0
                }
            }
        };

        var plan = TextLayoutPlanner.PlanNormalAutoFitOverflow(
            text,
            textAreaHeightDip: 40,
            new[]
            {
                new TextParagraphMeasure(
                    0,
                    62.5,
                    TextLayoutPlanner.PointsToDip(12.0),
                    TextLayoutPlanner.PointsToDip(6.0))
            });
        var scaled = TextLayoutPlanner.ApplyAutoFitPlan(text, plan);

        // Both the font and the spacing must land on the same 0.6x authored proportion.
        scaled.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(18.0 * 0.6, 0.001);
        scaled.Paragraphs[0].SpaceBeforePt.Should().BeApproximately(12.0 * 0.6, 0.001);
        scaled.Paragraphs[0].SpaceAfterPt.Should().BeApproximately(6.0 * 0.6, 0.001);
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
    public void SplitColumnText_UsesGreedyWordWrappingAndCollapsesParagraphBreaks()
    {
        var measured = new List<string>();

        var lines = TextLayoutPlanner.SplitColumnText(
            "  alpha\r\nbeta   gamma ",
            maxWidthDip: 10,
            wrap: true,
            text =>
            {
                measured.Add(text);
                return text.Length;
            });

        lines.Should().Equal("alpha beta", "gamma");
        measured.Should().Equal("alpha beta", "alpha beta gamma");
        TextLayoutPlanner.SplitColumnText("a\r\nb", 1, false, _ => 100)
            .Should().Equal("a\r\nb");
        TextLayoutPlanner.SplitColumnText(" \r\n ", 10, true, text => text.Length)
            .Should().Equal(string.Empty);
    }

    [Fact]
    public void CloneParagraphWithText_PreservesFragmentFormattingAndParagraphSemantics()
    {
        var tabStops = new[] { new ResolvedTabStop { PositionDip = 42 } };
        var run = new ResolvedRun
        {
            Text = "original",
            FontFamily = "Aptos",
            FontSizePt = 14,
            BaselineOffset = 30000,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            Color = new SrgbColor(1, 2, 3),
            TextShadow = new ResolvedRunShadow()
        };
        var paragraph = new ResolvedParagraph
        {
            Runs = new[] { run },
            Align = TextAlign.Center,
            RightToLeft = true,
            Level = 2,
            BulletKind = BulletKind.Char,
            BulletChar = "*",
            SpaceBeforePt = 3,
            SpaceAfterPt = 4,
            TabStops = tabStops,
            BulletText = "*",
            BulletColor = new SrgbColor(4, 5, 6),
            BulletFontFamily = "Wingdings",
            BulletFontSizePt = 9,
            IndentDip = 12,
            HangingDip = 5
        };

        var fragment = TextLayoutPlanner.CloneParagraphWithText(paragraph, run, "fragment");

        fragment.Should().NotBeSameAs(paragraph);
        fragment.Runs.Should().ContainSingle();
        fragment.Runs[0].Should().NotBeSameAs(run);
        fragment.Runs[0].Text.Should().Be("fragment");
        fragment.Runs[0].FontFamily.Should().Be(run.FontFamily);
        fragment.Runs[0].BaselineOffset.Should().Be(run.BaselineOffset);
        fragment.Runs[0].TextShadow.Should().BeSameAs(run.TextShadow);
        fragment.Align.Should().Be(paragraph.Align);
        fragment.RightToLeft.Should().BeTrue();
        fragment.TabStops.Should().BeSameAs(tabStops);
        fragment.BulletText.Should().Be(paragraph.BulletText);
        fragment.IndentDip.Should().Be(paragraph.IndentDip);
        run.Text.Should().Be("original");
    }

    [Fact]
    public void PlanBaselineLines_PreservesGreedyTokensCrLfAndLineGeometry()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = new[] { new ResolvedRun { Text = "ab cd\r\n  ef" } }
        };

        var lines = TextLayoutPlanner.PlanBaselineLines(
            paragraph,
            startX: 10,
            startY: 20,
            maxWidthDip: 3,
            (_, text, _) => new TextBaselineFragmentMeasure(text.Length, 3, 4));

        lines.Select(line => string.Concat(line.Fragments.Select(fragment => fragment.Text)))
            .Should().Equal("ab ", "cd", "ef");
        lines.Select(line => line.TopY).Should().Equal(20, 24, 28);
        lines.Select(line => line.BaselineY).Should().Equal(23, 27, 31);
        lines.SelectMany(line => line.Fragments).Select(fragment => fragment.Y)
            .Should().Equal(20, 20, 24, 28);
    }

    [Fact]
    public void PlanBaselineLines_SplitsOversizedTokensAndAdvancesEmptyLinesOneDip()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = new[] { new ResolvedRun { Text = "\nabcd", FontSizePt = 12, BaselineOffset = 25000 } }
        };

        var lines = TextLayoutPlanner.PlanBaselineLines(
            paragraph,
            startX: 5,
            startY: 10,
            maxWidthDip: 2,
            (_, text, _) => new TextBaselineFragmentMeasure(text.Length, 3, 4));

        lines.Should().HaveCount(3);
        lines[0].Fragments.Should().BeEmpty();
        lines[0].TopY.Should().Be(10);
        lines[1].TopY.Should().Be(11);
        lines.Skip(1)
            .Select(line => string.Concat(line.Fragments.Select(fragment => fragment.Text)))
            .Should().Equal("ab", "cd");
        lines[1].Fragments[0].Y.Should().Be(7);
    }

    [Fact]
    public void PlanInlineBaselineLine_AlignsMixedTextAndMathMetrics()
    {
        var paragraph = ParagraphWithRuns("text", "fraction", "tail");
        var measures = new[]
        {
            new TextInlineRunMeasure(4, 5, 7),
            new TextInlineRunMeasure(6, 12, 16),
            new TextInlineRunMeasure(3, 7, 9),
        };

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            paragraph,
            startX: 10,
            startY: 20,
            availableWidthDip: 0,
            (runIndex, _, _) => measures[runIndex]);

        line.TopY.Should().Be(20);
        line.BaselineY.Should().Be(32);
        line.WidthDip.Should().Be(13);
        line.HeightDip.Should().Be(16);
        line.Runs.Select(run => run.RunIndex).Should().Equal(0, 1, 2);
        line.Runs.Select(run => run.X).Should().Equal(10, 14, 20);
        line.Runs.Select(run => run.Y).Should().Equal(27, 20, 25);
        line.Runs.Should().OnlyContain(run => run.Y + run.AscentDip == line.BaselineY);
    }

    [Fact]
    public void PlanInlineBaselineLine_UsesNativeCallbackWidthsWithoutNormalization()
    {
        var paragraph = ParagraphWithRuns("text", "math");

        TextInlineBaselineLinePlan Plan(double textWidth, double mathWidth) =>
            TextLayoutPlanner.PlanInlineBaselineLine(
                paragraph,
                startX: 5,
                startY: 8,
                availableWidthDip: 0,
                (runIndex, _, _) => new TextInlineRunMeasure(
                    runIndex == 0 ? textWidth : mathWidth,
                    runIndex == 0 ? 4 : 9,
                    runIndex == 0 ? 6 : 12));

        var wpfMetrics = Plan(textWidth: 4, mathWidth: 6);
        var avaloniaMetrics = Plan(textWidth: 3.5, mathWidth: 5.25);

        wpfMetrics.WidthDip.Should().Be(10);
        wpfMetrics.Runs[1].X.Should().Be(9);
        avaloniaMetrics.WidthDip.Should().Be(8.75);
        avaloniaMetrics.Runs[1].X.Should().Be(8.5);
        avaloniaMetrics.BaselineY.Should().Be(wpfMetrics.BaselineY);
    }

    [Fact]
    public void PlanInlineBaselineLine_PreservesEmptyRunsAndRtlVisualOrder()
    {
        var paragraph = new ResolvedParagraph
        {
            RightToLeft = true,
            Runs = new[]
            {
                new ResolvedRun { Text = "\u05d0" },
                new ResolvedRun(),
                new ResolvedRun { Text = "LTR" },
            }
        };
        var measures = new[]
        {
            new TextInlineRunMeasure(4, 7, 9),
            new TextInlineRunMeasure(3, 0, 0),
            new TextInlineRunMeasure(5, 4, 6),
        };
        var measuredDirections = new List<bool>();

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            paragraph,
            startX: 100,
            startY: 20,
            availableWidthDip: 0,
            (runIndex, _, rightToLeft) =>
            {
                measuredDirections.Add(rightToLeft);
                return measures[runIndex];
            });

        measuredDirections.Should().Equal(true, true, false);
        line.BaselineY.Should().Be(27);
        line.WidthDip.Should().Be(12);
        line.HeightDip.Should().Be(9);
        line.Runs.Select(run => run.RunIndex).Should().Equal(2, 1, 0);
        line.Runs.Select(run => run.X).Should().Equal(100, 105, 108);
        line.Runs.Select(run => run.Y).Should().Equal(23, 27, 20);
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
        wpf.Should().Contain("TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>");
        wpf.Should().Contain("TextLayoutPlanner.PlanMeasuredColumns<FormattedText>");
        wpf.Should().Contain("TextLayoutPlanner.PlanMeasuredContinuousColumnFlow<FormattedText>");
        wpf.Should().Contain("TextLayoutPlanner.PlanNormalAutoFitOverflow");
        wpf.Should().Contain("TextLayoutPlanner.ApplyAutoFitPlan");
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
        avalonia.Should().Contain("TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>");
        avalonia.Should().Contain("TextLayoutPlanner.PlanMeasuredColumns<FormattedText>");
        avalonia.Should().Contain("TextLayoutPlanner.PlanMeasuredContinuousColumnFlow<FormattedText>");
        avalonia.Should().Contain("TextLayoutPlanner.PlanNormalAutoFitOverflow");
        avalonia.Should().Contain("TextLayoutPlanner.ApplyAutoFitPlan");
        wpf.Should().Contain("DrawTabLeaderWpf");
        avalonia.Should().Contain("DrawTabLeaderAvalonia");
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
    public void PlanMeasuredBodyText_OwnsAutoFitRemeasurementArtifactsAndRenderRoutes()
    {
        var requests = new List<TextParagraphMeasurementRequest>();
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Normal,
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[] { new ResolvedRun { Text = "Left\tRight", FontSizePt = 20 } }
                },
                new ResolvedParagraph()
            }
        };

        var plan = TextLayoutPlanner.PlanMeasuredBodyText(
            text,
            new LayoutRect(0, 0, 100, 60),
            request =>
            {
                requests.Add(request);
                return new TextNativeMeasurement<string>(
                    $"{request.ParagraphIndex}:{request.Paragraph.Runs[0].FontSizePt:F1}",
                    HeightDip: 120);
            });

        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(request => !request.UseIdealMetrics);
        plan.AutoFit.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        plan.RenderText.Paragraphs[0].Runs[0].FontSizePt.Should().Be(12);
        plan.Artifacts.Should().ContainKey(0).WhoseValue.Should().Be("0:12.0");
        plan.Artifacts.Should().NotContainKey(1);
        plan.Layout.Paragraphs.Should().ContainSingle();
        plan.Layout.Paragraphs[0].RenderRoute.Should().Be(TextParagraphRenderRoute.Tabs);
    }

    [Fact]
    public void PlanMeasuredColumns_OwnsColumnCapacityRemeasurementAndFinalArtifacts()
    {
        var requests = new List<TextParagraphMeasurementRequest>();
        var text = new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Normal,
            ColumnCount = 2,
            ColumnSpacingDip = 10,
            InsetLeftDip = 0,
            InsetRightDip = 0,
            InsetTopDip = 0,
            InsetBottomDip = 0,
            Paragraphs = new[]
            {
                new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun { Text = "Baseline", FontSizePt = 20, BaselineOffset = 1000 }
                    }
                }
            }
        };

        var plan = TextLayoutPlanner.PlanMeasuredColumns(
            text,
            new LayoutRect(0, 0, 210, 50),
            request =>
            {
                requests.Add(request);
                return new TextNativeMeasurement<string>(
                    request.Paragraph.Runs[0].FontSizePt.ToString("F1", CultureInfo.InvariantCulture),
                    HeightDip: 160);
            });

        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(request =>
            Math.Abs(request.MaxWidthDip - 100) < 0.001 && !request.UseIdealMetrics);
        plan.AutoFit.Mode.Should().Be(TextAutoFitOverflowMode.RuntimeShrink);
        plan.Artifacts[0].Should().Be("12.5");
        plan.Layout.Paragraphs[0].RenderRoute.Should().Be(TextParagraphRenderRoute.Baseline);
    }

    [Fact]
    public void PlanMeasuredContinuousColumnFlow_OwnsSplitMeasureAndPlacementLifecycle()
    {
        var phases = new List<TextColumnMeasurementPhase>();
        var text = new ResolvedTextLayout
        {
            ColumnCount = 2,
            ColumnSpacingDip = 10,
            Wrap = true,
            InsetLeftDip = 0,
            InsetRightDip = 0,
            InsetTopDip = 0,
            InsetBottomDip = 0,
            Paragraphs = new[] { Paragraph("one two three") }
        };

        var plan = TextLayoutPlanner.PlanMeasuredContinuousColumnFlow(
            text,
            new LayoutRect(0, 0, 80, 20),
            request =>
            {
                phases.Add(request.Phase);
                var value = request.Paragraph.Runs[0].Text;
                return new TextNativeMeasurement<string>(
                    $"{request.Phase}:{value}",
                    HeightDip: 12,
                    WidthDip: value.Length * 10);
            },
            _ => 0.5);

        plan.IsApplicable.Should().BeTrue();
        plan.Lines.Should().HaveCount(2);
        plan.Lines.Select(line => line.Paragraph.Runs[0].Text)
            .Should().Equal("one two", "three");
        plan.Lines.Select(line => line.Placement.ColumnIndex).Should().Equal(0, 1);
        plan.Lines.Should().OnlyContain(line =>
            line.HorizontalScale == 0.5 &&
            line.Artifact.StartsWith("Render:", StringComparison.Ordinal));
        phases.Should().Contain(TextColumnMeasurementPhase.WrapProbe);
        phases.Count(phase => phase == TextColumnMeasurementPhase.LineLayout).Should().Be(2);
        phases.Count(phase => phase == TextColumnMeasurementPhase.Render).Should().Be(2);
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_DelegateMeasuredTextOrchestrationAndKeepNativeDrawing()
    {
        var sources = new[]
        {
            ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs"),
            ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs")
        };

        foreach (var source in sources)
        {
            source.Should().Contain("TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>");
            source.Should().Contain("TextLayoutPlanner.PlanMeasuredColumns<FormattedText>");
            source.Should().Contain("TextLayoutPlanner.PlanMeasuredContinuousColumnFlow<FormattedText>");
            source.Should().Contain("switch (placement.RenderRoute)");
            source.Should().Contain("TextLayoutPlanner.PlanBaselineLines");
            source.Should().NotContain("TextLayoutPlanner.SplitColumnText(");
            source.Should().NotContain("TextLayoutPlanner.CloneParagraphWithText(");
            source.Should().NotContain("private static IReadOnlyList<string> SplitColumnText");
            source.Should().NotContain("private static ResolvedParagraph CloneParagraphWithText");
            source.Should().NotContain("private static List<BaselineLine> BuildBaselineLines");
            source.Should().NotContain("private sealed class BaselineLine");
        }
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_DelegateInlineBaselinePlacementAndKeepNativeRendering()
    {
        var renderers = new[]
        {
            (
                Source: ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs"),
                DrawMathOp: "DrawMathOpWpf"),
            (
                Source: ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs"),
                DrawMathOp: "DrawMathOpAvalonia"),
        };

        foreach (var renderer in renderers)
        {
            renderer.Source.Should().Contain("TextLayoutPlanner.PlanInlineBaselineLine");
            renderer.Source.Should().Contain(
                "new TextInlineRunMeasure(metrics.Width, metrics.Ascent, metrics.Height)");
            renderer.Source.Should().Contain("BuildSingleRunFormattedTextAt(");
            renderer.Source.Should().Contain("MathBoxRenderPlanner.Plan(");
            renderer.Source.Should().Contain(renderer.DrawMathOp);
            renderer.Source.Should().NotContain("internal static double ComputeBaselineY");
            renderer.Source.Should().NotContain("internal static double ComputeRunTopY");
            renderer.Source.Should().NotContain("lineAscent = Math.Max(lineAscent");
        }
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
        wpf.Should().Contain("TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>");
        wpf.Should().NotContain("InsetLeftPt");
        wpf.Should().NotContain("InsetTopPt");
        wpf.Should().NotContain("InsetRightPt");
        wpf.Should().NotContain("InsetBottomPt");

        avalonia.Should().Contain("TextLayoutPlanner.GetTextArea");
        avalonia.Should().Contain("TextLayoutPlanner.PlanMeasuredBodyText<FormattedText>");
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

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
