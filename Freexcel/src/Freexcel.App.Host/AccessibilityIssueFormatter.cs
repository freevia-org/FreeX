using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class AccessibilityIssueFormatter
{
    private const int MaxShownIssues = 20;

    public static string Format(IReadOnlyList<AccessibilityIssue> issues)
    {
        var message = string.Join(Environment.NewLine,
            issues.Take(MaxShownIssues).Select(issue => $"{issue.SheetName}!{issue.Location}: {issue.Message}"));
        if (issues.Count > MaxShownIssues)
            message += $"{Environment.NewLine}...and {issues.Count - MaxShownIssues} more.";
        return message;
    }
}
