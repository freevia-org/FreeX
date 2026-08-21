using System.Linq;
using System.Reflection;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r161: the WPF half of the bypass round 160 fixed in the Avalonia shell.
///
/// <para>
/// An ordinary Backspace or Delete first tries the shared portable path
/// (<c>DocumentView.TryApplyBodyDeletion</c>); only when that DECLINES does the native fallback run,
/// and the orphaned-anchor prune lived solely there. So a comment whose last anchoring run was
/// inside the deleted range was orphaned and kept for ever.
/// </para>
///
/// <para>
/// It stays invisible for a comment this app created: that always sits in the same paragraph as its
/// reference run, and the portable gate declines any paragraph containing one -- which is precisely
/// what <see cref="NoteAndCommentOrphanPruningTests"/>'s own summary relies on. A comment imported
/// from Word can span a paragraph boundary (DocxReader tracks the open range in a paragraph-local
/// variable and cannot follow it), leaving an anchor paragraph with no reference run. That paragraph
/// IS portable, so deleting its text takes the fast path and never prunes.
/// </para>
/// </summary>
public sealed class R161_PortableDeleteCommentPruningTests
{
    private static readonly MethodInfo TryApplyBodyDeletionMethod =
        typeof(DocumentView).GetMethod(
            "TryApplyBodyDeletion", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TryApplyBodyDeletion is the shared portable deletion path this test exists "
            + "to cover; if it was renamed, retarget the test rather than deleting it");

    [StaFact]
    public void DeletingTheOnlyAnchorThroughThePortablePath_PrunesTheComment()
    {
        var view = BuildCrossParagraphComment();

        // Select the anchor paragraph's text. That paragraph holds only a CommentId-bearing run and
        // no reference run, so the portable session accepts it and the fast path handles the delete.
        SelectFirstParagraph(view);

        InvokeBodyDeletion(view).Should().BeTrue(
            "this is the portable path -- if it declined, the test would be exercising the native "
            + "fallback that already pruned, and would prove nothing");

        view.Model.Comments.Should().NotContainKey(
            1,
            "no run anywhere still carries the comment's id, so it must be pruned exactly as the "
            + "native fallback would have done");
    }

    [StaFact]
    public void DeletingTextWhileAnotherRunStillAnchorsTheComment_KeepsIt()
    {
        // Sibling no-regression: the prune must drop only genuinely anchorless comments, so a
        // comment with a surviving anchor elsewhere has to be left alone.
        var view = BuildCrossParagraphComment(secondAnchor: true);

        SelectFirstParagraph(view);
        InvokeBodyDeletion(view).Should().BeTrue();

        view.Model.Comments.Should().ContainKey(
            1,
            "another run still carries the comment's id, so it is still anchored and must survive");
    }

    private static bool InvokeBodyDeletion(DocumentView view) =>
        (bool)TryApplyBodyDeletionMethod.Invoke(view, [false])!;

    private static void SelectFirstParagraph(DocumentView view)
    {
        // A real WPF selection, not a synthetic keystroke: TryApplyBodyDeletion reads the live
        // selection, and Selection.Select works without the window focus a keystroke would need.
        var paragraph = (System.Windows.Documents.Paragraph)view.Document.Blocks.FirstBlock;
        view.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
    }

    private static DocumentView BuildCrossParagraphComment(bool secondAnchor = false)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var anchorParagraph = new Paragraph();
        anchorParagraph.Runs.Add(new Run("Anchor text", RunFormatting.Default) { CommentId = 1 });
        document.Blocks.Add(anchorParagraph);

        var secondParagraph = new Paragraph();
        if (secondAnchor)
            secondParagraph.Runs.Add(new Run("Also anchored", RunFormatting.Default) { CommentId = 1 });
        else
            secondParagraph.Runs.Add(new Run("Plain", RunFormatting.Default));
        document.Blocks.Add(secondParagraph);

        document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }
}
