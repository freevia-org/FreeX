using System.Runtime.CompilerServices;

namespace FreeX.Integration.Tests;

internal static class WorkspacePathLocator
{
    public static string FindDirectoryFromSourceOrCurrentDirectory(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativeParts]);
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]);
    }
}
