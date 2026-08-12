using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-REVIEW: Review-tab wiring — tracked-change accept/reject (single + all), Track Changes toggle,
/// comment add/delete via the ribbon-backed DocumentView methods, word count, command resolution and undo.
/// Accept/reject ride the undoable DocumentCommandBus; comments reuse the AV-COMMENT infra; word count
/// reads DocumentStatistics from the model.
/// </summary>
public sealed class DocumentViewReviewTests
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

    // A paragraph: "Hello " (plain) + "world" (tracked insertion by Ann).
    private static TextDocument DocWithInsertion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Hello ", RunFormatting.Default));
        p.Runs.Add(new Run("world", RunFormatting.Default)
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Ann",
            RevisionDateXml = "2024-01-01T00:00:00Z",
        });
        doc.Blocks.Add(p);
        return doc;
    }

    // A paragraph: "Keep " (plain) + "gone" (tracked deletion).
    private static TextDocument DocWithDeletion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Keep ", RunFormatting.Default));
        p.Runs.Add(new Run("gone", RunFormatting.Default)
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Ann",
        });
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument DocWithInsertionAndDeletion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Keep ", RunFormatting.Default));
        p.Runs.Add(new Run("added", RunFormatting.Default) { Revision = RevisionKind.Inserted, RevisionAuthor = "Ann" });
        p.Runs.Add(new Run(" ", RunFormatting.Default));
        p.Runs.Add(new Run("gone", RunFormatting.Default) { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        doc.Blocks.Add(p);
        return doc;
    }

    private static TextDocument DocWithComment()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("note", RunFormatting.Default) { CommentId = 1 });
        p.Runs.Add(Run.CommentReference(1));
        doc.Blocks.Add(p);
        doc.Comments[1] = new Comment(1, "comment", "Ann", "A");
        return doc;
    }

    private static TextDocument DocWithFormatRevision()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("bold", RunFormatting.Default with { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Ann", "2024-01-01T00:00:00Z")
        });
        doc.Blocks.Add(p);
        return doc;
    }

    private static DocumentView Build(TextDocument doc)
    {
        var view = new DocumentView(new CustomDictionaryStore(null));
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string PlainText(DocumentView view) => ((Paragraph)view.Document.Blocks[0]).PlainText;
    private static bool HasInsertion(DocumentView view) =>
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Revision == RevisionKind.Inserted);
    private static bool HasDeletion(DocumentView view) =>
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Revision == RevisionKind.Deleted);

    // ── Accept ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptCurrent_clears_insertion_mark_keeping_text()
    {
        bool resolved = false, hadInsertion = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            resolved = view.AcceptCurrentRevision();
            hadInsertion = HasInsertion(view);
            text = PlainText(view);
        });
        if (!ran) return;

        resolved.Should().BeTrue();
        hadInsertion.Should().BeFalse("accepting an insertion clears its revision mark");
        text.Should().Be("Hello world", "the inserted text is kept as ordinary text");
    }

    [Fact]
    public async Task RejectCurrent_removes_inserted_text()
    {
        bool resolved = false; string text = "x";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            resolved = view.RejectCurrentRevision();
            text = PlainText(view);
        });
        if (!ran) return;

        resolved.Should().BeTrue();
        text.Should().Be("Hello ", "rejecting an insertion removes the inserted run");
    }

    [Fact]
    public async Task AcceptCurrent_on_deletion_removes_text()
    {
        string text = "x";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithDeletion());
            view.AcceptCurrentRevision();
            text = PlainText(view);
        });
        if (!ran) return;
        text.Should().Be("Keep ", "accepting a deletion drops the deleted run");
    }

    [Fact]
    public async Task RejectCurrent_on_deletion_keeps_text()
    {
        string text = "x"; bool hadDeletion = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithDeletion());
            view.RejectCurrentRevision();
            text = PlainText(view);
            hadDeletion = HasDeletion(view);
        });
        if (!ran) return;
        text.Should().Be("Keep gone", "rejecting a deletion restores it as ordinary text");
        hadDeletion.Should().BeFalse("the deletion mark is cleared");
    }

    // ── Accept-all / Reject-all ───────────────────────────────────────────────────

    [Fact]
    public async Task AcceptAll_clears_every_revision()
    {
        bool anyRevision = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptAllRevisions();
            anyRevision = view.HasRevisions;
            text = PlainText(view);
        });
        if (!ran) return;
        anyRevision.Should().BeFalse("accept-all resolves every tracked change");
        text.Should().Be("Hello world");
    }

    [Fact]
    public async Task RejectAll_clears_every_revision_and_drops_insertions()
    {
        bool anyRevision = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.RejectAllRevisions();
            anyRevision = view.HasRevisions;
            text = PlainText(view);
        });
        if (!ran) return;
        anyRevision.Should().BeFalse("reject-all resolves every tracked change");
        text.Should().Be("Hello ", "reject-all drops the inserted text");
    }

    [Fact]
    public async Task AcceptAll_on_clean_document_returns_false()
    {
        bool resolved = true;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("No changes here"));
            var view = Build(doc);
            resolved = view.AcceptAllRevisions();
        });
        if (!ran) return;
        resolved.Should().BeFalse("a clean document has nothing to accept");
    }

    // ── Undo ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_reverts_AcceptCurrent()
    {
        bool insertionAfterUndo = false; string textAfterUndo = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptCurrentRevision();
            view.Undo();
            insertionAfterUndo = HasInsertion(view);
            textAfterUndo = PlainText(view);
        });
        if (!ran) return;
        insertionAfterUndo.Should().BeTrue("Undo restores the tracked insertion mark");
        textAfterUndo.Should().Be("Hello world");
    }

    [Fact]
    public async Task Undo_reverts_RejectCurrent_restoring_inserted_text()
    {
        string textAfterUndo = ""; bool insertionAfterUndo = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.RejectCurrentRevision();      // removes "world"
            view.Undo();
            textAfterUndo = PlainText(view);
            insertionAfterUndo = HasInsertion(view);
        });
        if (!ran) return;
        textAfterUndo.Should().Be("Hello world", "Undo restores the removed inserted run");
        insertionAfterUndo.Should().BeTrue("Undo restores its insertion mark");
    }

    [Fact]
    public async Task Undo_reverts_AcceptAll()
    {
        bool revisionsAfterUndo = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptAllRevisions();
            view.Undo();
            revisionsAfterUndo = view.HasRevisions;
        });
        if (!ran) return;
        revisionsAfterUndo.Should().BeTrue("Undo restores every revision accept-all resolved");
    }

    [Fact]
    public async Task Shared_table_cell_revision_target_keeps_Avalonia_resolution_undoable()
    {
        bool resolved = false, tableRevisionAfterAccept = true, tableRevisionAfterUndo = false;
        var ran = await OnUiThread(() =>
        {
            var document = new TextDocument();
            document.Blocks.Add(new Paragraph("before"));
            var table = Table.Create(1, 1);
            var tableParagraph = table.Rows[0].Cells[0].Paragraphs[0];
            tableParagraph.Runs.Clear();
            tableParagraph.Runs.Add(new Run("table change") { Revision = RevisionKind.Inserted });
            document.Blocks.Add(table);
            var after = new Paragraph();
            after.Runs.Add(new Run("later change") { Revision = RevisionKind.Inserted });
            document.Blocks.Add(after);

            var view = Build(document);
            view.MoveCaretToBlock(1, 0);
            resolved = view.AcceptCurrentRevision();
            tableRevisionAfterAccept = tableParagraph.Runs.Any(run => run.Revision != RevisionKind.None);
            view.Undo();
            tableRevisionAfterUndo = tableParagraph.Runs.Any(run => run.Revision == RevisionKind.Inserted);
        });
        if (!ran) return;

        resolved.Should().BeTrue();
        tableRevisionAfterAccept.Should().BeFalse();
        tableRevisionAfterUndo.Should().BeTrue(
            "the renderer must still execute the shared target through its undoable command bus");
    }

    // ── Track Changes toggle + mark selection ─────────────────────────────────────

    [Fact]
    public async Task ToggleTrackChanges_flips_flag()
    {
        bool first = false, second = true, defaultOff = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            defaultOff = view.TrackChangesEnabled;
            first = view.ToggleTrackChanges();
            second = view.ToggleTrackChanges();
        });
        if (!ran) return;
        defaultOff.Should().BeFalse("Track Changes is off by default");
        first.Should().BeTrue("first toggle turns it on");
        second.Should().BeFalse("second toggle turns it off");
    }

    [Fact]
    public async Task RibbonTrackChanges_EnablingOverSelection_marks_exactly_that_selection()
    {
        bool ran = false;
        bool enabled = false;
        bool checkedAfter = false;
        string text = "";
        int insertionCount = -1;
        string? author = null;
        string? date = null;
        bool undoPreservedText = false;
        bool undoRemovedInsertion = false;

        ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 6, 0, 11);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();
            var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

            command!.Execute(RibbonCommandContext.Empty);

            var paragraph = (Paragraph)view.Document.Blocks[0];
            text = paragraph.PlainText;
            var insertions = paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted).ToList();
            insertionCount = insertions.Count;
            author = insertions.SingleOrDefault()?.RevisionAuthor;
            date = insertions.SingleOrDefault()?.RevisionDateXml;
            enabled = view.TrackChangesEnabled;
            checkedAfter = stateful.GetState().IsChecked;

            view.Undo();
            var undone = (Paragraph)view.Document.Blocks[0];
            undoPreservedText = undone.PlainText == "Hello world";
            undoRemovedInsertion = undone.Runs.All(run => run.Revision == RevisionKind.None);
        });

        ran.Should().BeTrue();
        enabled.Should().BeTrue();
        checkedAfter.Should().BeTrue();
        text.Should().Be("Hello world");
        insertionCount.Should().Be(1);
        author.Should().Be("FreeW User");
        date.Should().NotBeNullOrWhiteSpace();
        date.Should().MatchRegex("^\\d{4}-\\d{2}-\\d{2}T");
        undoPreservedText.Should().BeTrue();
        undoRemovedInsertion.Should().BeTrue();
    }

    [Fact]
    public async Task RibbonTrackChanges_empty_selection_does_not_invent_revision_and_disabling_does_not_mark()
    {
        bool ran = false;
        bool emptyRevision = true;
        bool disabledRevision = true;
        bool enabledChecked = false;
        bool disabledChecked = true;

        ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.track-changes"), out var command).Should().BeTrue();
            var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

            view.MoveCaretToBlock(0, 6);
            command!.Execute(RibbonCommandContext.Empty);
            enabledChecked = stateful.GetState().IsChecked;
            emptyRevision = ((Paragraph)view.Document.Blocks[0]).Runs.All(run => run.Revision == RevisionKind.None);

            view.SetSelectionRangePublic(0, 6, 0, 11);
            command.Execute(RibbonCommandContext.Empty);
            disabledChecked = stateful.GetState().IsChecked;
            disabledRevision = ((Paragraph)view.Document.Blocks[0]).Runs.All(run => run.Revision == RevisionKind.None);
        });

        ran.Should().BeTrue();
        enabledChecked.Should().BeTrue();
        disabledChecked.Should().BeFalse();
        emptyRevision.Should().BeTrue();
        disabledRevision.Should().BeTrue();
    }

    [Fact]
    public async Task MarkSelectionAsRevision_records_an_insertion()
    {
        bool marked = false; bool hasInsertion = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 6, 0, 11); // "world"
            marked = view.MarkSelectionAsRevision(RevisionKind.Inserted);
            hasInsertion = ((Paragraph)view.Document.Blocks[0]).Runs
                .Any(r => r.Revision == RevisionKind.Inserted && r.Text == "world");
        });
        if (!ran) return;
        marked.Should().BeTrue();
        hasInsertion.Should().BeTrue("the selected range is marked as a tracked insertion");
    }

    [Fact]
    public async Task Undo_reverts_MarkSelectionAsRevision()
    {
        bool hasInsertionAfterUndo = true;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 6, 0, 11);
            view.MarkSelectionAsRevision(RevisionKind.Inserted);
            view.Undo();
            hasInsertionAfterUndo = ((Paragraph)view.Document.Blocks[0]).Runs
                .Any(r => r.Revision == RevisionKind.Inserted);
        });
        if (!ran) return;
        hasInsertionAfterUndo.Should().BeFalse("Undo removes the tracked-change mark");
    }

    // ── Comments via ribbon-backed methods ────────────────────────────────────────

    [Fact]
    public async Task DisplayForReview_Defaults_to_all_markup_policy()
    {
        ReviewDisplayMode mode = ReviewDisplayMode.NoMarkup;
        ReviewDisplayPolicy policy = default;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertionAndDeletion());
            mode = view.DisplayForReview;
            policy = view.CurrentReviewDisplayPolicy;
        });
        if (!ran) return;

        mode.Should().Be(ReviewDisplayMode.AllMarkup);
        policy.Should().Be(ReviewDisplayPolicy.Default);
    }

    [Fact]
    public async Task CurrentReviewWorkflowStatus_UsesSharedReviewPlanner()
    {
        int revisions = 0, comments = 0, visible = 0;
        bool hasHiddenMarkup = false;
        string displayLabel = "";
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInsertionAndDeletion();
            var paragraph = (Paragraph)doc.Blocks[0];
            paragraph.Runs.Add(new Run(" note") { CommentId = 5 });
            doc.Comments[5] = new Comment(5, "Comment", "Bob");

            var view = Build(doc);
            view.ToggleTrackChanges();
            view.ApplyShowMarkupInsertionsAndDeletions(false);

            var status = view.CurrentReviewWorkflowStatus;
            revisions = status.RevisionCount;
            comments = status.CommentThreadCount;
            visible = status.VisibleReviewItemCount;
            hasHiddenMarkup = status.HasHiddenMarkup;
            displayLabel = status.DisplayModeLabel;
        });
        if (!ran) return;

        revisions.Should().Be(2);
        comments.Should().Be(1);
        visible.Should().Be(1);
        hasHiddenMarkup.Should().BeTrue();
        displayLabel.Should().Be("All Markup");
    }

    [Fact]
    public async Task DisplayForReview_AllMarkup_shows_and_styles_insertions_and_deletions()
    {
        bool insertedVisible = false, deletedVisible = false, insertedStyled = false, deletedStyled = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertionAndDeletion());
            var glyphs = view.ReviewGlyphsForTest;
            insertedVisible = glyphs.Any(g => g.Revision == RevisionKind.Inserted);
            deletedVisible = glyphs.Any(g => g.Revision == RevisionKind.Deleted);
            insertedStyled = glyphs.Any(g => g.Revision == RevisionKind.Inserted && g.IsRevisionStyled);
            deletedStyled = glyphs.Any(g => g.Revision == RevisionKind.Deleted && g.IsRevisionStyled);
        });
        if (!ran) return;

        insertedVisible.Should().BeTrue();
        deletedVisible.Should().BeTrue();
        insertedStyled.Should().BeTrue();
        deletedStyled.Should().BeTrue();
    }

    [Fact]
    public async Task DisplayForReview_NoMarkup_hides_deleted_text_without_losing_revision_data()
    {
        bool insertedVisible = false, deletedVisible = true, insertedStyled = true;
        bool modelStillHasDeletion = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertionAndDeletion());
            view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

            var glyphs = view.ReviewGlyphsForTest;
            insertedVisible = glyphs.Any(g => g.Revision == RevisionKind.Inserted);
            deletedVisible = glyphs.Any(g => g.Revision == RevisionKind.Deleted);
            insertedStyled = glyphs.Any(g => g.Revision == RevisionKind.Inserted && g.IsRevisionStyled);
            modelStillHasDeletion = HasDeletion(view);
        });
        if (!ran) return;

        insertedVisible.Should().BeTrue("No Markup shows final inserted text");
        deletedVisible.Should().BeFalse("No Markup hides deleted text visually");
        insertedStyled.Should().BeFalse("No Markup removes inline revision styling");
        modelStillHasDeletion.Should().BeTrue("hidden deleted runs remain in the document model");
    }

    [Fact]
    public async Task DisplayForReview_Original_hides_inserted_text_without_losing_revision_data()
    {
        bool insertedVisible = true, deletedVisible = false, deletedStyled = true;
        bool modelStillHasInsertion = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertionAndDeletion());
            view.ApplyDisplayForReview(ReviewDisplayMode.Original);

            var glyphs = view.ReviewGlyphsForTest;
            insertedVisible = glyphs.Any(g => g.Revision == RevisionKind.Inserted);
            deletedVisible = glyphs.Any(g => g.Revision == RevisionKind.Deleted);
            deletedStyled = glyphs.Any(g => g.Revision == RevisionKind.Deleted && g.IsRevisionStyled);
            modelStillHasInsertion = HasInsertion(view);
        });
        if (!ran) return;

        insertedVisible.Should().BeFalse("Original hides inserted text visually");
        deletedVisible.Should().BeTrue("Original shows the original deleted text");
        deletedStyled.Should().BeFalse("Original removes inline revision styling");
        modelStillHasInsertion.Should().BeTrue("hidden inserted runs remain in the document model");
    }

    [Fact]
    public async Task DisplayForReview_SimpleMarkup_uses_final_inline_text_and_change_bar()
    {
        bool insertedVisible = false, deletedVisible = true, insertedStyled = true;
        int changeBars = 0;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertionAndDeletion());
            view.ApplyDisplayForReview(ReviewDisplayMode.SimpleMarkup);

            var glyphs = view.ReviewGlyphsForTest;
            insertedVisible = glyphs.Any(g => g.Revision == RevisionKind.Inserted);
            deletedVisible = glyphs.Any(g => g.Revision == RevisionKind.Deleted);
            insertedStyled = glyphs.Any(g => g.Revision == RevisionKind.Inserted && g.IsRevisionStyled);
            changeBars = view.SimpleMarkupChangeBarsForTest.Count;
        });
        if (!ran) return;

        insertedVisible.Should().BeTrue();
        deletedVisible.Should().BeFalse();
        insertedStyled.Should().BeFalse();
        changeBars.Should().BeGreaterThan(0, "Simple Markup shows a paragraph-level change-bar cue");
    }

    [Fact]
    public async Task ShowMarkup_toggles_hide_visual_chrome_but_preserve_model_data()
    {
        bool revisionStyled = true, commentHighlighted = true, commentAnchorPreserved = false;
        bool formattingHighlighted = true, formatRevisionPreserved = false;
        var ran = await OnUiThread(() =>
        {
            var reviewView = Build(DocWithInsertionAndDeletion());
            reviewView.ApplyShowMarkupInsertionsAndDeletions(false);
            revisionStyled = reviewView.ReviewGlyphsForTest.Any(g => g.IsRevisionStyled);

            var commentView = Build(DocWithComment());
            commentView.ApplyShowMarkupComments(false);
            commentHighlighted = commentView.CommentHighlightGlyphsForTest.Count > 0;
            commentAnchorPreserved = commentView.CommentAnchorGlyphs().Count > 0
                && ((Paragraph)commentView.Document.Blocks[0]).Runs.Any(r => r.CommentId == 1);

            var formatView = Build(DocWithFormatRevision());
            formatView.ApplyShowMarkupFormatting(false);
            formattingHighlighted = formatView.ReviewGlyphsForTest.Any(g => g.IsFormatRevisionHighlighted);
            formatRevisionPreserved = ((Paragraph)formatView.Document.Blocks[0]).Runs[0].FormatRevision is not null;
        });
        if (!ran) return;

        revisionStyled.Should().BeFalse();
        commentHighlighted.Should().BeFalse();
        commentAnchorPreserved.Should().BeTrue();
        formattingHighlighted.Should().BeFalse();
        formatRevisionPreserved.Should().BeTrue();
    }

    [Fact]
    public async Task NewComment_adds_a_comment_over_the_selection()
    {
        int count = -1; int? id = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            id = view.NewComment("Please review");
            count = view.Document.Comments.Count;
        });
        if (!ran) return;
        id.Should().NotBeNull();
        count.Should().Be(1, "NewComment anchors a comment over the selection");
    }

    [Fact]
    public async Task DeleteCommentAtCaret_removes_the_comment()
    {
        int countAfterDelete = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            view.NewComment("note");
            view.MoveCaretToBlock(0, 2); // inside the commented range
            view.DeleteCommentAtCaret();
            countAfterDelete = view.Document.Comments.Count;
        });
        if (!ran) return;
        countAfterDelete.Should().Be(0, "DeleteCommentAtCaret removes the thread at the caret");
    }

    [Fact]
    public async Task ResolveComment_registry_command_toggles_the_comment_at_the_caret()
    {
        bool resolved = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.NewComment("note");
            view.MoveCaretToBlock(0, 2);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            Execute(registry, "freew.resolve-comment");

            resolved = id is { } commentId && view.Document.Comments[commentId].Resolved;
        });

        if (!ran) return;
        resolved.Should().BeTrue("the Review > Comments > Resolve command uses the editor comment model");
    }

    // ── Word count ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeStatistics_reports_word_and_paragraph_counts()
    {
        DocumentStatistics stats = default;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("The quick brown fox"));
            doc.Blocks.Add(new Paragraph("jumps over"));
            var view = Build(doc);
            stats = view.ComputeStatistics();
        });
        if (!ran) return;
        stats.Words.Should().Be(6, "six whitespace-delimited words across two paragraphs");
        stats.Paragraphs.Should().Be(2);
        stats.CharactersWithoutSpaces.Should().BeGreaterThan(0);
    }

    // ── Command resolution ────────────────────────────────────────────────────────

    // GB1: a multi-paragraph selection tags only the SELECTED sub-ranges — the first block from its
    // start offset to its end, the last block from its start to the end offset — leaving the
    // unselected characters in each spanned paragraph untouched (mirrors Word's per-run semantics).
    [Fact]
    public async Task Proofing_language_applies_only_to_the_selected_range_across_paragraphs()
    {
        string? firstParaText = null, secondParaText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Alpha"));
            doc.Blocks.Add(new Paragraph("Beta"));
            var view = Build(doc);

            // Select "lpha" (offset 1..end) in block 0 and "Be" (offset 0..2) in block 1.
            view.SetSelectionRangePublic(0, 1, 1, 2);
            view.SetProofingLanguage(" fr-FR ");

            firstParaText = DumpLanguageTags((Paragraph)view.Document.Blocks[0]);
            secondParaText = DumpLanguageTags((Paragraph)view.Document.Blocks[1]);
        });
        if (!ran) return;

        // "A" stays untagged; "lpha" becomes fr-FR.
        firstParaText.Should().Be("A:|lpha:fr-FR");
        // "Be" becomes fr-FR; "ta" stays untagged.
        secondParaText.Should().Be("Be:fr-FR|ta:");
    }

    // GB1: a selection wholly within a single paragraph tags only that sub-range, leaving the rest of
    // the paragraph's language tag unchanged.
    [Fact]
    public async Task Proofing_language_applies_only_to_a_single_block_sub_range()
    {
        string? runsDump = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);

            // Select offsets 5..10 (" worl" — the middle sub-range) and tag it French.
            view.SetSelectionRangePublic(0, 5, 0, 10);
            view.SetProofingLanguage("fr-FR");

            runsDump = DumpLanguageTags((Paragraph)view.Document.Blocks[0]);
        });
        if (!ran) return;

        runsDump.Should().Be("Hello:| worl:fr-FR|d:");
    }

    [Fact]
    public async Task Proofing_language_collapsed_caret_applies_to_current_word()
    {
        string? runsDump = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Alpha Beta"));
            var view = Build(doc);

            view.MoveCaretToBlock(0, 2);
            view.SetProofingLanguage("fr-FR");

            runsDump = DumpLanguageTags((Paragraph)view.Document.Blocks[0]);
        });
        if (!ran) return;

        runsDump.Should().Be("Alpha:fr-FR| Beta:");
    }

    [Fact]
    public async Task Proofing_language_collapsed_caret_without_current_word_does_not_stage_next_typed_text()
    {
        string? runsDump = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Alpha  Beta"));
            var view = Build(doc);

            view.MoveCaretToBlock(0, 6);
            view.SetProofingLanguage("fr-FR");
            view.InsertText("X");

            runsDump = DumpLanguageTags((Paragraph)view.Document.Blocks[0]);
        });
        if (!ran) return;

        runsDump.Should().Be("Alpha X Beta:");
    }

    // Helper: renders each Run's text alongside its resolved LanguageTag (empty when null), so a test
    // can assert exactly which characters were retagged without depending on Cell-splitting internals.
    private static string DumpLanguageTags(Paragraph paragraph) =>
        string.Join("|", paragraph.Runs.Select(r => $"{r.Text}:{r.Formatting.LanguageTag}"));

    [Fact]
    public async Task Proofing_commands_toggle_state_dictionary_thesaurus_and_language()
    {
        bool spellEnabled = true;
        bool inDictionary = false;
        bool thesaurusOpened = false;
        string? language = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh example"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 2);

            var callbacks = NoopCallbacks() with
            {
                ToggleSpellcheck = () => view.ToggleSpellCheck(),
                IsSpellcheckActive = () => view.SpellCheckEnabled,
                AddToDictionary = () => view.AddCurrentWordToDictionary(),
                OpenThesaurus = () => thesaurusOpened = true,
                SetProofingLanguage = () => view.SetProofingLanguage("de-DE"),
            };
            var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

            // Add-to-Dictionary reads the word AT THE CARET, so it must run while the caret is still
            // collapsed inside "teh" (before the selection below is made for the proofing-language check).
            Execute(registry, "freew.add-to-dictionary");
            Execute(registry, "freew.spellcheck-toggle");
            Execute(registry, "freew.thesaurus");

            // Select the whole paragraph so this command-wiring test observes the tag land on the model
            // independently from the collapsed-caret proofing-word behavior covered above.
            view.SetSelectionRangePublic(0, 0, 0, "teh example".Length);
            Execute(registry, "freew.set-proofing-language");

            spellEnabled = view.SpellCheckEnabled;
            inDictionary = view.IsInCustomDictionary("teh");
            language = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Formatting.LanguageTag;
        });
        if (!ran) return;

        spellEnabled.Should().BeFalse();
        inDictionary.Should().BeTrue();
        thesaurusOpened.Should().BeTrue();
        language.Should().Be("de-DE");
    }

    [Fact]
    public async Task Proofing_commands_fallback_to_editor_and_selected_language_value()
    {
        bool spellEnabled = true;
        bool stateBefore = false;
        bool stateAfter = true;
        bool inDictionary = false;
        string? language = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh example"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 2);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

            registry.TryGet(new RibbonCommandId("freew.spellcheck-toggle"), out var spellCommand)
                .Should().BeTrue();
            var spellState = spellCommand.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

            stateBefore = spellState.GetState().IsChecked;
            Execute(registry, "freew.add-to-dictionary");
            Execute(registry, "freew.spellcheck-toggle");
            stateAfter = spellState.GetState().IsChecked;

            view.SetSelectionRangePublic(0, 0, 0, "teh example".Length);
            Execute(registry, "freew.set-proofing-language", RibbonCommandContext.ForSelectedValue("fr-FR"));

            spellEnabled = view.SpellCheckEnabled;
            inDictionary = view.IsInCustomDictionary("teh");
            language = ((Paragraph)view.Document.Blocks[0]).Runs.Single().Formatting.LanguageTag;
        });
        if (!ran) return;

        stateBefore.Should().BeTrue();
        spellEnabled.Should().BeFalse();
        stateAfter.Should().BeFalse();
        inDictionary.Should().BeTrue();
        language.Should().Be("fr-FR");
    }

    [Fact]
    public async Task Thesaurus_replace_current_proofing_word_replaces_caret_word()
    {
        bool replaced = false;
        string? text = null;
        string? proofingWord = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("A happy day"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 4);

            replaced = view.ReplaceCurrentProofingWord("cheerful");
            text = ((Paragraph)view.Document.Blocks[0]).PlainText;
            proofingWord = view.CurrentProofingWord;
        });
        if (!ran) return;

        replaced.Should().BeTrue();
        text.Should().Be("A cheerful day");
        proofingWord.Should().Be("cheerful");
    }

    [Fact]
    public async Task Proofing_diagnostics_detect_typo_and_map_to_render_glyphs()
    {
        IReadOnlyList<ProofingDiagnostic> diagnostics = [];
        IReadOnlyList<(int Block, int Offset, Rect Rect)> glyphs = [];
        string? activeWord = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh example"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 1);

            diagnostics = view.ProofingDiagnosticsForTest;
            glyphs = view.ProofingSquiggleGlyphsForTest;
            activeWord = view.CurrentProofingDiagnostic?.Word;
        });
        if (!ran) return;

        diagnostics.Should().ContainSingle().Which.Word.Should().Be("teh");
        activeWord.Should().Be("teh");
        glyphs.Select(g => g.Offset).Should().Equal(0, 1, 2);
        glyphs.Should().OnlyContain(g => g.Rect.Width > 0 && g.Rect.Height > 0);
    }

    [Fact]
    public async Task Proofing_diagnostics_skip_no_proof_run_but_keep_adjacent_typo_glyphs()
    {
        IReadOnlyList<ProofingDiagnostic> diagnostics = [];
        IReadOnlyList<(int Block, int Offset, Rect Rect)> glyphs = [];

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("teh", RunFormatting.Default with { NoProof = true }));
            paragraph.Runs.Add(new Run(" teh"));
            doc.Blocks.Add(paragraph);
            var view = Build(doc);

            diagnostics = view.ProofingDiagnosticsForTest;
            glyphs = view.ProofingSquiggleGlyphsForTest;
        });
        if (!ran) return;

        diagnostics.Should().ContainSingle().Which.ParagraphOffset.Should().Be(4);
        glyphs.Select(g => g.Offset).Should().Equal(4, 5, 6);
    }

    [Fact]
    public async Task Proofing_diagnostics_surface_repeated_word_grammar_through_existing_glyphs()
    {
        IReadOnlyList<ProofingDiagnostic> diagnostics = [];
        IReadOnlyList<(int Block, int Offset, Rect Rect)> glyphs = [];
        ProofingDiagnosticKind? activeKind = null;
        bool addedToDictionary = true;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("We saw the the issue"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 12);

            diagnostics = view.ProofingDiagnosticsForTest;
            glyphs = view.ProofingSquiggleGlyphsForTest;
            activeKind = view.CurrentProofingDiagnostic?.Kind;
            addedToDictionary = view.AddCurrentWordToDictionary();
        });
        if (!ran) return;

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Kind.Should().Be(ProofingDiagnosticKind.Grammar);
        diagnostic.Word.Should().Be("the");
        diagnostic.ParagraphOffset.Should().Be(11);
        activeKind.Should().Be(ProofingDiagnosticKind.Grammar);
        glyphs.Select(g => g.Offset).Should().Equal(11, 12, 13);
        glyphs.Should().OnlyContain(g => g.Rect.Width > 0 && g.Rect.Height > 0);
        addedToDictionary.Should().BeFalse("grammar diagnostics are not custom-dictionary spelling entries");
    }

    [Theory]
    [InlineData(true, false, 8, 9, 10)]
    [InlineData(false, true, 0, 1, 2)]
    public async Task Proofing_visibility_flags_hide_only_their_squiggle_glyphs(
        bool hideSpelling,
        bool hideGrammar,
        params int[] expectedOffsets)
    {
        IReadOnlyList<ProofingDiagnostic> diagnostics = [];
        IReadOnlyList<(int Block, int Offset, Rect Rect)> glyphs = [];

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh the the"));
            doc.HideSpellingErrors = hideSpelling;
            doc.HideGrammaticalErrors = hideGrammar;
            var view = Build(doc);

            diagnostics = view.ProofingDiagnosticsForTest;
            glyphs = view.ProofingSquiggleGlyphsForTest;
        });
        if (!ran) return;

        diagnostics.Should().HaveCount(2, "hidden indicators must not remove proofing diagnostics");
        glyphs.Select(glyph => glyph.Offset).Should().Equal(expectedOffsets);
    }

    [Fact]
    public async Task AddCurrentWordToDictionary_requires_active_diagnostic()
    {
        bool normalWordAdded = true;
        bool typoAdded = false;
        bool typoInDictionary = false;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("normal teh"));
            var view = Build(doc);

            view.MoveCaretToBlock(0, 2);
            normalWordAdded = view.AddCurrentWordToDictionary();

            view.MoveCaretToBlock(0, 8);
            typoAdded = view.AddCurrentWordToDictionary();
            typoInDictionary = view.IsInCustomDictionary("teh");
        });
        if (!ran) return;

        normalWordAdded.Should().BeFalse("Add to Dictionary is only enabled for a flagged spelling diagnostic");
        typoAdded.Should().BeTrue();
        typoInDictionary.Should().BeTrue();
    }

    [Fact]
    public async Task Custom_dictionary_suppresses_existing_diagnostic()
    {
        int before = -1;
        int after = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 1);

            before = view.ProofingDiagnosticsForTest.Count;
            view.AddCurrentWordToDictionary();
            after = view.ProofingDiagnosticsForTest.Count;
        });
        if (!ran) return;

        before.Should().Be(1);
        after.Should().Be(0);
    }

    [Fact]
    public async Task Spellcheck_toggle_hides_diagnostics_and_blocks_add_to_dictionary()
    {
        bool enabled = true;
        bool added = true;
        int diagnostics = -1;
        int glyphs = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("teh"));
            var view = Build(doc);
            view.MoveCaretToBlock(0, 1);

            enabled = view.ToggleSpellCheck();
            diagnostics = view.ProofingDiagnosticsForTest.Count;
            glyphs = view.ProofingSquiggleGlyphsForTest.Count;
            added = view.AddCurrentWordToDictionary();
        });
        if (!ran) return;

        enabled.Should().BeFalse();
        diagnostics.Should().Be(0);
        glyphs.Should().Be(0);
        added.Should().BeFalse();
    }

    [Fact]
    public void Review_command_ids_resolve_in_the_registry()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        foreach (var id in new[]
        {
            "freew.track-changes",
            "freew.track-formatting",
            "freew.display-for-review",
            "freew.display-for-review-all-markup",
            "freew.display-for-review-simple-markup",
            "freew.display-for-review-no-markup",
            "freew.display-for-review-original",
            "freew.show-markup",
            "freew.show-markup-insertions-deletions",
            "freew.show-markup-comments",
            "freew.show-markup-formatting",
            "freew.show-markup-balloons",
            "freew.reviewing-pane",
            "freew.reviewingpane",
            "freew.statistics",
            "freew.word-count",
            "freew.spellcheck-toggle",
            "freew.add-to-dictionary",
            "freew.thesaurus",
            "freew.set-proofing-language",
            "freew.check-accessibility",
            "freew.accept-change",
            "freew.accept-this",
            "freew.reject-change",
            "freew.reject-this",
            "freew.accept-all",
            "freew.reject-all",
            "freew.previous-change",
            "freew.next-change",
            "freew.mark-as-final",
            "freew.restrict-editing",
            "freew.inspect-document",
            "freew.compare",
            "freew.combine",
            "freew.new-comment",
            "freew.delete-comment",
            "freew.previous-comment",
            "freew.next-comment",
            "freew.reply-comment",
            "freew.resolve-comment",
            "freew.show-comments",
        })
        {
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Review command '{id}' must be registered");
        }
    }

    [Fact]
    public void Review_display_markup_commands_update_editor_state()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.display-for-review-no-markup");
        Execute(registry, "freew.show-markup-comments");
        Execute(registry, "freew.show-markup-balloons");

        view.DisplayForReview.Should().Be(ReviewDisplayMode.NoMarkup);
        view.ShowMarkupComments.Should().BeFalse();
        view.ShowMarkupBalloons.Should().BeTrue();

        registry.TryGet(new RibbonCommandId("freew.display-for-review-no-markup"), out var displayCommand)
            .Should().BeTrue();
        (displayCommand as IRibbonStatefulCommand)!.GetState().IsChecked.Should().BeTrue();

        registry.TryGet(new RibbonCommandId("freew.show-markup-comments"), out var commentsCommand)
            .Should().BeTrue();
        (commentsCommand as IRibbonStatefulCommand)!.GetState().IsChecked.Should().BeFalse();

        registry.TryGet(new RibbonCommandId("freew.show-markup-balloons"), out var balloonsCommand)
            .Should().BeTrue();
        (balloonsCommand as IRibbonStatefulCommand)!.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Review_balloons_command_can_route_toggle_state_through_host_callback()
    {
        var active = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleReviewBalloons = () => active = !active,
            IsReviewBalloonsActive = () => active,
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.show-markup-balloons"), out var command)
            .Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

        stateful.GetState().IsChecked.Should().BeFalse();
        Execute(registry, "freew.show-markup-balloons");
        stateful.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Review_balloons_pane_renders_revisions_and_comments_from_model_data()
    {
        int count = -1;
        IReadOnlyList<string> kinds = [];
        IReadOnlyList<string> metadata = [];
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInsertion();
            var p = (Paragraph)doc.Blocks[0];
            p.Runs[0].CommentId = 1;
            p.Runs.Insert(1, Run.CommentReference(1));
            var comment = new Comment(1, "check intro", "Casey", "C")
            {
                DateXml = "2026-07-02T11:00:00Z",
                Resolved = true
            };
            comment.AddReply(2, "fixed", "Ann", "A");
            doc.Comments[1] = comment;

            var view = Build(doc);
            var pane = new ReviewBalloonsPane(view);
            pane.Refresh();
            count = pane.BalloonItemCount;
            var sources = ReviewBalloonsPane.EnumerateBalloons(view.Document, view.CurrentReviewDisplayPolicy);
            kinds = sources.Select(item => item.KindLabel).ToArray();
            metadata = sources.Select(item => item.MetadataText).ToArray();
        });
        if (!ran) return;

        count.Should().Be(2, "the strip renders one tracked revision and one comment balloon");
        kinds.Should().Equal("Resolved comment", "Inserted");
        metadata.Should().Equal("Resolved - 1 reply - 2026-07-02", "Tracked change - 2024-01-01");
    }

    [Fact]
    public async Task Review_balloons_pane_uses_shared_anchored_leader_line_layout()
    {
        IReadOnlyList<ReviewBalloonLayout> layouts = [];
        int visualChildren = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInsertion();
            var p = (Paragraph)doc.Blocks[0];
            p.Runs[0].CommentId = 1;
            p.Runs.Insert(1, Run.CommentReference(1));
            doc.Comments[1] = new Comment(1, "check intro", "Casey", "C");

            var view = Build(doc);
            var pane = new ReviewBalloonsPane(view);
            pane.Height = 420;
            pane.Measure(new Size(260, 420));
            pane.Arrange(new Rect(0, 0, 260, 420));
            pane.Refresh();

            layouts = pane.LayoutsForTest;
            visualChildren = pane.VisualChildCountForTest;
        });
        if (!ran) return;

        layouts.Should().HaveCount(2);
        layouts.Select(layout => layout.Source.KindLabel).Should().Equal("Comment", "Inserted");
        layouts.Select(layout => layout.BalloonY).Should().Equal(77, 287);
        layouts.Should().OnlyContain(layout => layout.BalloonMidY == layout.LeaderStartY);
        layouts.Should().OnlyContain(layout => layout.LeaderEndX == layout.BalloonX);
        layouts.Should().OnlyContain(layout => layout.LeaderEndY == layout.BalloonMidY);
        layouts.Select(layout => layout.LeaderStartY).Should().BeInAscendingOrder();
        visualChildren.Should().Be(layouts.Count * 2, "Avalonia draws one leader line and one balloon container per shared layout item");
    }

    [Fact]
    public void Review_command_ids_are_declared_in_the_ribbon_definition()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var ids = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .SelectMany(CommandIdsIncludingMenus)
            .Select(id => id.Value)
            .ToHashSet();

        foreach (var id in new[]
        {
            "freew.track-changes", "freew.track-formatting", "freew.reviewing-pane", "freew.statistics",
            "freew.display-for-review", "freew.show-markup", "freew.show-markup-balloons",
            "freew.spellcheck-toggle", "freew.add-to-dictionary",
            "freew.thesaurus", "freew.set-proofing-language",
            "freew.check-accessibility", "freew.accept-this", "freew.reject-this",
            "freew.accept-all", "freew.reject-all", "freew.previous-change", "freew.next-change", "freew.new-comment",
            "freew.delete-comment", "freew.previous-comment", "freew.next-comment",
            "freew.reply-comment", "freew.resolve-comment", "freew.show-comments",
            "freew.mark-as-final", "freew.restrict-editing",
            "freew.inspect-document", "freew.compare", "freew.combine",
        })
        {
            ids.Should().Contain(id, $"Review tab must declare '{id}'");
        }

        ids.Should().NotContain(new[]
        {
            "freew.reviewingpane",
            "freew.word-count",
            "freew.accept-change",
            "freew.reject-change",
        });
    }

    [Fact]
    public void Review_safety_commands_route_to_host_callbacks()
    {
        var callbacks = NoopCallbacks();
        var calls = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        callbacks = callbacks with
        {
            ToggleReviewingPane = () => calls.Add("reviewing-pane"),
            OpenWordCountDialog = () => calls.Add("statistics"),
            CheckAccessibility = () => calls.Add("accessibility"),
            InspectDocument = () => calls.Add("inspect"),
            MarkAsFinal = () => calls.Add("mark-final"),
            RestrictEditing = () => calls.Add("restrict"),
        };

        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.reviewing-pane");
        Execute(registry, "freew.reviewingpane");
        Execute(registry, "freew.statistics");
        Execute(registry, "freew.word-count");
        Execute(registry, "freew.check-accessibility");
        Execute(registry, "freew.inspect-document");
        Execute(registry, "freew.mark-as-final");
        Execute(registry, "freew.restrict-editing");

        calls.Should().Contain(new[]
        {
            "reviewing-pane",
            "statistics",
            "accessibility",
            "inspect",
            "mark-final",
            "restrict",
        });
    }

    [Fact]
    public void Review_change_navigation_commands_route_to_host_callbacks()
    {
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            PreviousChange = () => calls.Add("previous-change"),
            NextChange = () => calls.Add("next-change"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.previous-change");
        Execute(registry, "freew.next-change");

        calls.Should().Equal("previous-change", "next-change");
    }

    [Fact]
    public void Review_compare_commands_route_to_host_callbacks()
    {
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            CompareDocuments = () => calls.Add("compare"),
            CombineDocuments = () => calls.Add("combine"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.compare");
        Execute(registry, "freew.combine");

        calls.Should().Equal("compare", "combine");
    }

    [Fact]
    public void Review_comment_dialog_commands_route_to_host_callbacks()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello") { CommentId = 1 });
        paragraph.Runs.Add(Run.CommentReference(1));
        doc.Blocks.Add(paragraph);
        doc.Comments[1] = new Comment(1, "note", "A", "A");

        var view = new DocumentView();
        view.LoadDocument(doc);
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            ReplyComment = () => calls.Add("reply"),
            ShowComments = rows => calls.Add($"show:{rows.Count}"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

        Execute(registry, "freew.reply-comment");
        Execute(registry, "freew.show-comments");

        calls.Should().Equal("reply", "show:1");
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        Execute(registry, id, RibbonCommandContext.Empty);
    }

    private static void Execute(RibbonCommandRegistry registry, string id, RibbonCommandContext context)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(context);
    }

    private static RibbonCommandId? GetCommandId(RibbonControl control) => control switch
    {
        RibbonButton b => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c => c.CommandId,
        RibbonCheckBox cb => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d => d.CommandId,
        RibbonGallery g => g.CommandId,
        _ => (RibbonCommandId?)null,
    };

    private static IEnumerable<RibbonCommandId> CommandIdsIncludingMenus(RibbonControl control)
    {
        if (GetCommandId(control) is { } id && !string.IsNullOrEmpty(id.Value))
            yield return id;

        var menuIds = control switch
        {
            RibbonSplitButton splitButton => MenuCommandIds(splitButton.Menu.Items),
            RibbonDropdown dropdown => MenuCommandIds(dropdown.Menu.Items),
            _ => Enumerable.Empty<RibbonCommandId>(),
        };

        foreach (var menuId in menuIds)
            yield return menuId;
    }

    private static IEnumerable<RibbonCommandId> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id;

            foreach (var childId in MenuCommandIds(item.Children))
                yield return childId;
        }
    }
}
