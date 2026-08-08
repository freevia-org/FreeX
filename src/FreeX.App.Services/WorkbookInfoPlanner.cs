using System.IO;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Protection posture of a workbook, surfaced by the backstage Info pane. Framework-neutral so the
/// Avalonia/macOS shell renders it identically to any future host.
/// </summary>
public enum WorkbookProtectionPosture
{
    /// <summary>Neither workbook structure nor any sheet is protected.</summary>
    None,

    /// <summary>One or more worksheets are protected, but the workbook structure is not.</summary>
    SheetsProtected,

    /// <summary>The workbook structure is protected, but no worksheets are.</summary>
    StructureProtected,

    /// <summary>Both the workbook structure and one or more worksheets are protected.</summary>
    StructureAndSheetsProtected
}

/// <summary>
/// Framework-neutral, pre-shaped (but NOT localized) summary of the current workbook/file for the
/// backstage Info pane. Numbers and dates are raw so the rendering shell formats them with the active
/// culture and routes labels through its own localization catalog.
/// </summary>
public sealed record WorkbookInfoPlan(
    string WorkbookName,
    string? FilePath,
    bool IsSaved,
    bool FileExistsOnDisk,
    long? FileSizeBytes,
    System.DateTime? LastModifiedUtc,
    System.DateTime? LastModifiedLocal,
    string FormatExtension,
    bool HasUnsavedChanges,
    int SheetCount,
    int ProtectedSheetCount,
    bool IsStructureProtected,
    WorkbookProtectionPosture ProtectionPosture,
    int ActiveSheetIndex,
    bool ActiveSheetIsProtected,
    WorkbookStatistics Statistics,
    int FormulaIssueCount);

/// <summary>
/// Builds a <see cref="WorkbookInfoPlan"/> from the live workbook plus optional on-disk file metadata.
/// Pure data shaping: no UI, no localization, no platform APIs — so macOS inherits the same logic and the
/// rendering layer (Avalonia today) only has to lay out and localize the result.
/// </summary>
public static class WorkbookInfoPlanner
{
    public static WorkbookInfoPlan Build(
        Workbook workbook,
        string? currentFilePath,
        int activeSheetIndex,
        long? fileSizeBytes = null,
        System.DateTime? lastModifiedUtc = null,
        System.DateTime? lastModifiedLocal = null,
        bool hasUnsavedChanges = false,
        IReadOnlyCollection<CellAddress>? cyclicCells = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var statistics = WorkbookStatisticsService.GetStatistics(workbook);
        // R129-model-avalonia-info-formula-issues-1: same "Formulas with circular references" (and
        // other formula-error) surfacing the WPF host's BackstageInfoPlanner already does via
        // FormulaAuditingService.FindFormulaErrorIssues -- sheetId: null scans the whole workbook,
        // matching BackstageInfoPlanner's call. cyclicCells defaults to none so existing callers
        // (which don't yet have a RecalcEngine/session handy) keep compiling unchanged and simply
        // report zero circular references, same as passing no cyclicCells to FindFormulaErrorIssues
        // does today.
        var formulaIssueCount = FormulaAuditingService.FindFormulaErrorIssues(workbook, sheetId: null, cyclicCells).Count;
        var isSaved = !string.IsNullOrWhiteSpace(currentFilePath);
        var fileExists = isSaved && fileSizeBytes.HasValue;

        var protectedSheetCount = CountProtectedSheets(workbook);
        var isStructureProtected = workbook.IsStructureProtected;
        var posture = ResolvePosture(protectedSheetCount > 0, isStructureProtected);

        var clampedActiveIndex = activeSheetIndex < 0 || activeSheetIndex >= workbook.Sheets.Count
            ? (workbook.ActiveSheetIndex ?? 0)
            : activeSheetIndex;
        var activeSheetProtected = clampedActiveIndex >= 0
            && clampedActiveIndex < workbook.Sheets.Count
            && workbook.Sheets[clampedActiveIndex].IsProtected;

        return new WorkbookInfoPlan(
            WorkbookName: workbook.Name,
            FilePath: isSaved ? currentFilePath : null,
            IsSaved: isSaved,
            FileExistsOnDisk: fileExists,
            FileSizeBytes: fileExists ? fileSizeBytes : null,
            LastModifiedUtc: fileExists ? lastModifiedUtc : null,
            LastModifiedLocal: fileExists ? lastModifiedLocal : null,
            FormatExtension: ResolveFormatExtension(currentFilePath),
            HasUnsavedChanges: hasUnsavedChanges,
            SheetCount: workbook.Sheets.Count,
            ProtectedSheetCount: protectedSheetCount,
            IsStructureProtected: isStructureProtected,
            ProtectionPosture: posture,
            ActiveSheetIndex: clampedActiveIndex,
            ActiveSheetIsProtected: activeSheetProtected,
            Statistics: statistics,
            FormulaIssueCount: formulaIssueCount);
    }

    private static int CountProtectedSheets(Workbook workbook)
    {
        var count = 0;
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsProtected)
                count++;
        }

        return count;
    }

    private static WorkbookProtectionPosture ResolvePosture(bool anySheetProtected, bool structureProtected) =>
        (structureProtected, anySheetProtected) switch
        {
            (true, true) => WorkbookProtectionPosture.StructureAndSheetsProtected,
            (true, false) => WorkbookProtectionPosture.StructureProtected,
            (false, true) => WorkbookProtectionPosture.SheetsProtected,
            _ => WorkbookProtectionPosture.None
        };

    private static string ResolveFormatExtension(string? currentFilePath)
    {
        if (string.IsNullOrWhiteSpace(currentFilePath) || currentFilePath.IndexOf('\0') >= 0)
            return ".xlsx";

        try
        {
            var extension = Path.GetExtension(currentFilePath);
            return string.IsNullOrWhiteSpace(extension) ? ".xlsx" : extension.ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return ".xlsx";
        }
        catch (NotSupportedException)
        {
            return ".xlsx";
        }
        catch (PathTooLongException)
        {
            return ".xlsx";
        }
    }
}
