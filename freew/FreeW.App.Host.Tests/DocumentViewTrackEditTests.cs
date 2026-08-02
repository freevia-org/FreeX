using FreeW.App.Host.Editing;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

public sealed class DocumentViewTrackEditTests
{
    private static DocumentView BuildView(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Paragraph ParagraphOf(DocumentView view)
    {
        view.CommitToModel();
        return (Paragraph)view.Model.Blocks[0];
    }

    [StaFact]
    public void LoadModel_UsesAuthoredTrackRevisionsState()
    {
        var enabled = TextDocument.CreateEmpty();
        enabled.TrackRevisions = true;
        var view = new DocumentView();

        view.LoadModel(enabled);

        view.TrackChangesEnabled.Should().BeTrue();
        view.Model.TrackRevisions.Should().BeTrue();

        var disabled = TextDocument.CreateEmpty();
        view.LoadModel(disabled);

        view.TrackChangesEnabled.Should().BeFalse();
        view.Model.TrackRevisions.Should().BeFalse();
    }

    [StaFact]
    public void TrackChangesToggle_PersistsAuthoredDocumentState()
    {
        var view = BuildView("Hello world");
        var changed = 0;
        view.TextChanged += (_, _) => changed++;

        view.TrackChangesEnabled = true;
        view.Model.TrackRevisions.Should().BeTrue();
        changed.Should().Be(1);

        view.TrackChangesEnabled = false;
        view.Model.TrackRevisions.Should().BeFalse();
        changed.Should().Be(2);

        view.TrackChangesEnabled = false;
        changed.Should().Be(2, "assigning the current state must not dirty the document again");
    }

    [StaFact]
    public void TrackFormattingToggle_PersistsInverseWordSettingAndDirtiesOnce()
    {
        var document = TextDocument.CreateEmpty();
        document.DoNotTrackFormatting = true;
        var view = new DocumentView();
        view.LoadModel(document);
        var changed = 0;
        view.TextChanged += (_, _) => changed++;

        view.TrackFormattingEnabled.Should().BeFalse();
        view.TrackFormattingEnabled = true;

        view.Model.DoNotTrackFormatting.Should().BeFalse();
        changed.Should().Be(1);

        view.TrackFormattingEnabled = true;
        changed.Should().Be(1);
    }

    [StaFact]
    public void CharacterFormatting_TracksActiveAuthorAndHonorsPolicy()
    {
        var tracked = BuildView("Hello world");
        tracked.RevisionAuthor = "Ada Reviewer";
        tracked.TrackChangesEnabled = true;

        tracked.SetCharacterBorder(new ParagraphBorder("#0070C0", 1));
        tracked.CommitToModel();

        var revision = ((Paragraph)tracked.Model.Blocks[0]).Runs.Single().FormatRevision;
        revision.Should().NotBeNull();
        revision!.Author.Should().Be("Ada Reviewer");
        revision.PreviousFormatting.CharacterBorder.Should().BeNull();

        var excluded = BuildView("Hello world");
        excluded.TrackChangesEnabled = true;
        excluded.TrackFormattingEnabled = false;

        excluded.SetCharacterBorder(new ParagraphBorder("#0070C0", 1));
        excluded.CommitToModel();

        ((Paragraph)excluded.Model.Blocks[0]).Runs.Should().OnlyContain(run => run.FormatRevision == null);
    }

    [StaFact]
    public void RibbonBold_SelectedRangeTracksActiveAuthorAndUndoRedoRestoresExactFormatting()
    {
        var view = BuildView("Hello world");
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.bold"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().Be("Hello world");
        var formatted = paragraph.Runs.Single(run => run.Text == "world");
        formatted.Formatting.Bold.Should().BeTrue();
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Ada Reviewer");
        formatted.FormatRevision.PreviousFormatting.Bold.Should().BeFalse();
        var revisionDate = formatted.FormatRevision.DateXml;
        RenderedRun(view, "world").FontWeight.Should().Be(System.Windows.FontWeights.Bold);

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            !run.Formatting.Bold && run.FormatRevision == null);
        RenderedRun(view, "Hello world").FontWeight.Should().Be(System.Windows.FontWeights.Normal);

        view.Redo();
        formatted = ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world");
        formatted.Formatting.Bold.Should().BeTrue();
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Ada Reviewer");
        formatted.FormatRevision.DateXml.Should().Be(revisionDate);
        RenderedRun(view, "world").FontWeight.Should().Be(System.Windows.FontWeights.Bold);
    }

