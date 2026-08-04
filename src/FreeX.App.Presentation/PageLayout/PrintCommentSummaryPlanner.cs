using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>One printable comment entry in an at-end worksheet comment summary.</summary>
public sealed record PrintCommentSummaryEntry(CellAddress Address, string Text);

/// <summary>One at-end worksheet comment-summary page after pagination.</summary>
public sealed record PrintCommentSummaryPagePlan(
    int PageIndex,
    IReadOnlyList<PrintCommentSummaryEntry> Entries);

/// <summary>
/// UI-free planning for Excel-style "comments at end" print summaries. Renderers still own text
/// measurement, drawing, and document primitives; this planner owns comment ordering, pagination,
/// and bounded overlay line decisions.
/// </summary>
public static class PrintCommentSummaryPlanner
{
    public const double HeaderHeight = 34.0;
    public const double LineHeight = 24.0;
    public const int MaxOverlayLines = 3;
    public const string Ellipsis = "\u2026";

    /// <summary>
    /// Returns only notes/threaded comments whose row and column are printed together by at least one
    /// configured print area. This is the shared source-selection rule used by WPF and the Avalonia
    /// Entire Workbook preview for the "Comments: At end of sheet" appendix.
    /// </summary>
    public static (IReadOnlyDictionary<CellAddress, string> Comments, IReadOnlyDictionary<CellAddress, ThreadedComment> ThreadedComments)
        FilterToPrintedCells(Sheet sheet, WorksheetPrintRenderPlan printPlan)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(printPlan);

        if (sheet.Comments.Count == 0 && sheet.ThreadedComments.Count == 0)
            return (sheet.Comments, sheet.ThreadedComments);

        var areaCellSets = printPlan.AreaPlans
            .Select(area => (
                Rows: new HashSet<uint>(area.Pages.SelectMany(page => page.Rows)),
                Columns: new HashSet<uint>(area.Pages.SelectMany(page => page.Columns))))
            .ToList();

        bool IsPrinted(CellAddress address) =>
            areaCellSets.Any(area => area.Rows.Contains(address.Row) && area.Columns.Contains(address.Col));

        var comments = sheet.Comments.Count == 0
            ? sheet.Comments
            : sheet.Comments
                .Where(pair => IsPrinted(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        var threadedComments = sheet.ThreadedComments.Count == 0
            ? sheet.ThreadedComments
            : sheet.ThreadedComments
                .Where(pair => IsPrinted(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

        return (comments, threadedComments);
    }

    public static IReadOnlyList<PrintCommentSummaryPagePlan> BuildPages(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        double pageHeight,
        double marginTop)
    {
        var entries = BuildEntries(comments, threadedComments);
        if (entries.Count == 0)
            return [];

        var bodyHeight = Math.Max(
            LineHeight,
            pageHeight - marginTop * 2 - HeaderHeight);
        var entriesPerPage = Math.Max(1, (int)Math.Floor(bodyHeight / LineHeight));

        var pageCount = (entries.Count + entriesPerPage - 1) / entriesPerPage;
        var pages = new List<PrintCommentSummaryPagePlan>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var start = pageIndex * entriesPerPage;
            var count = Math.Min(entriesPerPage, entries.Count - start);
            var pageEntries = new List<PrintCommentSummaryEntry>(count);
            for (var index = 0; index < count; index++)
                pageEntries.Add(entries[start + index]);

            pages.Add(new PrintCommentSummaryPagePlan(pageIndex, pageEntries));
        }

        return pages;
    }

    public static IReadOnlyList<PrintCommentSummaryEntry> BuildEntries(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(threadedComments);

        var result = new List<PrintCommentSummaryEntry>(comments.Count + threadedComments.Count);
        foreach (var pair in comments)
        {
            var text = threadedComments.TryGetValue(pair.Key, out var thread)
                ? string.Concat(
                    "Note: ", pair.Value,
                    Environment.NewLine,
                    CommentNavigationPlanner.FormatThreadedComment(thread))
                : pair.Value;
            result.Add(new PrintCommentSummaryEntry(pair.Key, text));
        }

        foreach (var pair in threadedComments)
        {
            if (comments.ContainsKey(pair.Key))
                continue;

            result.Add(new PrintCommentSummaryEntry(
                pair.Key,
                CommentNavigationPlanner.FormatThreadedComment(pair.Value)));
        }

        result.Sort(static (left, right) =>
        {
            var rowComparison = left.Address.Row.CompareTo(right.Address.Row);
            return rowComparison != 0
                ? rowComparison
                : left.Address.Col.CompareTo(right.Address.Col);
        });
        return result;
    }

    public static IReadOnlyList<string> WrapOverlayText(
        string text,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines = MaxOverlayLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureWidth);

        if (maxLines <= 0)
            return [];

        var width = Math.Max(1, maxWidth);
        var lines = new List<string>();
        var hardLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var truncated = false;
        for (var hardLineIndex = 0; hardLineIndex < hardLines.Length && lines.Count < maxLines && !truncated; hardLineIndex++)
        {
            truncated = AddWrappedHardLine(
                lines,
                hardLines[hardLineIndex],
                width,
                measureWidth,
                maxLines);
        }

        if (lines.Count > 0 &&
            !lines[^1].EndsWith(Ellipsis, StringComparison.Ordinal) &&
            (truncated || lines.Count == maxLines && ProducesMoreLines(text, lines, width, measureWidth, maxLines)))
        {
            lines[^1] = TrimToWidth(lines[^1], width, measureWidth);
        }

        return lines;
    }

    private static bool AddWrappedHardLine(
        ICollection<string> lines,
        string hardLine,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines)
    {
        var words = hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            if (lines.Count < maxLines)
                lines.Add("");
            return false;
        }

        var index = 0;
        while (index < words.Length && lines.Count < maxLines)
        {
            var line = words[index++];
            while (index < words.Length && FitsWidth($"{line} {words[index]}", maxWidth, measureWidth))
                line = $"{line} {words[index++]}";

            if (!FitsWidth(line, maxWidth, measureWidth))
            {
                lines.Add(TrimToWidth(line, maxWidth, measureWidth));
                return true;
            }

            lines.Add(line);
        }

        return index < words.Length;
    }

    private static bool ProducesMoreLines(
        string originalText,
        IReadOnlyList<string> emittedLines,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines)
    {
        var replay = new List<string>();
        var hardLines = originalText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var hardLine in hardLines)
        {
            AddWrappedHardLine(replay, hardLine, maxWidth, measureWidth, int.MaxValue);
            if (replay.Count > maxLines)
                return true;
        }

        return replay.Count > emittedLines.Count;
    }

    private static bool FitsWidth(string text, double maxWidth, Func<string, double> measureWidth) =>
        measureWidth(text) <= maxWidth;

    private static string TrimToWidth(string text, double maxWidth, Func<string, double> measureWidth)
    {
        var candidate = text.TrimEnd();
        while (candidate.Length > 0 && !FitsWidth(candidate + Ellipsis, maxWidth, measureWidth))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? Ellipsis : candidate + Ellipsis;
    }
}
