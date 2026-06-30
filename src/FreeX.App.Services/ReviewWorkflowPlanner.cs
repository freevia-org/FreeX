using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.Accessibility;

namespace FreeX.App.Services;

public enum ReviewCommentKind
{
    Note,
    ThreadedComment
}

public readonly record struct ReviewSpellingIssueKey(
    CellAddress Address,
    string Word,
    SpellingIssueSource Source,
    int ReplyIndex,
    int StartIndex);

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

public static class ReviewWorkflowPlanner
{
    public static ReviewWorkflowPlan CreatePlan(
        Workbook workbook,
        SheetId activeSheetId,
        IReadOnlySet<string>? customDictionary = null,
        ISet<string>? ignoredWords = null,
        ISet<ReviewSpellingIssueKey>? ignoredIssues = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheet = workbook.GetSheet(activeSheetId);
        return new ReviewWorkflowPlan(
            WorkbookStatisticsService.GetStatistics(workbook),
            AccessibilityCheckerService.FindIssues(workbook),
            FilterSpellingIssues(
                SpellCheckService.FindIssues(workbook, activeSheetId, customDictionary),
                ignoredWords,
                ignoredIssues),
            CreateNoteItems(sheet),
            CreateThreadedCommentItems(sheet));
    }

    public static IReadOnlyList<SpellingIssue> FilterSpellingIssues(
        IEnumerable<SpellingIssue> issues,
        ISet<string>? ignoredWords,
        ISet<ReviewSpellingIssueKey>? ignoredIssues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var filtered = new List<SpellingIssue>();
        foreach (var issue in issues)
        {
            if (ContainsIgnoredWord(ignoredWords, issue.Word) ||
                ignoredIssues?.Contains(CreateSpellingIssueKey(issue)) == true)
            {
                continue;
            }

            filtered.Add(issue);
        }

        return filtered;
    }

    public static ReviewSpellingIssueKey CreateSpellingIssueKey(SpellingIssue issue) =>
        new(issue.Address, issue.Word, issue.Source, issue.ReplyIndex, issue.StartIndex);

    public static IWorkbookCommand BuildSpellingReplacementCommand(SpellingIssue issue, string replacement) =>
        BuildCommandForIssueText(issue, SpellCheckService.ApplyCorrection(issue, replacement));

    public static IWorkbookCommand? BuildSpellingReplaceAllCommand(
        IReadOnlyList<SpellingIssue> issues,
        string word,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var commands = new List<IWorkbookCommand>();
        var cellEditsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();
        var editedTargets = new HashSet<SpellingIssueTargetKey>();

        foreach (var issue in issues)
        {
            if (!string.Equals(issue.Word, word, StringComparison.OrdinalIgnoreCase) ||
                !editedTargets.Add(CreateTargetKey(issue)))
            {
                continue;
            }

            var correctedText = SpellCheckService.ApplyCorrectionToAllOccurrences(issue, replacement);
            if (issue.Source == SpellingIssueSource.CellText)
            {
                if (!cellEditsBySheet.TryGetValue(issue.Address.Sheet, out var edits))
                {
                    edits = [];
                    cellEditsBySheet[issue.Address.Sheet] = edits;
                }

                edits.Add((issue.Address, Cell.FromValue(new TextValue(correctedText))));
                continue;
            }

            commands.Add(BuildCommandForIssueText(issue, correctedText));
        }

        foreach (var (sheetId, edits) in cellEditsBySheet)
            commands.Add(new EditCellsCommand(sheetId, edits));

        return commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompositeWorkbookCommand("Spell Check", commands)
        };
    }

    public static ReviewNavigationPlan FindNextNote(
        Sheet? sheet,
        CellAddress current,
        bool previous) =>
        FindNextCommentTarget(
            sheet is null ? [] : OrderAddresses(sheet.Comments.Keys),
            current,
            previous,
            "No notes on the active sheet.");

    public static ReviewNavigationPlan FindNextThreadedComment(
        Sheet? sheet,
        CellAddress current,
        bool previous) =>
        FindNextCommentTarget(
            sheet is null ? [] : OrderAddresses(sheet.ThreadedComments.Keys),
            current,
            previous,
            "No threaded comments on the active sheet.");

    public static CellAddress GetAccessibilityNavigationTarget(AccessibilityIssue issue) =>
        AccessibilityCheckerDialogPlanner.GetNavigationTarget(issue);

    private static IReadOnlyList<ReviewCommentListItem> CreateNoteItems(Sheet? sheet) =>
        sheet is null
            ? []
            : OrderAddresses(sheet.Comments.Keys)
                .Select(address => new ReviewCommentListItem(
                    ReviewCommentKind.Note,
                    address,
                    sheet.Comments[address]))
                .ToList();

    private static IReadOnlyList<ReviewCommentListItem> CreateThreadedCommentItems(Sheet? sheet) =>
        sheet is null
            ? []
            : OrderAddresses(sheet.ThreadedComments.Keys)
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

        var index = previous
            ? FindFirstNotBefore(orderedAddresses, current) - 1
            : FindFirstAfter(orderedAddresses, current);
        if (index < 0)
            index = orderedAddresses.Count - 1;
        else if (index >= orderedAddresses.Count)
            index = 0;

        return ReviewNavigationPlan.NavigateTo(orderedAddresses[index]);
    }

    private static bool ContainsIgnoredWord(IEnumerable<string>? ignoredWords, string word)
    {
        if (ignoredWords is null)
            return false;

        foreach (var ignoredWord in ignoredWords)
        {
            if (string.Equals(ignoredWord, word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IWorkbookCommand BuildCommandForIssueText(SpellingIssue issue, string correctedText) =>
        issue.Source switch
        {
            SpellingIssueSource.CellText => new EditCellsCommand(
                issue.Address.Sheet,
                [(issue.Address, Cell.FromValue(new TextValue(correctedText)))]),
            SpellingIssueSource.Note => new SetCommentCommand(
                issue.Address.Sheet,
                issue.Address,
                correctedText),
            SpellingIssueSource.ThreadedComment => new UpdateThreadedCommentTextCommand(
                issue.Address.Sheet,
                issue.Address,
                correctedText),
            SpellingIssueSource.ThreadedCommentReply => new UpdateThreadedCommentReplyCommand(
                issue.Address.Sheet,
                issue.Address,
                issue.ReplyIndex,
                correctedText),
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue.Source, "Unknown spelling issue source.")
        };

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

    private static List<CellAddress> OrderAddresses(IEnumerable<CellAddress> addresses) =>
        addresses
            .OrderBy(address => address.Row)
            .ThenBy(address => address.Col)
            .ToList();

    private static int FindFirstAfter(IReadOnlyList<CellAddress> orderedAddresses, CellAddress current)
    {
        var low = 0;
        var high = orderedAddresses.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (ComparePosition(orderedAddresses[mid], current) <= 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static int FindFirstNotBefore(IReadOnlyList<CellAddress> orderedAddresses, CellAddress current)
    {
        var low = 0;
        var high = orderedAddresses.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (ComparePosition(orderedAddresses[mid], current) < 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static int ComparePosition(CellAddress left, CellAddress right)
    {
        var rowComparison = left.Row.CompareTo(right.Row);
        return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
    }

    private static SpellingIssueTargetKey CreateTargetKey(SpellingIssue issue) =>
        new(issue.Address, issue.Source, issue.ReplyIndex);

    private readonly record struct SpellingIssueTargetKey(
        CellAddress Address,
        SpellingIssueSource Source,
        int ReplyIndex);
}
