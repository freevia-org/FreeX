using System.Runtime.CompilerServices;

namespace FreeX.Core.Formula.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts)
    {
        var candidate = FindExistingFile(new DirectoryInfo(AppContext.BaseDirectory), relativeParts);
        return candidate ?? throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }

    public static string FindFromCurrentDirectoryOrSourceFile(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var cwdCandidate = Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]);
        if (File.Exists(cwdCandidate))
            return cwdCandidate;

        var startDirectory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        var candidate = FindExistingFile(startDirectory, relativeParts);
        return candidate ?? throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }

    private static string? FindExistingFile(DirectoryInfo? directory, string[] relativeParts)
    {
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
