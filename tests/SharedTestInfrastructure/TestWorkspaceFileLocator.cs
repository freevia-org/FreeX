using System.IO;
using System.Runtime.CompilerServices;

internal static class TestWorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        FindWithFailureMessage("Could not locate workspace file.", relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts)
    {
        foreach (var root in CandidateRoots())
        {
            var candidate = FindExistingFile(new DirectoryInfo(root), relativeParts);
            if (candidate is not null)
                return candidate;
        }

        throw new FileNotFoundException(message, Path.Combine(relativeParts));
    }

    public static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        return FindExistingFile(new DirectoryInfo(currentDirectory), relativeParts)
            ?? Path.Combine([currentDirectory, .. relativeParts]);
    }

    public static string FindFromCurrentDirectoryOrSourceFile(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var cwdCandidate = Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]);
        if (File.Exists(cwdCandidate))
            return cwdCandidate;

        var startDirectory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        return FindExistingFile(startDirectory, relativeParts)
            ?? throw new FileNotFoundException(
                "Could not locate workspace file.",
                Path.Combine(relativeParts));
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

    private static IEnumerable<string> CandidateRoots()
    {
        var envRoot = Environment.GetEnvironmentVariable("FREEX_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            yield return envRoot;

        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
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
