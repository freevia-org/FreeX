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
        // The run inherited its family from the document default, so nothing was explicitly set
        // before -- which is exactly what Word's w:rPrChange/w:rPr records. Materializing "Calibri"
        // here would bake direct formatting onto a run that had none and would survive a save.
        formatted.FormatRevision.PreviousFormatting.FontFamily.Should().BeNull();
        RenderedRun(view, "world").FontFamily.Source.Should().Be("Arial");

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.FontFamily == null && run.FormatRevision == null);
        // Undo restores inheritance, not an explicit value: the text still renders at the document
        // default even though the run carries no family of its own. The runs merge back into one
        // once the differing format is gone, so this reads the merged run.
        RenderedRun(view, "Hello world").FontFamily.Source.Should().Be("Calibri");

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
        // Inherited, so nothing was explicitly set before -- see the family test above.
        formatted.FormatRevision!.PreviousFormatting.FontSizePt.Should().BeNull();
        RenderedRun(view, "world").FontSize.Should().BeApproximately(16 * 96.0 / 72.0, 0.001);

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.FontSizePt == null && run.FormatRevision == null);
        RenderedRun(view, "Hello world").FontSize.Should().BeApproximately(11 * 96.0 / 72.0, 0.001);

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
        // The untouched run keeps inheriting its colour rather than materializing the default.
        paragraph.Runs.Single(run => run.Text == "Hello ").Formatting.ColorHex.Should().BeNull();
        var formatted = paragraph.Runs.Single(run => run.Text == "world");
        formatted.Formatting.ColorHex.Should().Be("#C00000");
        formatted.FormatRevision.Should().NotBeNull();
        formatted.FormatRevision!.Author.Should().Be("Color Reviewer");
        formatted.FormatRevision.PreviousFormatting.ColorHex.Should().BeNull();

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run =>
            run.Formatting.ColorHex == null && run.FormatRevision == null);
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

    [StaFact]
    public void CommitToModel_PreservesModelOnlyRunFormattingThroughWpfView()
    {
        var formatting = new RunFormatting
        {
            Bold = true,
            Underline = true,
            Strikethrough = true,
            FontFamily = "Arial",
            FontSizePt = 15,
            ColorHex = "#0070C0",
            HighlightColorHex = "#FFFF00",
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 12,
            PositionPt = 1.5,
            Ligatures = LigatureMode.StandardContextual,
            StylisticSet = 4,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
            CharacterBorder = new ParagraphBorder("#C00000", 1),
            CharacterShadingHex = "#E2F0D9",
            CharacterShadingPattern = ShadingPattern.Pct25,
            LanguageTag = "fr-FR",
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Preserve", formatting));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(document);

        view.CommitToModel();

        ((Paragraph)view.Model.Blocks[0]).Runs.Single().Formatting.Should().Be(formatting);
    }

    [StaFact]
    public void FormatPainter_SelectedRangeCopiesFullFormattingTracksAndUndoesAtomically()
    {
        var sourceFormatting = new RunFormatting
        {
            Bold = true,
            Underline = true,
            FontFamily = "Arial",
            FontSizePt = 16,
            ColorHex = "#0070C0",
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 12,
            PositionPt = 1.5,
            Ligatures = LigatureMode.StandardContextual,
            StylisticSet = 4,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
            CharacterBorder = new ParagraphBorder("#C00000", 1),
            CharacterShadingHex = "#E2F0D9",
            CharacterShadingPattern = ShadingPattern.Pct25,
            LanguageTag = "fr-FR",
        };
        var sourceParagraphFormatting = ParagraphFormatting.Default with
        {
            Alignment = TextAlignment.Center,
            SpaceAfterPt = 18,
        };
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var source = new Paragraph { Formatting = sourceParagraphFormatting };
        source.Runs.Add(new Run("Source", sourceFormatting));
        document.Blocks.Add(source);
        document.Blocks.Add(new Paragraph("Hello world"));
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        var targetBaseline = ((Paragraph)view.Model.Blocks[1]).Runs.Single().Formatting;
        var targetParagraphBaseline = ((Paragraph)view.Model.Blocks[1]).Formatting;
        view.RevisionAuthor = "Painter Reviewer";
        view.TrackChangesEnabled = true;

        view.SetSelectionRangeForTest(0, 0, 0, 6);
        view.ArmFormatPainter().Should().BeTrue();
        view.SetSelectionRangeForTest(1, 6, 1, 11);

        view.ApplyFormatPainterToSelectionForTest().Should().BeTrue();

        var target = (Paragraph)view.Model.Blocks[1];
        target.Runs.Single(run => run.Text == "Hello ").Formatting.Should().Be(targetBaseline);
        var painted = target.Runs.Single(run => run.Text == "world");
        painted.Formatting.Should().Be(sourceFormatting);
        painted.FormatRevision.Should().NotBeNull();
        painted.FormatRevision!.Author.Should().Be("Painter Reviewer");
        painted.FormatRevision.PreviousFormatting.Should().Be(targetBaseline);
        target.Formatting.Should().Be(sourceParagraphFormatting);
        view.FormatPainterActive.Should().BeFalse();

        view.Undo();

        target = (Paragraph)view.Model.Blocks[1];
        target.Runs.Should().ContainSingle();
        target.Runs[0].Text.Should().Be("Hello world");
        target.Runs[0].Formatting.Should().Be(targetBaseline);
        target.Runs[0].FormatRevision.Should().BeNull();
        target.Formatting.Should().Be(targetParagraphBaseline);

        view.Redo();
        ((Paragraph)view.Model.Blocks[1]).Runs.Single(run => run.Text == "world")
            .Formatting.Should().Be(sourceFormatting);
        ((Paragraph)view.Model.Blocks[1]).Formatting.Should().Be(sourceParagraphFormatting);
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
    public void RibbonTrackChanges_EnablingAcrossParagraphs_marks_text_and_boundary_exactly()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.SetSelectionRangeForTest(0, 2, 1, 3);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);
        view.CommitToModel();

        var first = (Paragraph)view.Model.Blocks[0];
        var second = (Paragraph)view.Model.Blocks[1];
        first.Runs.Should().Contain(run => run.Text == "Fi" && run.Revision == RevisionKind.None);
        first.Runs.Should().Contain(run => run.Text == "rst" && run.Revision == RevisionKind.Inserted);
        first.MarkRevision.Should().Be(RevisionKind.Inserted);
        second.Runs.Should().Contain(run => run.Text == "Sec" && run.Revision == RevisionKind.Inserted);
        second.Runs.Should().Contain(run => run.Text == "ond" && run.Revision == RevisionKind.None);
        second.MarkRevision.Should().Be(RevisionKind.None);
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

    // ── R135: Backspace/Delete at a paragraph boundary must record a tracked paragraph-mark deletion
    // instead of silently, permanently merging the two paragraphs (bypassing Track Changes entirely). ──

    private static DocumentView BuildTwoParagraphView(string first, string second)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(first));
        doc.Blocks.Add(new Paragraph(second));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void Backspace_AtParagraphStart_WithTrackChangesOn_MarksBoundaryDeletedWithoutMerging()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(1, 0);          // caret at the very start of the second paragraph

        view.BackspaceForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(2, "the two paragraphs must NOT be physically merged while the deletion is only tracked");
        var first = (Paragraph)view.Model.Blocks[0];
        var second = (Paragraph)view.Model.Blocks[1];
        first.MarkRevision.Should().Be(RevisionKind.Deleted, "the first paragraph's own mark records the tracked boundary deletion");
        first.MarkRevisionAuthor.Should().Be("FreeW User");
        first.PlainText.Should().Be("First");
        second.PlainText.Should().Be("Second");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeTrue();
    }

    [StaFact]
    public void Backspace_AtParagraphStart_WithTrackChangesOff_MergesParagraphsImmediately()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.MoveCaretToBlockForTest(1, 0);

        view.BackspaceForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(1, "with Track Changes off, Backspace merges the paragraphs as before (regression guard)");
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("FirstSecond");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void DeleteForward_AtParagraphEnd_WithTrackChangesOn_MarksBoundaryDeletedWithoutMerging()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(0, 5);           // caret at the very end of the first paragraph ("First")

        view.DeleteForwardForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(2, "the two paragraphs must NOT be physically merged while the deletion is only tracked");
        var first = (Paragraph)view.Model.Blocks[0];
        var second = (Paragraph)view.Model.Blocks[1];
        first.MarkRevision.Should().Be(RevisionKind.Deleted, "forward-Delete marks the SAME paragraph's own mark deleted as Backspace at the next paragraph's start would");
        first.PlainText.Should().Be("First");
        second.PlainText.Should().Be("Second");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeTrue();
    }

    [StaFact]
    public void DeleteForward_AtParagraphEnd_WithTrackChangesOff_MergesParagraphsImmediately()
    {
        // Sibling regression guard: Track-Changes-off behaviour at a paragraph boundary must be unchanged
        // by this fix (the WPF host's native RichTextBox EditingCommands.Delete merges paragraphs here,
        // same as before).
        var view = BuildTwoParagraphView("First", "Second");
        view.MoveCaretToBlockForTest(0, 5);

        view.DeleteForwardForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(1);
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("FirstSecond");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void AcceptAll_AfterTrackedParagraphBoundaryBackspace_PerformsTheMerge()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(1, 0);
        view.BackspaceForTest();                     // tracked boundary deletion only, no merge yet

        view.AcceptAllRevisions();                    // accept -> the merge actually happens now
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(1, "accepting the tracked paragraph-mark deletion performs the merge");
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("FirstSecond");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void RejectAll_AfterTrackedParagraphBoundaryBackspace_RestoresTwoSeparateParagraphs()
    {
        var view = BuildTwoParagraphView("First", "Second");
        view.TrackChangesEnabled = true;
        view.MoveCaretToBlockForTest(1, 0);
        view.BackspaceForTest();

        view.RejectAllRevisions();                    // reject -> the boundary deletion is undone
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(2, "rejecting keeps the two paragraphs separate");
        ((Paragraph)view.Model.Blocks[0]).MarkRevision.Should().Be(RevisionKind.None, "reject clears the tracked mark-deletion");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    // ── R136: the SIBLINGS of the R135 paragraph-mark fix — structural edits at a boundary that also
    // bypassed Track Changes entirely. (a) Deleting a table row removed it outright; (b) a selection
    // spanning two or more paragraphs fell through to native RichTextBox handling, which merged the
    // paragraphs and discarded the selected text with no revision recorded at all. ──

    private static (DocumentView View, Table Table) BuildTableView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(3, 2);
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 2; c++)
                table.Rows[r].Cells[c] = new TableCell($"R{r}C{c}");
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(doc);
        return (view, (Table)view.Model.Blocks[0]);
    }

    // CommitToModel rebuilds every table block from the FlowDocument, so a reference captured earlier goes
    // stale the moment anything commits (AcceptAllRevisions/RejectAllRevisions commit first).
    private static Table TableOf(DocumentView view) => (Table)view.Model.Blocks[0];

    private static DocumentView BuildThreeParagraphView(string first, string second, string third)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(first));
        doc.Blocks.Add(new Paragraph(second));
        doc.Blocks.Add(new Paragraph(third));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    // DeleteTableRowCommand is shared model code that both shells drive identically (ribbon
    // "freew.table-delete-row" -> DocumentView.DeleteTableRow -> MutateCaretTable -> this command); these
    // run it through the WPF host's own command bus and context, which is what supplies the revision author.

    [StaFact]
    public void DeleteTableRow_WithTrackChangesOn_MarksTheRowDeletedInsteadOfRemovingIt()
    {
        var (view, table) = BuildTableView();
        view.TrackChangesEnabled = true;

        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        table.Rows.Should().HaveCount(3, "a tracked row deletion leaves the row in place until it is accepted");
        table.Rows[1].RowRevision.Should().Be(RevisionKind.Deleted);
        table.Rows[1].RowRevisionAuthor.Should().Be("FreeW User");
        table.Rows[1].RowRevisionDateXml.Should().NotBeNullOrWhiteSpace();
        table.Rows[1].Cells[0].PlainText.Should().Be("R1C0", "the row's own content is untouched by the mark");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeTrue();
    }

    [StaFact]
    public void DeleteTableRow_WithTrackChangesOff_RemovesTheRowImmediately()
    {
        var (view, table) = BuildTableView();

        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        table.Rows.Should().HaveCount(2, "with Track Changes off the row is removed as before (regression guard)");
        table.Rows[1].Cells[0].PlainText.Should().Be("R2C0", "the row below shifts up");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void DeleteTableRow_TrackedThenUndone_RestoresTheRowsPreviousUnmarkedState()
    {
        var (view, table) = BuildTableView();
        view.TrackChangesEnabled = true;
        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        view.Commands.Undo();

        table.Rows.Should().HaveCount(3);
        table.Rows[1].RowRevision.Should().Be(RevisionKind.None, "undo clears the mark the tracked delete added");
        table.Rows[1].RowRevisionAuthor.Should().BeNull();
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void AcceptAll_AfterTrackedTableRowDeletion_ActuallyRemovesTheRow()
    {
        var (view, _) = BuildTableView();
        view.TrackChangesEnabled = true;
        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        view.AcceptAllRevisions();

        TableOf(view).Rows.Should().HaveCount(2, "accepting the tracked row deletion performs the removal");
        TableOf(view).Rows[1].Cells[0].PlainText.Should().Be("R2C0");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void TrackedTableRowDeletion_SurvivesACommitToModelRoundTrip()
    {
        // A WPF FlowDocument row has no slot for a row revision and CommitToModel rebuilds every table
        // from the view, so the mark only survives because WpfTableRowTag carries it (see BuildTable /
        // ReadTable). Without that side-band the tracked deletion would vanish on the next keystroke.
        var (view, _) = BuildTableView();
        view.TrackChangesEnabled = true;
        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        view.CommitToModel();

        TableOf(view).Rows.Should().HaveCount(3);
        TableOf(view).Rows[1].RowRevision.Should().Be(RevisionKind.Deleted);
        TableOf(view).Rows[1].RowRevisionAuthor.Should().Be("FreeW User");
        TableOf(view).Rows[1].RowRevisionDateXml.Should().NotBeNullOrWhiteSpace();
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeTrue();
    }

    [StaFact]
    public void RejectAll_AfterTrackedTableRowDeletion_KeepsTheRow()
    {
        var (view, _) = BuildTableView();
        view.TrackChangesEnabled = true;
        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        view.RejectAllRevisions();

        TableOf(view).Rows.Should().HaveCount(3, "rejecting the tracked row deletion keeps the row");
        TableOf(view).Rows[1].RowRevision.Should().Be(RevisionKind.None);
        TableOf(view).Rows[1].Cells[0].PlainText.Should().Be("R1C0");
    }

    [StaFact]
    public void DeleteTableRow_WithTrackChangesOn_RemovesTheAuthorsOwnPendingInsertedRowOutright()
    {
        var (view, table) = BuildTableView();
        view.TrackChangesEnabled = true;
        table.Rows[1].RowRevision = RevisionKind.Inserted;
        table.Rows[1].RowRevisionAuthor = "FreeW User";

        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        table.Rows.Should().HaveCount(2, "taking back your own still-pending inserted row removes it outright");
        table.Rows[1].Cells[0].PlainText.Should().Be("R2C0");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void DeleteTableRow_WithTrackChangesOn_MarksAnotherAuthorsInsertedRowRatherThanRemovingIt()
    {
        var (view, table) = BuildTableView();
        view.TrackChangesEnabled = true;
        table.Rows[1].RowRevision = RevisionKind.Inserted;
        table.Rows[1].RowRevisionAuthor = "Someone Else";

        view.Commands.Execute(new DeleteTableRowCommand(0, 1));

        table.Rows.Should().HaveCount(3, "only your OWN pending insertion may be taken back outright");
        table.Rows[1].RowRevision.Should().Be(RevisionKind.Deleted);
        table.Rows[1].RowRevisionAuthor.Should().Be("FreeW User");
    }

    [StaFact]
    public void Backspace_AcrossParagraphSelection_WithTrackChangesOn_StrikesTextAndMarksBoundariesWithoutMerging()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);   // "rst" + all of "Second" + "Thi"

        view.BackspaceForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(3, "nothing may be physically merged while the deletion is only tracked");
        var first = (Paragraph)view.Model.Blocks[0];
        var second = (Paragraph)view.Model.Blocks[1];
        var third = (Paragraph)view.Model.Blocks[2];

        first.PlainText.Should().Be("First", "the struck text is kept, not removed");
        first.Runs.Should().Contain(r => r.Text == "rst" && r.Revision == RevisionKind.Deleted);
        first.Runs.Should().Contain(r => r.Text == "Fi" && r.Revision == RevisionKind.None);
        second.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Deleted);
        third.Runs.Should().Contain(r => r.Text == "Thi" && r.Revision == RevisionKind.Deleted);
        third.Runs.Should().Contain(r => r.Text == "rd" && r.Revision == RevisionKind.None);

        first.MarkRevision.Should().Be(RevisionKind.Deleted, "the boundary after the first paragraph is inside the selection");
        second.MarkRevision.Should().Be(RevisionKind.Deleted, "so is the boundary after the fully covered middle paragraph");
        third.MarkRevision.Should().Be(RevisionKind.None, "the last paragraph's own mark lies past the selection's end");
    }

    [StaFact]
    public void DeleteForward_AcrossParagraphSelection_WithTrackChangesOn_RecordsTheSameRevisions()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);

        view.DeleteForwardForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(3);
        ((Paragraph)view.Model.Blocks[0]).MarkRevision.Should().Be(RevisionKind.Deleted);
        ((Paragraph)view.Model.Blocks[1]).MarkRevision.Should().Be(RevisionKind.Deleted);
        ((Paragraph)view.Model.Blocks[2]).MarkRevision.Should().Be(RevisionKind.None);
        ((Paragraph)view.Model.Blocks[1]).Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Deleted);
    }

    [StaFact]
    public void TypingOverACrossParagraphSelection_WithTrackChangesOn_MarksOldDeletedAndNewInserted()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);

        view.InsertText("Z");
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(3);
        var first = (Paragraph)view.Model.Blocks[0];
        first.PlainText.Should().Be("FiZrst", "the replacement is inserted at the selection's start, ahead of the struck text");
        first.Runs.Should().Contain(r => r.Text == "Z" && r.Revision == RevisionKind.Inserted);
        first.Runs.Should().Contain(r => r.Text == "rst" && r.Revision == RevisionKind.Deleted);
        first.MarkRevision.Should().Be(RevisionKind.Deleted);
        ((Paragraph)view.Model.Blocks[1]).MarkRevision.Should().Be(RevisionKind.Deleted);
    }

    [StaFact]
    public void Backspace_AcrossParagraphSelection_WithTrackChangesOff_DeletesAndMergesAsBefore()
    {
        // Regression guard: this fix only adds a Track-Changes-on branch. With Track Changes off the
        // native RichTextBox path is untouched.
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.SetSelectionRangeForTest(0, 2, 2, 3);

        view.BackspaceForTest();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(1);
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("Fird");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void AcceptAll_AfterTrackedCrossParagraphDeletion_CollapsesTheSpanIntoOneParagraph()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);
        view.BackspaceForTest();

        view.AcceptAllRevisions();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(1, "accepting drops the struck text and performs both paragraph merges");
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("Fird");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void RejectAll_AfterTrackedCrossParagraphDeletion_RestoresTheOriginalThreeParagraphs()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);
        view.BackspaceForTest();

        view.RejectAllRevisions();
        view.CommitToModel();

        view.Model.Blocks.Should().HaveCount(3);
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("First");
        ((Paragraph)view.Model.Blocks[1]).PlainText.Should().Be("Second");
        ((Paragraph)view.Model.Blocks[2]).PlainText.Should().Be("Third");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse();
    }

    [StaFact]
    public void Undo_AfterTrackedCrossParagraphDeletion_RevertsTheWholeSpanInOneStep()
    {
        var view = BuildThreeParagraphView("First", "Second", "Third");
        view.TrackChangesEnabled = true;
        view.SetSelectionRangeForTest(0, 2, 2, 3);
        view.BackspaceForTest();

        view.Commands.Undo();

        view.Model.Blocks.Should().HaveCount(3);
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("First");
        ((Paragraph)view.Model.Blocks[1]).PlainText.Should().Be("Second");
        ((Paragraph)view.Model.Blocks[2]).PlainText.Should().Be("Third");
        FreeW.Core.Model.TrackChanges.HasRevisions(view.Model).Should().BeFalse(
            "the whole cross-paragraph edit is a single undo group");
    }
}
