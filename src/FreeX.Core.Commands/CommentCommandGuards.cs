using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class CommentCommandGuards
{
    private const string ThreadedCommentNotFoundMessage = "No threaded comment exists at the selected cell.";
    private const string ThreadedCommentReplyNotFoundMessage = "No threaded comment reply exists at the selected index.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome ThreadedCommentNotFound() =>
        new(false, ThreadedCommentNotFoundMessage);

    public static CommandOutcome ThreadedCommentReplyNotFound() =>
        new(false, ThreadedCommentReplyNotFoundMessage);
}
