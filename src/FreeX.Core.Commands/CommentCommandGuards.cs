using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class CommentCommandGuards
{
    private const string ThreadedCommentNotFoundMessage = "No threaded comment exists at the selected cell.";
    private const string ThreadedCommentReplyNotFoundMessage = "No threaded comment reply exists at the selected index.";
    private const string CellAlreadyHasThreadedCommentMessage =
        "This cell already has a threaded comment. Excel does not allow a cell to have both a Note and a Comment -- delete the comment first.";
    private const string CellAlreadyHasNoteMessage =
        "This cell already has a Note. Excel does not allow a cell to have both a Note and a Comment -- delete the note first.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome ThreadedCommentNotFound() =>
        new(false, ThreadedCommentNotFoundMessage);

    public static CommandOutcome ThreadedCommentReplyNotFound() =>
        new(false, ThreadedCommentReplyNotFoundMessage);

    // R124: real Excel never lets a single cell carry both a legacy Note and a threaded
    // Comment -- FreeX's own XLSX writer relies on that invariant (XlsxFileAdapter.Save.cs,
    // the Comments-vs-ThreadedComments loops) and ConvertNotesToCommentsCommand already skips
    // a cell with a pre-existing threaded comment for exactly this reason. These two checks are
    // the symmetric guards for the two direct-authoring commands (SetCommentCommand /
    // SetThreadedCommentCommand) so the invalid combined state can never be written in the
    // first place, regardless of which UI entry point (or future automation caller) tries to
    // create it.
    public static CommandOutcome? RejectIfCellHasThreadedComment(Sheet sheet, CellAddress address) =>
        sheet.ThreadedComments.ContainsKey(address)
            ? new CommandOutcome(false, CellAlreadyHasThreadedCommentMessage)
            : null;

    public static CommandOutcome? RejectIfCellHasNote(Sheet sheet, CellAddress address) =>
        sheet.Comments.ContainsKey(address)
            ? new CommandOutcome(false, CellAlreadyHasNoteMessage)
            : null;
}
