using System.Globalization;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record InfoPanelSummaryPlan(
    string SheetCount,
    string CellsWithDataCount,
    string FormulaCount,
    string CommentCount,
    string ChartCount,
    string PictureCount,
    string ShapeCount,
    string NamedRangeCount,
    string WorkbookProtectionSummary,
    string ActiveSheetProtectionSummary,
    string AccessibilityIssueSummary);

public static class InfoPanelSummaryPlanner
{
    public static InfoPanelSummaryPlan Create(
        Workbook workbook,
        Sheet? activeSheet,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var statistics = WorkbookStatisticsService.GetStatistics(workbook);
        var accessibilityIssueCount = AccessibilityCheckerService.FindIssues(workbook).Count;

        return new InfoPanelSummaryPlan(
            SheetCount: FormatCount(statistics.WorksheetCount, culture),
            CellsWithDataCount: FormatCount(statistics.CellCount, culture),
            FormulaCount: FormatCount(statistics.FormulaCount, culture),
            CommentCount: FormatCount(statistics.CommentCount, culture),
            ChartCount: FormatCount(statistics.ChartCount, culture),
            PictureCount: FormatCount(statistics.PictureCount, culture),
            ShapeCount: FormatCount(statistics.ShapeCount, culture),
            NamedRangeCount: FormatCount(statistics.NamedRangeCount, culture),
            WorkbookProtectionSummary: FormatWorkbookProtectionSummary(workbook),
            ActiveSheetProtectionSummary: FormatActiveSheetProtectionSummary(activeSheet),
            AccessibilityIssueSummary: FormatAccessibilityIssueSummary(accessibilityIssueCount, culture));
    }

    public static string FormatWorkbookProtectionSummary(Workbook workbook) =>
        workbook.IsStructureProtected
            ? "Workbook structure protected."
            : "Workbook structure unprotected.";

    public static string FormatActiveSheetProtectionSummary(Sheet? activeSheet) =>
        activeSheet switch
        {
            null => "No active sheet.",
            { IsProtected: true } => "Active sheet protected.",
            _ => "Active sheet unprotected."
        };

    public static string FormatAccessibilityIssueSummary(int issueCount, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return issueCount switch
        {
            0 => "No accessibility issues found.",
            1 => "1 accessibility issue found.",
            _ => $"{FormatCount(issueCount, culture)} accessibility issues found."
        };
    }

    public static string FormatCount(int count, CultureInfo? culture = null) =>
        count.ToString("N0", culture ?? CultureInfo.CurrentCulture);
}
