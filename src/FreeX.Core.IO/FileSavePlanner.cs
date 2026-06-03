namespace FreeX.Core.IO;

public sealed record FileSaveTarget(string Path, IFileAdapter Adapter);

public static class FileSavePlanner
{
    public static bool CanSkipCleanSave(
        bool workbookDirty,
        string? currentFilePath,
        FileSaveTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CanSkipCleanSave(workbookDirty, currentFilePath, target.Path);
    }

    public static bool CanSkipCleanSave(
        bool workbookDirty,
        string? currentFilePath,
        string targetPath)
    {
        if (workbookDirty ||
            string.IsNullOrWhiteSpace(currentFilePath) ||
            string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        return TryNormalizePath(currentFilePath, out var current) &&
               TryNormalizePath(targetPath, out var target) &&
               string.Equals(current, target, PathComparison);
    }

    public static bool TryResolveExistingPath(
        string? currentFilePath,
        IEnumerable<IFileAdapter> adapters,
        out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return false;

        var savePath = currentFilePath.Trim();
        if (ContainsInvalidPathCharacter(savePath))
            return false;

        if (!TryGetExtension(savePath, out var extension))
            return false;

        var adapter = FileFormatResolver.FindSaveAdapter(adapters, extension, out _);
        if (adapter is null)
            return false;

        target = new FileSaveTarget(savePath, adapter);
        return true;
    }

    private static bool ContainsInvalidPathCharacter(string path) =>
        path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;

    private static bool TryGetExtension(string path, out string extension)
    {
        try
        {
            if (path.Contains('\0', StringComparison.Ordinal) ||
                path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                extension = "";
                return false;
            }

            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrWhiteSpace(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }

    private static bool TryNormalizePath(string path, out string normalized)
    {
        normalized = "";
        try
        {
            var trimmed = path.Trim();
            if (trimmed.Contains('\0', StringComparison.Ordinal) ||
                trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            normalized = Path.GetFullPath(trimmed);
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

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