    [StaFact]
    public void RibbonItalic_SelectedRangeHonorsTrackFormattingSuppressionAndRemainsUndoable()
    {
        var view = BuildView("Hello world");
        view.TrackChangesEnabled = true;
        view.TrackFormattingEnabled = false;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.italic"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        var formatted = ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world");
        formatted.Formatting.Italic.Should().BeTrue();
        formatted.FormatRevision.Should().BeNull();

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run => !run.Formatting.Italic);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world").Formatting.Italic.Should().BeTrue();
    }

    [StaFact]
    public void RibbonSuperscript_SelectedRangeTracksAndUndoRestoresBaseline()
    {
        var view = BuildView("H2O");
        view.RevisionAuthor = "Chem Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 1, 0, 2);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.superscript"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        var formatted = ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "2");
        formatted.Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Chem Reviewer");
        formatted.FormatRevision.PreviousFormatting.VerticalAlign.Should().Be(VerticalAlign.Baseline);

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.VerticalAlign == VerticalAlign.Baseline && run.FormatRevision == null);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "2")
            .Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    }

    [StaFact]
    public void RibbonSmallCapsAndAllCaps_SelectedRangeStayMutuallyExclusive()
    {
        var view = BuildView("Caps");
        view.SetSelectionRangeForTest(0, 0, 0, 4);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.smallcaps"), out var smallCaps).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.allcaps"), out var allCaps).Should().BeTrue();

        smallCaps!.Execute(RibbonCommandContext.Empty);
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.SmallCaps && !run.Formatting.AllCaps);

        view.SetSelectionRangeForTest(0, 0, 0, 4);
        allCaps!.Execute(RibbonCommandContext.Empty);
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.AllCaps && !run.Formatting.SmallCaps);
    }

    [StaFact]
    public void RibbonFontFamily_SelectedRangeTracksAndUndoRestoresInheritedFamily()
    {
        var view = BuildView("Hello world");
        view.RevisionAuthor = "Type Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.font-family"), out var command).Should().BeTrue();

        command!.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "Arial" }));

        var formatted = ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world");
        formatted.Formatting.FontFamily.Should().Be("Arial");
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Type Reviewer");
        formatted.FormatRevision.PreviousFormatting.FontFamily.Should().Be("Calibri");
        RenderedRun(view, "world").FontFamily.Source.Should().Be("Arial");

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.FontFamily == "Calibri" && run.FormatRevision == null);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world")
            .Formatting.FontFamily.Should().Be("Arial");
    }

    [StaFact]
    public void RibbonFontSize_SelectedRangeTracksPointsAndUndoRestoresInheritedSize()
    {
        var view = BuildView("Hello world");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.font-size"), out var command).Should().BeTrue();

        command!.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "16" }));

        var formatted = ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world");
        formatted.Formatting.FontSizePt.Should().Be(16);
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.PreviousFormatting.FontSizePt.Should().Be(11);
        RenderedRun(view, "world").FontSize.Should().BeApproximately(16 * 96.0 / 72.0, 0.001);

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.FontSizePt == 11 && run.FormatRevision == null);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world")
            .Formatting.FontSizePt.Should().Be(16);
    }

    [StaFact]
    public void RibbonFontFamilyAndSize_CollapsedCaretKeepNativePendingFormatting()
    {
        var view = BuildView("Hello");
        view.MoveCaretToBlockForTest(0, 5);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.font-family"), out var family).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.font-size"), out var size).Should().BeTrue();

        family!.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "Arial" }));
        size!.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "16" }));

        view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontFamilyProperty)
            .Should().Be(new System.Windows.Media.FontFamily("Arial"));
        view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty)
            .Should().Be(16 * 96.0 / 72.0);
    }

    [StaFact]
    public void CharacterBorder_SelectedRangeTracksOnlyExactCharactersAndUndoRestoresParagraph()
    {
        var view = BuildView("Hello world");
        view.RevisionAuthor = "Border Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var border = new ParagraphBorder("#0070C0", 1);

        view.SetCharacterBorder(border);

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Single(run => run.Text == "Hello ").Formatting.CharacterBorder.Should().BeNull();
        var formatted = paragraph.Runs.Single(run => run.Text == "world");
        formatted.Formatting.CharacterBorder.Should().Be(border);
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Border Reviewer");
        formatted.FormatRevision.PreviousFormatting.CharacterBorder.Should().BeNull();

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.CharacterBorder == null && run.FormatRevision == null);
    }

    [StaFact]
    public void CharacterShading_SelectedRangeTracksOnlyExactCharactersAndUndoRestoresParagraph()
    {
        var view = BuildView("Hello world");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 0, 0, 5);

        view.SetCharacterShading("#FFFF00", ShadingPattern.Pct25);

        var paragraph = (Paragraph)view.Model.Blocks[0];
        var formatted = paragraph.Runs.Single(run => run.Text == "Hello");
        formatted.Formatting.CharacterShadingHex.Should().Be("#FFFF00");
        formatted.Formatting.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);
        formatted.FormatRevision.Should().NotBeNull();
        paragraph.Runs.Single(run => run.Text == " world").Formatting.CharacterShadingHex.Should().BeNull();

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.CharacterShadingHex == null
            && run.Formatting.CharacterShadingPattern == ShadingPattern.Clear
            && run.FormatRevision == null);
    }

    [StaFact]
    public void ClearFormatting_SelectedRangeTracksOnlyExactCharactersAndUndoRestoresFormatting()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var original = new RunFormatting { Bold = true, Italic = true, ColorHex = "#C00000" };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello world", original));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        var baseline = ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting;
        view.RevisionAuthor = "Cleanup Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);

        view.ClearFormatting();

        paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Single(run => run.Text == "Hello ").Formatting.Should().Be(baseline);
        var cleared = paragraph.Runs.Single(run => run.Text == "world");
        cleared.Formatting.Should().Be(RunFormatting.Default);
        cleared.FormatRevision.Should().NotBeNull();
        cleared.FormatRevision!.Author.Should().Be("Cleanup Reviewer");
        cleared.FormatRevision.PreviousFormatting.Should().Be(baseline);

        view.Undo();
        paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Text.Should().Be("Hello world");
        paragraph.Runs[0].Formatting.Should().Be(baseline);
        paragraph.Runs[0].FormatRevision.Should().BeNull();
    }

    [StaFact]
    public void ClearFormatting_CollapsedCaretRetainsParagraphFallback()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold ", new RunFormatting { Bold = true }));
        paragraph.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(document);
        view.MoveCaretToBlockForTest(0, 2);

        view.ClearFormatting();

        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting == RunFormatting.Default);
    }

    [StaFact]
    public void RibbonTextColor_SelectedRangeTracksOnlyExactCharactersAndUndoRestoresColor()
    {
        var view = BuildView("Hello world");
        view.RevisionAuthor = "Color Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.font-color"), out var command).Should().BeTrue();

        command!.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "#C00000" }));

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Single(run => run.Text == "Hello ").Formatting.ColorHex.Should().Be("#000000");
        var formatted = paragraph.Runs.Single(run => run.Text == "world");
        formatted.Formatting.ColorHex.Should().Be("#C00000");
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Color Reviewer");
        formatted.FormatRevision.PreviousFormatting.ColorHex.Should().Be("#000000");

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.ColorHex == "#000000" && run.FormatRevision == null);
    }

    [StaFact]
    public void HighlightClear_SelectedRangeTracksOnlyExactCharactersAndUndoRestoresHighlight()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello world", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        var baseline = ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting;
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 0, 0, 5);

        view.SetHighlightColor(null);

        paragraph = (Paragraph)view.Model.Blocks[0];
        var cleared = paragraph.Runs.Single(run => run.Text == "Hello");
        cleared.Formatting.HighlightColorHex.Should().BeNull();
        cleared.FormatRevision.Should().NotBeNull();
        cleared.FormatRevision!.PreviousFormatting.Should().Be(baseline);
        paragraph.Runs.Single(run => run.Text == " world").Formatting.HighlightColorHex.Should().Be("#FFFF00");

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting == baseline && run.FormatRevision == null);
    }

    [StaFact]
    public void TextColorAndHighlight_CollapsedCaretKeepNativePendingColors()
    {
        var view = BuildView("Hello");
        view.MoveCaretToBlockForTest(0, 5);

        view.SetTextColor("#C00000");
        view.SetHighlightColor("#FFFF00");

        var foreground = view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.ForegroundProperty)
            .Should().BeOfType<System.Windows.Media.SolidColorBrush>().Subject;
        foreground.Color.Should().Be(System.Windows.Media.Color.FromRgb(0xC0, 0x00, 0x00));
        var background = view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty)
            .Should().BeOfType<System.Windows.Media.SolidColorBrush>().Subject;
        background.Color.Should().Be(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0x00));
    }

    [StaFact]
    public void FontDialogFormatting_SelectedRangeAppliesCompleteTrackedSnapshotAndUndoRedo()
    {
        var view = BuildView("Hello world");
        view.CommitToModel();
        var baseline = ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting;
        var target = baseline with
        {
            FontFamily = "Arial",
            FontSizePt = 16,
            Bold = true,
            Italic = true,
            Underline = true,
            ColorHex = "#0070C0",
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 12,
            PositionPt = 1.5,
            Ligatures = LigatureMode.StandardContextual,
            StylisticSet = 4,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
        };
        view.RevisionAuthor = "Font Reviewer";
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 6, 0, 11);

        view.ApplyFontFormatting(target);

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Single(run => run.Text == "Hello ").Formatting.Should().Be(baseline);
        var formatted = paragraph.Runs.Single(run => run.Text == "world");
        formatted.Formatting.Should().Be(target);
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Font Reviewer");
        formatted.FormatRevision.PreviousFormatting.Should().Be(baseline);
        var rendered = RenderedRun(view, "world");
        rendered.FontFamily.Source.Should().Be("Arial");
        rendered.FontSize.Should().BeApproximately(16 * 96.0 / 72.0, 0.001);
        rendered.FontWeight.Should().Be(System.Windows.FontWeights.Bold);

        view.Undo();
        paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Formatting.Should().Be(baseline);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "world")
            .Formatting.Should().Be(target);
    }

    [StaFact]
    public void FontDialogFormatting_CollapsedCaretKeepsVisiblePendingFormatWithoutRewritingParagraph()
    {
        var view = BuildView("Hello");
        view.CommitToModel();
        var baseline = ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting;
        var target = baseline with
        {
            FontFamily = "Arial",
            FontSizePt = 16,
            Bold = true,
            CharacterSpacingPt = 1.25,
        };
        view.MoveCaretToBlockForTest(0, 5);

        view.ApplyFontFormatting(target);

        view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontFamilyProperty)
            .Should().Be(new System.Windows.Media.FontFamily("Arial"));
        view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty)
            .Should().Be(16 * 96.0 / 72.0);
        view.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontWeightProperty)
            .Should().Be(System.Windows.FontWeights.Bold);
        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting.Should().Be(baseline);
    }

    private static WpfRun RenderedRun(DocumentView view, string text) =>
        view.Document.Blocks.OfType<WpfParagraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<WpfRun>())
            .Single(run => run.Text == text);

    [StaFact]
    public void InsertText_WithTrackChangesOn_RecordsInsertedRevision()
    {
        var view = BuildView("Hello ");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 6);

        view.InsertText("world");

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("Hello world");
        var inserted = paragraph.Runs.Single(r => r.Text == "world");
        inserted.Revision.Should().Be(RevisionKind.Inserted);
        inserted.RevisionAuthor.Should().Be("FreeW User");
        inserted.RevisionDateXml.Should().NotBeNullOrEmpty();
    }

    [StaFact]
    public void Backspace_WithTrackChangesOn_MarksDeletion()
    {
        var view = BuildView("abc");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 3);

        view.BackspaceForTest();

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abc");
        var deleted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Deleted);
        deleted.Text.Should().Be("c");
        deleted.RevisionAuthor.Should().Be("FreeW User");
    }

    [StaFact]
    public void Delete_WithTrackChangesOn_MarksDeletion()
    {
        var view = BuildView("abc");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 0);

        view.DeleteForwardForTest();

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abc");
        var deleted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Deleted);
        deleted.Text.Should().Be("a");
    }

    [StaFact]
    public void TypingOverSelection_WithTrackChangesOn_MarksOldDeletedAndNewInserted()
    {
        var view = BuildView("abcdef");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 0, 5);

        view.InsertText("Z");

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("abZcdef");
        paragraph.Runs.Should().Contain(r => r.Text == "Z" && r.Revision == RevisionKind.Inserted);
        paragraph.Runs.Should().Contain(r => r.Text == "cde" && r.Revision == RevisionKind.Deleted);
    }

    [StaFact]
    public void RibbonTrackChanges_EnablingOverSelection_marks_exactly_that_selection()
    {
        var view = BuildView("Hello world");
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

        stateful.GetState().IsChecked.Should().BeFalse();
        command!.Execute(RibbonCommandContext.Empty);

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("Hello world");
        paragraph.Runs.Should().ContainSingle(run =>
            run.Text == "world"
            && run.Revision == RevisionKind.Inserted
            && run.RevisionAuthor == "FreeW User"
            && !string.IsNullOrWhiteSpace(run.RevisionDateXml));
        stateful.GetState().IsChecked.Should().BeTrue();

        command.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeFalse();
        ParagraphOf(view).Runs.Count(run => run.Revision == RevisionKind.Inserted).Should().Be(1);

        // The WPF authority mutates the model directly, so this selection mark is not a new WPF
        // undo entry. Existing text and the authority's mark remain intact when Undo is invoked.
        view.Undo();
        ParagraphOf(view).PlainText.Should().Be("Hello world");
        ParagraphOf(view).Runs.Count(run => run.Revision == RevisionKind.Inserted).Should().Be(1);
    }

    [StaFact]
    public void RibbonTrackChanges_empty_selection_does_not_invent_a_revision_and_undo_keeps_text()
    {
        var view = BuildView("Hello world");
        view.MoveCaretToBlockForTest(0, 6);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        var paragraph = ParagraphOf(view);
        paragraph.PlainText.Should().Be("Hello world");
        paragraph.Runs.Should().NotContain(run => run.Revision != RevisionKind.None);
        view.TrackChangesEnabled.Should().BeTrue();
        view.Undo();
        ParagraphOf(view).PlainText.Should().Be("Hello world");
        ParagraphOf(view).Runs.Should().NotContain(run => run.Revision != RevisionKind.None);
    }

    [StaFact]
    public void RibbonTrackChanges_disabling_over_selection_does_not_mark_again()
    {
        var view = BuildView("Hello world");
        view.SetSelectionRangeForTest(0, 6, 0, 11);
        view.TrackChangesEnabled = true;
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        view.TrackChangesEnabled.Should().BeFalse();
        ParagraphOf(view).Runs.Should().NotContain(run => run.Revision != RevisionKind.None);
    }

    [StaFact]
    public void AcceptReject_AfterLiveTrackedEdits_ResolvesCorrectly()
    {
        var acceptView = BuildView("abc");
        acceptView.TrackChangesEnabled = true;
        acceptView.MoveCaretToBlockForTest(0, 3);
        acceptView.BackspaceForTest();
        acceptView.AcceptAllRevisions();
        ParagraphOf(acceptView).PlainText.Should().Be("ab");
        ParagraphOf(acceptView).Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);

        var rejectView = BuildView("abc");
        rejectView.TrackChangesEnabled = true;
        rejectView.MoveCaretToBlockForTest(0, 3);
        rejectView.BackspaceForTest();
        rejectView.RejectAllRevisions();
        ParagraphOf(rejectView).PlainText.Should().Be("abc");
        ParagraphOf(rejectView).Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }
}
