using System.Linq;
using System.Reflection;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for pruning orphaned footnote/endnote/comment entries after a native-fallback edit removes
/// the marker/anchor run directly out of the live FlowDocument. <c>DocumentView.OnPreviewKeyDown</c>'s
/// Backspace/Delete branch falls through to native RichTextBox editing (via
/// <c>TryPrepareNativeFallback</c>) whenever the model-aware body-edit session declines -- which it always
/// does for any paragraph containing a footnote/endnote/comment-reference run (see
/// <c>DocumentEditingSession.IsPortableBodyTextRun</c>) -- so native editing can delete such a marker run
/// with zero knowledge of <c>TextDocument.Footnotes</c>/<c>Endnotes</c>/<c>Comments</c>.
/// <c>DocumentView.ApplyNativeFallbackDeleteAndPruneOrphanedAnchors</c> is the real production choke point
/// (wired into <c>OnPreviewKeyDown</c>'s Backspace/Delete branch); its actual native keystroke half is
/// itself untestable headlessly (real WPF window-activation/focus timing makes a synthetic keystroke
/// unreliable to raise -- see <c>ContentControlKeyboardLockTests</c>'s identical rationale for reflecting
/// straight into the choke point instead of raising a full keystroke), so these tests invoke its second
/// half -- <c>PruneOrphanedNoteAndCommentAnchorsAfterNativeEdit</c>, the resync-and-prune step -- via
/// reflection directly, against a FlowDocument whose marker/anchor run has already been removed exactly as
/// native Backspace/Delete would leave it. Runs on an STA thread (<c>[StaFact]</c>) because DocumentView is
/// a WPF RichTextBox.
/// </summary>
public sealed class NoteAndCommentOrphanPruningTests
{
    private static readonly MethodInfo PruneAfterNativeEditMethod =
        typeof(DocumentView).GetMethod(
            "PruneOrphanedNoteAndCommentAnchorsAfterNativeEdit", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.PruneOrphanedNoteAndCommentAnchorsAfterNativeEdit not found -- the choke point this test targets was renamed or removed.");

    /// <summary>Invokes the real (private) post-native-edit resync-and-prune step directly on <paramref name="view"/>.</summary>
    private static void PruneAfterNativeEdit(DocumentView view) =>
        PruneAfterNativeEditMethod.Invoke(view, null);

    private static System.Windows.Documents.Paragraph SingleParagraph(DocumentView view) =>
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();

    /// <summary>
    /// Removes every Inline in <paramref name="paragraph"/> whose Tag is DocumentView's private nested
    /// <c>FootnoteMarker</c>/<c>EndnoteMarker</c> record carrying <paramref name="id"/> -- the same removal
    /// a native Backspace/Delete across the marker run performs directly on the live FlowDocument.
    /// </summary>
    private static void RemoveNoteMarker(System.Windows.Documents.Paragraph paragraph, string typeName, int id)
    {
        var markerType = typeof(DocumentView).GetNestedType(typeName, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"DocumentView.{typeName} not found -- it was renamed or removed.");
        var idProperty = markerType.GetProperty(typeName == "FootnoteMarker" ? "FootnoteId" : "EndnoteId")
            ?? throw new InvalidOperationException($"{typeName}'s id property not found.");

        var toRemove = paragraph.Inlines
            .OfType<System.Windows.Documents.Run>()
            .Where(run => run.Tag is not null
                && run.Tag.GetType() == markerType
                && (int)idProperty.GetValue(run.Tag)! == id)
            .ToList();
        foreach (var run in toRemove)
            paragraph.Inlines.Remove(run);
    }

    /// <summary>
    /// Removes every Inline in <paramref name="paragraph"/> carrying a <c>RunMarkers.Comment</c> facet for
    /// <paramref name="commentId"/> -- both the textless reference-anchor run and any covered text run --
    /// mirroring a Backspace/Delete selection that spans the whole commented range (e.g. select-all-and-delete).
    /// </summary>
    private static void RemoveCommentAnchors(System.Windows.Documents.Paragraph paragraph, int commentId)
    {
        var runMarkersType = typeof(DocumentView).GetNestedType("RunMarkers", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DocumentView.RunMarkers not found -- it was renamed or removed.");
        var commentProperty = runMarkersType.GetProperty("Comment")
            ?? throw new InvalidOperationException("RunMarkers.Comment not found.");
        var commentMarkerType = typeof(DocumentView).GetNestedType("CommentMarker", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DocumentView.CommentMarker not found -- it was renamed or removed.");
        var commentIdProperty = commentMarkerType.GetProperty("CommentId")
            ?? throw new InvalidOperationException("CommentMarker.CommentId not found.");

        var toRemove = new List<System.Windows.Documents.Run>();
        foreach (var run in paragraph.Inlines.OfType<System.Windows.Documents.Run>())
        {
            if (run.Tag is null || run.Tag.GetType() != runMarkersType)
                continue;
            var comment = commentProperty.GetValue(run.Tag);
            if (comment is not null && (int)commentIdProperty.GetValue(comment)! == commentId)
                toRemove.Add(run);
        }
        foreach (var run in toRemove)
            paragraph.Inlines.Remove(run);
    }

    [StaFact]
    public void NativeEdit_PrunesOrphanedFootnote_AfterTheMarkerRunIsRemoved()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("before after"));
        var view = new DocumentView();
        view.LoadModel(doc);
        view.MoveCaretToBlockForTest(0, 7);
        view.InsertFootnote("note text");
        view.Model.Footnotes.Should().ContainKey(1);

        // Simulate what native RichTextBox Backspace/Delete does once TryPrepareNativeFallback lets it
        // through: remove the marker Inline directly from the live FlowDocument, with no knowledge of
        // TextDocument.Footnotes.
        RemoveNoteMarker(SingleParagraph(view), "FootnoteMarker", 1);

        PruneAfterNativeEdit(view);

        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be(
            "before after", "the marker run is gone from the rebuilt body text either way");
        view.Model.Footnotes.Should().BeEmpty(
            "the orphaned footnote entry must be pruned once its only reference mark is gone");
    }

    [StaFact]
    public void NativeEdit_PrunesOrphanedComment_AfterTheAnchorRunsAreRemoved()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("before "));
        paragraph.Runs.Add(new Run("flag") { CommentId = 0 });
        paragraph.Runs.Add(Run.CommentReference(0));
        paragraph.Runs.Add(new Run(" after"));
        doc.Blocks.Add(paragraph);
        doc.Comments[0] = new Comment(0, "note", "A", "A");

