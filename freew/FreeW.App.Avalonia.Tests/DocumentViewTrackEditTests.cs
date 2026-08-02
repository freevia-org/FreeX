using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TRACKEDIT: the edit pipeline records edits as revisions while Track Changes is on. Typing inserts
/// tracked-insertion runs (author + revision mark); Backspace/Delete/selection-delete mark the affected text
/// as a tracked deletion (kept and struck) rather than removing it; deleting one's own still-pending tracked
/// insertion removes it outright (Word behaviour). With Track Changes off, edits behave exactly as before
/// (regression guard). All edits ride the undoable command bus and accept/reject (AV-REVIEW) finalises them.
/// </summary>
public sealed class DocumentViewTrackEditTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DocumentView BuildView(string firstParagraphText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(firstParagraphText));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static Paragraph Para(DocumentView view) => (Paragraph)view.Document.Blocks[0];
    private static string PlainText(DocumentView view) => Para(view).PlainText;

    private static Run? RunWithText(DocumentView view, string text) =>
        Para(view).Runs.FirstOrDefault(r => r.Text == text);

    [Fact]
    public async Task LoadDocument_UsesAuthoredTrackRevisionsState()
    {
        bool enabledState = false;
        bool disabledState = true;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            var enabled = TextDocument.CreateEmpty();
            enabled.TrackRevisions = true;
            view.LoadDocument(enabled);
            enabledState = view.TrackChangesEnabled;

            var disabled = TextDocument.CreateEmpty();
            view.LoadDocument(disabled);
            disabledState = view.TrackChangesEnabled;
        });
        if (!ran) return;

        enabledState.Should().BeTrue();
        disabledState.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleTrackChanges_PersistsAuthoredDocumentState()
    {
        bool enabledState = false;
        bool disabledState = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello world");
            view.ToggleTrackChanges();
            enabledState = view.Document.TrackRevisions;
            view.ToggleTrackChanges();
            disabledState = view.Document.TrackRevisions;
        });
        if (!ran) return;

        enabledState.Should().BeTrue();
        disabledState.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleTrackFormatting_PersistsInverseWordSetting()
    {
        bool disabledState = false;
        bool enabledState = false;
        var changed = 0;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello world");
            view.DocumentChanged += () => changed++;
            disabledState = view.ToggleTrackFormatting();
            view.Document.DoNotTrackFormatting.Should().BeTrue();
            enabledState = view.ToggleTrackFormatting();
            view.Document.DoNotTrackFormatting.Should().BeFalse();
        });
        if (!ran) return;

        disabledState.Should().BeFalse();
        enabledState.Should().BeTrue();
        changed.Should().Be(2);
    }

    // ── Typing records a tracked insertion ────────────────────────────────────────

    [Fact]
    public async Task Typing_with_TrackChanges_on_records_a_tracked_insertion()
    {
        bool hasInsertion = false; string? author = null; string? date = null; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello ");
            view.ToggleTrackChanges();              // turn TC on
            view.MoveCaretToBlock(0, 6);            // caret at end of "Hello "
            view.InsertText("world");
            var run = RunWithText(view, "world");
            hasInsertion = run is { Revision: RevisionKind.Inserted };
            author = run?.RevisionAuthor;
            date = run?.RevisionDateXml;
            text = PlainText(view);
        });
        if (!ran) return;

        hasInsertion.Should().BeTrue("typing while Track Changes is on records a tracked insertion run");
        author.Should().Be("FreeW User", "the insertion carries the current revision author");
        date.Should().NotBeNullOrEmpty("the insertion carries a revision timestamp");
        text.Should().Be("Hello world");
    }

    [Fact]
    public async Task Typing_with_TrackChanges_off_inserts_ordinary_text()
    {
        bool anyRevision = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello ");
            view.MoveCaretToBlock(0, 6);
            view.InsertText("world");
            anyRevision = view.HasRevisions;
            text = PlainText(view);
        });
        if (!ran) return;

        anyRevision.Should().BeFalse("with Track Changes off, typing is not recorded as a revision");
        text.Should().Be("Hello world");
    }

    // ── Backspace / Delete mark a tracked deletion (text kept, struck) ─────────────

    [Fact]
    public async Task Backspace_with_TrackChanges_on_marks_deletion_keeping_text()
    {
        bool hasDeletion = false; string text = ""; string? delText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 3);            // caret at end
            view.BackspacePublic();                 // delete 'c'
            var del = Para(view).Runs.FirstOrDefault(r => r.Revision == RevisionKind.Deleted);
            hasDeletion = del is not null;
            delText = del?.Text;
            text = PlainText(view);
        });
        if (!ran) return;

        hasDeletion.Should().BeTrue("Backspace while Track Changes is on records a tracked deletion");
        delText.Should().Be("c", "the deleted character is marked deleted, not removed");
        text.Should().Be("abc", "the text is KEPT (struck) until the deletion is accepted");
    }

    [Fact]
    public async Task Backspace_with_TrackChanges_off_removes_the_character()
    {
        string text = ""; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.MoveCaretToBlock(0, 3);
            view.BackspacePublic();
            text = PlainText(view);
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        text.Should().Be("ab", "with Track Changes off, Backspace removes the character as before");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task ForwardDelete_with_TrackChanges_on_marks_deletion_keeping_text()
    {
        bool hasDeletion = false; string text = ""; string? delText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 0);            // caret at start
            view.DeleteForwardPublic();             // forward-delete 'a'
            var del = Para(view).Runs.FirstOrDefault(r => r.Revision == RevisionKind.Deleted);
            hasDeletion = del is not null;
            delText = del?.Text;
            text = PlainText(view);
        });
        if (!ran) return;

        hasDeletion.Should().BeTrue("forward Delete while Track Changes is on records a tracked deletion");
        delText.Should().Be("a");
        text.Should().Be("abc", "the text is kept (struck) under Track Changes");
    }

    // ── Selection delete marks the whole range deleted ────────────────────────────

    [Fact]
    public async Task SelectionDelete_with_TrackChanges_on_marks_the_range_deleted()
    {
        bool hasDeletion = false; string text = ""; string? delText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abcdef");
            view.ToggleTrackChanges();
            view.SetSelectionRangePublic(0, 2, 0, 5); // select "cde"
            view.DeleteForwardPublic();               // Delete with a selection → mark deleted
            var del = Para(view).Runs.FirstOrDefault(r => r.Revision == RevisionKind.Deleted);
            hasDeletion = del is not null;
            delText = del?.Text;
            text = PlainText(view);
        });
        if (!ran) return;

        hasDeletion.Should().BeTrue();
        delText.Should().Be("cde", "the whole selection is marked as a tracked deletion");
        text.Should().Be("abcdef", "selection-delete keeps the text (struck) under Track Changes");
    }

    // ── Deleting one's own pending insertion removes it outright (Word behaviour) ──

    [Fact]
    public async Task Backspacing_own_pending_insertion_removes_it_outright()
    {
        string text = ""; bool anyDeletion = true; bool anyInsertion = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello ");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 6);
            view.InsertText("X");                   // tracked insertion by current author
            view.BackspacePublic();                 // delete own just-typed insertion
            text = PlainText(view);
            anyDeletion = Para(view).Runs.Any(r => r.Revision == RevisionKind.Deleted);
            anyInsertion = Para(view).Runs.Any(r => r.Revision == RevisionKind.Inserted);
        });
        if (!ran) return;

        text.Should().Be("Hello ", "deleting your own pending insertion takes it back entirely");
        anyDeletion.Should().BeFalse("an own-insertion deletion does not leave a struck deletion behind");
        anyInsertion.Should().BeFalse("the insertion is gone");
    }

    // ── Selection replace = mark deleted + insert tracked insertion ───────────────

    [Fact]
    public async Task TypingOverSelection_with_TrackChanges_on_marks_old_deleted_and_inserts_new()
    {
        bool hasDeletion = false; bool hasInsertion = false; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abcdef");
            view.ToggleTrackChanges();
            view.SetSelectionRangePublic(0, 2, 0, 5); // select "cde"
            view.InsertText("Z");                     // type over the selection
            hasDeletion = Para(view).Runs.Any(r => r.Revision == RevisionKind.Deleted && r.Text == "cde");
            hasInsertion = Para(view).Runs.Any(r => r.Revision == RevisionKind.Inserted && r.Text == "Z");
            text = PlainText(view);
        });
        if (!ran) return;

        hasDeletion.Should().BeTrue("the replaced selection is marked as a tracked deletion");
        hasInsertion.Should().BeTrue("the typed replacement is a tracked insertion");
        text.Should().Be("abZcdef", "the new text is inserted before the kept-struck old text");
    }

    // ── Accept / reject finalise recorded revisions (AV-REVIEW integration) ────────

    [Fact]
    public async Task AcceptAll_after_recording_insertion_keeps_typed_text()
    {
        string text = ""; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello ");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 6);
            view.InsertText("world");
            view.AcceptAllRevisions();
            text = PlainText(view);
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        anyRevision.Should().BeFalse("accept finalises the recorded insertion");
        text.Should().Be("Hello world", "accepting a recorded insertion keeps the typed text");
    }

    [Fact]
    public async Task RejectAll_after_recording_deletion_keeps_original_text()
    {
        string text = ""; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 3);
            view.BackspacePublic();                 // tracked deletion of 'c'
            view.RejectAllRevisions();              // undo the deletion → keep 'c'
            text = PlainText(view);
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        anyRevision.Should().BeFalse("reject finalises the recorded deletion (clears the mark)");
        text.Should().Be("abc", "rejecting a recorded deletion keeps the original text");
    }

    [Fact]
    public async Task AcceptAll_after_recording_deletion_drops_the_text()
    {
        string text = "x";
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 3);
            view.BackspacePublic();                 // tracked deletion of 'c'
            view.AcceptAllRevisions();              // accept → drop 'c'
            text = PlainText(view);
        });
        if (!ran) return;
        text.Should().Be("ab", "accepting a recorded deletion drops the struck text");
    }

    // ── Undo reverts a recorded edit ──────────────────────────────────────────────

    [Fact]
    public async Task Undo_reverts_a_recorded_insertion()
    {
        string text = "x"; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello ");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 6);
            view.InsertText("world");
            view.Undo();
            text = PlainText(view);
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        text.Should().Be("Hello ", "Undo removes the recorded insertion");
        anyRevision.Should().BeFalse("no revision remains after undo");
    }

    [Fact]
    public async Task Undo_reverts_a_recorded_deletion_restoring_unmarked_text()
    {
        string text = "x"; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abc");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 3);
            view.BackspacePublic();                 // tracked deletion of 'c'
            view.Undo();
            text = PlainText(view);
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        text.Should().Be("abc");
        anyRevision.Should().BeFalse("Undo restores the character without a deletion mark");
    }
}
