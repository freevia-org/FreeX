using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record BackstageInfoPlan(
    string WorkbookName,
    string FilePath,
    string SheetCount,
    string Format,
    string StatisticsSummary,
    string AccessibilitySummary,
    string FormulaErrorSummary);

public static class BackstageInfoPlanner
{
    public static BackstageInfoPlan Build(Workbook workbook, string? currentFilePath)
    {
        var statistics = WorkbookStatisticsService.GetStatistics(workbook);
        var accessibilityIssues = AccessibilityCheckerService.FindIssues(workbook);
        var formulaIssues = FormulaAuditingService.FindFormulaErrorIssues(workbook);
        var filePath = string.IsNullOrWhiteSpace(currentFilePath)
            ? UiText.Get("Backstage_Info_NotSavedYet")
            : currentFilePath;
        var format = string.IsNullOrWhiteSpace(currentFilePath)
            ? ".xlsx"
            : System.IO.Path.GetExtension(currentFilePath).ToLowerInvariant();

        return new BackstageInfoPlan(
            workbook.Name,
            filePath,
            workbook.Sheets.Count.ToString(CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(format) ? ".xlsx" : format,
            WorkbookStatisticsFormatter.Format(statistics),
            FormatAccessibilitySummary(accessibilityIssues.Count),
            FormatFormulaErrorSummary(formulaIssues.Count));
    }

    private static string FormatAccessibilitySummary(int issueCount) =>
        FormatIssueSummary(issueCount, UiText.Get("Backstage_Info_NoAccessibilityIssues"));

    private static string FormatFormulaErrorSummary(int issueCount) =>
        FormatIssueSummary(issueCount, UiText.Get("Backstage_Info_NoFormulaErrors"));

    private static string FormatIssueSummary(int issueCount, string emptySummary) =>
        issueCount == 0
            ? emptySummary
            : issueCount == 1
                ? UiText.Get("Backstage_Info_OneIssueFound")
                : UiText.Format("Backstage_Info_MultipleIssuesFound", issueCount);
}
