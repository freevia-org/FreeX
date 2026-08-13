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

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for a directory whose path,
    /// combined with <paramref name="relativeParts"/>, exists. The neutral engine behind per-app
    /// <c>RepositoryFileLocator.FindDirectory</c> shims.
    /// </summary>
    public static string FindDirectoryFromBaseDirectory(params string[] relativeParts) =>
        FindExistingDirectory(new DirectoryInfo(AppContext.BaseDirectory), relativeParts)
        ?? throw new DirectoryNotFoundException($"Could not find repository directory '{Path.Combine(relativeParts)}'.");

    public static string? TryFindDirectoryFromBaseDirectory(params string[] relativeParts) =>
        FindExistingDirectory(new DirectoryInfo(AppContext.BaseDirectory), relativeParts);

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for the directory
    /// containing a required sentinel file. This keeps repository-root discovery neutral
    /// across sister-app test projects while preserving directory failure semantics.
    /// </summary>
    public static string FindDirectoryContainingFileFromBaseDirectory(string sentinelFileName) =>
        FindDirectoryContainingFile(new DirectoryInfo(AppContext.BaseDirectory), sentinelFileName)
        ?? throw new DirectoryNotFoundException(
            $"Could not locate directory containing '{sentinelFileName}' from {AppContext.BaseDirectory}.");

    public static string ResolveFromDirectoryContainingFile(
        string sentinelFileName,
        params string[] relativeParts) =>
        Path.Combine([FindDirectoryContainingFileFromBaseDirectory(sentinelFileName), .. relativeParts]);

    public static string FindContainingDirectoryFromBaseDirectory(params string[] relativeFileParts) =>
        Path.GetDirectoryName(FindFileFromBaseDirectory(relativeFileParts))
        ?? throw new DirectoryNotFoundException(
            $"Could not locate directory containing '{Path.Combine(relativeFileParts)}'.");

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for a file whose path,
    /// combined with <paramref name="relativeParts"/>, exists. The neutral engine behind per-app
    /// <c>RepositoryFileLocator.Find</c> shims.
    /// </summary>
    public static string FindFileFromBaseDirectory(params string[] relativeParts) =>
        FindExistingFile(new DirectoryInfo(AppContext.BaseDirectory), relativeParts)
        ?? throw new FileNotFoundException($"Could not find repository file '{Path.Combine(relativeParts)}'.");

    public static string? TryFindFileFromBaseDirectory(params string[] relativeParts) =>
        FindExistingFile(new DirectoryInfo(AppContext.BaseDirectory), relativeParts);

    public static string ReadAllTextFromBaseDirectory(params string[] relativeParts) =>
        File.ReadAllText(FindFileFromBaseDirectory(relativeParts));

    public static string ReadAllText(params string[] relativeParts) =>
        File.ReadAllText(Find(relativeParts));

    /// <summary>
    /// Builds a reader rooted at a project directory (e.g. <c>["src", "FreeX.App.Host"]</c>):
    /// the returned delegate reads a source file by name relative to that root, locating it up
    /// the directory tree via <see cref="ReadAllText(string[])"/>. App-neutral: a sister app
    /// supplies its own project-root parts. This is the engine behind per-app "read host source"
    /// helpers — keep those as thin shims over this factory.
    /// </summary>
    public static Func<string, string> SourceReaderRootedAt(params string[] projectRootParts) =>
        fileName => ReadAllText([.. projectRootParts, fileName]);

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
        var root = FindDirectoryContainingFile(
            new DirectoryInfo(AppContext.BaseDirectory),
            "FreeX.slnx");
        return root is not null
            ? Path.Combine([root, .. relativeParts])
            : throw new FileNotFoundException(
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

    private static string? FindDirectoryContainingFile(DirectoryInfo? directory, string sentinelFileName)
    {
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, sentinelFileName)))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
