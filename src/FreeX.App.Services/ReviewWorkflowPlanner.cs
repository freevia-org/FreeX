using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.Presentation.Comments;

namespace FreeX.App.Services;

public enum ReviewCommentKind
{
    Note,
    ThreadedComment
}

public sealed record ReviewCommentListItem(
    ReviewCommentKind Kind,
    CellAddress Address,
    string PreviewText);

public sealed record ReviewNavigationPlan(
    bool Success,
    CellAddress? Target,
    string? ErrorMessage)
{
    public static ReviewNavigationPlan Failed(string errorMessage) =>
        new(false, null, errorMessage);

    public static ReviewNavigationPlan NavigateTo(CellAddress target) =>
        new(true, target, null);
}

public sealed record ReviewWorkflowPlan(
    WorkbookStatistics Statistics,
    IReadOnlyList<AccessibilityIssue> AccessibilityIssues,
    IReadOnlyList<SpellingIssue> SpellingIssues,
    IReadOnlyList<ReviewCommentListItem> Notes,
    IReadOnlyList<ReviewCommentListItem> ThreadedComments);

public sealed record ReviewWorkflowDisplayModel(
    string Summary,
    IReadOnlyList<string> SpellingIssues,
    IReadOnlyList<string> AccessibilityIssues,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> ThreadedComments);

public static class ReviewWorkflowPlanner
{
    public static ReviewWorkflowPlan CreatePlan(
        Workbook workbook,
        SheetId activeSheetId,
        IReadOnlySet<string>? customDictionary = null,
        ISet<string>? ignoredWords = null,
        ISet<SpellingIssueKey>? ignoredIssues = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheet = workbook.GetSheet(activeSheetId);
        return new ReviewWorkflowPlan(
            WorkbookStatisticsService.GetStatistics(workbook),
            AccessibilityCheckerService.FindIssues(workbook),
            SpellCheckWorkflowPlanner.FilterIssues(
                SpellCheckService.FindIssues(workbook, activeSheetId, customDictionary),
                ignoredWords,
                ignoredIssues),
            CreateNoteItems(sheet),
            CreateThreadedCommentItems(sheet));
    }

    public static ReviewNavigationPlan FindNextNote(
        Sheet? sheet,
        CellAddress current,
        bool previous) =>
        FindNextCommentTarget(
            sheet is null ? [] : CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments),
            current,
            previous,
            "No notes on the active sheet.");

    public static ReviewNavigationPlan FindNextThreadedComment(
        Sheet? sheet,
        CellAddress current,
        bool previous) =>
        FindNextCommentTarget(
            sheet is null ? [] : CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments),
            current,
            previous,
            "No threaded comments on the active sheet.");

    public static CellAddress GetAccessibilityNavigationTarget(AccessibilityIssue issue) =>
        AccessibilityCheckerDialogPlanner.GetNavigationTarget(issue);

    public static ReviewWorkflowDisplayModel CreateDisplayModel(ReviewWorkflowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var statistics = plan.Statistics;
        var summary = string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Workbook comments: {statistics.CommentCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Named ranges: {statistics.NamedRangeCount}",
            "",
            $"Spelling issues: {plan.SpellingIssues.Count}",
            $"Accessibility issues: {plan.AccessibilityIssues.Count}",
            $"Notes on active sheet: {plan.Notes.Count}",
            $"Threaded comments on active sheet: {plan.ThreadedComments.Count}");

        return new(
            summary,
            CreatePreviewItems(
                plan.SpellingIssues,
                issue =>
                {
                    var suggestion = string.IsNullOrWhiteSpace(issue.Suggestion)
                        ? "no suggestion"
                        : issue.Suggestion;
                    return $"{issue.Address.ToA1()}: {issue.Word} -> {suggestion} ({FormatSpellingIssueSource(issue.Source)})";
                },
                "No spelling issues."),
            CreatePreviewItems(
                plan.AccessibilityIssues,
                issue => $"{TrimPreview(issue.SheetName)}!{TrimPreview(issue.Location)}: {TrimPreview(issue.Message)}",
                "No accessibility issues."),
            CreatePreviewItems(
                plan.Notes,
                item => $"{item.Address.ToA1()}: {TrimPreview(item.PreviewText)}",
                "No notes on the active sheet."),
            CreatePreviewItems(
                plan.ThreadedComments,
                item => $"{item.Address.ToA1()}: {TrimPreview(item.PreviewText)}",
                "No threaded comments on the active sheet."));
    }

    private static IReadOnlyList<ReviewCommentListItem> CreateNoteItems(Sheet? sheet) =>
        sheet is null
            ? []
            : CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)
                .Select(address => new ReviewCommentListItem(
                    ReviewCommentKind.Note,
                    address,
                    sheet.Comments[address]))
                .ToList();

    private static IReadOnlyList<ReviewCommentListItem> CreateThreadedCommentItems(Sheet? sheet) =>
        sheet is null
            ? []
            : CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)
                .Select(address => new ReviewCommentListItem(
                    ReviewCommentKind.ThreadedComment,
                    address,
                    FormatThreadedCommentPreview(sheet.ThreadedComments[address])))
                .ToList();

    private static ReviewNavigationPlan FindNextCommentTarget(
        IReadOnlyList<CellAddress> orderedAddresses,
        CellAddress current,
        bool previous,
        string emptyMessage)
    {
        if (orderedAddresses.Count == 0)
            return ReviewNavigationPlan.Failed(emptyMessage);

        return ReviewNavigationPlan.NavigateTo(
            CommentNavigationPlanner.FindNext(orderedAddresses, current, previous));
    }

    private static IReadOnlyList<string> CreatePreviewItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> format,
        string emptyMessage)
    {
        if (items.Count == 0)
            return [emptyMessage];

        const int previewLimit = 6;
        var preview = items.Take(previewLimit).Select(format).ToList();
        if (items.Count > preview.Count)
            preview.Add($"... and {items.Count - preview.Count} more");

        return preview;
    }

    private static string FormatSpellingIssueSource(SpellingIssueSource source) =>
        source switch
        {
            SpellingIssueSource.CellText => "cell text",
            SpellingIssueSource.Note => "note",
            SpellingIssueSource.ThreadedComment => "threaded comment",
            SpellingIssueSource.ThreadedCommentReply => "threaded reply",
            _ => "spelling"
        };

    private static string TrimPreview(string text)
    {
        var normalized = string.Join(
            " ",
            text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "(blank)";

        const int maxLength = 96;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..(maxLength - 3)] + "...";
    }

    private static string FormatThreadedCommentPreview(ThreadedComment thread)
    {
        var preview = string.IsNullOrWhiteSpace(thread.Author)
            ? thread.Text
            : $"{thread.Author}: {thread.Text}";

        if (thread.Replies.Count > 0)
            preview += $" ({thread.Replies.Count} replies)";

        return thread.IsResolved
            ? $"{preview} | Resolved"
            : preview;
    }
}
