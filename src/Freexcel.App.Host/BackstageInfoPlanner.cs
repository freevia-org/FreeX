using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record BackstageInfoPlan(
    string WorkbookName,
    string FilePath,
    string SheetCount,
    string Format,
    string StatisticsSummary,
    string AccessibilitySummary);

public static class BackstageInfoPlanner
{
    public static BackstageInfoPlan Build(Workbook workbook, string? currentFilePath)
    {
        var statistics = WorkbookStatisticsService.GetStatistics(workbook);
        var accessibilityIssues = AccessibilityCheckerService.FindIssues(workbook);
        var filePath = string.IsNullOrWhiteSpace(currentFilePath)
            ? "Not saved yet"
            : currentFilePath;
        var format = string.IsNullOrWhiteSpace(currentFilePath)
            ? ".xlsx"
            : System.IO.Path.GetExtension(currentFilePath).ToLowerInvariant();

        return new BackstageInfoPlan(
            workbook.Name,
            filePath,
            workbook.Sheets.Count.ToString(),
            string.IsNullOrWhiteSpace(format) ? ".xlsx" : format,
            WorkbookStatisticsFormatter.Format(statistics),
            FormatAccessibilitySummary(accessibilityIssues.Count));
    }

    private static string FormatAccessibilitySummary(int issueCount) =>
        issueCount == 0
            ? "No accessibility issues found"
            : issueCount == 1
                ? "1 issue found"
                : $"{issueCount} issues found";
}
