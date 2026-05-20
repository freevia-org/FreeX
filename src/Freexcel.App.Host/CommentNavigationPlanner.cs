using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class CommentNavigationPlanner
{
    public static List<CellAddress> OrderedCommentAddresses(IReadOnlyDictionary<CellAddress, string> comments) =>
        comments.Keys
            .OrderBy(address => address.Row)
            .ThenBy(address => address.Col)
            .ToList();

    public static List<CellAddress> OrderedCommentAddresses(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        comments.Keys
            .Concat(threadedComments.Keys)
            .Distinct()
            .OrderBy(address => address.Row)
            .ThenBy(address => address.Col)
            .ToList();

    public static CellAddress FindNext(IReadOnlyList<CellAddress> orderedComments, CellAddress current, bool previous)
    {
        if (orderedComments.Count == 0)
            return default;

        var target = previous
            ? orderedComments.LastOrDefault(address => address.Row < current.Row || (address.Row == current.Row && address.Col < current.Col))
            : orderedComments.FirstOrDefault(address => address.Row > current.Row || (address.Row == current.Row && address.Col > current.Col));
        return target.Equals(default(CellAddress))
            ? previous ? orderedComments[^1] : orderedComments[0]
            : target;
    }

    public static string FormatCommentList(IReadOnlyDictionary<CellAddress, string> comments) =>
        string.Join(Environment.NewLine,
            OrderedCommentAddresses(comments).Select(address => $"{address.ToA1()}: {comments[address]}"));

    public static string FormatCommentList(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        string.Join(Environment.NewLine,
            OrderedCommentAddresses(comments, threadedComments)
                .Select(address => $"{address.ToA1()}: {GetCommentText(comments, threadedComments, address)}"));

    public static string GetDefaultCommentText(IReadOnlyDictionary<CellAddress, string> comments, CellAddress address) =>
        comments.TryGetValue(address, out var comment)
            ? comment
            : string.Empty;

    private static string GetCommentText(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        CellAddress address) =>
        comments.TryGetValue(address, out var comment)
            ? comment
            : threadedComments[address].Text;
}
