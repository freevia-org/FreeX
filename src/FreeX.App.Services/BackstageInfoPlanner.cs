using System.Globalization;
using Free.Shared.AppServices;
using Free.Shared.Localization;
using FreeX.App.Presentation.Backstage;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

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
        ResourceKeyTextResolver strings,
        Sheet? activeSheet = null,
        CultureInfo? culture = null,
        Func<string, bool>? fileExists = null,
        bool hasSelection = false,
        IReadOnlyCollection<CellAddress>? cyclicCells = null)
    {
        ArgumentNullException.ThrowIfNull(strings);
        culture ??= CultureInfo.CurrentCulture;
        var accessibilityIssues = AccessibilityCheckerService.FindIssues(workbook);
        var formulaIssues = FormulaAuditingService.FindFormulaErrorIssues(workbook, sheetId: null, cyclicCells);
        var summary = InfoPanelSummaryPlanner.Create(workbook, activeSheet, culture);
        var sharingStatus = WorkbookShareReadinessPlanner.FormatStatus(
            WorkbookShareReadinessPlanner.CreatePlan(
                currentFilePath,
                WorkbookShareSurface.WindowsShare,
                fileExists));
        var exportStatus = WorkbookExportReadinessPlanner.Create(workbook, hasSelection).StatusText;
        var workbookInfoPlan = WorkbookInfoFileMetadataReader.BuildPlan(
            workbook,
            currentFilePath,
            ResolveActiveSheetIndex(workbook, activeSheet));
        var display = WorkbookInfoDisplayPlanner.Build(
            workbookInfoPlan,
            WorkbookInfoDisplaySurface.WindowsBackstagePane,
            strings,
            culture);

        return new BackstageInfoPlan(
            display.WorkbookName,
            display.FilePath,
            display.SheetCount,
            display.Format,
            display.StatisticsSummary,
            FormatAccessibilitySummary(accessibilityIssues.Count, strings),
            FormatFormulaErrorSummary(formulaIssues.Count, strings),
            display.FileSize,
            display.LastModified,
            sharingStatus,
            exportStatus,
            summary);
    }

    public static FreeXBackstageInfoPaneRequest CreatePaneRequest(BackstageInfoPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FreeXBackstageInfoPaneRequest(
            plan.WorkbookName,
            plan.FilePath,
            plan.SheetCount,
            plan.Format,
            plan.FileSize,
            plan.LastModified,
            plan.SharingStatus,
            plan.ExportStatus,
            plan.Summary.WorkbookProtectionSummary,
            plan.Summary.ActiveSheetProtectionSummary,
            plan.StatisticsSummary,
            plan.AccessibilitySummary,
            plan.FormulaErrorSummary);
    }

    private static string FormatAccessibilitySummary(int issueCount, ResourceKeyTextResolver strings) =>
        FormulaIssueSummaryFormatter.Format(issueCount, "Backstage_Info_NoAccessibilityIssues", strings);

    private static string FormatFormulaErrorSummary(int issueCount, ResourceKeyTextResolver strings) =>
        FormulaIssueSummaryFormatter.Format(issueCount, "Backstage_Info_NoFormulaErrors", strings);

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

}
