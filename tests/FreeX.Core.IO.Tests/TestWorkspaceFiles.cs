namespace FreeX.Core.IO.Tests;

internal static class TestWorkspaceFiles
{
    public static string FindWorkspaceFile(params string[] relativeParts) =>
        FindFileUpward(AppContext.BaseDirectory, relativeParts)
        ?? throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));

    public static string FindRepoFile(params string[] relativeParts)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        return FindFileUpward(currentDirectory, relativeParts)
            ?? Path.Combine([currentDirectory, .. relativeParts]);
    }

    private static string? FindFileUpward(string root, string[] relativeParts)
    {
        var directory = new DirectoryInfo(root);
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
