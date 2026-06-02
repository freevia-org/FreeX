using System.Globalization;
using System.IO;
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
        var statistics = WorkbookStatisticsService.GetStatistics(workbook);
        var accessibilityIssues = AccessibilityCheckerService.FindIssues(workbook);
        var formulaIssues = FormulaAuditingService.FindFormulaErrorIssues(workbook);
        var summary = InfoPanelSummaryPlanner.Create(workbook, activeSheet, culture);
        var sharingStatus = ShareWorkbookPlanner.FormatStatus(
            ShareWorkbookPlanner.CreatePlan(currentFilePath, fileExists));
        var exportStatus = ExportReadinessPlanner.Create(workbook, hasSelection).StatusText;
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
            FormatFormulaErrorSummary(formulaIssues.Count),
            FormatFileSize(currentFilePath, culture),
            FormatLastModified(currentFilePath, culture),
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

    private static string FormatFileSize(string? currentFilePath, CultureInfo culture)
    {
        if (!TryGetFileInfo(currentFilePath, out var fileInfo))
            return FormatMissingFileMetadata(currentFilePath);

        return FormatByteSize(fileInfo.Length, culture);
    }

    private static string FormatLastModified(string? currentFilePath, CultureInfo culture)
    {
        if (!TryGetFileInfo(currentFilePath, out var fileInfo))
            return FormatMissingFileMetadata(currentFilePath);

        return fileInfo.LastWriteTime.ToString("g", culture);
    }

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

    private static string FormatMissingFileMetadata(string? currentFilePath) =>
        string.IsNullOrWhiteSpace(currentFilePath)
            ? UiText.Get("Backstage_Info_NotSavedYet")
            : UiText.Get("Backstage_Info_FileMissing");

    private static string FormatByteSize(long bytes, CultureInfo culture)
    {
        bytes = Math.Max(0, bytes);
        if (bytes == 1)
            return UiText.Format("Backstage_Info_ByteSingularFormat", bytes.ToString("N0", culture));

        if (bytes < 1024)
            return UiText.Format("Backstage_Info_BytePluralFormat", bytes.ToString("N0", culture));

        var value = (double)bytes;
        var unitIndex = -1;
        string[] units = ["KB", "MB", "GB", "TB"];
        do
        {
            value /= 1024;
            unitIndex++;
        }
        while (value >= 1024 && unitIndex < units.Length - 1);

        var valueText = value >= 10
            ? value.ToString("N0", culture)
            : value.ToString("N1", culture);

        return UiText.Format(
            "Backstage_Info_ByteSizeWithUnitFormat",
            valueText,
            units[unitIndex],
            bytes.ToString("N0", culture));
    }
}
