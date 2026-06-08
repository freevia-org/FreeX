using FreeX.App.Services;

namespace FreeX.App.Host;

public enum ShareWorkbookPlanKind
{
    ShareExistingFile,
    SaveAsBeforeShare
}

public enum ShareWorkbookSaveAsReason
{
    None,
    UnsavedWorkbook,
    MissingFile,
    InvalidPath
}

public sealed record ShareWorkbookPlan(
    ShareWorkbookPlanKind Kind,
    string? Path,
    ShareWorkbookSaveAsReason SaveAsReason = ShareWorkbookSaveAsReason.None,
    string? CandidatePath = null);

public static class ShareWorkbookPlanner
{
    public static ShareWorkbookPlan CreatePlan(string? currentFilePath, Func<string, bool>? fileExists = null)
    {
        var plan = WorkbookShareReadinessPlanner.CreatePlan(
            currentFilePath,
            WorkbookShareSurface.WindowsShare,
            fileExists);

        return ToHostPlan(plan);
    }

    public static string FormatStatus(ShareWorkbookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return WorkbookShareReadinessPlanner.FormatStatus(ToReadinessPlan(plan));
    }

    private static ShareWorkbookPlan ToHostPlan(WorkbookShareReadinessPlan plan) =>
        new(
            ToHostKind(plan.Kind),
            plan.Path,
            ToHostSaveAsReason(plan.SaveAsReason),
            plan.CandidatePath);

    private static WorkbookShareReadinessPlan ToReadinessPlan(ShareWorkbookPlan plan) =>
        new(
            ToReadinessKind(plan.Kind),
            plan.Path,
            ToReadinessSaveAsReason(plan.SaveAsReason),
            plan.CandidatePath,
            WorkbookShareSurface.WindowsShare);

    private static ShareWorkbookPlanKind ToHostKind(WorkbookShareReadinessPlanKind kind) =>
        kind switch
        {
            WorkbookShareReadinessPlanKind.ShareExistingFile => ShareWorkbookPlanKind.ShareExistingFile,
            WorkbookShareReadinessPlanKind.SaveAsBeforeShare => ShareWorkbookPlanKind.SaveAsBeforeShare,
            _ => ShareWorkbookPlanKind.SaveAsBeforeShare
        };

    private static WorkbookShareReadinessPlanKind ToReadinessKind(ShareWorkbookPlanKind kind) =>
        kind switch
        {
            ShareWorkbookPlanKind.ShareExistingFile => WorkbookShareReadinessPlanKind.ShareExistingFile,
            _ => WorkbookShareReadinessPlanKind.SaveAsBeforeShare
        };

    private static ShareWorkbookSaveAsReason ToHostSaveAsReason(WorkbookShareReadinessSaveAsReason reason) =>
        reason switch
        {
            WorkbookShareReadinessSaveAsReason.UnsavedWorkbook => ShareWorkbookSaveAsReason.UnsavedWorkbook,
            WorkbookShareReadinessSaveAsReason.MissingFile => ShareWorkbookSaveAsReason.MissingFile,
            WorkbookShareReadinessSaveAsReason.InvalidPath => ShareWorkbookSaveAsReason.InvalidPath,
            _ => ShareWorkbookSaveAsReason.None
        };

    private static WorkbookShareReadinessSaveAsReason ToReadinessSaveAsReason(ShareWorkbookSaveAsReason reason) =>
        reason switch
        {
            ShareWorkbookSaveAsReason.UnsavedWorkbook => WorkbookShareReadinessSaveAsReason.UnsavedWorkbook,
            ShareWorkbookSaveAsReason.MissingFile => WorkbookShareReadinessSaveAsReason.MissingFile,
            ShareWorkbookSaveAsReason.InvalidPath => WorkbookShareReadinessSaveAsReason.InvalidPath,
            _ => WorkbookShareReadinessSaveAsReason.None
        };
}
