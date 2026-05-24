using System.IO;
using Freexcel.Core.IO;

namespace Freexcel.App.Host;

public static class WorkbookDropPlanner
{
    public static string? SelectOpenableFile(IEnumerable<string> paths, IEnumerable<IFileAdapter> adapters)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
                continue;

            var extension = Path.GetExtension(path);
            if (FileDialogFilterBuilder.FindOpenAdapter(adapters, extension, out _) is not null)
                return path;
        }

        return null;
    }
}
