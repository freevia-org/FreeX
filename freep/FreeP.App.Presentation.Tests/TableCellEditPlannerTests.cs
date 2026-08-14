using FreeP.App.Compositor;
using FreeP.Core.IO;

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
    public void GroupedTableChild_UsesSharedCellEditingRoutes()
    {
        var table = MakeMergedTableShape();
        var group = new SlideShape { Id = 70, Kind = SlideShapeKind.Group };
        group.Children.Add(table);
        var slide = new Slide { Shapes = { group } };

        var state = TableCellEditPlanner.PlanSelectedCell(slide, [table.Id], (0, 0));
        state.HasSelectedTable.Should().BeTrue();
        state.CanEditText.Should().BeTrue();

        var begin = TableCellEditPlanner.BeginEdit(
            slideIndex: 0,
            slide,
            table.Id,
            row: 0,
            col: 0,
            new SlideTransformCore(2, 10, 20, 960, 540),
            minimumWidth: 30,
            minimumHeight: 18);
        begin.Status.Should().Be(TableCellEditStartStatus.Ready);

        var navigation = TableCellEditPlanner.PlanNavigation(
            slide,
            [table.Id],
            activeCell: (0, 0),
            TableCellNavigationDirection.Next);
        navigation.Status.Should().Be(TableCellNavigationStatus.Ready);
        navigation.Row.Should().Be(0);
        navigation.Col.Should().Be(2);
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
    public void PlanNavigation_ContinuationCell_MovesToNextEditableAnchor()
    {
        var shape = MakeMergedTableShape();
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanNavigation(
            slide,
            [shape.Id],
            activeCell: (0, 1),
            TableCellNavigationDirection.Next);

        plan.Status.Should().Be(TableCellNavigationStatus.Ready);
        plan.ShapeId.Should().Be(shape.Id);
        plan.Row.Should().Be(0);
        plan.Col.Should().Be(2);
    }

    [Fact]
    public void PlanNavigation_PreviousFromRightCell_MovesToMergedAnchor()
    {
        var shape = MakeMergedTableShape();
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanNavigation(
            slide,
            [shape.Id],
            activeCell: (0, 2),
            TableCellNavigationDirection.Previous);

        plan.Status.Should().Be(TableCellNavigationStatus.Ready);
        plan.Row.Should().Be(0);
        plan.Col.Should().Be(0);
    }

    [Fact]
    public void PlanNavigation_RowMajorAcrossRows_StopsAtTableBoundary()
    {
        var shape = MakeTwoRowTableShape();
        var slide = new Slide { Shapes = { shape } };

        var nextRow = TableCellEditPlanner.PlanNavigation(
            slide,
            [shape.Id],
            activeCell: (0, 1),
            TableCellNavigationDirection.Next);
        var atEnd = TableCellEditPlanner.PlanNavigation(
            slide,
            [shape.Id],
            activeCell: (1, 1),
            TableCellNavigationDirection.Next);

        nextRow.Status.Should().Be(TableCellNavigationStatus.Ready);
        nextRow.Row.Should().Be(1);
        nextRow.Col.Should().Be(0);
        atEnd.Status.Should().Be(TableCellNavigationStatus.NoTargetCell);
        atEnd.Row.Should().Be(1);
        atEnd.Col.Should().Be(1);
    }

    [Fact]
    public void PlanKeyboard_MatchesWpfTableCellEditingSemantics()
    {
        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Escape,
                TableCellEditKeyboardModifiers.Control)
            .Action.Should().Be(TableCellEditKeyboardAction.Cancel);

        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Tab,
                TableCellEditKeyboardModifiers.None)
            .Should().Be(new TableCellEditKeyboardPlan(
                TableCellEditKeyboardAction.Navigate,
                TableCellNavigationDirection.Next));
        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Tab,
                TableCellEditKeyboardModifiers.Shift)
            .Should().Be(new TableCellEditKeyboardPlan(
                TableCellEditKeyboardAction.Navigate,
                TableCellNavigationDirection.Previous));

        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Tab,
                TableCellEditKeyboardModifiers.Control)
            .Action.Should().Be(TableCellEditKeyboardAction.None);
        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.Tab,
                TableCellEditKeyboardModifiers.Platform)
            .Action.Should().Be(TableCellEditKeyboardAction.None);

        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.B,
                TableCellEditKeyboardModifiers.Control | TableCellEditKeyboardModifiers.Shift)
            .Should().Be(new TableCellEditKeyboardPlan(
                TableCellEditKeyboardAction.ToggleTextFormat,
                TextFormatKind: TableCellTextFormatKind.Bold));
        TableCellEditPlanner.PlanKeyboard(
                TableCellEditKeyboardKey.U,
                TableCellEditKeyboardModifiers.None)
            .Action.Should().Be(TableCellEditKeyboardAction.None);
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
    public void PlanRichTextEdit_SelectionReportsParagraphAndListMetadata()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].Align = TextAlign.Left;
        var second = new Paragraph
        {
            Align = TextAlign.Center,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanLcPeriod,
            AutoNumStartAt = 3,
            Level = 1,
            MarginLeftEmu = 457200,
            IndentEmu = -228600,
        };
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var third = new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
        };
        third.Runs.Add(new Run { Text = "Gamma" });
        body.Paragraphs.Add(third);

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(6, 10));

        rich.Paragraphs
            .Select(paragraph => (
                paragraph.ParagraphIndex,
                paragraph.Start,
                paragraph.End,
                paragraph.Text,
                paragraph.BulletKind,
                paragraph.AutoNumType,
                paragraph.Level))
            .Should()
            .Equal(
                (0, 0, 5, "Alpha", BulletKind.None, null, 0),
                (1, 6, 10, "Beta", BulletKind.Auto, AutoNumType.RomanLcPeriod, 1),
                (2, 11, 16, "Gamma", BulletKind.Char, null, 0));
        rich.SelectedParagraphs.Should().ContainSingle();
        rich.SelectedParagraphs[0].ParagraphIndex.Should().Be(1);
        rich.SelectedParagraphs[0].AutoNumStartAt.Should().Be(3);
        rich.SelectedParagraphs[0].MarginLeftEmu.Should().Be(457200);
        rich.SelectedParagraphs[0].IndentEmu.Should().Be(-228600);
        rich.SelectedListState.HasSelectedParagraphs.Should().BeTrue();
        rich.SelectedListState.HasListFormatting.Should().BeTrue();
        rich.SelectedListState.HasMixedListFormatting.Should().BeFalse();
        rich.SelectedListState.PresetId.Should().Be(TableCellListPresetCatalog.NumberRomanLowerPeriodId);
        rich.SelectedListState.DisplayName.Should().Be("Roman i.");
        rich.SelectedListState.PreviewText.Should().Be("i.  Roman i.");
        rich.SelectedListState.GalleryItemKind.Should().Be(PresentationListGalleryItemKind.Numbering);
        rich.SelectedListState.AutoNumStartAt.Should().Be(3);
        rich.HasListFormatting.Should().BeTrue();
        rich.HasMixedParagraphFormatting.Should().BeTrue();
    }

    [Fact]
    public void PlanRichTextEdit_ImageBulletParagraph_ReportsImageBulletMetadata()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].BulletKind = BulletKind.Image;
        body.Paragraphs[0].BulletImage = new ImagePart
        {
            Bytes = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
        };

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(0, 5));

        rich.Paragraphs.Should().ContainSingle();
        rich.Paragraphs[0].BulletKind.Should().Be(BulletKind.Image);
        rich.Paragraphs[0].BulletImage.Should().NotBeNull();
        rich.Paragraphs[0].BulletImage!.ContentType.Should().Be("image/png");
        rich.Paragraphs[0].BulletImage!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
        rich.SelectedParagraphs.Should().ContainSingle();
        rich.SelectedParagraphs[0].BulletImage.Should().BeSameAs(rich.Paragraphs[0].BulletImage);
        rich.SelectedListState.HasListFormatting.Should().BeTrue();
        rich.SelectedListState.HasResolvedPreset.Should().BeFalse();
        rich.SelectedListState.IsPictureBullet.Should().BeTrue();
        rich.SelectedListState.DisplayName.Should().Be("Picture Bullet");
        rich.SelectedListState.PreviewText.Should().Be("[image]");
        rich.SelectedListState.GalleryItemKind.Should().Be(PresentationListGalleryItemKind.ImageBullet);
        rich.HasListFormatting.Should().BeTrue();
    }

    [Fact]
    public void PlanRichTextEdit_MixedSelectedListParagraphs_ReportsMixedVisibleListState()
    {
        var body = MakeBody("Alpha");
        body.Paragraphs[0].BulletKind = BulletKind.Char;
        body.Paragraphs[0].BulletChar = "\u25AA";

        var second = new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.AlphaUcPeriod,
            AutoNumStartAt = 1,
        };
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);

        var rich = TableCellEditPlanner.PlanRichTextEdit(
            body,
            new InCanvasEditorTextSelection(0, "Alpha\nBeta".Length));

        rich.SelectedParagraphs.Should().HaveCount(2);
        rich.SelectedListState.HasSelectedParagraphs.Should().BeTrue();
        rich.SelectedListState.HasListFormatting.Should().BeTrue();
        rich.SelectedListState.HasMixedListFormatting.Should().BeTrue();
        rich.SelectedListState.PresetId.Should().BeNull();
        rich.SelectedListState.PreviewText.Should().BeNull();
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
    [InlineData(TableCellTextFormatKind.Strikethrough)]
    [InlineData(TableCellTextFormatKind.Superscript)]
    [InlineData(TableCellTextFormatKind.Subscript)]
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
    public void PlanParagraphAlignment_DisabledStates_MatchActiveTableCellRequirements()
    {
        var shape = MakeMergedTableShape();
        var notTable = new SlideShape { Id = 77, Kind = SlideShapeKind.AutoShape };

        TableCellEditPlanner.PlanParagraphAlignment(0, null, [shape.Id], (0, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.MissingSlide);
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { shape } }, [], (0, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.ShapeNotFound);
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { shape } }, [999], (0, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.ShapeNotFound);
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { notTable } }, [notTable.Id], (0, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.NotTable);
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { shape } }, [shape.Id], null, TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.MissingActiveCell);
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { shape } }, [shape.Id], (99, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.CellOutOfRange);

        shape.Table!.Rows[0].Cells[0].TextBody = null;
        TableCellEditPlanner.PlanParagraphAlignment(0, new Slide { Shapes = { shape } }, [shape.Id], (0, 0), TextAlign.Center)
            .Status.Should().Be(TableCellTextFormatStatus.MissingTextBody);
    }

    [Fact]
    public void PlanParagraphAlignment_WholeCell_BuildsUndoableCommandAndPreservesRuns()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Align = TextAlign.Left;
        body.Paragraphs[0].Runs.Add(new Run { Text = " suffix", Bold = true, BoldSet = true });
        var second = new Paragraph { Align = TextAlign.Right };
        second.Runs.Add(new Run { Text = "Second", Italic = true, ItalicSet = true });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphAlignment(
            0,
            slide,
            [shape.Id],
            (0, 1),
            TextAlign.Center);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.ShapeId.Should().Be(shape.Id);
        plan.Row.Should().Be(0);
        plan.Col.Should().Be(0);
        plan.Value.Should().Be(TextAlign.Center);
        plan.Command.Should().NotBeNull();
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(0, "Anchor suffix\nSecond".Length));
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.PlainText.Should().Be("Anchor suffix\nSecond");

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Should().OnlyContain(paragraph => paragraph.Align == TextAlign.Center);
        paragraphs[0].Runs[1].Bold.Should().BeTrue("run formatting should survive paragraph formatting");
        paragraphs[1].Runs[0].Italic.Should().BeTrue("run formatting should survive paragraph formatting");

        bus.Undo();
        paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].Align.Should().Be(TextAlign.Left);
        paragraphs[1].Align.Should().Be(TextAlign.Right);
    }

    [Fact]
    public void PlanParagraphAlignment_SubRangeSelection_AlignsOnlyTouchedParagraphs()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Align = TextAlign.Left;
        var second = new Paragraph { Align = TextAlign.Left };
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var third = new Paragraph { Align = TextAlign.Left };
        third.Runs.Add(new Run { Text = "Gamma" });
        body.Paragraphs.Add(third);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphAlignment(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TextAlign.Right,
            selection: (7, 11));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(7, 11));

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].Align.Should().Be(TextAlign.Left);
        paragraphs[1].Align.Should().Be(TextAlign.Right);
        paragraphs[2].Align.Should().Be(TextAlign.Left);
    }

    [Fact]
    public void PlanParagraphAlignment_CollapsedCaret_AlignsOnlyTheCaretParagraph()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Align = TextAlign.Left;
        var second = new Paragraph { Align = TextAlign.Left };
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphAlignment(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TextAlign.Right,
            selection: (8, 8));

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        new PresentationCommandBus(presentation).Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].Align.Should().Be(TextAlign.Left);
        paragraphs[1].Align.Should().Be(TextAlign.Right);
    }

    [Theory]
    [InlineData(TableCellParagraphFormatKind.BulletToggle)]
    [InlineData(TableCellParagraphFormatKind.NumberingToggle)]
    [InlineData(TableCellParagraphFormatKind.Indent)]
    [InlineData(TableCellParagraphFormatKind.Outdent)]
    public void PlanParagraphFormat_DisabledStates_MatchActiveTableCellRequirements(
        TableCellParagraphFormatKind kind)
    {
        var shape = MakeMergedTableShape();
        var notTable = new SlideShape { Id = 77, Kind = SlideShapeKind.AutoShape };

        Plan(kind, null, [shape.Id], (0, 0)).Status.Should().Be(TableCellTextFormatStatus.MissingSlide);
        Plan(kind, new Slide { Shapes = { shape } }, [], (0, 0)).Status.Should().Be(TableCellTextFormatStatus.ShapeNotFound);
        Plan(kind, new Slide { Shapes = { shape } }, [999], (0, 0)).Status.Should().Be(TableCellTextFormatStatus.ShapeNotFound);
        Plan(kind, new Slide { Shapes = { notTable } }, [notTable.Id], (0, 0)).Status.Should().Be(TableCellTextFormatStatus.NotTable);
        Plan(kind, new Slide { Shapes = { shape } }, [shape.Id], null).Status.Should().Be(TableCellTextFormatStatus.MissingActiveCell);
        Plan(kind, new Slide { Shapes = { shape } }, [shape.Id], (99, 0)).Status.Should().Be(TableCellTextFormatStatus.CellOutOfRange);

        shape.Table!.Rows[0].Cells[0].TextBody = null;
        Plan(kind, new Slide { Shapes = { shape } }, [shape.Id], (0, 0)).Status.Should().Be(TableCellTextFormatStatus.MissingTextBody);
    }

    [Fact]
    public void PlanParagraphBulletToggle_WholeCell_BuildsUndoableCommand()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Second" });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphBulletToggle(0, slide, [shape.Id], (0, 1));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.BulletToggle);
        plan.BulletEnabled.Should().BeTrue();
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(0, "Anchor\nSecond".Length));

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Should().OnlyContain(paragraph =>
            paragraph.BulletKind == BulletKind.Char &&
            paragraph.BulletChar == "\u2022" &&
            !paragraph.BulletSuppressed);

        bus.Undo();
        paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Should().OnlyContain(paragraph => paragraph.BulletKind == BulletKind.None);
    }

    [Fact]
    public void PlanParagraphBulletToggle_AllTargetParagraphsBulleted_SuppressesBullets()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].BulletKind = BulletKind.Char;
        body.Paragraphs[0].BulletChar = "\u2022";
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphBulletToggle(0, slide, [shape.Id], (0, 0));

        plan.BulletEnabled.Should().BeFalse();

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.None);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeTrue();
    }

    [Fact]
    public void PlanParagraphNumberingToggle_WholeCell_BuildsUndoableCommand()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Second" });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphNumberingToggle(0, slide, [shape.Id], (0, 1));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.NumberingToggle);
        plan.BulletEnabled.Should().BeTrue();
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(0, "Anchor\nSecond".Length));

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Should().OnlyContain(paragraph =>
            paragraph.BulletKind == BulletKind.Auto &&
            paragraph.AutoNumType == AutoNumType.ArabicPeriod &&
            paragraph.AutoNumStartAt == 1 &&
            paragraph.BulletChar == null &&
            !paragraph.BulletSuppressed);

        bus.Undo();
        paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Should().OnlyContain(paragraph => paragraph.BulletKind == BulletKind.None);
    }

    [Fact]
    public void PlanParagraphNumberingToggle_AllTargetParagraphsAutoNumbered_SuppressesNumbering()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].BulletKind = BulletKind.Auto;
        body.Paragraphs[0].AutoNumType = AutoNumType.RomanUcPeriod;
        body.Paragraphs[0].AutoNumStartAt = 4;
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphNumberingToggle(0, slide, [shape.Id], (0, 0));

        plan.BulletEnabled.Should().BeFalse();

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.None);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeTrue();
    }

    [Fact]
    public void PlanParagraphNumberingToggle_SubRangeSelection_NumbersOnlyTouchedParagraphs()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var third = new Paragraph();
        third.Runs.Add(new Run { Text = "Gamma" });
        body.Paragraphs.Add(third);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphNumberingToggle(
            0,
            slide,
            [shape.Id],
            (0, 0),
            selection: (7, 11));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(7, 11));

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[1].AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        paragraphs[1].AutoNumStartAt.Should().Be(1);
        paragraphs[2].BulletKind.Should().Be(BulletKind.None);
    }

    [Fact]
    public void BuiltInListPresets_ExposeStablePowerPointLikeTableCellDescriptors()
    {
        TableCellListPresetCatalog.BuiltIn.Select(preset => preset.Id)
            .Should()
            .Equal(
                TableCellListPresetCatalog.BulletDiscId,
                TableCellListPresetCatalog.BulletHollowCircleId,
                TableCellListPresetCatalog.BulletSquareId,
                TableCellListPresetCatalog.BulletDashId,
                TableCellListPresetCatalog.BulletCheckId,
                TableCellListPresetCatalog.NumberArabicPeriodId,
                TableCellListPresetCatalog.NumberRomanUpperPeriodId,
                TableCellListPresetCatalog.NumberRomanLowerPeriodId,
                TableCellListPresetCatalog.NumberAlphaUpperPeriodId,
                TableCellListPresetCatalog.NumberAlphaLowerPeriodId);

        TableCellListPresetCatalog.BulletSquare.BulletChar.Should().Be("\u25AA");
        TableCellListPresetCatalog.BulletCheck.BulletChar.Should().Be("\u2713");
        TableCellListPresetCatalog.NumberRomanUpperPeriod.AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        TableCellListPresetCatalog.NumberAlphaLowerPeriod.AutoNumType.Should().Be(AutoNumType.AlphaLcPeriod);
    }

    [Fact]
    public void PlanParagraphListPreset_RomanUpperSelection_BuildsUndoableSharedCommand()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var third = new Paragraph { BulletKind = BulletKind.Char, BulletChar = "\u2022" };
        third.Runs.Add(new Run { Text = "Gamma" });
        body.Paragraphs.Add(third);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphListPreset(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellListPresetCatalog.NumberRomanUpperPeriod,
            selection: (7, 11));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.ListPreset);
        plan.ListPreset.Should().Be(TableCellListPresetCatalog.NumberRomanUpperPeriod);
        plan.EffectiveSelection.Should().Be(new InCanvasEditorTextSelection(7, 11));
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.SelectedParagraphs.Should().ContainSingle();
        plan.ResultRichTextPlan.SelectedParagraphs[0].ParagraphIndex.Should().Be(1);
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        plan.ResultRichTextPlan.SelectedParagraphs[0].AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        plan.ResultRichTextPlan.SelectedParagraphs[0].AutoNumStartAt.Should().Be(1);
        plan.ResultRichTextPlan.SelectedListState.HasResolvedPreset.Should().BeTrue();
        plan.ResultRichTextPlan.SelectedListState.PresetId.Should().Be(TableCellListPresetCatalog.NumberRomanUpperPeriodId);
        plan.ResultRichTextPlan.SelectedListState.DisplayName.Should().Be("Roman I.");
        plan.ResultRichTextPlan.SelectedListState.PreviewText.Should().Be("I.  Roman I.");
        plan.ResultRichTextPlan.SelectedListState.GalleryItemKind.Should().Be(PresentationListGalleryItemKind.Numbering);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[1].AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        paragraphs[1].AutoNumStartAt.Should().Be(1);
        paragraphs[1].BulletChar.Should().BeNull();
        paragraphs[1].BulletSuppressed.Should().BeFalse();
        paragraphs[2].BulletKind.Should().Be(BulletKind.Char);

        bus.Undo();
        paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[1].BulletKind.Should().Be(BulletKind.None);
        paragraphs[2].BulletKind.Should().Be(BulletKind.Char);
    }

    [Fact]
    public void PlanParagraphListPreset_AlphaLowerWholeCell_AppliesToAllParagraphs()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphListPreset(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellListPresetCatalog.NumberAlphaLowerPeriod);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs
            .Should()
            .OnlyContain(paragraph =>
                paragraph.BulletKind == BulletKind.Auto &&
                paragraph.AutoNumType == AutoNumType.AlphaLcPeriod &&
                paragraph.AutoNumStartAt == 1 &&
                !paragraph.BulletSuppressed);
    }

    [Fact]
    public void PlanParagraphListPreset_NonDefaultStartContinuesAcrossSelectedParagraphs()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Runs[0].Text = "Alpha";
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Beta" } } });
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Gamma" } } });
        var slide = new Slide { Shapes = { shape } };
        var preset = new TableCellListPresetDescriptor(
            "custom-number-4",
            "Number 4.",
            BulletKind.Auto,
            AutoNumType: AutoNumType.ArabicPeriod,
            StartAt: 4);

        var plan = TableCellEditPlanner.PlanParagraphListPreset(
            0,
            slide,
            [shape.Id],
            (0, 0),
            preset);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs.Select(paragraph => paragraph.AutoNumStartAt)
            .Should().Equal(4, 1, 1);
        paragraphs.Select(paragraph => paragraph.AutoNumStartAtSpecified)
            .Should().Equal(true, false, false);

        var markerState = new PresentationListMarkerContinuationState();
        paragraphs.Select(paragraph => markerState.Next(
                paragraph.Level,
                paragraph.AutoNumType,
                paragraph.AutoNumStartAt,
                paragraph.AutoNumStartAtSpecified))
            .Should().Equal(4, 5, 6);
    }

    [Fact]
    public void PlanParagraphListPreset_CharacterPresetClearsExistingPictureBulletPayload()
    {
        var shape = MakeMergedTableShape();
        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind = BulletKind.Image;
        paragraph.BulletImage = new ImagePart
        {
            Bytes = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
        };
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphListPreset(
            0,
            slide,
            [shape.Id],
            (0, 0),
            TableCellListPresetCatalog.BulletSquare);

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.SelectedParagraphs.Should().ContainSingle();
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletKind.Should().Be(BulletKind.Char);
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletImage.Should().BeNull();

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("\u25AA");
        paragraph.BulletImage.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void PlanParagraphPictureBullet_SelectionBuildsUndoableSharedCommand()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var slide = new Slide { Shapes = { shape } };
        var payload = PresentationPictureBulletAuthoringPlanner.CreatePayload(
            [0x89, 0x50, 0x4E, 0x47],
            "image/png",
            "bullet.png");

        var plan = TableCellEditPlanner.PlanParagraphPictureBullet(
            0,
            slide,
            [shape.Id],
            (0, 0),
            payload,
            selection: (7, 11));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.PictureBullet);
        plan.BulletImage.Should().NotBeNull();
        plan.BulletImage!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
        plan.ResultRichTextPlan.Should().NotBeNull();
        plan.ResultRichTextPlan!.SelectedParagraphs.Should().ContainSingle();
        plan.ResultRichTextPlan.SelectedParagraphs[0].BulletKind.Should().Be(BulletKind.Image);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        paragraphs[1].BulletKind.Should().Be(BulletKind.Image);
        paragraphs[1].BulletImage.Should().NotBeNull();
        paragraphs[1].BulletImage!.ContentType.Should().Be("image/png");
        paragraphs[1].BulletImage!.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
        paragraphs[1].BulletChar.Should().BeNull();
        paragraphs[1].BulletSuppressed.Should().BeFalse();

        bus.Undo();
        paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[1].BulletKind.Should().Be(BulletKind.None);
        paragraphs[1].BulletImage.Should().BeNull();
    }

    [Fact]
    public void PlanParagraphIndent_SubRangeSelection_IndentsOnlyTouchedParagraphs()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        var second = new Paragraph { BulletKind = BulletKind.Char, BulletChar = "\u2022" };
        second.Runs.Add(new Run { Text = "Beta" });
        body.Paragraphs.Add(second);
        var third = new Paragraph();
        third.Runs.Add(new Run { Text = "Gamma" });
        body.Paragraphs.Add(third);
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphIndent(
            0,
            slide,
            [shape.Id],
            (0, 0),
            selection: (7, 11));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.Indent);
        plan.LevelDelta.Should().Be(1);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraphs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs;
        paragraphs[0].Level.Should().Be(0);
        paragraphs[0].MarginLeftEmu.Should().BeNull();
        paragraphs[1].Level.Should().Be(1);
        paragraphs[1].MarginLeftEmu.Should().Be(457200);
        paragraphs[1].IndentEmu.Should().Be(-228600);
        paragraphs[2].Level.Should().Be(0);
    }

    [Fact]
    public void PlanParagraphOutdent_WholeCell_ClampsAtZeroAndPreservesUndo()
    {
        var shape = MakeMergedTableShape();
        var body = shape.Table!.Rows[0].Cells[0].TextBody!;
        body.Paragraphs[0].Level = 2;
        body.Paragraphs[0].MarginLeftEmu = 914400;
        body.Paragraphs[0].IndentEmu = -228600;
        var slide = new Slide { Shapes = { shape } };

        var plan = TableCellEditPlanner.PlanParagraphOutdent(0, slide, [shape.Id], (0, 0));

        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.Outdent);
        plan.LevelDelta.Should().Be(-1);

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(1);
        paragraph.MarginLeftEmu.Should().Be(457200);
        paragraph.IndentEmu.Should().Be(-228600);

        bus.Undo();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(2);
        paragraph.MarginLeftEmu.Should().Be(914400);
        paragraph.IndentEmu.Should().Be(-228600);
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

    [Fact]
    public void TableGraphicFrameTransform_RoundTripsThroughPptxReaderAndWriter()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = MakeMergedTableShape();
        shape.RotationDeg = 30;
        shape.FlipH = true;
        shape.FlipV = true;
        slide.Shapes.Add(shape);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var loaded = PptxPackageReader.Read(stream);
        var roundTripped = loaded.Slides[0].Shapes.Single();

        roundTripped.Kind.Should().Be(SlideShapeKind.Table);
        roundTripped.RotationDeg.Should().BeApproximately(30, 0.001);
        roundTripped.FlipH.Should().BeTrue();
        roundTripped.FlipV.Should().BeTrue();
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

    private static SlideShape MakeTwoRowTableShape()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(ToEmu(96));
        table.ColumnWidthsEmu.Add(ToEmu(96));

        for (int rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var row = new TableRow { HeightEmu = ToEmu(48) };
            row.Cells.Add(new TableCell { TextBody = MakeBody($"R{rowIndex}C0") });
            row.Cells.Add(new TableCell { TextBody = MakeBody($"R{rowIndex}C1") });
            table.Rows.Add(row);
        }

        return new SlideShape
        {
            Id = 84,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = ToEmu(192),
            ExtentCyEmu = ToEmu(96),
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
        TableCellTextFormatKind.Strikethrough => run.Strikethrough,
        TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
        TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static TableCellParagraphFormatPlan Plan(
        TableCellParagraphFormatKind kind,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell) =>
        kind switch
        {
            TableCellParagraphFormatKind.BulletToggle =>
                TableCellEditPlanner.PlanParagraphBulletToggle(0, slide, selectedShapeIds, activeCell),
            TableCellParagraphFormatKind.NumberingToggle =>
                TableCellEditPlanner.PlanParagraphNumberingToggle(0, slide, selectedShapeIds, activeCell),
            TableCellParagraphFormatKind.Indent =>
                TableCellEditPlanner.PlanParagraphIndent(0, slide, selectedShapeIds, activeCell),
            TableCellParagraphFormatKind.Outdent =>
                TableCellEditPlanner.PlanParagraphOutdent(0, slide, selectedShapeIds, activeCell),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
