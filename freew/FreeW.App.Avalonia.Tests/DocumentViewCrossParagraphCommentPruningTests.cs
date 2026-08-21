using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r160 remediation, T1: <see cref="DocumentView.DeleteSelection"/>, <see cref="DocumentView.Backspace"/>
/// and <see cref="DocumentView.DeleteForward"/> all try the shared
/// <c>DocumentEditingSession.Body.TryApplyDeletion</c> fast path FIRST and return immediately once it
/// succeeds -- so the orphaned-comment prune added to their local ParaCells fallback branches
/// (<see cref="DocumentViewCommentPruningTests"/>) never ran for the ordinary case. That fallback is only
/// reached when the shared session's portability gate (<c>DocumentEditingSession.IsPortableBodyTextParagraph</c>
/// / <c>IsPortableBodyTextRun</c>) declines a paragraph -- which it only does when the paragraph itself holds
/// the comment's textless <see cref="Run.IsCommentReference"/> run. It does NOT examine <see cref="Run.CommentId"/>
/// on an ordinary anchored-text run, so a paragraph holding only a comment's highlighted text (no reference
/// run) is portable and sails through the fast path untouched.
///
/// For a comment this app creates via <see cref="DocumentView.AddComment"/>, the anchor text and the
/// reference run always land in the SAME paragraph (see <c>AddCommentCommand.MarkCommentRange</c>), which
/// makes that paragraph non-portable and hides the gap -- every fast-path deletion of a commented paragraph
/// in <see cref="DocumentViewCommentPruningTests"/> actually declines to the (already correct) fallback.
/// A comment imported from a real Word .docx whose range crosses a paragraph boundary is different: DocxReader
/// tracks the open comment range with a paragraph-local variable (see <c>DocxReader.ReadParagraph</c>'s
/// <c>activeCommentId</c>), so it lands the anchored text in one paragraph and the reference run in the next.
/// Deleting just the anchor paragraph is then fast-path-eligible and, before this fix, left the orphaned
/// <see cref="TextDocument.Comments"/> entry lingering forever once nothing anywhere still carried its id.
///
/// Because the reference run's own paragraph is NEVER portable (i.e. never fast-path-eligible), it always
/// declines to the (already-pruning) fallback -- so the only way to observe the comment becoming truly
/// orphaned (no <see cref="Run.CommentId"/> anywhere) is to remove the reference run FIRST via that already
/// -correct fallback, and only THEN delete the anchor paragraph through the fast path this gap is about. That
/// two-step sequence is exactly what each test below does; it is also an entirely ordinary thing for a user
/// to do (delete a stray trailing reference mark, then clean up the paragraph before it).
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
    /// Removes paragraph 1's own trailing text via the SAME shared entry point (<see cref="DocumentView.TryDeleteSelection"/>).
    /// Paragraph 1 is never portable (it holds the reference run), so this always declines the fast path and
    /// goes through the already-correct local fallback, which prunes -- but the comment must still be
    /// considered "used" afterward because paragraph 0's anchor run is untouched. This intentionally drops
    /// the now-anchorless reference run from paragraph 1 (see <c>DocumentView.SetRuns</c>'s
    /// "referencedComments" re-anchoring, which only re-emits a reference run when some surviving cell in
    /// THAT paragraph still carries its id) -- leaving paragraph 0's anchor run as the comment's only
    /// remaining anchor anywhere in the document, exactly as a real cross-paragraph import can end up after
    /// an earlier, unrelated edit near the reference mark.
    /// </summary>
    private static void RemoveReferenceParagraphsTrailingText(DocumentView view)
    {
        view.SetSelectionRangePublic(1, 0, 1, "Trailing".Length);
        view.TryDeleteSelection().Should().BeTrue();
        view.Document.Comments.Should().ContainKey(1,
            "paragraph 0's anchor run is untouched, so the comment must still read as used");
        view.Document.Blocks.OfType<Paragraph>().ElementAt(1).Runs.Should().BeEmpty(
            "the reference run's own paragraph rebuild drops it once no cell in that paragraph anchors its id");
    }

    // ── DeleteSelection ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSelection_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);

        // Now delete paragraph 0's anchor text. Paragraph 0 holds only a non-reference CommentId run, so
        // DocumentEditingSession.IsPortableBodyTextParagraph accepts it and this goes through the shared
        // fast path -- the exact call DeleteSelection() makes before ever reaching its local fallback.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "no run anywhere still carries the comment's id, so the fast path must prune it just like the fallback would");
    }

    // ── Backspace ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Backspace_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);

        // A selection is active, so Backspace's own TryApplyDeletion call (CurrentBodyTextRange returns
        // the selection) deletes the whole selection through the shared fast path -- Backspace's own success
        // branch, not DeleteSelection's.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.BackspacePublic();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "Backspace's own fast-path success branch must prune too, not just DeleteSelection's");
    }

    // ── DeleteForward ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteForward_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphComment(out var commentId);
        RemoveReferenceParagraphsTrailingText(view);

        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.DeleteForwardPublic();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "DeleteForward's own fast-path success branch must prune too, not just DeleteSelection's");
    }

    // ── Sibling / no-regression: a surviving anchor elsewhere must never be pruned away ─────────────────

    [Fact]
    public void DeleteSelection_ThroughSharedFastPath_LeavesCommentAloneWhileTheReferenceParagraphStillAnchorsIt()
    {
        var view = BuildCrossParagraphComment(out var commentId);

        // Delete paragraph 0's anchor text WITHOUT first touching paragraph 1 -- the reference run is still
        // out there, so the comment must be read as used even though the fast path's new prune call runs.
        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeTrue(
            "paragraph 1's reference run still anchors the comment, so the new prune call must be a no-op here");
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

        view.SetSelectionRangePublic(0, 0, 0, "Anchor text".Length);
        view.SimulateTextInputForTest("x");

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "replacing the last run that carried the comment's id leaves nothing anchored, so the "
            + "comment must be pruned exactly as it is when the same range is deleted");
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
}
