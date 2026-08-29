using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r169: the WPF host's counterpart to Avalonia's <c>DocumentViewCrossParagraphCommentPruningTests</c>
/// "fourth door" case. <see cref="DocumentView.TryApplyBodyDeletion"/> (Backspace/Delete) already prunes
/// an orphaned comment after going through the shared portable fast path (r161) -- see the comment at
/// its <c>PruneOrphanedNoteAndCommentAnchorsAfterPortableEdit</c> call site -- but
/// <see cref="DocumentView.TryApplyBodyTextInput"/>, which typing over a selection also routes through
/// (it deletes the selection before inserting), never called it. A comment imported from Word whose
/// range crosses a paragraph boundary lands its anchored text in one paragraph and its
/// <c>w:commentReference</c> in the next (<c>DocxReader.ReadParagraph</c>'s paragraph-local
/// <c>activeCommentId</c>). Once an earlier edit strips the reference run out of the anchor paragraph's
/// sibling (or, as modelled directly below, the reference run never survived into the current document
/// shape), that anchor paragraph holds the ONLY remaining run carrying the comment's id -- an ordinary
/// <see cref="Run.CommentId"/> run with no <see cref="Run.IsCommentReference"/> marker. That makes it
/// "portable": <c>DocumentEditingSession.IsPortableBodyTextParagraph</c>/<c>IsPortableBodyTextRun</c> only
/// exclude a paragraph for holding the textless <see cref="Run.IsCommentReference"/> run, never for
/// merely holding a plain <see cref="Run.CommentId"/> run. So an ordinary select-then-type over that
/// paragraph sails through <see cref="DocumentView.TryApplyBodyTextInput"/>'s fast path, deletes the
/// document's last surviving anchor for the comment, and -- before this fix -- left it permanently
/// orphaned in <see cref="TextDocument.Comments"/>.
/// </summary>
public sealed class DocumentViewCrossParagraphCommentPruningTests
{
    /// <summary>
    /// Two-paragraph shape matching a cross-paragraph Word import once an earlier edit has already
    /// stripped the trailing paragraph's <c>w:commentReference</c> run: paragraph 0 holds only the
    /// comment's anchored (highlighted) text -- an ordinary <see cref="Run.CommentId"/> run, portable
    /// and fast-path-eligible on its own -- and paragraph 1 holds unrelated plain text with no comment
    /// marks at all, so paragraph 0's run is the ONLY surviving anchor for the comment anywhere in the
    /// document.
    /// </summary>
    private static DocumentView BuildCrossParagraphCommentWithReferenceAlreadyStripped(out int commentId)
    {
        var document = new TextDocument();

        var anchorParagraph = new Paragraph();
        anchorParagraph.Runs.Add(new Run("Anchor text", RunFormatting.Default) { CommentId = 1 });
        document.Blocks.Add(anchorParagraph);

        document.Blocks.Add(new Paragraph("Trailing"));

        document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");

        var view = new DocumentView();
        view.LoadModel(document);
        commentId = 1;
        return view;
    }

    /// <summary>
    /// Same shape, but paragraph 1 still carries the <c>w:commentReference</c> run alongside its
    /// trailing text -- the ordinary, not-yet-edited cross-paragraph-import shape, where the comment
    /// has TWO surviving anchors (paragraph 0's text run and paragraph 1's reference run).
    /// </summary>
    private static DocumentView BuildCrossParagraphCommentWithReferenceStillPresent(out int commentId)
    {
        var document = new TextDocument();

        var anchorParagraph = new Paragraph();
        anchorParagraph.Runs.Add(new Run("Anchor text", RunFormatting.Default) { CommentId = 1 });
        document.Blocks.Add(anchorParagraph);

        var referenceParagraph = new Paragraph();
        referenceParagraph.Runs.Add(Run.CommentReference(1));
        referenceParagraph.Runs.Add(new Run("Trailing", RunFormatting.Default));
        document.Blocks.Add(referenceParagraph);

        document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");

        var view = new DocumentView();
        view.LoadModel(document);
        commentId = 1;
        return view;
    }

    [StaFact]
    public void TypingOverASelection_ThroughSharedFastPath_PrunesCommentOnceLastAnchorIsGone()
    {
        var view = BuildCrossParagraphCommentWithReferenceAlreadyStripped(out var commentId);

        // Paragraph 0 holds only a non-reference CommentId run, so IsPortableBodyTextParagraph accepts
        // it and typing over its selection goes through TryApplyBodyTextInput's shared fast path -- the
        // exact path OnPreviewTextInput takes for an ordinary keystroke.
        view.SetSelectionRangeForTest(0, 0, 0, "Anchor text".Length);
        view.SimulateTypeText("x");

        view.Model.Comments.ContainsKey(commentId).Should().BeFalse(
            "replacing the last run that carried the comment's id leaves nothing anchored anywhere in "
            + "the document, so the fast path must prune it just like Backspace/Delete already do");
    }

    // ── Sibling / no-regression: a surviving anchor elsewhere must never be pruned away ─────────────────

    [StaFact]
    public void TypingOverASelection_ThroughSharedFastPath_LeavesCommentAloneWhileTheReferenceParagraphStillAnchorsIt()
    {
        var view = BuildCrossParagraphCommentWithReferenceStillPresent(out var commentId);

        // Type over paragraph 0's anchor text -- paragraph 1's reference run is still out there, so the
        // comment must be read as used even though the new prune call runs.
        view.SetSelectionRangeForTest(0, 0, 0, "Anchor text".Length);
        view.SimulateTypeText("x");

        view.Model.Comments.ContainsKey(commentId).Should().BeTrue(
            "paragraph 1's reference run still anchors the comment, so the new prune call must be a no-op here");
        ((Paragraph)view.Model.Blocks[1]).Runs.Should().Contain(r => r.IsCommentReference && r.CommentId == commentId,
            "the surviving reference run itself must be untouched by this unrelated edit");
    }

    // ── Sibling / no-regression: ordinary typed replacement with no notes/comments at all is unaffected ─

    [StaFact]
    public void TypingOverASelection_ThroughSharedFastPath_InADocumentWithNoCommentsIsUnaffected()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Head tail"));
        var view = new DocumentView();
        view.LoadModel(document);

        view.SetSelectionRangeForTest(0, 0, 0, 5);
        view.SimulateTypeText("x");

        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("xtail");
        view.Model.Comments.Should().BeEmpty();
        view.Model.Footnotes.Should().BeEmpty();
        view.Model.Endnotes.Should().BeEmpty();
    }
}
