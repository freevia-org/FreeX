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
