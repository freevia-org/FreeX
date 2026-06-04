namespace FreeX.Core.Calc.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts)
    {
        var candidate = FindFrom(new DirectoryInfo(AppContext.BaseDirectory), relativeParts);
        return candidate ?? throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }

    public static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidate = FindFrom(new DirectoryInfo(currentDirectory), relativeParts);
        return candidate ?? Path.Combine([currentDirectory, .. relativeParts]);
    }

    private static string? FindFrom(DirectoryInfo? directory, string[] relativeParts)
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
