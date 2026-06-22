using System.Globalization;
using System.IO;
using Free.Shared.AppServices;
using FreeX.App.Services;
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
    string FormulaErrorSummary,
    string FileSize,
    string LastModified,
    string SharingStatus,
    string ExportStatus,
    InfoPanelSummaryPlan Summary);

public static class BackstageInfoPlanner
{
    public static BackstageInfoPlan Build(
        Workbook workbook,
        string? currentFilePath,
        Sheet? activeSheet = null,
        CultureInfo? culture = null,
        Func<string, bool>? fileExists = null,
        bool hasSelection = false)
    {
        culture ??= CultureInfo.CurrentCulture;
        var accessibilityIssues = AccessibilityCheckerService.FindIssues(workbook);
        var formulaIssues = FormulaAuditingService.FindFormulaErrorIssues(workbook);
        var summary = InfoPanelSummaryPlanner.Create(workbook, activeSheet, culture);
        var sharingStatus = WorkbookShareReadinessPlanner.FormatStatus(
            WorkbookShareReadinessPlanner.CreatePlan(
                currentFilePath,
                WorkbookShareSurface.WindowsShare,
                fileExists));
        var exportStatus = WorkbookExportReadinessPlanner.Create(workbook, hasSelection).StatusText;
        var fileInfo = TryGetFileInfo(currentFilePath, out var currentFileInfo)
            ? currentFileInfo
            : null;
        var workbookInfoPlan = WorkbookInfoPlanner.Build(
            workbook,
            currentFilePath,
            ResolveActiveSheetIndex(workbook, activeSheet),
            fileInfo?.Length,
            fileInfo?.LastWriteTimeUtc,
            fileInfo?.LastWriteTime);
        var display = WorkbookInfoDisplayPlanner.Build(
            workbookInfoPlan,
            WorkbookInfoDisplaySurface.WindowsBackstagePane,
            CreateDisplayStrings(),
            culture);

        return new BackstageInfoPlan(
            display.WorkbookName,
            display.FilePath,
            display.SheetCount,
            display.Format,
            display.StatisticsSummary,
            FormatAccessibilitySummary(accessibilityIssues.Count),
            FormatFormulaErrorSummary(formulaIssues.Count),
            display.FileSize,
            display.LastModified,
            sharingStatus,
            exportStatus,
            summary);
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

    private static bool TryGetFileInfo(string? currentFilePath, out FileInfo fileInfo)
    {
        fileInfo = null!;
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return false;

        try
        {
            fileInfo = new FileInfo(currentFilePath);
            return fileInfo.Exists;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    private static int ResolveActiveSheetIndex(Workbook workbook, Sheet? activeSheet)
    {
        if (activeSheet is not null)
        {
            for (var i = 0; i < workbook.Sheets.Count; i++)
            {
                if (ReferenceEquals(workbook.Sheets[i], activeSheet))
                    return i;
            }
        }

        return workbook.ActiveSheetIndex ?? 0;
    }

    private static WorkbookInfoDisplayStrings CreateDisplayStrings() =>
        new(UiText.Get, (key, args) => UiText.Format(key, args));
}
