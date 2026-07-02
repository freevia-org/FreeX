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
        plan.OriginalBody!.Paragraphs[0].Runs[0].Text.Should().Be("Anchor");
        plan.EditPlanner.Should().NotBeNull();
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
