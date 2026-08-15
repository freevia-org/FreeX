using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasTextEditPlannerTests
{
    [Fact]
    public void NestedShapeTextEdit_ResolvesPath_PlacesWithChildTransform_AndUndoRestores()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var child = new SlideShape
        {
            Id = 3,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            RotationDeg = 22,
            FlipV = true,
            TextBody = MakeBody("Nested original"),
        };
        var nestedGroup = new SlideShape { Id = 2, Kind = SlideShapeKind.Group };
        nestedGroup.Children.Add(child);
        var outerGroup = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        outerGroup.Children.Add(nestedGroup);
        slide.Shapes.Add(outerGroup);

        ShapeHitTester.FindShapePath(slide, child.Id).Should().Equal(0, 0, 0);
        ShapeHitTester.ResolveShapePath(slide, [0, 0, 0]).Should().BeSameAs(child);

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            child.Id,
            new SlideTransformCore(2, 10, 20, 960, 540),
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.RichText);

        plan.IsReady.Should().BeTrue();
        plan.Placement!.Value.RotationDegrees.Should().Be(22);
        plan.Placement.Value.FlipVertical.Should().BeTrue();
        plan.Placement.Value.Left.Should().Be(202);
        plan.Placement.Value.Top.Should().Be(116);

        var decision = plan.EditPlanner!.CommitRichText(MakeBody("Nested edited"));
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command!);
        child.TextBody.Should().NotBeNull();
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Nested edited");

        bus.Undo();
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Nested original");
    }

    [Fact]
    public void NestedShapeTextEdit_CancelDoesNotChangeDescendant()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var child = new SlideShape { Id = 2, TextBody = MakeBody("Keep me") };
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0, presentation, slide, child.Id, SlideTransformCore.Identity,
            40, 20, InCanvasTextEditKind.RichText);

        plan.EditPlanner!.Cancel().Outcome.Should().Be(InCanvasTextEditOutcome.Canceled);
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Keep me");
    }

    [Fact]
    public void BeginShapeEdit_RichText_ReturnsPlacementSnapshotAndPlanner()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = MakeBody("Hello");
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 457200L,
            TextBody = body,
        };
        slide.Shapes.Add(shape);
        var transform = new SlideTransformCore(2, 10, 20, 960, 540);

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            shape.Id,
            transform,
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.RichText);

        plan.Status.Should().Be(InCanvasTextEditStartStatus.Ready);
        plan.Kind.Should().Be(InCanvasTextEditKind.RichText);
        plan.Placement.Should().Be(new InCanvasEditorPlacement(10, 20, 192, 96));
        plan.InitialSelection.Should().Be(new InCanvasEditorTextSelection(0, "Hello".Length));
        plan.OriginalPlainText.Should().Be("Hello");
        plan.OriginalBody.Should().NotBeSameAs(body);
        plan.RichTextPlan.Should().NotBeNull();
        plan.RichTextPlan!.PlainText.Should().Be("Hello");
        plan.RichTextPlan.Runs.Should().ContainSingle();
        plan.RichTextPlan.SuggestedEditorStyle.FontFamily.Should().Be("Aptos");
        plan.RichTextPlan.SuggestedEditorStyle.Bold.Should().BeTrue();
        plan.EditPlanner.Should().NotBeNull();

        body.Paragraphs[0].Runs[0].Text = "Mutated after plan";
        plan.OriginalBody!.Paragraphs[0].Runs[0].Text.Should().Be("Hello");

        var decision = plan.EditPlanner!.CommitRichText(MakeBody("Edited"));
        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command!.Label.Should().Be("Edit Rich Text");
    }

    [Fact]
    public void BeginShapeEdit_RotatedShape_CarriesEditorTransformMetadata()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 457200L,
            RotationDeg = 37.5,
            FlipH = true,
            TextBody = MakeBody("Rotated"),
        };
        slide.Shapes.Add(shape);

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            shape.Id,
            new SlideTransformCore(2, 10, 20, 960, 540),
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.RichText);

        plan.IsReady.Should().BeTrue();
        plan.Placement.Should().NotBeNull();
        plan.Placement!.Value.RotationDegrees.Should().Be(37.5);
        plan.Placement.Value.FlipHorizontal.Should().BeTrue();
        plan.Placement.Value.FlipVertical.Should().BeFalse();
        plan.Placement.Value.EffectiveTransformOriginX.Should().Be(96);
        plan.Placement.Value.EffectiveTransformOriginY.Should().Be(48);
    }

    [Fact]
    public void BeginShapeEdit_MissingTextBody_ReturnsDisabledPlan()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 1 });

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            1,
            SlideTransformCore.Identity,
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.PlainText);

        plan.Status.Should().Be(InCanvasTextEditStartStatus.MissingTextBody);
        plan.IsReady.Should().BeFalse();
        plan.Placement.Should().BeNull();
        plan.InitialSelection.Should().Be(new InCanvasEditorTextSelection(0, 0));
        plan.RichTextPlan.Should().BeNull();
        plan.EditPlanner.Should().BeNull();
        plan.OriginalPlainText.Should().BeEmpty();
    }

    [Fact]
    public void BeginShapeEdit_MixedRuns_ReturnsRendererNeutralRichTextPlan()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = MakeBody("Hello");
        body.Paragraphs[0].Runs.Add(new Run
        {
            Text = "World",
            FontFamily = "Consolas",
            FontSizePt = 22,
            Italic = true,
            ItalicSet = true,
        });
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 457200L,
            TextBody = body,
        };
        slide.Shapes.Add(shape);

        var plan = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            shape.Id,
            SlideTransformCore.Identity,
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.PlainText);

        plan.IsReady.Should().BeTrue();
        plan.InitialSelection.Should().Be(new InCanvasEditorTextSelection(0, "HelloWorld".Length));
        plan.RichTextPlan.Should().NotBeNull();
        plan.RichTextPlan!.PlainText.Should().Be("HelloWorld");
        plan.RichTextPlan.Runs.Should().HaveCount(2);
        plan.RichTextPlan.Runs[0].Text.Should().Be("Hello");
        plan.RichTextPlan.Runs[1].Text.Should().Be("World");
        plan.RichTextPlan.HasRichFormatting.Should().BeTrue();
        plan.RichTextPlan.HasMixedFormatting.Should().BeTrue();
        plan.RichTextPlan.SuggestedEditorStyle.FontFamily.Should().Be("Aptos");
        plan.RichTextPlan.SuggestedEditorStyle.Bold.Should().BeTrue();
        plan.RichTextPlan.InitialSelectionStyle.FontFamily.Should().BeNull();
        plan.RichTextPlan.InitialSelectionStyle.Italic.Should().BeNull();
    }

    [Fact]
    public void CommitPlainText_UnchangedText_ReturnsNoCommand()
    {
        var body = MakeBody("Hello");
        var planner = InCanvasTextEditPlanner.BeginPlainText(0, 1, body);

        var decision = planner.CommitPlainText("Hello");

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Unchanged);
        decision.Command.Should().BeNull();
    }

    [Fact]
    public void CommitPlainText_ChangedText_BuildsUndoableShapeTextBodyCommand()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape { Id = 1, TextBody = MakeBody("Hello") };
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);

        var planner = InCanvasTextEditPlanner.BeginPlainText(0, shape.Id, shape.TextBody);
        var decision = planner.CommitPlainText("First\nSecond");

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();
        decision.Command!.Label.Should().Be("Edit Text");

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command);

        shape.TextBody!.Paragraphs.Should().HaveCount(2);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("First");
        shape.TextBody.Paragraphs[1].Runs[0].Text.Should().Be("Second");
        shape.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().Be("Aptos");
        shape.TextBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue();

        bus.Undo();
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Hello");
    }

    [Fact]
    public void PlanTextFormat_ShapeSubRangeSelection_SplitsRunsAndFormatsOnlySelection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            TextBody = MakeBody("one two three"),
        };
        shape.TextBody!.Paragraphs[0].Runs[0].Bold = false;
        shape.TextBody.Paragraphs[0].Runs[0].BoldSet = false;
        slide.Shapes.Add(shape);

        var plan = InCanvasTextEditPlanner.PlanTextFormat(
            0,
            slide,
            shape.Id,
            TableCellTextFormatKind.Bold,
            selection: (4, 7));

        plan.Status.Should().Be(InCanvasShapeTextFormatStatus.Ready);
        plan.TargetValue.Should().BeTrue();
        plan.Command.Should().NotBeNull();
        plan.Command!.Label.Should().Be("Edit Rich Text");

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command);

        var runs = shape.TextBody!.Paragraphs[0].Runs;
        string.Concat(runs.Select(r => r.Text)).Should().Be("one two three");
        runs.Should().Contain(r => r.Text == "two" && r.Bold && r.BoldSet);
        runs.Where(r => r.Text != "two").Should().OnlyContain(r => !r.Bold);

        bus.Undo();
        shape.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => !r.Bold);
    }

    [Fact]
    public void PlanTextFormat_ShapeSubRangeSelection_PreservesRunGlowAndSoftEdge()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var body = MakeBody("one two three");
        var glow = new RunTextGlow
        {
            Color = new ThemeAwareColor(new SrgbColor(0x22, 0x88, 0xFF)),
            Alpha = 144,
            RadiusPt = 4.5,
        };
        var softEdge = new RunTextSoftEdge { RadiusPt = 2.25 };
        body.Paragraphs[0].Runs[0].TextGlow = glow;
        body.Paragraphs[0].Runs[0].TextSoftEdge = softEdge;
        body.Paragraphs[0].Runs[0].Bold = false;
        body.Paragraphs[0].Runs[0].BoldSet = false;
        var shape = new SlideShape
        {
            Id = 1,
            TextBody = body,
        };
        slide.Shapes.Add(shape);

        var plan = InCanvasTextEditPlanner.PlanTextFormat(
            0,
            slide,
            shape.Id,
            TableCellTextFormatKind.Bold,
            selection: (4, 7));

        plan.Status.Should().Be(InCanvasShapeTextFormatStatus.Ready);
        plan.Command.Should().NotBeNull();

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(plan.Command!);

        var runs = shape.TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(3);
        foreach (var run in runs)
        {
            run.TextGlow.Should().NotBeNull();
            run.TextGlow.Should().NotBeSameAs(glow);
            run.TextGlow!.RadiusPt.Should().Be(4.5);
            run.TextSoftEdge.Should().NotBeNull();
            run.TextSoftEdge.Should().NotBeSameAs(softEdge);
            run.TextSoftEdge!.RadiusPt.Should().Be(2.25);
        }

        runs.Should().ContainSingle(r => r.Text == "two" && r.Bold && r.BoldSet);

        bus.Undo();

        var restoredRun = shape.TextBody!.Paragraphs[0].Runs[0];
        restoredRun.Text.Should().Be("one two three");
        restoredRun.TextGlow.Should().NotBeNull();
        restoredRun.TextGlow.Should().NotBeSameAs(glow);
        restoredRun.TextGlow!.RadiusPt.Should().Be(4.5);
        restoredRun.TextSoftEdge.Should().NotBeNull();
        restoredRun.TextSoftEdge.Should().NotBeSameAs(softEdge);
        restoredRun.TextSoftEdge!.RadiusPt.Should().Be(2.25);
    }

    [Fact]
    public void CommitRichText_ColorOnlyChange_ReturnsCommand()
    {
        var original = MakeBody("Hello", new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));
        var edited = MakeBody("Hello", new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)));
        var planner = InCanvasTextEditPlanner.BeginRichText(0, 1, original);

        var decision = planner.CommitRichText(edited);

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();
        decision.Command!.Label.Should().Be("Edit Rich Text");
    }

    [Fact]
    public void ApplyTextValueFormat_AutomaticColor_ClearsOnlySelectedRunColor()
    {
        var explicitColor = new ThemeAwareColor(new SrgbColor(0x22, 0x66, 0xAA));
        var source = MakeBody("one two", explicitColor);

        var edited = InCanvasTextEditPlanner.ApplyTextValueFormat(
            source,
            TableCellTextValueFormatKind.Color,
            value: null,
            selection: (4, 7));

        edited.Paragraphs[0].Runs.Should().HaveCount(2);
        edited.Paragraphs[0].Runs[0].Text.Should().Be("one ");
        edited.Paragraphs[0].Runs[0].Color.Should().NotBeNull();
        edited.Paragraphs[0].Runs[0].Color!.Resolved.Should().Be(explicitColor.Resolved);
        edited.Paragraphs[0].Runs[1].Text.Should().Be("two");
        edited.Paragraphs[0].Runs[1].Color.Should().BeNull(
            "Automatic means inherit the theme color rather than retain an explicit run color");

        source.Paragraphs[0].Runs.Should().ContainSingle();
        source.Paragraphs[0].Runs[0].Color.Should().BeSameAs(explicitColor,
            "shared text mutations must not alter the source snapshot");
    }

    [Fact]
    public void CommitTableCellRichText_ChangedText_BuildsUndoableCellTextCommand()
    {
        var presentation = Presentation.CreateEmpty();
        var original = MakeBody("Original");
        var shape = MakeTableShape(1, original);
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);

        var planner = InCanvasTableCellTextEditPlanner.BeginRichText(0, shape.Id, 0, 0, original);
        var replacement = MakeBody("Replacement");

        var decision = planner.CommitRichText(replacement);

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        decision.Command.Should().NotBeNull();
        decision.Command!.Label.Should().Be("Edit Cell Text");

        replacement.Paragraphs[0].Runs[0].Text = "Mutated before apply";

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command);

        shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Replacement");

        bus.Undo();

        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
    }

    [Fact]
    public void CommitTableCellRichText_ParagraphAlignOnlyChange_ReturnsNoCommand()
    {
        var original = MakeBody("Hello");
        original.Paragraphs[0].Align = TextAlign.Left;
        var edited = MakeBody("Hello");
        edited.Paragraphs[0].Align = TextAlign.Right;
        var planner = InCanvasTableCellTextEditPlanner.BeginRichText(0, 1, 0, 0, original);

        var decision = planner.CommitRichText(edited);

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Unchanged);
        decision.Command.Should().BeNull();
    }

    [Fact]
    public void SetShapeTextBodyCommand_ClonesInputAndUndoSnapshots()
    {
        var presentation = Presentation.CreateEmpty();
        var original = MakeBody("Original");
        original.Paragraphs[0].Runs[0].Hyperlink = new Hyperlink { Url = "https://example.test" };
        var replacement = MakeBody("Replacement");

        var shape = new SlideShape { Id = 1, TextBody = original };
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);

        var bus = new PresentationCommandBus(presentation);
        var command = new SetShapeTextBodyCommand(0, shape.Id, replacement);
        replacement.Paragraphs[0].Runs[0].Text = "Mutated before apply";

        bus.Execute(command);
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Replacement");

        shape.TextBody.Paragraphs[0].Runs[0].Text = "Mutated after apply";
        bus.Undo();

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Original");
        shape.TextBody.Paragraphs[0].Runs[0].Hyperlink.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].Hyperlink);
        shape.TextBody.Paragraphs[0].Runs[0].Hyperlink!.Url.Should().Be("https://example.test");
    }

    [Fact]
    public void SelectedRunHyperlink_SplitsRangeAndSupportsUndoThroughTextBodyCommand()
    {
        var source = MakeBody("Click here");
        var link = new Hyperlink { Url = "https://example.test", Tooltip = "Example" };

        var edited = InCanvasTextEditPlanner.ApplySelectedRunHyperlink(
            source,
            link,
            (0, 5));

        edited.Paragraphs[0].Runs.Should().HaveCount(2);
        edited.Paragraphs[0].Runs[0].Text.Should().Be("Click");
        edited.Paragraphs[0].Runs[0].Hyperlink.Should().NotBeSameAs(link);
        edited.Paragraphs[0].Runs[0].Hyperlink!.Url.Should().Be(link.Url);
        edited.Paragraphs[0].Runs[1].Text.Should().Be(" here");
        edited.Paragraphs[0].Runs[1].Hyperlink.Should().BeNull();
        InCanvasTextEditPlanner.GetSelectedRunHyperlink(edited, (0, 5))!.Url
            .Should().Be(link.Url);

        var cleared = InCanvasTextEditPlanner.ApplySelectedRunHyperlink(edited, null, (0, 5));
        cleared.Paragraphs[0].Runs.Should().ContainSingle();
        cleared.Paragraphs[0].Runs[0].Hyperlink.Should().BeNull();
    }

    [Fact]
    public void InlineTableCloneAndEquality_PreserveRowHorizontalAlignment()
    {
        var source = new InlineTableInfo();
        source.Table.Rows.Add(new TableRow
        {
            HorizontalAlignment = TableRowHorizontalAlignment.Center,
            Cells = { new TableCell { TextBody = MakeBody("text") } },
        });

        var clone = source.Clone();

        TextBodyModelCloner.InlineTablesEqual(source, clone).Should().BeTrue();
        clone.Table.Rows[0].HorizontalAlignment = TableRowHorizontalAlignment.Right;
        TextBodyModelCloner.InlineTablesEqual(source, clone).Should().BeFalse();
    }

    private static TextBody MakeBody(string text, ThemeAwareColor? color = null)
    {
        var body = new TextBody { Wrap = true, Anchor = VerticalAnchor.Middle };
        var paragraph = new Paragraph { Align = TextAlign.Left };
        paragraph.Runs.Add(new Run
        {
            Text = text,
            FontFamily = "Aptos",
            FontSizePt = 18,
            Bold = true,
            Color = color,
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static SlideShape MakeTableShape(uint id, TextBody? cellBody)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L);
        var row = new TableRow { HeightEmu = 457200L };
        row.Cells.Add(new TableCell { TextBody = cellBody });
        table.Rows.Add(row);

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 457200L,
            Table = table,
        };
    }
}
