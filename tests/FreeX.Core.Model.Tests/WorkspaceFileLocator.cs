namespace FreeX.Core.Model.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts)
    {
        var candidate = FindExistingFile(new DirectoryInfo(AppContext.BaseDirectory), relativeParts);
        return candidate ?? throw new FileNotFoundException(
            $"Could not find workspace file: {Path.Combine(relativeParts)}");
    }

    public static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidate = FindExistingFile(new DirectoryInfo(currentDirectory), relativeParts);
        return candidate ?? Path.Combine([currentDirectory, .. relativeParts]);
    }

    public static string FindFromWorkspaceRoot(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return Path.Combine([directory.FullName, .. relativeParts]);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {Path.Combine(relativeParts)} from {AppContext.BaseDirectory}.");
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
