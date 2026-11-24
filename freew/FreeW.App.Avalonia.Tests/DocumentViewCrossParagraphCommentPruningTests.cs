using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// A comment range and its textless reference may live in different paragraphs. Editing unrelated text
/// beside the reference must preserve that exact marker. Removing the final substantive range anchor
/// then removes the thread and its reference markers together, and Undo restores both paragraphs.
/// Reference-only point comments that had no substantive anchor before an edit are a separate case.
/// </summary>
public sealed class DocumentViewCrossParagraphCommentPruningTests
{
    /// <summary>
    /// Builds the two-paragraph shape a real cross-paragraph Word import produces: paragraph 0 holds only
    /// the comment's anchored (highlighted) text -- portable, fast-path-eligible on its own -- and
    /// paragraph 1 holds the textless reference run plus unrelated trailing text -- never portable, because
    /// it carries <see cref="Run.IsCommentReference"/>.
    /// </summary>
    private static DocumentView BuildCrossParagraphComment(out int commentId)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var anchorParagraph = new Paragraph();
        anchorParagraph.Runs.Add(new Run("Anchor text", RunFormatting.Default) { CommentId = 1 });
        document.Blocks.Add(anchorParagraph);

        var referenceParagraph = new Paragraph();
        referenceParagraph.Runs.Add(Run.CommentReference(1));
        referenceParagraph.Runs.Add(new Run("Trailing", RunFormatting.Default));
        document.Blocks.Add(referenceParagraph);

        document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");

        var view = new DocumentView();
        view.LoadDocument(document);
        commentId = 1;
        return view;
    }

    /// <summary>
    /// Removes only paragraph 1's trailing text, preserving the reference whose actual range is in
    /// paragraph 0. The later last-anchor deletion must clean up this surviving external reference.
    /// </summary>
    private static void RemoveReferenceParagraphsTrailingText(DocumentView view)
    {
        var reference = view.Document.Blocks.OfType<Paragraph>().ElementAt(1).Runs[0];
        view.SetSelectionRangePublic(1, 0, 1, "Trailing".Length);
        view.TryDeleteSelection().Should().BeTrue();
        view.Document.Comments.Should().ContainKey(1,
            "paragraph 0's anchor run is untouched, so the comment must still read as used");
        view.Document.Blocks.OfType<Paragraph>().ElementAt(1).Runs.Should().ContainSingle()
            .Which.Should().BeSameAs(reference,
                "editing unrelated trailing text must preserve the cross-paragraph reference marker");
    }

    // ── DeleteSelection ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSelection_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);
        var before = CaptureCommentState(view, commentId);

        // Delete the original range. Its reference in the other paragraph must be removed in the
        // same undoable operation, regardless of which text editing route handles the gesture.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "the last original range anchor was removed");
        AssertPrunedReferencesAndExactUndo(view, commentId, before);
    }

    // ── Backspace ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Backspace_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);
        var before = CaptureCommentState(view, commentId);

        // Backspace must apply the same lifecycle policy as the explicit selection-delete command.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.BackspacePublic();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "Backspace's own fast-path success branch must prune too, not just DeleteSelection's");
        AssertPrunedReferencesAndExactUndo(view, commentId, before);
    }

    // ── DeleteForward ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteForward_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);
        var before = CaptureCommentState(view, commentId);

        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.DeleteForwardPublic();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "DeleteForward's own fast-path success branch must prune too, not just DeleteSelection's");
        AssertPrunedReferencesAndExactUndo(view, commentId, before);
    }

    // ── An untouched reference must not turn a removed text range into a new point comment ───────────

    [Fact]
    public void DeleteSelection_PrunesTheExternalReferenceWhenItsLastOriginalRangeAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        var before = CaptureCommentState(view, commentId);

        // Paragraph 1's marker refers to the original text range; it was not a point comment before
        // the edit. Its unrelated trailing text must remain when the range and marker are removed.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "a reference to a removed text range must not convert that range into a new point comment");
        view.Document.Blocks.OfType<Paragraph>().ElementAt(1).PlainText.Should().Be("Trailing");
        AssertPrunedReferencesAndExactUndo(view, commentId, before);
    }

    /// <summary>
    /// r160 remediation, fourth door. Typing over a selection DELETES that selection through the
    /// same shared fast path the three delete gestures use, so it orphans a comment the same way.
    /// The remediation that added pruning to Backspace, Delete and DeleteSelection missed this
    /// because it enumerated delete GESTURES rather than the deletion OPERATION; an auditor found
    /// it by searching for the operation and reproduced it against the already-fixed code.
    /// </summary>
    [Fact]
    public void TypingOverASelection_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);
        var before = CaptureCommentState(view, commentId);

        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.SimulateTextInputForTest("x");

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "replacing the last run that carried the comment's id leaves nothing anchored, so the "
            + "comment must be pruned exactly as it is when the same range is deleted");
        view.Document.Blocks.OfType<Paragraph>().First().Runs.Should().OnlyContain(run => run.CommentId == null);
        AssertPrunedReferencesAndExactUndo(view, commentId, before);
    }

    // ── Sibling / no-regression: ordinary deletion with no notes/comments at all is unaffected ─────────

    [Fact]
    public void DeleteSelection_ThroughSharedFastPath_InADocumentWithNoCommentsIsUnaffected()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Head tail"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.SetSelectionRangePublic(0, 0, 0, 5);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Paragraphs.Single().PlainText.Should().Be("tail");
        view.Document.Comments.Should().BeEmpty();
        view.Document.Footnotes.Should().BeEmpty();
        view.Document.Endnotes.Should().BeEmpty();
    }

    private static (Run[] AnchorRuns, Run[] ReferenceRuns, Comment Thread) CaptureCommentState(DocumentView view, int id) =>
        (((Paragraph)view.Document.Blocks[0]).Runs.ToArray(), ((Paragraph)view.Document.Blocks[1]).Runs.ToArray(), view.Document.Comments[id]);

    private static void AssertPrunedReferencesAndExactUndo(DocumentView view, int id,
        (Run[] AnchorRuns, Run[] ReferenceRuns, Comment Thread) before)
    {
        var anchor = (Paragraph)view.Document.Blocks[0];
        var reference = (Paragraph)view.Document.Blocks[1];
        reference.Runs.Should().NotContain(run => run.IsCommentReference && run.CommentId == id);
        view.Undo();
        view.Document.Comments[id].Should().BeSameAs(before.Thread);
        anchor.Runs.Should().Equal(before.AnchorRuns);
        reference.Runs.Should().Equal(before.ReferenceRuns);
        view.Redo();
        view.Document.Comments.Should().NotContainKey(id);
        reference.Runs.Should().NotContain(run => run.IsCommentReference && run.CommentId == id);
    }
}
