using FreeX.Core.Model;
using SharedCommentNavigationPlanner = FreeX.App.Presentation.Comments.CommentNavigationPlanner;

namespace FreeX.App.Host;

public static class CommentNavigationPlanner
{
    public static List<CellAddress> OrderedCommentAddresses(IReadOnlyDictionary<CellAddress, string> comments) =>
        SharedCommentNavigationPlanner.OrderedCommentAddresses(comments);

    public static List<CellAddress> OrderedNoteAddresses(IReadOnlyDictionary<CellAddress, string> notes) =>
        SharedCommentNavigationPlanner.OrderedNoteAddresses(notes);

    public static List<CellAddress> OrderedThreadedCommentAddresses(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        SharedCommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments);

    public static List<CellAddress> OrderedCommentAddresses(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        SharedCommentNavigationPlanner.OrderedCommentAddresses(comments, threadedComments);

    public static CellAddress FindNext(IReadOnlyList<CellAddress> orderedComments, CellAddress current, bool previous) =>
        SharedCommentNavigationPlanner.FindNext(orderedComments, current, previous);

    public static string FormatCommentList(IReadOnlyDictionary<CellAddress, string> comments) =>
        SharedCommentNavigationPlanner.FormatCommentList(comments);

    public static string FormatNoteList(IReadOnlyDictionary<CellAddress, string> notes) =>
        SharedCommentNavigationPlanner.FormatNoteList(notes);

    public static string FormatThreadedCommentList(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        SharedCommentNavigationPlanner.FormatThreadedCommentList(threadedComments);

    public static string FormatCommentList(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        SharedCommentNavigationPlanner.FormatCommentList(comments, threadedComments);

    public static string GetDefaultCommentText(IReadOnlyDictionary<CellAddress, string> comments, CellAddress address) =>
        SharedCommentNavigationPlanner.GetDefaultCommentText(comments, address);

    public static string FormatThreadedComment(ThreadedComment thread) =>
        SharedCommentNavigationPlanner.FormatThreadedComment(thread);

    public static string? FormatCellCommentPreview(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        CellAddress address) =>
        SharedCommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address);
}
