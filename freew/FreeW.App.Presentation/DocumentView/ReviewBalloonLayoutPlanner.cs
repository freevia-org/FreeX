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
    bool Resolved = false,
    int ReplyCount = 0,
    string? DateXml = null)
{
    public string KindLabel => Kind switch
    {
        ReviewBalloonKind.Comment => Resolved ? "Resolved comment" : "Comment",
        ReviewBalloonKind.Insertion => "Inserted",
        ReviewBalloonKind.Deletion => "Deleted",
        ReviewBalloonKind.Formatting => "Formatting",
        _ => Kind.ToString()
    };

    public string HeaderText => Author;

    public string BodyText => Text;

    public string MetadataText => BuildMetadataText(Kind, Resolved, ReplyCount, DateXml);

    private static string BuildMetadataText(
        ReviewBalloonKind kind,
        bool resolved,
        int replyCount,
        string? dateXml)
    {
        var parts = new List<string>();

        if (kind == ReviewBalloonKind.Comment)
        {
            parts.Add(resolved ? "Resolved" : "Open thread");
            if (replyCount > 0)
                parts.Add($"{replyCount} repl{(replyCount == 1 ? "y" : "ies")}");
        }
        else
        {
            parts.Add("Tracked change");
        }

        if (!string.IsNullOrWhiteSpace(dateXml))
            parts.Add(FormatDateLabel(dateXml));

        return string.Join(" - ", parts);
    }

    private static string FormatDateLabel(string dateXml)
    {
        var trimmed = dateXml.Trim();
        return trimmed.Length >= 10 && trimmed[4] == '-' && trimmed[7] == '-'
            ? trimmed[..10]
            : trimmed;
    }
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

        var comments = policy.ShouldHighlightComments
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
        var leaderStartYs = new double[sources.Count];
        var desiredBalloonYs = new double[sources.Count];

        for (var i = 0; i < sources.Count; i++)
        {
            var leaderStartY = canvasHeight * (i + 0.5) / totalSlots;
            leaderStartYs[i] = leaderStartY;
            desiredBalloonYs[i] = leaderStartY - layoutOptions.BalloonHeight / 2;
        }

        var balloonYs = ResolveBalloonTops(
            desiredBalloonYs,
            canvasHeight,
            layoutOptions.BalloonHeight,
            layoutOptions.BalloonGap);
        var layouts = new List<ReviewBalloonLayout>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            var balloonY = balloonYs[i];
            var leaderStartY = leaderStartYs[i];
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

    private static double[] ResolveBalloonTops(
        IReadOnlyList<double> desiredBalloonYs,
        double viewportHeight,
        double balloonHeight,
        double balloonGap)
    {
        var count = desiredBalloonYs.Count;
        if (count == 0)
            return [];

        var minimumY = balloonGap;
        var maximumY = Math.Max(minimumY, viewportHeight - balloonHeight - balloonGap);
        var stride = balloonHeight + balloonGap;
        var resolved = new double[count];

        resolved[0] = Math.Clamp(desiredBalloonYs[0], minimumY, maximumY);
        for (var i = 1; i < count; i++)
        {
            var desired = Math.Clamp(desiredBalloonYs[i], minimumY, maximumY);
            resolved[i] = Math.Max(desired, resolved[i - 1] + stride);
        }

        var totalStackSpan = (count - 1) * stride;
        var visibleStackSpan = maximumY - minimumY;
        if (totalStackSpan <= visibleStackSpan && resolved[^1] > maximumY)
        {
            resolved[^1] = maximumY;
            for (var i = count - 2; i >= 0; i--)
                resolved[i] = Math.Min(resolved[i], resolved[i + 1] - stride);
        }

        if (resolved[0] < minimumY)
        {
            resolved[0] = minimumY;
            for (var i = 1; i < count; i++)
                resolved[i] = resolved[i - 1] + stride;
        }

        return resolved;
    }

    public static string TruncatePreview(string text, int maxLength, string suffix = "...")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(suffix);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        return text.Length <= maxLength ? text : text[..maxLength] + suffix;
    }

    private static bool ShouldShowRevision(RevisionEntry entry, ReviewDisplayPolicy policy)
    {
        if (policy.DisplayMode is not (ReviewDisplayMode.AllMarkup or ReviewDisplayMode.SimpleMarkup))
            return false;

        return entry.Kind switch
        {
            RevisionEntryKind.Formatting => policy.ShowFormatting,
            RevisionEntryKind.Insertion or RevisionEntryKind.Deletion => policy.ShowInsertionsAndDeletions,
            _ => true,
        };
    }

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
            SortKind: 0,
            DateXml: entry.DateXml);
    }

    private static ReviewBalloonSource FromComment(CommentListItem item) =>
        new(
            ReviewBalloonKind.Comment,
            string.IsNullOrWhiteSpace(item.Author) ? "Unknown" : item.Author,
            NormalizePreview(item.Text),
            item.BlockIndex,
            item.Anchor.Offset,
            SortKind: 1,
            item.Resolved,
            item.ReplyCount,
            item.DateXml);

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
