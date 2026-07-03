using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum ReviewBalloonKind
{
    Comment,
    Insertion,
    Deletion,
    Formatting
}

public sealed record ReviewBalloonSource(
    ReviewBalloonKind Kind,
    string Author,
    string Text,
    int BlockIndex,
    int Offset,
    int SortKind,
    bool Resolved = false)
{
    public string KindLabel => Kind switch
    {
        ReviewBalloonKind.Comment => Resolved ? "Resolved comment" : "Comment",
        ReviewBalloonKind.Insertion => "Inserted",
        ReviewBalloonKind.Deletion => "Deleted",
        ReviewBalloonKind.Formatting => "Formatting",
        _ => Kind.ToString()
    };
}

public sealed record ReviewBalloonLayoutOptions(
    double StripWidth = 200,
    double BalloonWidth = 176,
    double BalloonHeight = 56,
    double BalloonGap = 8,
    double BalloonX = 12,
    double BalloonCornerRadius = 4,
    double LeaderStartX = 0,
    double LeaderThickness = 1);

public sealed record ReviewBalloonLayout(
    ReviewBalloonSource Source,
    int Ordinal,
    double BalloonX,
    double BalloonY,
    double BalloonWidth,
    double BalloonHeight,
    double LeaderStartX,
    double LeaderStartY,
    double LeaderEndX,
    double LeaderEndY)
{
    public double BalloonMidY => BalloonY + BalloonHeight / 2;
}

public static class ReviewBalloonLayoutPlanner
{
    public static IReadOnlyList<ReviewBalloonSource> BuildSources(
        TextDocument document,
        ReviewDisplayPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(document);

        var revisions = RevisionList.Enumerate(document)
            .Where(entry => ShouldShowRevision(entry, policy))
            .Select(FromRevision);

        var comments = policy.ShowComments
            ? CommentListPlanner.Build(document).Select(FromComment)
            : Enumerable.Empty<ReviewBalloonSource>();

        return revisions.Concat(comments)
            .OrderBy(item => item.BlockIndex)
            .ThenBy(item => item.Offset)
            .ThenBy(item => item.SortKind)
            .ToList();
    }

    public static IReadOnlyList<ReviewBalloonLayout> BuildLayout(
        TextDocument document,
        ReviewDisplayPolicy policy,
        double viewportHeight,
        ReviewBalloonLayoutOptions? options = null)
    {
        return BuildLayout(BuildSources(document, policy), viewportHeight, options);
    }

    public static IReadOnlyList<ReviewBalloonLayout> BuildLayout(
        IReadOnlyList<ReviewBalloonSource> sources,
        double viewportHeight,
        ReviewBalloonLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var layoutOptions = options ?? new ReviewBalloonLayoutOptions();
        var canvasHeight = viewportHeight > 0 ? viewportHeight : 800;
        var totalSlots = Math.Max(sources.Count, 1);
        var layouts = new List<ReviewBalloonLayout>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            var balloonY = layoutOptions.BalloonGap + i * (layoutOptions.BalloonHeight + layoutOptions.BalloonGap);
            var leaderStartY = canvasHeight * (i + 0.5) / totalSlots;
            var leaderEndY = balloonY + layoutOptions.BalloonHeight / 2;

            layouts.Add(new ReviewBalloonLayout(
                sources[i],
                i,
                layoutOptions.BalloonX,
                balloonY,
                layoutOptions.BalloonWidth,
                layoutOptions.BalloonHeight,
                layoutOptions.LeaderStartX,
                leaderStartY,
                layoutOptions.BalloonX,
                leaderEndY));
        }

        return layouts;
    }

    public static string TruncatePreview(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static bool ShouldShowRevision(RevisionEntry entry, ReviewDisplayPolicy policy) =>
        entry.Kind switch
        {
            RevisionEntryKind.Formatting => policy.ShowFormatting,
            RevisionEntryKind.Insertion or RevisionEntryKind.Deletion => policy.ShowInsertionsAndDeletions,
            _ => true,
        };

    private static ReviewBalloonSource FromRevision(RevisionEntry entry)
    {
        var kind = entry.Kind switch
        {
            RevisionEntryKind.Insertion => ReviewBalloonKind.Insertion,
            RevisionEntryKind.Deletion => ReviewBalloonKind.Deletion,
            _ => ReviewBalloonKind.Formatting,
        };

        return new ReviewBalloonSource(
            kind,
            string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author,
            string.IsNullOrWhiteSpace(entry.Text) ? "(formatting change)" : NormalizePreview(entry.Text),
            entry.BlockIndex,
            RevisionOffset(entry),
            SortKind: 0);
    }

    private static ReviewBalloonSource FromComment(CommentListItem item) =>
        new(
            ReviewBalloonKind.Comment,
            string.IsNullOrWhiteSpace(item.Author) ? "Unknown" : item.Author,
            item.ReplyCount > 0
                ? $"{item.Text} ({item.ReplyCount} repl{(item.ReplyCount == 1 ? "y" : "ies")})"
                : item.Text,
            item.BlockIndex,
            item.Anchor.Offset,
            SortKind: 1,
            item.Resolved);

    private static int RevisionOffset(RevisionEntry entry)
    {
        var offset = 0;
        foreach (var run in entry.Paragraph.Runs)
        {
            if (ReferenceEquals(run, entry.Run))
                return offset;

            offset += run.Text.Length;
        }

        return 0;
    }

    private static string NormalizePreview(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
