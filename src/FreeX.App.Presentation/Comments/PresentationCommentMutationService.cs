using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

public sealed record PresentationCommentMutationPlan(
    string Label,
    Func<GridRange, IWorkbookCommand> CreateCommand);

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
