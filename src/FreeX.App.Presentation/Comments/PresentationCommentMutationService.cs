using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

/// <summary>
/// An undo label plus a deferred factory that turns the caller's <see cref="GridRange"/> into the
/// <see cref="IWorkbookCommand"/> to push onto the workbook command bus. Nothing is applied here.
/// <para>
/// Cross-app note (assessed 2026-08-15): <c>FreeP.App.Compositor.PresentationCommentMutationPlan</c>
/// shares only this type's <em>name</em>, and the collision is purely lexical — "Presentation" here
/// names the <c>FreeX.App.Presentation</c> layer, whereas in FreeP it names the PowerPoint
/// presentation being edited. That record is a materialized before/after slide-comment state
/// (intent, should-apply flag, slide index, comment index, <c>SlideComment</c>, validation message);
/// this one is a label and an unapplied command factory. Do not merge them.
/// </para>
/// </summary>
public sealed record PresentationCommentMutationPlan(
    string Label,
    Func<GridRange, IWorkbookCommand> CreateCommand);

/// <summary>
/// Projects spreadsheet note/threaded-comment intents into undoable
/// <see cref="PresentationCommentMutationPlan"/> command factories keyed by <see cref="SheetId"/>
/// and cell address.
/// <para>
/// Cross-app note (assessed 2026-08-15):
/// <c>FreeP.App.Compositor.PresentationCommentMutationService</c> shares only this type's
/// <em>name</em>; "Presentation" means the <c>FreeX.App.Presentation</c> layer here and the
/// PowerPoint presentation there. The domains do not overlap: this service addresses a cell within
/// a sheet, models Excel-only concepts (legacy notes vs. threaded comments, per-note and
/// show-all-notes visibility toggles, convert-notes-to-comments, resolve/unresolve, reply
/// edit/delete by index), returns <em>unapplied</em> command factories so the host's undo stack
/// owns execution, and carries per-intent undo labels. The FreeP service addresses a comment within
/// a slide, models PowerPoint-only concepts (EMU x/y anchor position, author initials, timestamps,
/// resolved-at/resolved-by), is a <c>static</c> class that <em>applies</em> the mutation to a live
/// <c>IReadOnlyList&lt;Slide&gt;</c> and then renormalizes the selected comment index, and delegates
/// to <c>PresentationReviewWorkflowPlanner</c> rather than to any command type. Ignoring braces and
/// short lines, the two files share <em>zero</em> identical lines — not even the type declaration,
/// which is <c>sealed class</c> here and <c>static class</c> there. There is no neutral contract to
/// extract; do not merge them.
/// </para>
/// </summary>
public sealed class PresentationCommentMutationService
{
    public PresentationCommentMutationPlan PlanSetNote(
        SheetId sheetId,
        string text) =>
        new(
            "Comment",
            range => new SetCommentCommand(sheetId, range.Start, text));

    public PresentationCommentMutationPlan PlanDeleteNote(SheetId sheetId) =>
        new(
            "Comment",
            range => new DeleteCommentCommand(sheetId, range.Start));

    public PresentationCommentMutationPlan PlanToggleNoteVisibility(SheetId sheetId) =>
        new(
            "Show/Hide Note",
            range => new ShowHideCommentCommand(sheetId, range.Start));

    public PresentationCommentMutationPlan PlanToggleAllNotesVisibility(SheetId sheetId) =>
        new(
            "Show All Notes",
            _ => new ShowAllNotesCommand(sheetId));

    public PresentationCommentMutationPlan PlanDeleteThreadedComment(SheetId sheetId) =>
        new(
            "Threaded Comment",
            range => new DeleteThreadedCommentCommand(sheetId, range.Start));

    public PresentationCommentMutationPlan PlanConvertNotesToComments(SheetId sheetId) =>
        new(
            "Convert to Comments",
            _ => new ConvertNotesToCommentsCommand(sheetId));

    public PresentationCommentMutationPlan PlanResolveThreadedComment(
        SheetId sheetId,
        bool resolved) =>
        new(
            resolved ? "Resolve Comment" : "Unresolve Comment",
            range => new ResolveThreadedCommentCommand(sheetId, range.Start, resolved));

    public PresentationCommentMutationPlan? PlanThreadedComment(
        SheetId sheetId,
        ThreadedComment? existing,
        ThreadedCommentDialogResult result,
        string author)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (existing is null)
        {
            return result.ReplyText is null
                ? null
                : new PresentationCommentMutationPlan(
                    "Threaded Comment",
                    range => new SetThreadedCommentCommand(sheetId, range.Start, result.ReplyText, author));
        }

        return result.Action switch
        {
            ThreadedCommentDialogAction.EditReply when
                result.ReplyIndex is { } replyIndex && result.ReplyEditText is not null =>
                new PresentationCommentMutationPlan(
                    "Edit Comment Reply",
                    range => new UpdateThreadedCommentReplyCommand(
                        sheetId,
                        range.Start,
                        replyIndex,
                        result.ReplyEditText,
                        result.IsResolved)),
            ThreadedCommentDialogAction.DeleteReply when result.ReplyIndex is { } replyIndex =>
                new PresentationCommentMutationPlan(
                    "Delete Comment Reply",
                    range => new DeleteThreadedCommentReplyCommand(
                        sheetId,
                        range.Start,
                        replyIndex,
                        result.IsResolved)),
            _ when result.RootText is not null ||
                   result.ReplyText is not null ||
                   result.IsResolved != existing.IsResolved =>
                new PresentationCommentMutationPlan(
                    "Edit Comment",
                    range => new ApplyThreadedCommentChangesCommand(
                        sheetId,
                        range.Start,
                        result.RootText,
                        result.ReplyText,
                        result.IsResolved,
                        author)),
            _ => null,
        };
    }
}
