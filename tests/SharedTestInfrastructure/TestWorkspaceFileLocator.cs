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

    public static string FindContainingDirectory(params string[] relativeParts) =>
        Path.GetDirectoryName(Find(relativeParts))
        ?? throw new DirectoryNotFoundException($"Could not locate workspace directory for {Path.Combine(relativeParts)}.");

    public static string ReadAllText(params string[] relativeParts) =>
        File.ReadAllText(Find(relativeParts));

    public static string ReadAllTextWithFailureMessage(string message, params string[] relativeParts) =>
        File.ReadAllText(FindWithFailureMessage(message, relativeParts));

    public static string[] ReadAllLines(params string[] relativeParts) =>
        File.ReadAllLines(Find(relativeParts));

    public static byte[] ReadAllBytes(params string[] relativeParts) =>
        File.ReadAllBytes(Find(relativeParts));

    public static string ReadAllTextFromCurrentDirectoryOrFallback(params string[] relativeParts) =>
        File.ReadAllText(FindFromCurrentDirectoryOrFallback(relativeParts));

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

    public static string FindDirectoryFromSourceOrCurrentDirectory(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory);
        var candidate = FindExistingDirectory(startDirectory, relativeParts);
        return candidate ?? Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]);
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

    public static string ReadAllTextFromWorkspaceRoot(params string[] relativeParts) =>
        File.ReadAllText(FindFromWorkspaceRoot(relativeParts));

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

    private static string? FindExistingDirectory(DirectoryInfo? directory, string[] relativeParts)
    {
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
