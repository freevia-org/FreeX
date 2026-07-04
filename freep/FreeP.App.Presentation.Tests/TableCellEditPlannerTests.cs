using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class TableCellEditPlannerTests
{
    private const double EmuPerDip = 9525.0;

    [Fact]
    public void PlanSelectedCell_NonTableSelection_DisablesCellEditing()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });

        var state = TableCellEditPlanner.PlanSelectedCell(slide, [7], (0, 0));

        state.HasSelectedTable.Should().BeFalse();
        state.CanEditText.Should().BeFalse();
        state.CanFormatText.Should().BeFalse();
    }

    [Fact]
    public void PlanSelectedCell_ContinuationCell_NormalizesToMergeAnchor()
    {
        var shape = MakeMergedTableShape();
        var state = TableCellEditPlanner.PlanSelectedCell(
            new Slide { Shapes = { shape } },
            [shape.Id],
            (0, 1));

        state.ShapeId.Should().Be(shape.Id);
        state.Row.Should().Be(0);
        state.Col.Should().Be(0);
        state.CanEditText.Should().BeTrue();
        state.CanSplitCell.Should().BeTrue();
        state.CanMergeWithRight.Should().BeTrue();
    }

    [Fact]
    public void BeginEdit_ContinuationCell_ReturnsAnchorPlacementAndEditPlanner()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = MakeMergedTableShape();
        slide.Shapes.Add(shape);
        var transform = new SlideTransformCore(2, 10, 20, 960, 540);

        var plan = TableCellEditPlanner.BeginEdit(
            slideIndex: 0,
            slide,
            shape.Id,
            row: 0,
            col: 1,
            transform,
            minimumWidth: 30,
            minimumHeight: 18);

        plan.Status.Should().Be(TableCellEditStartStatus.Ready);
        plan.Row.Should().Be(0);
        plan.Col.Should().Be(0);
        plan.Cell.Should().BeSameAs(shape.Table!.Rows[0].Cells[0]);
        plan.CellRect.Should().NotBeNull();
        plan.Placement.Should().Be(new InCanvasEditorPlacement(10, 20, 384, 96));
        plan.InitialSelection.Should().Be(new InCanvasEditorTextSelection(0, "Anchor".Length));
        plan.OriginalBody!.Paragraphs[0].Runs[0].Text.Should().Be("Anchor");
        plan.RichTextPlan.Should().NotBeNull();
        plan.RichTextPlan!.PlainText.Should().Be("Anchor");
        plan.EditPlanner.Should().NotBeNull();
    }

    [Fact]
    public void BeginEdit_MixedRichRuns_ReturnsRendererNeutralRichTextPlan()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "Hello",
            FontFamily = "Aptos",
            FontSizePt = 14,
        });
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "World",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
            Italic = true,
            ItalicSet = true,
            Underline = true,
            Color = new ThemeAwareColor(new SrgbColor(0x22, 0x44, 0x66)),
        });
        slide.Shapes.Add(shape);

        var plan = TableCellEditPlanner.BeginEdit(
            0,
            slide,
            shape.Id,
            0,
            0,
            SlideTransformCore.Identity,
            30,
            18);

        var rich = plan.RichTextPlan!;
        rich.PlainText.Should().Be("HelloWorld");
        rich.HasRichFormatting.Should().BeTrue();
        rich.HasMixedFormatting.Should().BeTrue();
        rich.Runs.Should().HaveCount(2);
        rich.Runs[0].Should().Match<InCanvasEditorRunStyle>(run =>
            run.Start == 0 &&
            run.End == 5 &&
            run.Text == "Hello" &&
            run.FontFamily == "Aptos" &&
            run.FontSizePt == 14 &&
            !run.Bold);
        rich.Runs[1].Should().Match<InCanvasEditorRunStyle>(run =>
            run.Start == 5 &&
            run.End == 10 &&
            run.Text == "World" &&
            run.FontFamily == "Consolas" &&
            run.FontSizePt == 18 &&
            run.Bold &&
            run.Italic &&
            run.Underline);
        rich.SuggestedEditorStyle.FontFamily.Should().Be("Aptos");
        rich.SuggestedEditorStyle.FontSizePt.Should().Be(14);
        rich.SuggestedEditorStyle.Bold.Should().BeFalse();
        rich.InitialSelectionStyle.FontFamily.Should().BeNull("whole-cell selection spans mixed run families");
        rich.InitialSelectionStyle.Bold.Should().BeNull("whole-cell selection spans mixed bold state");
    }

    [Fact]
    public void PlanRichTextEdit_MultiParagraphRuns_OffsetsIncludeNewlineSeparator()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "Beta",
            FontFamily = "Aptos",
            FontSizePt = 14,
            Bold = true,
            BoldSet = true,
        });
        var second = new Paragraph();
        second.Runs.Add(new Run
        {
            Text = "Gamma",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Italic = true,
            ItalicSet = true,
        });
        body.Paragraphs.Add(second);

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(10, 15));

        rich.PlainText.Should().Be("AlphaBeta\nGamma");
        rich.Runs.Select(run => (run.ParagraphIndex, run.RunIndex, run.Start, run.End, run.Text))
            .Should()
            .Equal(
                (0, 0, 0, 5, "Alpha"),
                (0, 1, 5, 9, "Beta"),
                (1, 0, 10, 15, "Gamma"));
        rich.InitialSelectionStyle.FontFamily.Should().Be("Consolas");
        rich.InitialSelectionStyle.FontSizePt.Should().Be(18);
        rich.InitialSelectionStyle.Italic.Should().BeTrue();
        rich.InitialSelectionStyle.Bold.Should().BeFalse();
    }

    [Fact]
    public void PlanRichTextEdit_SelectionAcrossParagraphBoundary_ReportsMixedSelectionStyle()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "Beta",
            FontFamily = "Aptos",
            FontSizePt = 14,
            Bold = true,
            BoldSet = true,
        });
        var second = new Paragraph();
        second.Runs.Add(new Run
        {
            Text = "Gamma",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Italic = true,
            ItalicSet = true,
        });
        body.Paragraphs.Add(second);

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(7, 12));

        rich.InitialSelectionStyle.FontFamily.Should().BeNull("selection overlaps runs on both sides of the paragraph separator");
        rich.InitialSelectionStyle.FontSizePt.Should().BeNull();
        rich.InitialSelectionStyle.Bold.Should().BeNull();
        rich.InitialSelectionStyle.Italic.Should().BeNull();
    }

    [Fact]
    public void PlanRichTextEdit_SubRangeSelection_ReportsExplicitSelectedRunRanges()
    {
        var body = MakeBody("one ");
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "two",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Italic = true,
            ItalicSet = true,
        });
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = " three",
            FontFamily = "Aptos",
            FontSizePt = 14,
            Bold = true,
            BoldSet = true,
        });

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(2, 9));

        rich.Selection.Should().Be(new InCanvasEditorTextSelection(2, 9));
        rich.SelectedRunRanges
            .Select(range => (range.ParagraphIndex, range.RunIndex, range.SelectionStart, range.SelectionEnd, range.Text))
            .Should()
            .Equal(
                (0, 0, 2, 4, "e "),
                (0, 1, 4, 7, "two"),
                (0, 2, 7, 9, " t"));
        rich.InitialSelectionStyle.FontFamily.Should().BeNull();
        rich.InitialSelectionStyle.Italic.Should().BeNull();
    }

    [Fact]
    public void PlanRichTextEdit_CollapsedSelection_UsesCaretRunStyle()
    {
        var body = MakeBody("Hello");
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "World",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
        });

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(7, 7));

        rich.InitialSelectionStyle.FontFamily.Should().Be("Consolas");
        rich.InitialSelectionStyle.FontSizePt.Should().Be(18);
        rich.InitialSelectionStyle.Bold.Should().BeTrue();
        rich.SuggestedEditorStyle.FontFamily.Should().Be("Aptos");
        rich.SuggestedEditorStyle.FontSizePt.Should().Be(14);
        rich.SuggestedEditorStyle.Bold.Should().BeFalse();
    }

    [Fact]
    public void PlanRichTextEdit_CollapsedSelectionAtRunBoundary_UsesPrecedingRunStyle()
    {
        var body = MakeBody("Left");
        body.Paragraphs[0].Runs[0].FontFamily = "Aptos";
        body.Paragraphs[0].Runs[0].FontSizePt = 14;
        body.Paragraphs[0].Runs[0].Bold = true;
        body.Paragraphs[0].Runs[0].BoldSet = true;
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "Right",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Italic = true,
            ItalicSet = true,
        });

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(4, 4));

        rich.InitialSelectionStyle.FontFamily.Should().Be("Aptos");
        rich.InitialSelectionStyle.FontSizePt.Should().Be(14);
        rich.InitialSelectionStyle.Bold.Should().BeTrue();
        rich.InitialSelectionStyle.Italic.Should().BeFalse();
    }

    [Fact]
    public void PlanRichTextEdit_CollapsedSelectionAtParagraphSeparator_UsesPreviousParagraphStyle()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].Runs[0].FontFamily = "Aptos";
        body.Paragraphs[0].Runs[0].FontSizePt = 14;
        body.Paragraphs[0].Runs[0].Underline = true;
        var second = new Paragraph();
        second.Runs.Add(new Run
        {
            Text = "Beta",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
        });
        body.Paragraphs.Add(second);

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(5, 5));

        rich.InitialSelectionStyle.FontFamily.Should().Be("Aptos");
        rich.InitialSelectionStyle.FontSizePt.Should().Be(14);
        rich.InitialSelectionStyle.Underline.Should().BeTrue();
        rich.InitialSelectionStyle.Bold.Should().BeFalse();
    }

    [Fact]
    public void PlanInitialSelection_SelectsAllPlainCellTextAcrossParagraphs()
    {
        var body = MakeBody("First");
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Second" });
        body.Paragraphs.Add(second);

        var selection = TableCellEditPlanner.PlanInitialSelection(body);

        selection.Should().Be(new InCanvasEditorTextSelection(0, "First\nSecond".Length));
    }

    [Fact]
    public void PlanInitialSelection_EmptyBody_ReturnsCollapsedCaretAtStart()
    {
        TableCellEditPlanner.PlanInitialSelection(new TextBody())
            .Should()
            .Be(new InCanvasEditorTextSelection(0, 0));
    }

    [Fact]
    public void CommitRichText_ChangedBody_ReturnsUndoableCellCommand()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = MakeMergedTableShape();
        slide.Shapes.Add(shape);
        var plan = TableCellEditPlanner.BeginEdit(
            0,
            slide,
            shape.Id,
            0,
            0,
            SlideTransformCore.Identity,
            30,
            18);

        var decision = TableCellEditPlanner.CommitRichText(plan.EditPlanner, MakeBody("Edited"));

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command!);
        shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Edited");

        bus.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Anchor");
    }

    [Theory]
    [InlineData(TableCellTextFormatKind.Bold)]
    [InlineData(TableCellTextFormatKind.Italic)]
    [InlineData(TableCellTextFormatKind.Underline)]
    public void PlanTextFormat_ContinuationCell_BuildsUndoableRunFormatCommand(TableCellTextFormatKind kind)
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Add(new Run { Text = " suffix", Bold = true, Italic = true, Underline = true });
        slide.Shapes.Add(shape);

        var plan = TableCellEditPlanner.PlanTextFormat(
            slideIndex: 0,
            slide,
            [shape.Id],
            activeCell: (0, 1),
            kind);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.ShapeId.Should().Be(shape.Id);
        plan.Row.Should().Be(0);
        plan.Col.Should().Be(0);
        plan.TargetValue.Should().BeTrue();
        plan.Command.Should().NotBeNull();

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);
        runs.Should().OnlyContain(run => ReadFormat(run, kind));
        if (kind == TableCellTextFormatKind.Bold)
            runs.Should().OnlyContain(run => run.BoldSet);
        if (kind == TableCellTextFormatKind.Italic)
            runs.Should().OnlyContain(run => run.ItalicSet);

        bus.Undo();
        ReadFormat(shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0], kind).Should().BeFalse();
    }

    [Fact]
    public void PlanTextFormat_SubRangeSelection_SplitsRunsAndFormatsOnlySelection()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run { Text = "one two three" });
        var slide = new Slide { Shapes = { shape } };

        // Select "two" (offsets 4..7) within the single-run cell text "one two three".
        var plan = TableCellEditPlanner.PlanTextFormat(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellTextFormatKind.Bold,
            selection: (4, 7));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.TargetValue.Should().BeTrue();
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(4, 7));
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.Selection.Should().Be(new InCanvasEditorTextSelection(4, 7));
        plan.ResultRichTextPlan.SelectedRunRanges.Should().ContainSingle(range => range.Text == "two");
        plan.ResultRichTextPlan.InitialSelectionStyle.Bold.Should().BeTrue();

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var realBus = new PresentationCommandBus(presentation);
        realBus.Execute(plan.Command!);

        var runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three");
        runs.Should().Contain(r => r.Text == "two" && r.Bold);
        runs.Where(r => r.Text != "two").Should().OnlyContain(r => !r.Bold);

        realBus.Undo();
        var undoneRuns = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        string.Concat(undoneRuns.Select(r => r.Text)).Should().Be("one two three");
        undoneRuns.Should().OnlyContain(r => !r.Bold);
    }

    [Fact]
    public void PlanFontFamily_SubRangeSelection_SplitsRunsAndFormatsOnlySelection()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run { Text = "one two three", FontFamily = "Aptos" });
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanFontFamily(
            0,
            slide,
            [shape.Id],
            (0, 0),
            "Consolas",
            selection: (4, 7));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Value.Should().Be("Consolas");

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three");
        runs.Should().Contain(r => r.Text == "two" && r.FontFamily == "Consolas");
        runs.Where(r => r.Text != "two").Should().OnlyContain(r => r.FontFamily == "Aptos");

        bus.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().OnlyContain(r => r.FontFamily == "Aptos");
    }

    [Fact]
    public void PlanColor_SubRangeSelectionAcrossMixedRuns_PreservesUnselectedFormatting()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run { Text = "one ", FontFamily = "Aptos", FontSizePt = 14 });
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "two",
            FontFamily = "Aptos",
            FontSizePt = 16,
            Italic = true,
            ItalicSet = true,
        });
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = " three",
            FontFamily = "Consolas",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
        });
        var slide = new Slide { Shapes = { shape } };
        var color = new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30));

        // Select "e two t" across the original three mixed-format runs.
        var plan = TableCellEditPlanner.PlanColor(
            0,
            slide,
            [shape.Id],
            (0, 0),
            color,
            selection: (2, 9));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        runs.Select(r => r.Text).Should().Equal("on", "e ", "two", " t", "hree");
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three");

        runs[0].Color.Should().BeNull();
        runs[0].FontFamily.Should().Be("Aptos");
        runs[1].Color!.Resolved.Should().Be(color.Resolved);
        runs[2].Color!.Resolved.Should().Be(color.Resolved);
        runs[2].Italic.Should().BeTrue("selected middle run keeps existing italic");
        runs[3].Color!.Resolved.Should().Be(color.Resolved);
        runs[3].Bold.Should().BeTrue("selected slice keeps existing bold");
        runs[4].Color.Should().BeNull();
        runs[4].Bold.Should().BeTrue("unselected suffix keeps existing bold");

        bus.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs.Select(r => r.Text)
            .Should().Equal("one ", "two", " three");
    }

    [Fact]
    public void PlanFontSizeAndColor_WholeCellSelection_FormatsAllRunsAndPreservesUndo()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = " suffix",
            FontFamily = "Aptos",
            FontSizePt = 10,
            Color = new ThemeAwareColor(new SrgbColor(1, 2, 3)),
            Italic = true,
            ItalicSet = true,
        });
        var color = new ThemeAwareColor(new SrgbColor(0x22, 0x44, 0x66));
        var slide = new Slide { Shapes = { shape } };

        var sizePlan = TableCellEditPlanner.PlanFontSize(0, slide, [shape.Id], (0, 0), 21, selection: (0, 13));

        sizePlan.Status.Should().Be(TableCellTextFormatStatus.Ready);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(sizePlan.Command!);

        var colorPlan = TableCellEditPlanner.PlanColor(0, slide, [shape.Id], (0, 0), color, selection: (0, 13));
        colorPlan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        bus.Execute(colorPlan.Command!);

        var runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        runs.Should().OnlyContain(r => r.FontSizePt == 21);
        runs.Should().OnlyContain(r => r.Color != null && r.Color.Resolved == color.Resolved);
        runs.Should().Contain(r => r.Italic, "existing mixed formatting should survive value formatting");

        bus.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().OnlyContain(r => r.FontSizePt == 21);

        bus.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().Contain(r => r.FontSizePt == 10);
    }

    [Fact]
    public void PlanTextFormat_CollapsedSelection_FallsBackToWholeCell()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run { Text = "abc" });
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanTextFormat(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellTextFormatKind.Bold,
            selection: (2, 2));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().OnlyContain(r => r.Bold);
    }

    [Fact]
    public void PlanFontFamily_CollapsedSelection_FallsBackToWholeCell()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs.Clear();
        body.Paragraphs[0].Runs.Add(new Run { Text = "left", FontFamily = "Aptos" });
        body.Paragraphs[0].Runs.Add(new Run { Text = "right", FontFamily = "Calibri" });
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanFontFamily(
            0,
            slide,
            [shape.Id],
            (0, 0),
            "Consolas",
            selection: (2, 2));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs
            .Should().OnlyContain(r => r.FontFamily == "Consolas");
    }

    [Fact]
    public void PlanTextFormat_AllRunsAlreadyFormatted_TogglesOff()
    {
        var shape = MakeMergedTableShape();
        foreach (var run in shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs.SelectMany(p => p.Runs))
            run.Underline = true;

        var plan = TableCellEditPlanner.PlanTextFormat(
            0,
            new Slide { Shapes = { shape } },
            [shape.Id],
            (0, 0),
            TableCellTextFormatKind.Underline);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.TargetValue.Should().BeFalse();
    }

    [Fact]
    public void BeginEdit_OutOfRangeCell_ReturnsDisabledPlan()
    {
        var shape = MakeMergedTableShape();
        var plan = TableCellEditPlanner.BeginEdit(
            0,
            new Slide { Shapes = { shape } },
            shape.Id,
            99,
            0,
            SlideTransformCore.Identity,
            30,
            18);

        plan.Status.Should().Be(TableCellEditStartStatus.CellOutOfRange);
        plan.IsReady.Should().BeFalse();
        plan.EditPlanner.Should().BeNull();
    }

    private static SlideShape MakeMergedTableShape()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(ToEmu(96));
        table.ColumnWidthsEmu.Add(ToEmu(96));
        table.ColumnWidthsEmu.Add(ToEmu(96));

        var row0 = new TableRow { HeightEmu = ToEmu(48) };
        row0.Cells.Add(new TableCell { GridSpan = 2, TextBody = MakeBody("Anchor") });
        row0.Cells.Add(new TableCell { HMerge = true });
        row0.Cells.Add(new TableCell { TextBody = MakeBody("Right") });
        table.Rows.Add(row0);

        return new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = ToEmu(288),
            ExtentCyEmu = ToEmu(48),
            Table = table,
        };
    }

    private static TextBody MakeBody(string text)
    {
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, FontFamily = "Aptos", FontSizePt = 14 });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    private static bool ReadFormat(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