        var view = new DocumentView();
        view.LoadModel(doc);
        view.Model.Comments.Should().ContainKey(0);

        RemoveCommentAnchors(SingleParagraph(view), 0);

        PruneAfterNativeEdit(view);

        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be(
            "before  after", "the commented text and its reference mark are gone from the rebuilt body either way");
        view.Model.Comments.Should().BeEmpty(
            "the orphaned comment entry must be pruned once none of its anchors survive");
    }

    // ── No-regression siblings: an anchor that SURVIVES must never be pruned by an unrelated edit ────

    [StaFact]
    public void NativeEdit_KeepsFootnote_WhenItsMarkerRunStillExists()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("before after"));
        var view = new DocumentView();
        view.LoadModel(doc);
        view.MoveCaretToBlockForTest(0, 7);
        view.InsertFootnote("note text");

        // No marker removal here -- the run is left untouched, as an edit elsewhere in the document would.
        PruneAfterNativeEdit(view);

        view.Model.Footnotes.Should().ContainKey(
            1, "a footnote whose reference mark is still present in the body must not be pruned");
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().Contain(run => run.FootnoteId == 1);
    }

    [StaFact]
    public void NativeEdit_KeepsComment_WhenItsAnchorRunsStillExist()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("before "));
        paragraph.Runs.Add(new Run("flag") { CommentId = 0 });
        paragraph.Runs.Add(Run.CommentReference(0));
        paragraph.Runs.Add(new Run(" after"));
        doc.Blocks.Add(paragraph);
        doc.Comments[0] = new Comment(0, "note", "A", "A");

        var view = new DocumentView();
        view.LoadModel(doc);

        // No anchor removal here -- both runs are left untouched.
        PruneAfterNativeEdit(view);

        view.Model.Comments.Should().ContainKey(
            0, "a comment whose anchor is still present in the body must not be pruned");
    }

    /// <summary>
    /// Regression guard for the over-broad first draft of this fix (hooking the prune into
    /// <c>CommitToModel</c> generally instead of scoping it to the native-fallback delete choke point):
    /// a plain <see cref="DocumentView.CommitToModel"/> call must never prune a footnote/comment that has
    /// no anchor for an unrelated reason -- e.g. a mid-edit workflow that manages
    /// <see cref="TextDocument.Footnotes"/> directly, exactly like <see cref="DocumentView.ReplaceNoteContent"/>'s
    /// own tests construct.
    /// </summary>
    [StaFact]
    public void PlainCommitToModel_NeverPrunesAMarkerlessFootnote()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Footnotes[1] = new Footnote(1, "original text");
        var view = new DocumentView();
        view.LoadModel(doc);

        view.CommitToModel();

        view.Model.Footnotes.Should().ContainKey(
            1, "CommitToModel alone must never prune a note/comment -- only the native-fallback delete choke point does");
    }
}
