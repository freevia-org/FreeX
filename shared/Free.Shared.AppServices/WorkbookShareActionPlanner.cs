namespace Free.Shared.AppServices;

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

    internal DocumentShareActionSurface ToDocumentSurface() => new(
        ShareSheetLabel,
        CanShowShareSheet,
        CanOpenContainingFolder,
        OpenContainingFolderLabel);

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

/// <summary>Compatibility facade over the document-neutral share action planner.</summary>
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
        return FromDocumentPlan(DocumentShareActionPlanner.CreatePlan(
            currentFilePath,
            surface.ToDocumentSurface(),
            fileExists));
    }

    public static string FormatStatus(WorkbookShareActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return DocumentShareActionPlanner.FormatStatus(
            ToDocumentPlan(plan),
            DocumentShareActionTextSpec.WorkbookEnglish);
    }

    private static DocumentShareActionPlan ToDocumentPlan(WorkbookShareActionPlan plan) => new(
        (DocumentShareActionPlanKind)plan.Kind,
        plan.Path,
        plan.ContainingFolderPath,
        WorkbookShareReadinessPlanner.ToDocumentReason(plan.SaveAsReason),
        plan.CandidatePath,
        (DocumentShareActionUnavailableReason)plan.UnavailableReason,
        plan.EffectiveSurface.ToDocumentSurface());

    private static WorkbookShareActionPlan FromDocumentPlan(DocumentShareActionPlan plan) => new(
        (WorkbookShareActionPlanKind)plan.Kind,
        plan.Path,
        plan.ContainingFolderPath,
        WorkbookShareReadinessPlanner.FromDocumentReason(plan.SaveAsReason),
        plan.CandidatePath,
        (WorkbookShareActionUnavailableReason)plan.UnavailableReason,
        new WorkbookShareActionSurface(
            plan.EffectiveSurface.ShareSheetLabel,
            plan.EffectiveSurface.CanShowShareSheet,
            plan.EffectiveSurface.CanOpenContainingFolder,
            plan.EffectiveSurface.OpenContainingFolderLabel));
}
