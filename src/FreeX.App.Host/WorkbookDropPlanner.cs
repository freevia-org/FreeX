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

        var extension = Path.GetExtension(path);
        return FileDialogFilterBuilder.FindOpenAdapter(adapters, extension, out _) is not null;
    }

    private static bool IsFilePathCandidate(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path);
}
