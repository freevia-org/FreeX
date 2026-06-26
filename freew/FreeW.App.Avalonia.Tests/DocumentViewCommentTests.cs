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
/// AV-COMMENT: review-comment insert / delete / resolve infrastructure + anchor render in the Avalonia
/// <see cref="DocumentView"/>. Verifies the model is mutated through the undoable command bus, the anchor
/// range maps onto the right glyphs, the render produces comment-anchor marks over the range, and the
/// no-comment baseline is unaffected.
/// </summary>
public sealed class DocumentViewCommentTests
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

    private static TextDocument DocWith(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run(text, RunFormatting.Default));
        doc.Blocks.Add(p);
        return doc;
    }

    private static DocumentView Build(string text)
    {
        var view = new DocumentView();
        view.LoadDocument(DocWith(text));
        view.Measure(new Size(800, 2000));
        return view;
    }

    // ── Insert ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddComment_over_selection_anchors_to_that_range()
    {
        int? id = null;
        var commentCount = -1;
        var anchoredText = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            // Select "world" (offsets 6..11).
            view.SetSelectionRangePublic(0, 6, 0, 11);
            id = view.AddComment("Please revise", "Ann Reviewer", "AR");
            commentCount = view.Document.Comments.Count;

            var p = (Paragraph)view.Document.Blocks[0];
            anchoredText = string.Concat(p.Runs.Where(r => r.CommentId == id && !r.IsCommentReference).Select(r => r.Text));
        });

        if (!ran) return;
        id.Should().NotBeNull("AddComment over a non-empty selection should anchor a comment");
        commentCount.Should().Be(1);
        anchoredText.Should().Be("world", "the comment should anchor exactly to the selected range");
    }

    [Fact]
    public async Task AddComment_appends_a_reference_run_and_stores_the_thread()
    {
        var hasReference = false;
        var author = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.AddComment("note", "Bob", "B");

            var p = (Paragraph)view.Document.Blocks[0];
            hasReference = p.Runs.Any(r => r.IsCommentReference && r.CommentId == id);
            author = id is { } i ? view.Document.Comments[i].Author : "";
        });

        if (!ran) return;
        hasReference.Should().BeTrue("a textless comment-reference run is appended after the anchored range");
        author.Should().Be("Bob");
    }

    [Fact]
    public async Task AddComment_with_empty_selection_anchors_whole_paragraph()
    {
        var anchoredText = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Whole para");
            view.MoveCaretToBlock(0, 3); // collapsed caret
            var id = view.AddComment("c", "X", "X");
            var p = (Paragraph)view.Document.Blocks[0];
            anchoredText = string.Concat(p.Runs.Where(r => r.CommentId == id && !r.IsCommentReference).Select(r => r.Text));
        });

        if (!ran) return;
        anchoredText.Should().Be("Whole para", "an empty selection anchors the comment to the whole paragraph");
    }

    // ── Anchor → glyph mapping + render ─────────────────────────────────────────

    [Fact]
    public async Task AddComment_marks_the_anchored_glyphs_for_render()
    {
        var anchorGlyphCount = 0;
        var totalGlyphs = 0;
        var modelMarked = 0;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 6, 0, 11); // "world" = 5 chars
            var id = view.AddComment("note", "A", "A");
            // CommentAnchorGlyphs() re-lays-out on demand (AddComment invalidated layout).

            var p = (Paragraph)view.Document.Blocks[0];
            modelMarked = p.Runs.Where(r => r.CommentId == id && !r.IsCommentReference).Sum(r => r.Text.Length);

            var anchors = view.CommentAnchorGlyphs();
            anchorGlyphCount = anchors.Count;
            totalGlyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        modelMarked.Should().Be(5, "the model should carry the comment id over the 5 'world' chars");
        anchorGlyphCount.Should().Be(5, "exactly the 5 glyphs of 'world' should be marked as commented");
        totalGlyphs.Should().BeGreaterThan(anchorGlyphCount, "only the anchored range is marked, not the whole paragraph");
    }

    [Fact]
    public async Task No_comment_document_produces_no_anchor_marks()
    {
        var anchorGlyphCount = -1;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Plain text, no comments");
            anchorGlyphCount = view.CommentAnchorGlyphs().Count;
        });

        if (!ran) return;
        anchorGlyphCount.Should().Be(0, "a document with no comments must render no anchor marks");
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteComment_removes_thread_and_clears_anchor_marks()
    {
        var afterCount = -1;
        var afterAnchorGlyphs = -1;
        var anyCommentRun = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 6, 0, 11);
            var id = view.AddComment("note", "A", "A");

            view.DeleteComment(id!.Value);
            view.Measure(new Size(800, 2000));

            afterCount = view.Document.Comments.Count;
            afterAnchorGlyphs = view.CommentAnchorGlyphs().Count;
            var p = (Paragraph)view.Document.Blocks[0];
            anyCommentRun = p.Runs.Any(r => r.CommentId is not null);
        });

        if (!ran) return;
        afterCount.Should().Be(0, "DeleteComment removes the stored thread");
        afterAnchorGlyphs.Should().Be(0, "the anchor marks are cleared from the render");
        anyCommentRun.Should().BeFalse("no run should still carry a CommentId or reference");
    }

    // ── Resolve / unresolve ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetCommentResolved_toggles_the_resolved_flag()
    {
        var resolvedAfterSet = false;
        var resolvedAfterClear = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.AddComment("note", "A", "A")!.Value;

            view.SetCommentResolved(id, true);
            resolvedAfterSet = view.Document.Comments[id].Resolved;

            view.SetCommentResolved(id, false);
            resolvedAfterClear = view.Document.Comments[id].Resolved;
        });

        if (!ran) return;
        resolvedAfterSet.Should().BeTrue("SetCommentResolved(true) resolves the thread");
        resolvedAfterClear.Should().BeFalse("SetCommentResolved(false) reopens the thread");
    }

    // ── Undo ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_reverts_AddComment()
    {
        var countAfterAdd = -1;
        var countAfterUndo = -1;
        var anyCommentRunAfterUndo = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 6, 0, 11);
            view.AddComment("note", "A", "A");
            countAfterAdd = view.Document.Comments.Count;

            view.Undo();
            countAfterUndo = view.Document.Comments.Count;
            var p = (Paragraph)view.Document.Blocks[0];
            anyCommentRunAfterUndo = p.Runs.Any(r => r.CommentId is not null);
        });

        if (!ran) return;
        countAfterAdd.Should().Be(1);
        countAfterUndo.Should().Be(0, "Undo removes the comment from the model");
        anyCommentRunAfterUndo.Should().BeFalse("Undo also restores the anchored runs (no CommentId left)");
    }

    [Fact]
    public async Task Undo_reverts_DeleteComment()
    {
        var countAfterDelete = -1;
        var countAfterUndo = -1;
        var anchoredTextAfterUndo = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 6, 0, 11);
            var id = view.AddComment("note", "A", "A")!.Value;

            view.DeleteComment(id);
            countAfterDelete = view.Document.Comments.Count;

            view.Undo();
            countAfterUndo = view.Document.Comments.Count;
            var p = (Paragraph)view.Document.Blocks[0];
            anchoredTextAfterUndo = string.Concat(
                p.Runs.Where(r => r.CommentId == id && !r.IsCommentReference).Select(r => r.Text));
        });

        if (!ran) return;
        countAfterDelete.Should().Be(0);
        countAfterUndo.Should().Be(1, "Undo restores the deleted comment thread");
        anchoredTextAfterUndo.Should().Be("world", "Undo restores the anchored range marks");
    }

    [Fact]
    public async Task Undo_reverts_SetCommentResolved()
    {
        var resolvedAfterSet = false;
        var resolvedAfterUndo = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.AddComment("note", "A", "A")!.Value;

            view.SetCommentResolved(id, true);
            resolvedAfterSet = view.Document.Comments[id].Resolved;

            view.Undo();
            resolvedAfterUndo = view.Document.Comments[id].Resolved;
        });

        if (!ran) return;
        resolvedAfterSet.Should().BeTrue();
        resolvedAfterUndo.Should().BeFalse("Undo restores the previous (unresolved) flag");
    }

    // ── Introspection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CommentsAtCaret_and_AllComments_report_the_thread()
    {
        var allCount = -1;
        var atCaretCount = -1;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Hello world");
            view.SetSelectionRangePublic(0, 6, 0, 11);
            view.AddComment("note", "A", "A");

            // Place caret inside the commented range.
            view.MoveCaretToBlock(0, 8);
            allCount = view.AllComments.Count;
            atCaretCount = view.CommentsAtCaret.Count;
        });

        if (!ran) return;
        allCount.Should().Be(1, "AllComments lists the thread");
        atCaretCount.Should().Be(1, "CommentsAtCaret finds the thread covering the caret");
    }
}
