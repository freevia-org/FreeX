namespace FreeX.App.Presentation.Tests;

internal static class RepositoryFileLocator
{
    public static string FindDirectory(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = parts.Aggregate(directory.FullName, Path.Combine);
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException($"Could not find repository directory '{Path.Combine(parts)}'.");
    }
}
