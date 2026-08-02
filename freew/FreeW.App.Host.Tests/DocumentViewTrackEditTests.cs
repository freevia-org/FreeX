using FreeW.App.Host.Editing;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

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
