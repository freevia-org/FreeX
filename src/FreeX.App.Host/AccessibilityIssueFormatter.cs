using FreeX.Core.Commands;

namespace FreeX.App.Host;

public static class AccessibilityIssueFormatter
{
    private const int MaxShownIssues = 20;

    public static string Format(IReadOnlyList<AccessibilityIssue> issues)
    {
        var message = string.Join(Environment.NewLine,
            issues.Take(MaxShownIssues).Select(FormatIssue));
        if (issues.Count > MaxShownIssues)
            message += FormatOverflowSummary(issues.Count);
        return message;
    }

    private static string FormatIssue(AccessibilityIssue issue) =>
        $"{issue.SheetName}!{issue.Location}: {issue.Message}";

    private static string FormatOverflowSummary(int issueCount) =>
        $"{Environment.NewLine}...and {issueCount - MaxShownIssues} more.";
}
