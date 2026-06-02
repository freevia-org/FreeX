using System.IO;

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
        return TryGetShareableWorkbookPath(
            currentFilePath,
            fileExists ?? File.Exists,
            out var shareablePath,
            out var saveAsReason,
            out var candidatePath)
            ? new ShareWorkbookPlan(ShareWorkbookPlanKind.ShareExistingFile, shareablePath)
            : new ShareWorkbookPlan(ShareWorkbookPlanKind.SaveAsBeforeShare, null, saveAsReason, candidatePath);
    }

    public static string FormatStatus(ShareWorkbookPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Kind == ShareWorkbookPlanKind.ShareExistingFile)
            return string.IsNullOrWhiteSpace(plan.Path)
                ? "Ready for Windows Share from the saved local file."
                : $"Ready for Windows Share from {plan.Path}.";

        return plan.SaveAsReason switch
        {
            ShareWorkbookSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(plan.CandidatePath) =>
                $"Save As is required before Windows Share can send the workbook because the saved path is missing: {plan.CandidatePath}.",
            ShareWorkbookSaveAsReason.InvalidPath =>
                "Save As is required before Windows Share can send the workbook because the saved path is not a valid local file path.",
            _ =>
                "Save As is required before Windows Share can send the workbook because it has not been saved yet."
        };
    }

    private static bool TryGetShareableWorkbookPath(
        string? currentFilePath,
        Func<string, bool> fileExists,
        out string shareablePath,
        out ShareWorkbookSaveAsReason saveAsReason,
        out string? candidatePath)
    {
        shareablePath = "";
        candidatePath = null;
        saveAsReason = ShareWorkbookSaveAsReason.None;

        if (string.IsNullOrWhiteSpace(currentFilePath))
        {
            saveAsReason = ShareWorkbookSaveAsReason.UnsavedWorkbook;
            return false;
        }

        var trimmedPath = currentFilePath.Trim();
        if (!TryNormalizePath(trimmedPath, out var normalizedPath))
        {
            saveAsReason = ShareWorkbookSaveAsReason.InvalidPath;
            candidatePath = trimmedPath;
            return false;
        }

        candidatePath = normalizedPath;
        if (!FileExists(fileExists, normalizedPath))
        {
            saveAsReason = ShareWorkbookSaveAsReason.MissingFile;
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
