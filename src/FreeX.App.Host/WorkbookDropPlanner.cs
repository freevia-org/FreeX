using System.IO;
using FreeX.Core.IO;

namespace FreeX.App.Host;

public static class WorkbookDropPlanner
{
    public static string? SelectOpenableFile(IEnumerable<string> paths, IEnumerable<IFileAdapter> adapters)
    {
        foreach (var path in paths)
        {
            if (IsOpenableFile(path, adapters))
                return path;
        }

        return null;
    }

    private static bool IsOpenableFile(string? path, IEnumerable<IFileAdapter> adapters)
    {
        if (!IsFilePathCandidate(path))
            return false;

        if (!TryGetExtension(path, out var extension))
            return false;

        return FileDialogFilterBuilder.FindOpenAdapter(adapters, extension, out _) is not null;
    }

    private static bool IsFilePathCandidate(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path);

    private static bool TryGetExtension(string? path, out string extension)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            extension = "";
            return false;
        }

        try
        {
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
}
