using System.IO;

namespace FreeX.App.UI.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        Find("Could not locate workspace file.", relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        Find(message, relativeParts);

    private static string Find(string message, params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(message, Path.Combine(relativeParts));
    }
}
