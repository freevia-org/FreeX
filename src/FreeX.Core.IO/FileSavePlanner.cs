using Free.Shared.IO;

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
        return CanSkipCleanSave(workbookDirty, currentFilePath, targetPath, PathComparison);
    }

    internal static bool CanSkipCleanSave(
        bool workbookDirty,
        string? currentFilePath,
        string targetPath,
        StringComparison pathComparison)
    {
        if (workbookDirty ||
            string.IsNullOrWhiteSpace(currentFilePath) ||
            string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        return FilePathPolicy.TryGetFullPath(currentFilePath, out var current) &&
               FilePathPolicy.TryGetFullPath(targetPath, out var target) &&
               string.Equals(current, target, pathComparison);
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
        if (!FilePathPolicy.TryGetExtension(savePath, out var extension))
            return false;

        var adapter = FileFormatResolver.FindSaveAdapter(adapters, extension, out _);
        if (adapter is null)
            return false;

        target = new FileSaveTarget(savePath, adapter);
        return true;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
