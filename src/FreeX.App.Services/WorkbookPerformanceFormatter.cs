using FreeX.Core.Commands;

namespace FreeX.App.Services;

public static class WorkbookPerformanceFormatter
{
    public static string Format(WorkbookPerformanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasIssues)
            return "No formatting-only cells extend the used range. No local performance issues were found.";

        var lines = new List<string>
        {
            $"Found {report.Issues.Count} worksheet{(report.Issues.Count == 1 ? string.Empty : "s")} with formatting outside workbook content.",
            string.Empty,
        };

        foreach (var issue in report.Issues)
        {
            var content = issue.ContentRange is { } range ? range.ToString() : "no cell content";
            lines.Add($"{issue.SheetName}: {issue.FormattingOnlyCellCount:N0} formatting-only cells.");
            lines.Add($"  Content: {content}");
            lines.Add($"  Used range: {issue.FormattingRange}");
            lines.Add("  To reduce workbook size, select unneeded formatted cells and use Home > Clear > Clear Formats.");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}
