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

        var index = previous
            ? FindFirstNotBefore(orderedComments, current) - 1
            : FindFirstAfter(orderedComments, current);
        if (index < 0)
            index = orderedComments.Count - 1;
        else if (index >= orderedComments.Count)
            index = 0;

        return orderedComments[index];
    }

    public static string FormatCommentList(IReadOnlyDictionary<CellAddress, string> comments) =>
        string.Join(Environment.NewLine,
            OrderedCommentAddresses(comments).Select(address => $"{address.ToA1()}: {comments[address]}"));

    public static string FormatCommentList(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        string.Join(Environment.NewLine,
            OrderedCommentAddresses(comments, threadedComments)
                .SelectMany(address => GetCommentListLines(comments, threadedComments, address)));

    public static string GetDefaultCommentText(IReadOnlyDictionary<CellAddress, string> comments, CellAddress address) =>
        comments.TryGetValue(address, out var comment)
            ? comment
            : string.Empty;

    public static string FormatThreadedComment(ThreadedComment thread)
    {
        var parts = new List<string> { FormatCommentPart(thread.Author, thread.Text) };
        parts.AddRange(thread.Replies.Select(reply => FormatCommentPart(reply.Author, reply.Text)));
        if (thread.IsResolved)
            parts.Add("Resolved");

        return string.Join(" | ", parts);
    }

    public static string? FormatCellCommentPreview(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        CellAddress address)
    {
        var parts = new List<string>();
        if (comments.TryGetValue(address, out var note))
            parts.Add($"Note: {note}");
        if (threadedComments.TryGetValue(address, out var thread))
            parts.Add(FormatThreadedComment(thread));

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static IEnumerable<string> GetCommentListLines(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        CellAddress address)
    {
        var prefix = address.ToA1();

        if (comments.TryGetValue(address, out var note) &&
            threadedComments.TryGetValue(address, out var thread))
        {
            yield return $"{prefix}: Note: {note}";
            yield return $"{prefix}: Threaded: {FormatThreadedComment(thread)}";
            yield break;
        }

        if (comments.TryGetValue(address, out note))
        {
            yield return $"{prefix}: {note}";
            yield break;
        }

        if (threadedComments.TryGetValue(address, out thread))
            yield return $"{prefix}: {FormatThreadedComment(thread)}";
    }

    private static string FormatCommentPart(string author, string text) =>
        string.IsNullOrWhiteSpace(author)
            ? text
            : $"{author.Trim()}: {text}";

    private static int FindFirstAfter(IReadOnlyList<CellAddress> orderedComments, CellAddress current)
    {
        var low = 0;
        var high = orderedComments.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (ComparePosition(orderedComments[mid], current) <= 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static int FindFirstNotBefore(IReadOnlyList<CellAddress> orderedComments, CellAddress current)
    {
        var low = 0;
        var high = orderedComments.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (ComparePosition(orderedComments[mid], current) < 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static int ComparePosition(CellAddress left, CellAddress right)
    {
        var row = left.Row.CompareTo(right.Row);
        return row != 0 ? row : left.Col.CompareTo(right.Col);
    }
}
