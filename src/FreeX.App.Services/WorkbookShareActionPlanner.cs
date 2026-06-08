namespace FreeX.App.Services;

public enum WorkbookShareActionPlanKind
{
    ShareSheet,
    OpenContainingFolder,
    SaveAsBeforeShare,
    Deferred
}

public enum WorkbookShareActionUnavailableReason
{
    None,
    ShareSheetUnavailable,
    ContainingFolderUnavailable
}

public sealed record WorkbookShareActionSurface(
    string ShareSheetLabel,
    bool CanShowShareSheet,
    bool CanOpenContainingFolder = false,
    string OpenContainingFolderLabel = "Open Containing Folder")
{
    public static WorkbookShareActionSurface MacOsPreview { get; } =
        new("macOS Share Sheet", CanShowShareSheet: false);

    public string ShareSheetLabel { get; init; } = NormalizeLabel(ShareSheetLabel);

    public string OpenContainingFolderLabel { get; init; } = NormalizeLabel(OpenContainingFolderLabel);

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}

public sealed record WorkbookShareActionPlan(
    WorkbookShareActionPlanKind Kind,
    string? Path,
    string? ContainingFolderPath = null,
    WorkbookShareReadinessSaveAsReason SaveAsReason = WorkbookShareReadinessSaveAsReason.None,
    string? CandidatePath = null,
    WorkbookShareActionUnavailableReason UnavailableReason = WorkbookShareActionUnavailableReason.None,
    WorkbookShareActionSurface? Surface = null)
{
    public WorkbookShareActionSurface EffectiveSurface => Surface ?? WorkbookShareActionSurface.MacOsPreview;
}

public static class WorkbookShareActionPlanner
{
    public static WorkbookShareActionPlan CreatePlan(
        string? currentFilePath,
        Func<string, bool>? fileExists = null) =>
        CreatePlan(currentFilePath, WorkbookShareActionSurface.MacOsPreview, fileExists);

    public static WorkbookShareActionPlan CreatePlan(
        string? currentFilePath,
        WorkbookShareActionSurface surface,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var readiness = WorkbookShareReadinessPlanner.CreatePlan(
            currentFilePath,
            new WorkbookShareSurface(surface.ShareSheetLabel),
            fileExists);
        var hasNativeAction = surface.CanShowShareSheet || surface.CanOpenContainingFolder;

        if (readiness.Kind != WorkbookShareReadinessPlanKind.ShareExistingFile)
            return new WorkbookShareActionPlan(
                hasNativeAction ? WorkbookShareActionPlanKind.SaveAsBeforeShare : WorkbookShareActionPlanKind.Deferred,
                null,
                SaveAsReason: readiness.SaveAsReason,
                CandidatePath: readiness.CandidatePath,
                UnavailableReason: hasNativeAction ? WorkbookShareActionUnavailableReason.None : WorkbookShareActionUnavailableReason.ShareSheetUnavailable,
                Surface: surface);

        if (surface.CanShowShareSheet)
            return new WorkbookShareActionPlan(
                WorkbookShareActionPlanKind.ShareSheet,
                readiness.Path,
                Surface: surface);

        if (surface.CanOpenContainingFolder)
        {
            if (TryGetContainingFolderPath(readiness.Path, out var containingFolderPath))
                return new WorkbookShareActionPlan(
                    WorkbookShareActionPlanKind.OpenContainingFolder,
                    readiness.Path,
                    containingFolderPath,
                    UnavailableReason: WorkbookShareActionUnavailableReason.ShareSheetUnavailable,
                    Surface: surface);

            return new WorkbookShareActionPlan(
                WorkbookShareActionPlanKind.Deferred,
                readiness.Path,
                UnavailableReason: WorkbookShareActionUnavailableReason.ContainingFolderUnavailable,
                Surface: surface);
        }

        return new WorkbookShareActionPlan(
            WorkbookShareActionPlanKind.Deferred,
            readiness.Path,
            UnavailableReason: WorkbookShareActionUnavailableReason.ShareSheetUnavailable,
            Surface: surface);
    }

    public static string FormatStatus(WorkbookShareActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var surface = plan.EffectiveSurface;
        return plan.Kind switch
        {
            WorkbookShareActionPlanKind.ShareSheet =>
                string.IsNullOrWhiteSpace(plan.Path)
                    ? $"Ready for {surface.ShareSheetLabel} from the saved local file."
                    : $"Ready for {surface.ShareSheetLabel} from {plan.Path}.",
            WorkbookShareActionPlanKind.OpenContainingFolder =>
                string.IsNullOrWhiteSpace(plan.Path)
                    ? $"{surface.ShareSheetLabel} is unavailable in this build; use {surface.OpenContainingFolderLabel} for the saved local file."
                    : $"{surface.ShareSheetLabel} is unavailable in this build; use {surface.OpenContainingFolderLabel} for {plan.Path}.",
            WorkbookShareActionPlanKind.SaveAsBeforeShare =>
                FormatSaveAsStatus(plan, surface),
            _ =>
                FormatDeferredStatus(plan, surface)
        };
    }

    private static string FormatSaveAsStatus(WorkbookShareActionPlan plan, WorkbookShareActionSurface surface)
    {
        var actionLabel = surface.CanShowShareSheet
            ? surface.ShareSheetLabel
            : surface.OpenContainingFolderLabel;

        return plan.SaveAsReason switch
        {
            WorkbookShareReadinessSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(plan.CandidatePath) =>
                $"Save As is required before {actionLabel} can use the workbook because the saved path is missing: {plan.CandidatePath}.",
            WorkbookShareReadinessSaveAsReason.InvalidPath when WorkbookShareReadinessPlanner.IsUnsupportedLinkCandidate(plan.CandidatePath) =>
                $"Save As is required before {actionLabel} can use the workbook because cloud or web links are not supported; save the workbook to a local file first.",
            WorkbookShareReadinessSaveAsReason.InvalidPath =>
                $"Save As is required before {actionLabel} can use the workbook because the saved path is not a valid local file path.",
            _ =>
                $"Save As is required before {actionLabel} can use the workbook because it has not been saved yet."
        };
    }

    private static string FormatDeferredStatus(
        WorkbookShareActionPlan plan,
        WorkbookShareActionSurface surface) =>
        plan.UnavailableReason switch
        {
            WorkbookShareActionUnavailableReason.ContainingFolderUnavailable =>
                $"{surface.OpenContainingFolderLabel} is unavailable for the saved workbook path.",
            _ =>
                $"{surface.ShareSheetLabel} is unavailable in this build and no open-containing-folder adapter is available."
        };

    private static bool TryGetContainingFolderPath(string? filePath, out string containingFolderPath)
    {
        containingFolderPath = "";
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            containingFolderPath = directory;
            return true;
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
}
