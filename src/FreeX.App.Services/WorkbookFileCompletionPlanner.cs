using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Pure post-open/post-save file-context planning shared by WPF and Avalonia hosts.
/// Hosts execute the returned plan by rebinding UI state, marking dirty state, and registering recent files.
/// </summary>
public static class WorkbookFileCompletionPlanner
{
    public static WorkbookOpenCompletionPlan PlanOpen(
        WorkbookOpenTarget target,
        WorkbookOpenResult result,
        bool suppressRecentFiles = false,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? result.DisplayName
            : displayName;
        var sourceFileAccessIdentity = target.FileAccessIdentity ?? WorkbookFileAccessIdentity.FromLocalPath(target.Path);

        return new WorkbookOpenCompletionPlan(
            Workbook: result.Workbook,
            FeatureReport: result.FeatureReport,
            DisplayName: effectiveDisplayName,
            ActiveSheetId: ResolveActiveSheetId(result.Workbook),
            SourcePath: target.Path,
            CurrentFilePath: result.OpenedAsTemplate ? null : target.Path,
            OpenedAsTemplate: result.OpenedAsTemplate,
            SourceFileAccessIdentity: sourceFileAccessIdentity,
            RecentFileRegistration: new RecentFileRegistrationRequest(
                target.Path,
                SuppressRecentFiles: suppressRecentFiles,
                FileAccessIdentity: sourceFileAccessIdentity),
            Status: $"Opened {FileFormatResolver.NormalizeExtension(target.Extension)}.");
    }

    public static WorkbookSaveFileContext PlanSaveFileContext(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? WindowTitlePlanner.DisplayNameFromPath(path)
            : displayName;

        return new WorkbookSaveFileContext(
            Path: path,
            DisplayName: effectiveDisplayName,
            FileAccessIdentity: fileAccessIdentity,
            RecentFileRegistration: new RecentFileRegistrationRequest(
                path,
                FileAccessIdentity: fileAccessIdentity));
    }

    private static SheetId ResolveActiveSheetId(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (workbook.Sheets.Count == 0)
            throw new InvalidOperationException("Opened workbook must contain at least one sheet.");

        var activeSheetIndex =
            workbook.ActiveSheetIndex is { } savedActiveIndex &&
            savedActiveIndex >= 0 &&
            savedActiveIndex < workbook.Sheets.Count
                ? savedActiveIndex
                : 0;
        return workbook.Sheets[activeSheetIndex].Id;
    }
}

public sealed record WorkbookOpenCompletionPlan(
    Workbook Workbook,
    XlsxFeatureReport? FeatureReport,
    string DisplayName,
    SheetId ActiveSheetId,
    string SourcePath,
    string? CurrentFilePath,
    bool OpenedAsTemplate,
    WorkbookFileAccessIdentity SourceFileAccessIdentity,
    RecentFileRegistrationRequest RecentFileRegistration,
    string Status);

public sealed record WorkbookSaveFileContext(
    string Path,
    string DisplayName,
    WorkbookFileAccessIdentity? FileAccessIdentity,
    RecentFileRegistrationRequest RecentFileRegistration);
