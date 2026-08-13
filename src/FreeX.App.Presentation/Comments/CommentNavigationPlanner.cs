using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

public sealed record CommentListRowPlan(CellAddress Address, string Cell, string Text);

public static class CommentNavigationPlanner
{
    public static List<CellAddress> OrderedCommentAddresses(IReadOnlyDictionary<CellAddress, string> comments) =>
        OrderedNoteAddresses(comments);

    public static List<CellAddress> OrderedNoteAddresses(IReadOnlyDictionary<CellAddress, string> notes) =>
        OrderAddresses(notes.Keys);

    public static List<CellAddress> OrderedThreadedCommentAddresses(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        OrderAddresses(threadedComments.Keys);

    public static List<CellAddress> OrderedCommentAddresses(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        OrderAddresses(comments.Keys.Concat(threadedComments.Keys).Distinct());

    public static IReadOnlyList<CommentListRowPlan> CreateThreadedCommentRows(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        OrderedThreadedCommentAddresses(threadedComments)
            .Select(address => new CommentListRowPlan(
                address,
                address.ToA1(),
                FormatThreadedComment(threadedComments[address])))
            .ToArray();

    public static IReadOnlyList<CommentListRowPlan> CreateNoteRows(
        IReadOnlyDictionary<CellAddress, string> notes) =>
        OrderedNoteAddresses(notes)
            .Select(address => new CommentListRowPlan(address, address.ToA1(), notes[address]))
            .ToArray();

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
        FormatNoteList(comments);

    public static string FormatNoteList(IReadOnlyDictionary<CellAddress, string> notes) =>
        string.Join(Environment.NewLine,
            OrderedNoteAddresses(notes).Select(address => $"{address.ToA1()}: {notes[address]}"));

    public static string FormatThreadedCommentList(
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments) =>
        string.Join(Environment.NewLine,
            OrderedThreadedCommentAddresses(threadedComments)
                .Select(address => $"{address.ToA1()}: {FormatThreadedComment(threadedComments[address])}"));

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
        var formattedRoot = FormatCommentPart(thread.Author, thread.Text, thread.CreatedAtUtc);
        if (thread.Replies.Count == 0)
            return thread.IsResolved
                ? string.Concat(formattedRoot, " | Resolved")
                : formattedRoot;

        var builder = new StringBuilder(formattedRoot);
        foreach (var reply in thread.Replies)
        {
            builder.Append(" | ");
            builder.Append(FormatCommentPart(reply.Author, reply.Text, reply.CreatedAtUtc));
        }

        if (thread.IsResolved)
            builder.Append(" | Resolved");

        return builder.ToString();
    }

    public static string? FormatCellCommentPreview(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        CellAddress address)
    {
        if (!comments.TryGetValue(address, out var note))
            return threadedComments.TryGetValue(address, out var threadOnly)
                ? FormatThreadedComment(threadOnly)
                : null;

        if (!threadedComments.TryGetValue(address, out var thread))
            return $"Note: {note}";

        return string.Concat("Note: ", note, Environment.NewLine, FormatThreadedComment(thread));
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

    private static string FormatCommentPart(string author, string text, DateTimeOffset? createdAtUtc = null)
    {
        var label = author.Trim();
        if (createdAtUtc is { } timestamp)
        {
            var formatted = timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            label = string.IsNullOrWhiteSpace(label)
                ? formatted
                : $"{label} ({formatted})";
        }

        return string.IsNullOrWhiteSpace(label)
            ? text
            : $"{label}: {text}";
    }

    private static List<CellAddress> OrderAddresses(IEnumerable<CellAddress> addresses) =>
        addresses
            .OrderBy(address => address.Row)
            .ThenBy(address => address.Col)
            .ToList();

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
