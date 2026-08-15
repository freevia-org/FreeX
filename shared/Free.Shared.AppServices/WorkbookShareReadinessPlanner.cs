namespace Free.Shared.AppServices;

public enum WorkbookShareReadinessPlanKind
{
    ShareExistingFile,
    SaveAsBeforeShare,
    ShareSurfaceUnavailable
}

public enum WorkbookShareReadinessSaveAsReason
{
    None,
    UnsavedWorkbook,
    MissingFile,
    InvalidPath
}

public sealed record WorkbookShareSurface(string Label, bool CanShareLocalFiles = true)
{
    public static WorkbookShareSurface WindowsShare { get; } = new("Windows Share");

    public string Label { get; init; } = NormalizeLabel(Label);

    internal DocumentShareSurface ToDocumentSurface() => new(Label, CanShareLocalFiles);

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}

public sealed record WorkbookShareReadinessPlan(
    WorkbookShareReadinessPlanKind Kind,
    string? Path,
    WorkbookShareReadinessSaveAsReason SaveAsReason = WorkbookShareReadinessSaveAsReason.None,
    string? CandidatePath = null,
    WorkbookShareSurface? Surface = null)
{
    public WorkbookShareSurface EffectiveSurface => Surface ?? WorkbookShareSurface.WindowsShare;
}

/// <summary>Compatibility facade over the document-neutral share planner.</summary>
public static class WorkbookShareReadinessPlanner
{
    public static WorkbookShareReadinessPlan CreatePlan(
        string? currentFilePath,
        Func<string, bool>? fileExists = null) =>
        CreatePlan(currentFilePath, WorkbookShareSurface.WindowsShare, fileExists);

    public static WorkbookShareReadinessPlan CreatePlan(
        string? currentFilePath,
        WorkbookShareSurface surface,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return FromDocumentPlan(DocumentShareReadinessPlanner.CreatePlan(
            currentFilePath,
            surface.ToDocumentSurface(),
            fileExists));
    }

    public static string FormatStatus(WorkbookShareReadinessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return DocumentShareReadinessPlanner.FormatStatus(
            ToDocumentPlan(plan),
            DocumentShareReadinessTextSpec.WorkbookEnglish);
    }

    internal static bool IsUnsupportedLinkCandidate(string? candidatePath) =>
        DocumentShareReadinessPlanner.IsUnsupportedLinkCandidate(candidatePath);

    internal static DocumentShareReadinessPlan ToDocumentPlan(WorkbookShareReadinessPlan plan) => new(
        (DocumentShareReadinessPlanKind)plan.Kind,
        plan.Path,
        ToDocumentReason(plan.SaveAsReason),
        plan.CandidatePath,
        plan.EffectiveSurface.ToDocumentSurface());

    private static WorkbookShareReadinessPlan FromDocumentPlan(DocumentShareReadinessPlan plan) => new(
        (WorkbookShareReadinessPlanKind)plan.Kind,
        plan.Path,
        FromDocumentReason(plan.SaveAsReason),
        plan.CandidatePath,
        new WorkbookShareSurface(plan.EffectiveSurface.Label, plan.EffectiveSurface.CanShareLocalFiles));

    internal static DocumentShareSaveAsReason ToDocumentReason(WorkbookShareReadinessSaveAsReason reason) =>
        reason switch
        {
            WorkbookShareReadinessSaveAsReason.UnsavedWorkbook => DocumentShareSaveAsReason.UnsavedDocument,
            WorkbookShareReadinessSaveAsReason.MissingFile => DocumentShareSaveAsReason.MissingFile,
            WorkbookShareReadinessSaveAsReason.InvalidPath => DocumentShareSaveAsReason.InvalidPath,
            _ => DocumentShareSaveAsReason.None
        };

    internal static WorkbookShareReadinessSaveAsReason FromDocumentReason(DocumentShareSaveAsReason reason) =>
        reason switch
        {
            DocumentShareSaveAsReason.UnsavedDocument => WorkbookShareReadinessSaveAsReason.UnsavedWorkbook,
            DocumentShareSaveAsReason.MissingFile => WorkbookShareReadinessSaveAsReason.MissingFile,
            DocumentShareSaveAsReason.InvalidPath => WorkbookShareReadinessSaveAsReason.InvalidPath,
            _ => WorkbookShareReadinessSaveAsReason.None
        };
}
