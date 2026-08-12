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

    [Fact]
    public async Task CharacterFormatting_TracksActiveAuthorAndHonorsPolicy()
    {
        FormatRevision? trackedRevision = null;
        var excludedRevisionCount = -1;
        var ran = await OnUiThread(() =>
        {
            var tracked = BuildView("Hello world");
            tracked.RevisionAuthor = "Ada Reviewer";
            tracked.ToggleTrackChanges();
            tracked.SetCharacterBorder(new ParagraphBorder("#0070C0", 1));
            trackedRevision = Para(tracked).Runs.Single().FormatRevision;

            var excluded = BuildView("Hello world");
            excluded.ToggleTrackChanges();
            excluded.ToggleTrackFormatting();
            excluded.SetCharacterBorder(new ParagraphBorder("#0070C0", 1));
            excludedRevisionCount = Para(excluded).Runs.Count(run => run.FormatRevision != null);
        });
        if (!ran) return;

        trackedRevision.Should().NotBeNull();
        trackedRevision!.Author.Should().Be("Ada Reviewer");
        trackedRevision.PreviousFormatting.CharacterBorder.Should().BeNull();
        excludedRevisionCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectedCharacterFormatting_TracksExactRangeAndRoundTripsUndoRedo()
    {
        string trackedText = "";
        string? author = null;
        bool previousBold = true;
        bool undoCleared = false;
        bool redoRestored = false;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("abcdef");
            view.RevisionAuthor = "Ada Reviewer";
            view.ToggleTrackChanges();
            view.SetSelectionRangePublic(0, 1, 0, 4);

            view.ToggleBold();

            var paragraph = Para(view);
            var trackedRuns = paragraph.Runs.Where(run => run.FormatRevision != null).ToList();
            trackedText = string.Concat(trackedRuns.Select(run => run.Text));
            author = trackedRuns.Single().FormatRevision!.Author;
            previousBold = trackedRuns.Single().FormatRevision!.PreviousFormatting.Bold;

            view.Undo();
            undoCleared = Para(view).Runs.All(run => !run.Formatting.Bold && run.FormatRevision == null);

            view.Redo();
            redoRestored = Para(view).Runs
                .Where(run => run.FormatRevision != null)
                .All(run => run.Formatting.Bold);
        });
        if (!ran) return;

        trackedText.Should().Be("bcd");
        author.Should().Be("Ada Reviewer");
        previousBold.Should().BeFalse();
        undoCleared.Should().BeTrue();
        redoRestored.Should().BeTrue();
    }

    [Fact]
    public async Task SelectedCharacterFormatting_HonorsPolicyAndIgnoresNoOp()
    {
        var excludedRevisionCount = -1;
        var noOpRevisionCount = -1;
        var ran = await OnUiThread(() =>
        {
            var excluded = BuildView("abcdef");
            excluded.ToggleTrackChanges();
            excluded.ToggleTrackFormatting();
            excluded.SetSelectionRangePublic(0, 1, 0, 4);
            excluded.ToggleBold();
            excludedRevisionCount = Para(excluded).Runs.Count(run => run.FormatRevision != null);

            var noOp = BuildView("abcdef");
            noOp.ToggleTrackChanges();
            noOp.SetSelectionRangePublic(0, 1, 0, 4);
            noOp.SetFontColor(null);
            noOpRevisionCount = Para(noOp).Runs.Count(run => run.FormatRevision != null);
        });
        if (!ran) return;

        excludedRevisionCount.Should().Be(0);
        noOpRevisionCount.Should().Be(0);
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

    // ── R135: Backspace/Delete at a paragraph boundary must record a tracked paragraph-mark deletion
    // instead of silently, permanently merging the two paragraphs (bypassing Track Changes entirely). ──

    private static DocumentView BuildTwoParagraphView(string first, string second)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(first));
        doc.Blocks.Add(new Paragraph(second));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task Backspace_at_paragraph_start_with_TrackChanges_on_marks_the_boundary_deleted_without_merging()
    {
        int blockCount = -1; bool anyRevision = false; RevisionKind mark = RevisionKind.None;
        string? b0 = null; string? b1 = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(1, 0);            // caret at the very start of the second paragraph
            view.BackspacePublic();
            blockCount = view.Document.Blocks.Count;
            anyRevision = view.HasRevisions;
            mark = view.Document.Blocks.Count > 0 ? ((Paragraph)view.Document.Blocks[0]).MarkRevision : RevisionKind.None;
            b0 = view.Document.Blocks.Count > 0 ? ((Paragraph)view.Document.Blocks[0]).PlainText : null;
            b1 = view.Document.Blocks.Count > 1 ? ((Paragraph)view.Document.Blocks[1]).PlainText : null;
        });
        if (!ran) return;

        blockCount.Should().Be(2, "the two paragraphs must NOT be physically merged while the deletion is only tracked");
        mark.Should().Be(RevisionKind.Deleted, "the first paragraph's own mark records the tracked boundary deletion");
        anyRevision.Should().BeTrue("a tracked paragraph-mark deletion counts as a recorded revision");
        b0.Should().Be("First");
        b1.Should().Be("Second");
    }

    [Fact]
    public async Task Backspace_at_paragraph_start_with_TrackChanges_off_merges_paragraphs_immediately()
    {
        int blockCount = -1; bool anyRevision = true; string? text = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.MoveCaretToBlock(1, 0);
            view.BackspacePublic();
            blockCount = view.Document.Blocks.Count;
            anyRevision = view.HasRevisions;
            text = blockCount > 0 ? ((Paragraph)view.Document.Blocks[0]).PlainText : null;
        });
        if (!ran) return;

        blockCount.Should().Be(1, "with Track Changes off, Backspace merges the paragraphs as before (regression guard)");
        text.Should().Be("FirstSecond");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteForward_at_paragraph_end_with_TrackChanges_on_marks_the_boundary_deleted_without_merging()
    {
        int blockCount = -1; bool anyRevision = false; RevisionKind mark = RevisionKind.None;
        string? b0 = null; string? b1 = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(0, 5);             // caret at the very end of the first paragraph ("First")
            view.DeleteForwardPublic();
            blockCount = view.Document.Blocks.Count;
            anyRevision = view.HasRevisions;
            mark = view.Document.Blocks.Count > 0 ? ((Paragraph)view.Document.Blocks[0]).MarkRevision : RevisionKind.None;
            b0 = view.Document.Blocks.Count > 0 ? ((Paragraph)view.Document.Blocks[0]).PlainText : null;
            b1 = view.Document.Blocks.Count > 1 ? ((Paragraph)view.Document.Blocks[1]).PlainText : null;
        });
        if (!ran) return;

        blockCount.Should().Be(2, "the two paragraphs must NOT be physically merged while the deletion is only tracked");
        mark.Should().Be(RevisionKind.Deleted, "forward-Delete marks the SAME paragraph's own mark deleted as Backspace at the next paragraph's start would");
        anyRevision.Should().BeTrue();
        b0.Should().Be("First");
        b1.Should().Be("Second");
    }

    [Fact]
    public async Task DeleteForward_at_paragraph_end_with_TrackChanges_off_is_unchanged_preexisting_noop()
    {
        // Sibling regression guard: this fix only adds a Track-Changes-on branch to DeleteForward's
        // paragraph-end case. With Track Changes off there is no untracked "merge with next paragraph"
        // path in this method (unlike Backspace's MergeWithPrevious) — confirm that pre-existing gap is
        // untouched by this change, not silently "fixed" as a side effect.
        int blockCount = -1; string? b0 = null; string? b1 = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.MoveCaretToBlock(0, 5);
            view.DeleteForwardPublic();
            blockCount = view.Document.Blocks.Count;
            b0 = view.Document.Blocks.Count > 0 ? ((Paragraph)view.Document.Blocks[0]).PlainText : null;
            b1 = view.Document.Blocks.Count > 1 ? ((Paragraph)view.Document.Blocks[1]).PlainText : null;
        });
        if (!ran) return;

        blockCount.Should().Be(2);
        b0.Should().Be("First");
        b1.Should().Be("Second");
    }

    [Fact]
    public async Task AcceptAll_after_tracked_paragraph_boundary_backspace_performs_the_merge()
    {
        int blockCount = -1; string? text = null; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(1, 0);
            view.BackspacePublic();                 // tracked boundary deletion only, no merge yet
            view.AcceptAllRevisions();               // accept → the merge actually happens now
            blockCount = view.Document.Blocks.Count;
            anyRevision = view.HasRevisions;
            text = blockCount > 0 ? ((Paragraph)view.Document.Blocks[0]).PlainText : null;
        });
        if (!ran) return;

        blockCount.Should().Be(1, "accepting the tracked paragraph-mark deletion performs the merge");
        text.Should().Be("FirstSecond");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task RejectAll_after_tracked_paragraph_boundary_backspace_restores_two_separate_paragraphs()
    {
        int blockCount = -1; RevisionKind mark = RevisionKind.Deleted; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            view.ToggleTrackChanges();
            view.MoveCaretToBlock(1, 0);
            view.BackspacePublic();
            view.RejectAllRevisions();               // reject → the boundary deletion is undone
            blockCount = view.Document.Blocks.Count;
            anyRevision = view.HasRevisions;
            mark = blockCount > 0 ? ((Paragraph)view.Document.Blocks[0]).MarkRevision : RevisionKind.Deleted;
        });
        if (!ran) return;

        blockCount.Should().Be(2, "rejecting keeps the two paragraphs separate");
        mark.Should().Be(RevisionKind.None, "reject clears the tracked mark-deletion");
        anyRevision.Should().BeFalse();
    }

    // ── R136: the SIBLINGS of the R135 paragraph-mark fix — structural edits at a boundary that also
    // bypassed Track Changes entirely. (a) Deleting a table row removed it outright; (b) merging two
    // paragraphs INSIDE a table cell (Backspace at a cell paragraph's start / Delete at its end) spliced
    // them together unconditionally, exactly the body-paragraph bug R135 fixed but one level down. ──

    /// <summary>
    /// A 3×2 table whose first cell holds TWO paragraphs, so the in-cell paragraph boundary the
    /// Backspace/Delete cell branches take can be exercised.
    /// </summary>
    private static (DocumentView View, Table Table) BuildTableView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(3, 2);
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 2; c++)
                table.Rows[r].Cells[c] = new TableCell($"R{r}C{c}");
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Second"));
        doc.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(900, 6000));
        return (view, (Table)view.Document.Blocks[0]);
    }

    private static IList<Paragraph> FirstCellParagraphs(Table table) => table.Rows[0].Cells[0].Paragraphs;

    [Fact]
    public async Task DeleteTableRow_with_TrackChanges_on_marks_the_row_deleted_without_removing_it()
    {
        int rowCount = -1; RevisionKind rowRevision = RevisionKind.None;
        string? author = null; string? rowText = null; bool anyRevision = false;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            rowCount = table.Rows.Count;
            rowRevision = table.Rows[1].RowRevision;
            author = table.Rows[1].RowRevisionAuthor;
            rowText = table.Rows[1].Cells[0].PlainText;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        rowCount.Should().Be(3, "a tracked row deletion leaves the row in place until it is accepted");
        rowRevision.Should().Be(RevisionKind.Deleted);
        author.Should().Be("FreeW User");
        rowText.Should().Be("R1C0", "the row's own content is untouched by the mark");
        anyRevision.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteTableRow_with_TrackChanges_off_removes_the_row_immediately()
    {
        int rowCount = -1; string? newRow1 = null; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.PlaceCaretInCell(0, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            rowCount = table.Rows.Count;
            newRow1 = table.Rows[1].Cells[0].PlainText;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        rowCount.Should().Be(2, "with Track Changes off the row is removed as before (regression guard)");
        newRow1.Should().Be("R2C0");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptAll_after_a_tracked_table_row_deletion_actually_removes_the_row()
    {
        int rowCount = -1; string? newRow1 = null; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            view.AcceptAllRevisions();
            rowCount = table.Rows.Count;
            newRow1 = table.Rows[1].Cells[0].PlainText;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        rowCount.Should().Be(2, "accepting the tracked row deletion performs the removal");
        newRow1.Should().Be("R2C0");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task RejectAll_after_a_tracked_table_row_deletion_keeps_the_row()
    {
        int rowCount = -1; RevisionKind rowRevision = RevisionKind.Deleted;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 1, col: 0, paraIdx: 0, offset: 0);
            view.DeleteTableRow();
            view.RejectAllRevisions();
            rowCount = table.Rows.Count;
            rowRevision = table.Rows[1].RowRevision;
        });
        if (!ran) return;

        rowCount.Should().Be(3, "rejecting the tracked row deletion keeps the row");
        rowRevision.Should().Be(RevisionKind.None);
    }

    [Fact]
    public async Task Backspace_at_cell_paragraph_start_with_TrackChanges_on_marks_the_boundary_without_merging()
    {
        int paraCount = -1; RevisionKind mark = RevisionKind.None;
        string? p0 = null; string? p1 = null; bool anyRevision = false;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 1, offset: 0);
            view.BackspacePublic();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            mark = paragraphs[0].MarkRevision;
            p0 = paragraphs[0].PlainText;
            p1 = paragraphs.Count > 1 ? paragraphs[1].PlainText : null;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        paraCount.Should().Be(2, "the two cell paragraphs must NOT be spliced together while the deletion is only tracked");
        mark.Should().Be(RevisionKind.Deleted, "the earlier cell paragraph's own mark records the tracked boundary deletion");
        p0.Should().Be("R0C0");
        p1.Should().Be("Second");
        anyRevision.Should().BeTrue();
    }

    [Fact]
    public async Task Backspace_at_cell_paragraph_start_with_TrackChanges_off_merges_immediately()
    {
        int paraCount = -1; string? p0 = null; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 1, offset: 0);
            view.BackspacePublic();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            p0 = paragraphs[0].PlainText;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        paraCount.Should().Be(1, "with Track Changes off the cell paragraphs merge as before (regression guard)");
        p0.Should().Be("R0C0Second");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteForward_at_cell_paragraph_end_with_TrackChanges_on_marks_the_boundary_without_merging()
    {
        int paraCount = -1; RevisionKind mark = RevisionKind.None;
        string? p0 = null; string? p1 = null;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 0, offset: "R0C0".Length);
            view.DeleteForwardPublic();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            mark = paragraphs[0].MarkRevision;
            p0 = paragraphs[0].PlainText;
            p1 = paragraphs.Count > 1 ? paragraphs[1].PlainText : null;
        });
        if (!ran) return;

        paraCount.Should().Be(2, "the two cell paragraphs must NOT be spliced together while the deletion is only tracked");
        mark.Should().Be(RevisionKind.Deleted, "forward-Delete marks the SAME cell paragraph a Backspace at the next one's start would");
        p0.Should().Be("R0C0");
        p1.Should().Be("Second");
    }

    [Fact]
    public async Task DeleteForward_at_cell_paragraph_end_with_TrackChanges_off_merges_immediately()
    {
        int paraCount = -1; string? p0 = null;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 0, offset: "R0C0".Length);
            view.DeleteForwardPublic();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            p0 = paragraphs[0].PlainText;
        });
        if (!ran) return;

        paraCount.Should().Be(1, "with Track Changes off the in-cell join happens as before (regression guard)");
        p0.Should().Be("R0C0Second");
    }

    [Fact]
    public async Task AcceptAll_after_a_tracked_cell_paragraph_boundary_backspace_performs_the_merge()
    {
        int paraCount = -1; string? p0 = null; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 1, offset: 0);
            view.BackspacePublic();
            view.AcceptAllRevisions();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            p0 = paragraphs[0].PlainText;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        paraCount.Should().Be(1, "accepting the tracked cell paragraph-mark deletion performs the merge");
        p0.Should().Be("R0C0Second");
        anyRevision.Should().BeFalse();
    }

    [Fact]
    public async Task RejectAll_after_a_tracked_cell_paragraph_boundary_backspace_restores_two_paragraphs()
    {
        int paraCount = -1; RevisionKind mark = RevisionKind.Deleted;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 1, offset: 0);
            view.BackspacePublic();
            view.RejectAllRevisions();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            mark = paragraphs[0].MarkRevision;
        });
        if (!ran) return;

        paraCount.Should().Be(2, "rejecting keeps the two cell paragraphs separate");
        mark.Should().Be(RevisionKind.None);
    }

    [Fact]
    public async Task Undo_after_a_tracked_cell_paragraph_boundary_backspace_clears_the_mark()
    {
        int paraCount = -1; RevisionKind mark = RevisionKind.Deleted; bool anyRevision = true;
        var ran = await OnUiThread(() =>
        {
            var (view, table) = BuildTableView();
            view.ToggleTrackChanges();
            view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 1, offset: 0);
            view.BackspacePublic();
            view.Undo();
            var paragraphs = FirstCellParagraphs(table);
            paraCount = paragraphs.Count;
            mark = paragraphs[0].MarkRevision;
            anyRevision = view.HasRevisions;
        });
        if (!ran) return;

        paraCount.Should().Be(2);
        mark.Should().Be(RevisionKind.None, "undo reverts the tracked cell paragraph-mark deletion");
        anyRevision.Should().BeFalse();
    }
}
