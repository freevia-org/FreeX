namespace FreeX.App.Services;

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

        if (!surface.CanShareLocalFiles)
            return new WorkbookShareReadinessPlan(
                WorkbookShareReadinessPlanKind.ShareSurfaceUnavailable,
                null,
                Surface: surface);

        return TryGetShareableWorkbookPath(
            currentFilePath,
            fileExists ?? File.Exists,
            out var shareablePath,
            out var saveAsReason,
            out var candidatePath)
            ? new WorkbookShareReadinessPlan(
                WorkbookShareReadinessPlanKind.ShareExistingFile,
                shareablePath,
                Surface: surface)
            : new WorkbookShareReadinessPlan(
                WorkbookShareReadinessPlanKind.SaveAsBeforeShare,
                null,
                saveAsReason,
                candidatePath,
                surface);
    }

    public static string FormatStatus(WorkbookShareReadinessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var surfaceLabel = plan.EffectiveSurface.Label;
        if (plan.Kind == WorkbookShareReadinessPlanKind.ShareSurfaceUnavailable)
            return $"{surfaceLabel} cannot send local workbook files from this build.";

        if (plan.Kind == WorkbookShareReadinessPlanKind.ShareExistingFile)
            return string.IsNullOrWhiteSpace(plan.Path)
                ? $"Ready for {surfaceLabel} from the saved local file."
                : $"Ready for {surfaceLabel} from {plan.Path}.";

        return plan.SaveAsReason switch
        {
            WorkbookShareReadinessSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(plan.CandidatePath) =>
                $"Save As is required before {surfaceLabel} can send the workbook because the saved path is missing: {plan.CandidatePath}.",
            WorkbookShareReadinessSaveAsReason.InvalidPath =>
                $"Save As is required before {surfaceLabel} can send the workbook because the saved path is not a valid local file path.",
            _ =>
                $"Save As is required before {surfaceLabel} can send the workbook because it has not been saved yet."
        };
    }

    private static bool TryGetShareableWorkbookPath(
        string? currentFilePath,
        Func<string, bool> fileExists,
        out string shareablePath,
        out WorkbookShareReadinessSaveAsReason saveAsReason,
        out string? candidatePath)
    {
        shareablePath = "";
        candidatePath = null;
        saveAsReason = WorkbookShareReadinessSaveAsReason.None;

        if (string.IsNullOrWhiteSpace(currentFilePath))
        {
            saveAsReason = WorkbookShareReadinessSaveAsReason.UnsavedWorkbook;
            return false;
        }

        var trimmedPath = currentFilePath.Trim();
        if (!TryNormalizePath(trimmedPath, out var normalizedPath))
        {
            saveAsReason = WorkbookShareReadinessSaveAsReason.InvalidPath;
            candidatePath = trimmedPath;
            return false;
        }

        candidatePath = normalizedPath;
        if (!FileExists(fileExists, normalizedPath))
        {
            saveAsReason = WorkbookShareReadinessSaveAsReason.MissingFile;
            return false;
        }

        shareablePath = normalizedPath;
        return true;
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        normalizedPath = "";
        try
        {
            normalizedPath = System.IO.Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(normalizedPath);
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

    private static bool FileExists(Func<string, bool> fileExists, string path)
    {
        try
        {
            return fileExists(path);
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
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
