using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for Wave 5A additions to <see cref="EditingSession"/>:
/// clipboard, theme, slide size, insert table/chart, format painter.
/// </summary>
public sealed class EditingSession5ATests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static EditingSession Make(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    private static SlideShape MakeShape(uint id, bool withText = false)
    {
        var shape = new SlideShape
        {
            Id          = id,
            Name        = $"S{id}",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 100_000L * id,
            OffsetYEmu  = 100_000L * id,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 300_000,
        };
        if (withText)
        {
            var tb   = new TextBody();
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = "Hello", FontFamily = "Arial", FontSizePt = 12, Bold = true, Italic = false });
            tb.Paragraphs.Add(para);
            shape.TextBody = tb;
        }
        return shape;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // CLIPBOARD — SHAPES
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CopySelectedShapes_CanPasteBecomesTrue()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.CanPaste.Should().BeTrue();
    }

    [Fact]
    public void PasteShapes_AddsCloneWithFreshId()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();

        var slide = sess.CurrentSlide!;
        slide.Shapes.Should().HaveCount(2);
        var pasted = slide.Shapes[1];
        pasted.Id.Should().NotBe(1u, "paste must assign a new Id");
    }

    [Fact]
    public void PasteShapes_AppliesPositionOffset()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();

        var pasted = sess.CurrentSlide!.Shapes[1];
        pasted.OffsetXEmu.Should().BeGreaterThan(shape.OffsetXEmu, "pasted shape should be offset");
        pasted.OffsetYEmu.Should().BeGreaterThan(shape.OffsetYEmu);
    }

    [Fact]
    public void PasteShapes_OriginalIsUntouched()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        long origX = shape.OffsetXEmu;
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();

        shape.OffsetXEmu.Should().Be(origX, "original shape must not be moved");
        shape.Id.Should().Be(1u, "original shape Id must not change");
    }

    [Fact]
    public void PasteShapes_SelectsNewlyPastedShapes()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();

        sess.SelectedShapeIds.Should().HaveCount(1);
        sess.SelectedShapeIds[0].Should().NotBe(1u);
    }

    [Fact]
    public void PasteShapes_IsUndoable()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();
        sess.Undo();

        sess.CurrentSlide!.Shapes.Should().HaveCount(1, "undo should remove the pasted shape");
    }

    [Fact]
    public void CutSelectedShapes_RemovesOriginalAndAllowsPaste()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CutSelectedShapes();

        sess.CurrentSlide!.Shapes.Should().BeEmpty("cut removes the original");
        sess.CanPaste.Should().BeTrue();
    }

    [Fact]
    public void CutAndPaste_RestoresShapeElsewhere()
    {
        var sess = Make(2);
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CutSelectedShapes();
        // Navigate to slide 2 and paste there.
        sess.SelectSlide(1);
        sess.PasteShapes();

        sess.CurrentSlide!.Shapes.Should().HaveCount(1);
        sess.Presentation.Slides[0].Shapes.Should().BeEmpty();
    }

    [Fact]
    public void PasteShapes_RepeatedPasteProducesIndependentCopies()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();
        sess.PasteShapes();

        sess.CurrentSlide!.Shapes.Should().HaveCount(3);
        // All three ids must be distinct.
        sess.CurrentSlide!.Shapes.Select(s => s.Id).Distinct().Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // CLIPBOARD — SLIDES
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PasteShapes_RemapGroupedChildIdsAndInternalConnectorTargets()
    {
        var sess = Make();
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        var child = MakeShape(2);
        var connector = new SlideShape
        {
            Id = 3,
            Kind = SlideShapeKind.Connector,
            ConnectionStart = new ConnectorAttachment { ShapeId = child.Id, SiteIndex = 1 },
        };
        group.Children.Add(child);
        group.Children.Add(connector);
        sess.CurrentSlide!.Shapes.Add(group);
        sess.Select(group.Id);

        sess.CopySelectedShapes();
        sess.PasteShapes();

        var pasted = sess.CurrentSlide.Shapes[1];
        pasted.Kind.Should().Be(SlideShapeKind.Group);
        pasted.Id.Should().NotBe(group.Id);
        var pastedChild = pasted.Children[0];
        var pastedConnector = pasted.Children[1];
        pastedChild.Id.Should().NotBe(child.Id);
        pastedConnector.Id.Should().NotBe(connector.Id);
        pastedConnector.ConnectionStart.Should().NotBeNull();
        pastedConnector.ConnectionStart!.ShapeId.Should().Be(pastedChild.Id);

        sess.CurrentSlide.Shapes.SelectMany(Enumerate)
            .Select(shape => shape.Id)
            .Should().OnlyHaveUniqueItems();
    }

    private static IEnumerable<SlideShape> Enumerate(SlideShape shape)
    {
        yield return shape;
        foreach (var child in shape.Children)
        {
            foreach (var nested in Enumerate(child))
                yield return nested;
        }
    }

    [Fact]
    public void CopyCurrentSlide_CanPasteBecomesTrue()
    {
        var sess = Make();
        sess.CopyCurrentSlide();
        sess.CanPaste.Should().BeTrue();
    }

    [Fact]
    public void PasteSlide_InsertsCloneAfterCurrent()
    {
        var sess = Make();
        var original = sess.CurrentSlide!;
        sess.CopyCurrentSlide();
        sess.PasteSlide();

        sess.Presentation.Slides.Should().HaveCount(2);
        sess.CurrentSlideIndex.Should().Be(1);
        sess.CurrentSlide.Should().NotBeSameAs(original, "pasted slide must be a different object");
    }

    [Fact]
    public void PasteSlide_CopiedSlideIsDeepClone()
    {
        var sess = Make();
        sess.CurrentSlide!.Shapes.Add(MakeShape(1));
        sess.CopyCurrentSlide();
        sess.PasteSlide();

        var clone = sess.Presentation.Slides[1];
        clone.Shapes.Should().HaveCount(1);
        clone.Shapes[0].Should().NotBeSameAs(sess.Presentation.Slides[0].Shapes[0]);
    }

    [Fact]
    public void PasteSlide_IsUndoable()
    {
        var sess = Make();
        sess.CopyCurrentSlide();
        sess.PasteSlide();
        sess.Undo();

        sess.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void CutCurrentSlide_RemovesAndAllowsPaste()
    {
        var sess = Make(2);
        sess.CutCurrentSlide();

        sess.Presentation.Slides.Should().HaveCount(1);
        sess.CanPaste.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // UNIFIED PASTE
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Paste_UsesShapeClipboardWhenNonEmpty()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.Paste();

        sess.CurrentSlide!.Shapes.Should().HaveCount(2);
    }

    [Fact]
    public void Paste_UsesSlideClipboardWhenShapeClipboardEmpty()
    {
        var sess = Make();
        sess.CopyCurrentSlide();
        sess.Paste();

        sess.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void CanPaste_FalseInitially()
    {
        var sess = Make();
        sess.CanPaste.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // THEME
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetTheme_SwapsTheme()
    {
        var sess      = Make();
        var newTheme  = BuiltInThemes.GetById(BuiltInThemes.Id.Berlin)!;
        sess.SetTheme(newTheme);

        sess.Presentation.Theme.Should().BeSameAs(newTheme);
    }

    [Fact]
    public void SetTheme_IsUndoable()
    {
        var sess      = Make();
        var original  = sess.Presentation.Theme;
        var newTheme  = BuiltInThemes.GetById(BuiltInThemes.Id.Facet)!;
        sess.SetTheme(newTheme);
        sess.Undo();

        sess.Presentation.Theme.Should().BeSameAs(original);
    }

    [Fact]
    public void SetTheme_ByStringId_Works()
    {
        var sess = Make();
        sess.SetTheme(BuiltInThemes.Id.Ion);
        sess.Presentation.Theme.Name.Should().Be("Ion");
    }

    [Fact]
    public void SetTheme_SchemeColorResolvesToNewTheme()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        // Give the shape a scheme-color fill.
        var schemeRef  = new SchemeColorRef { Slot = ThemeColorSlot.Accent1 };
        var themeColor = new ThemeAwareColor(SrgbColor.Black, schemeRef);
        shape.Fill = new ShapeFill.Solid(themeColor);
        sess.CurrentSlide!.Shapes.Add(shape);

        var newTheme = BuiltInThemes.GetById(BuiltInThemes.Id.Berlin)!;
        sess.SetTheme(newTheme);

        // The resolved Accent1 under Berlin should differ from the Office default.
        var expected = newTheme.ColorScheme[ThemeColorSlot.Accent1];
        var resolved = ThemeColorResolver.Resolve(themeColor, sess.Presentation.Theme);
        resolved.Should().Be(expected);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SLIDE SIZE
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetSlideSize16x9_UpdatesDimensions()
    {
        var sess = Make();
        sess.SetSlideSize16x9();
        sess.Presentation.SlideSizeCxEmu.Should().Be(12192000L);
        sess.Presentation.SlideSizeCyEmu.Should().Be(6858000L);
    }

    [Fact]
    public void SetSlideSize4x3_UpdatesDimensions()
    {
        var sess = Make();
        sess.SetSlideSize4x3();
        sess.Presentation.SlideSizeCxEmu.Should().Be(9144000L);
        sess.Presentation.SlideSizeCyEmu.Should().Be(6858000L);
    }

    [Fact]
    public void SetSlideSizeCustom_UpdatesDimensions()
    {
        var sess = Make();
        sess.SetSlideSizeCustom(11_000_000L, 5_500_000L);
        sess.Presentation.SlideSizeCxEmu.Should().Be(11_000_000L);
        sess.Presentation.SlideSizeCyEmu.Should().Be(5_500_000L);
    }

    [Fact]
    public void SetSlideSize_IsUndoable()
    {
        var sess  = Make();
        long oldCx = sess.Presentation.SlideSizeCxEmu;
        long oldCy = sess.Presentation.SlideSizeCyEmu;
        sess.SetSlideSize4x3();
        sess.Undo();
        sess.Presentation.SlideSizeCxEmu.Should().Be(oldCx);
        sess.Presentation.SlideSizeCyEmu.Should().Be(oldCy);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // INSERT TABLE
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertTable_AddsShapeOfKindTable()
    {
        var sess  = Make();
        var shape = sess.InsertTable(3, 4);
        shape.Kind.Should().Be(SlideShapeKind.Table);
    }

    [Fact]
    public void InsertTable_CorrectRowAndColumnCount()
    {
        var sess  = Make();
        var shape = sess.InsertTable(3, 4);
        shape.Table!.Rows.Should().HaveCount(3);
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(4);
    }

    [Fact]
    public void InsertTable_EachRowHasCorrectCellCount()
    {
        var sess  = Make();
        var shape = sess.InsertTable(2, 3);
        foreach (var row in shape.Table!.Rows)
            row.Cells.Should().HaveCount(3);
    }

    [Fact]
    public void InsertTable_ShapeIsOnCurrentSlide()
    {
        var sess  = Make();
        var shape = sess.InsertTable(2, 2);
        sess.CurrentSlide!.Shapes.Should().Contain(shape);
    }

    [Fact]
    public void InsertTable_IsUndoable()
    {
        var sess = Make();
        sess.InsertTable(2, 2);
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // INSERT CHART
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertChart_AddsShapeOfKindChart()
    {
        var sess  = Make();
        var shape = sess.InsertChart();
        shape.Kind.Should().Be(SlideShapeKind.Chart);
    }

    [Fact]
    public void InsertChart_HasExpectedChartType()
    {
        var sess  = Make();
        var shape = sess.InsertChart(ChartType.Pie);
        shape.Chart!.ChartType.Should().Be(ChartType.Pie);
    }

    [Fact]
    public void InsertChart_HasSampleData()
    {
        var sess  = Make();
        var shape = sess.InsertChart();
        shape.Chart!.Categories.Should().NotBeEmpty();
        shape.Chart!.Series.Should().NotBeEmpty();
    }

    [Fact]
    public void InsertChart_StockHasEditableOhlcSampleData()
    {
        var sess = Make();
        var shape = sess.InsertChart(ChartType.Stock);

        shape.Chart!.Categories.Should().Equal("Day 1", "Day 2", "Day 3");
        shape.Chart.Series.Select(series => series.Name)
            .Should().Equal("Open", "High", "Low", "Close");
        shape.Chart.Series[1].Values.Should().Equal(14, 16, 15);
        shape.Chart.Series[2].Values.Should().Equal(8, 9, 10);
        ChartRenderPlanner.BuildStockPrimitivePlan(
                shape.Chart,
                new ChartPlanRect(0, 0, 320, 180))
            .HighLowLines.Should().HaveCount(3);
    }

    [Fact]
    public void InsertChart_FunnelHasEditableStages()
    {
        var sess = Make();
        var shape = sess.InsertChart(ChartType.Funnel);

        shape.Chart!.Categories.Should().Equal("Awareness", "Interest", "Consideration", "Conversion");
        shape.Chart.Series.Should().ContainSingle();
        shape.Chart.Series[0].Name.Should().Be("Value");
        shape.Chart.Series[0].Values.Should().Equal(100, 68, 42, 18);
    }

    [Fact]
    public void InsertChart_WaterfallHasEditableIncrements()
    {
        var sess = Make();
        var shape = sess.InsertChart(ChartType.Waterfall);

        shape.Chart!.Categories.Should().Equal("Starting value", "Reduction", "Growth", "Ending value");
        shape.Chart.Series.Should().ContainSingle();
        shape.Chart.Series[0].Values.Should().Equal(100, -30, 20, 90);
    }

    [Fact]
    public void InsertChart_IsUndoable()
    {
        var sess = Make();
        sess.InsertChart();
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // FORMAT PAINTER
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CopyFormatting_SetsHasFormatClipboard()
    {
        var sess   = Make();
        var source = MakeShape(1, withText: true);
        source.Fill = ShapeFill.None.Instance;
        sess.CurrentSlide!.Shapes.Add(source);
        sess.Select(1u);
        sess.CopyFormatting();

        sess.HasFormatClipboard.Should().BeTrue();
    }

    [Fact]
    public void ApplyFormattingToSelection_CopiesFillToTarget()
    {
        var sess   = Make();
        var source = MakeShape(1);
        source.Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)));
        var target = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.CopyFormatting();
        sess.Select(2u);
        sess.ApplyFormattingToSelection();

        target.Fill.Should().BeSameAs(source.Fill);
    }

    [Fact]
    public void ApplyFormattingToSelection_CopiesFontFamilyToTarget()
    {
        var sess   = Make();
        var source = MakeShape(1, withText: true); // font "Arial"
        var target = MakeShape(2, withText: true);
        target.TextBody!.Paragraphs[0].Runs[0].FontFamily = "Times New Roman";

        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.CopyFormatting();
        sess.Select(2u);
        sess.ApplyFormattingToSelection();

        target.TextBody!.Paragraphs[0].Runs[0].FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void ApplyFormattingToSelection_IsUndoable()
    {
        var sess   = Make();
        var source = MakeShape(1, withText: true);
        source.Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)));
        var target = MakeShape(2);
        var origFill = target.Fill;
        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.CopyFormatting();
        sess.Select(2u);
        sess.ApplyFormattingToSelection();
        sess.Undo();

        target.Fill.Should().BeSameAs(origFill, "undo should restore original fill");
    }

    [Fact]
    public void ApplyFormattingToSelection_NoOp_WhenNoFormatClipboard()
    {
        var sess   = Make();
        var target = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(target);
        sess.Select(1u);

        var act = () => sess.ApplyFormattingToSelection();
        act.Should().NotThrow();
        sess.Bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void BeginFormatPainter_ThenTargetShape_AppliesOnceAndSelectsTarget()
    {
        var sess = Make();
        var source = MakeShape(1, withText: true);
        source.Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x336699)));
        var target = MakeShape(2, withText: true);
        target.TextBody!.Paragraphs[0].Runs[0].FontFamily = "Times New Roman";
        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.BeginFormatPainter().Should().BeTrue();
        sess.IsFormatPainterActive.Should().BeTrue();
        sess.TryApplyFormatPainterToShape(2u).Should().BeTrue();

        target.Fill.Should().BeSameAs(source.Fill);
        target.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().Be("Arial");
        sess.SelectedShapeIds.Should().Equal(2u);
        sess.IsFormatPainterActive.Should().BeFalse();
    }

    [Fact]
    public void FormatPainterTarget_IsUndoableWithoutChangingSource()
    {
        var sess = Make();
        var source = MakeShape(1);
        source.Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x336699)));
        var target = MakeShape(2);
        var originalSourceFill = source.Fill;
        var originalTargetFill = target.Fill;
        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.BeginFormatPainter().Should().BeTrue();
        sess.TryApplyFormatPainterToShape(2u).Should().BeTrue();
        sess.Undo();

        target.Fill.Should().BeSameAs(originalTargetFill);
        source.Fill.Should().BeSameAs(originalSourceFill);
    }

    [Fact]
    public void CancelFormatPainter_LeavesSourceAndTargetUnchanged()
    {
        var sess = Make();
        var source = MakeShape(1, withText: true);
        var target = MakeShape(2);
        var originalSourceFill = source.Fill;
        var originalTargetFill = target.Fill;
        sess.CurrentSlide!.Shapes.AddRange([source, target]);

        sess.Select(1u);
        sess.BeginFormatPainter().Should().BeTrue();
        sess.CancelFormatPainter();

        sess.IsFormatPainterActive.Should().BeFalse();
        sess.TryApplyFormatPainterToShape(2u).Should().BeFalse();
        source.Fill.Should().BeSameAs(originalSourceFill);
        target.Fill.Should().BeSameAs(originalTargetFill);
    }

    [Fact]
    public void FormatPainter_CanCopyBetweenNestedGroupChildrenAndUndo()
    {
        var sess = Make();
        var source = MakeShape(11, withText: true);
        source.Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x336699)));
        var target = MakeShape(12, withText: true);
        target.TextBody!.Paragraphs[0].Runs[0].FontFamily = "Times New Roman";
        var sourceGroup = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        sourceGroup.Children.Add(source);
        var targetGroup = new SlideShape { Id = 2, Kind = SlideShapeKind.Group };
        targetGroup.Children.Add(target);
        sess.CurrentSlide!.Shapes.AddRange([sourceGroup, targetGroup]);

        sess.Select(11u);
        sess.BeginFormatPainter().Should().BeTrue();
        sess.TryApplyFormatPainterToShape(12u).Should().BeTrue();
        target.Fill.Should().BeSameAs(source.Fill);
        target.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().Be("Arial");

        sess.Undo();
        target.Fill.Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // BUILT-IN THEMES CATALOGUE
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuiltInThemes_GetAll_ReturnsFiveEntries()
    {
        BuiltInThemes.GetAll().Should().HaveCount(5);
    }

    [Fact]
    public void BuiltInThemes_GetById_ReturnsCorrectTheme()
    {
        var theme = BuiltInThemes.GetById(BuiltInThemes.Id.Slice);
        theme.Should().NotBeNull();
        theme!.Name.Should().Be("Slice");
    }

    [Fact]
    public void BuiltInThemes_GetById_UnknownId_ReturnsNull()
    {
        BuiltInThemes.GetById("DoesNotExist").Should().BeNull();
    }

    [Fact]
    public void BuiltInThemes_AllEntriesHave12ColorSlots()
    {
        foreach (var entry in BuiltInThemes.GetAll())
        {
            foreach (ThemeColorSlot slot in Enum.GetValues<ThemeColorSlot>())
            {
                // Should not throw and should return some color.
                var c = entry.Theme.ColorScheme[slot];
                (c.R + c.G + c.B).Should().BeGreaterThanOrEqualTo(0);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SET FONT FAMILY
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetFontFamilyOnSelection_SetsRunFontFamily()
    {
        var sess  = Make();
        var shape = MakeShape(1, withText: true);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.SetFontFamilyOnSelection("Verdana");

        shape.TextBody!.Paragraphs[0].Runs[0].FontFamily.Should().Be("Verdana");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // UNDO BYTE-BUDGET — PASTE / OLE (r140 remediation 2: the estimator was blind to
    // pasted shapes, OLE objects, SmartArt pictures, and chart picture fills)
    // ════════════════════════════════════════════════════════════════════════════════

    private static SlideShape MakePictureShape(uint id, int imageBytes) => new()
    {
        Id          = id,
        Name        = $"Picture{id}",
        Kind        = SlideShapeKind.Picture,
        OffsetXEmu  = 0,
        OffsetYEmu  = 0,
        ExtentCxEmu = 1_000_000,
        ExtentCyEmu = 1_000_000,
        Picture     = new ImagePart { Bytes = new byte[imageBytes], ContentType = "image/png" },
    };

    /// <summary>
    /// The literal Ctrl+V path: <c>EditingSession.CopySelectedShapes</c> then
    /// <c>EditingSession.Paste</c> (which dispatches to <c>PasteShapes</c> ->
    /// <c>PasteShapeCopies</c> -> <c>Bus.Execute(new PasteShapesCommand(...))</c>), driven
    /// through the real session with the real <see cref="PresentationCommandBus"/> -- no direct
    /// command construction. Before the fix, <c>PasteShapesCommand.EstimatedBytes</c> was
    /// <c>256 + _shapes.Count * 512</c>, a flat heuristic blind to the pasted shapes' actual
    /// picture content, so pasting a handful of image-heavy shapes repeatedly never tripped the
    /// 50MB undo byte budget.
    /// </summary>
    [Fact]
    public void Paste_LargeImagePayload_TriggersByteBudgetEviction_ViaRealEditingSessionEntryPoint()
    {
        const int pasteCount = 8;
        const int imageBytesPerPaste = 8 * 1024 * 1024; // 8 x 8MB = 64MB > 50MB budget.

        var sess = Make();
        sess.CurrentSlide!.Shapes.Add(MakePictureShape(1, imageBytesPerPaste));
        sess.Select(1u);
        sess.CopySelectedShapes();

        for (var i = 0; i < pasteCount; i++)
            sess.Paste();

        sess.CurrentSlide!.Shapes.Should().HaveCount(1 + pasteCount);

        var undone = 0;
        while (sess.CanUndo)
        {
            sess.Undo();
            undone++;
        }

        undone.Should().BeLessThan(pasteCount,
            "64MB of pasted picture bytes across eight real Paste() calls should have tripped the " +
            "50MB undo byte budget; before the fix PasteShapesCommand always reported a flat " +
            "256 + count*512 estimate regardless of the pasted shapes' picture content, so every " +
            "paste stayed undoable");
    }

    /// <summary>
    /// The real Insert &gt; Object path: <c>EditingSession.InsertEmbeddedObject</c> builds an
    /// OLE shape (<c>Kind == Ole</c>, <c>OleObject.EmbeddedBytes</c> holding the raw embedded
    /// file) and adds it via the shared <c>AddShape</c> -&gt; <c>Bus.Execute(new
    /// AddShapeCommand(...))</c> path. <c>AddShapeCommand</c> itself was already fixed to use
    /// <c>PresentationCommandSizeEstimator.EstimateBytes(SlideShape)</c>, but that SHARED helper
    /// never read <c>SlideShape.OleObject</c> at all, so the fix didn't actually cover this case
    /// until the estimator itself was extended.
    /// </summary>
    [Fact]
    public void InsertEmbeddedObject_LargeEmbeddedPayload_TriggersByteBudgetEviction_ViaRealEditingSessionEntryPoint()
    {
        const int insertCount = 8;
        const int embeddedBytesPerObject = 8 * 1024 * 1024; // 8 x 8MB = 64MB > 50MB budget.

        var sess = Make();

        for (var i = 0; i < insertCount; i++)
            sess.InsertEmbeddedObject(new byte[embeddedBytesPerObject], $"Workbook{i}.xlsx");

        sess.CurrentSlide!.Shapes.Should().HaveCount(insertCount);

        var undone = 0;
        while (sess.CanUndo)
        {
            sess.Undo();
            undone++;
        }

        undone.Should().BeLessThan(insertCount,
            "64MB of embedded OLE object bytes across eight real InsertEmbeddedObject() calls " +
            "(the Insert > Object path) should have tripped the 50MB undo byte budget; before the " +
            "fix the shared PresentationCommandSizeEstimator never read SlideShape.OleObject at " +
            "all, so every embedded object stayed undoable no matter how large");
    }
}
