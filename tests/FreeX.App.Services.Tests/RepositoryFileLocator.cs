namespace FreeX.App.Services.Tests;

internal static class RepositoryFileLocator
{
    public static string Find(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = parts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(parts)}'.");
    }
}
