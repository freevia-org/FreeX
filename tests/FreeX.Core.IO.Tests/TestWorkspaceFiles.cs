using System.IO;

namespace FreeX.Core.IO.Tests;

internal static class TestWorkspaceFiles
{
    internal static string FindWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    internal static string FindRepoFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindFromCurrentDirectoryOrFallback(relativeParts);

    internal static string FindWorkspaceFileDirectory(params string[] relativeParts) =>
        Path.GetDirectoryName(FindWorkspaceFile(relativeParts))
        ?? throw new DirectoryNotFoundException($"Could not locate workspace directory for {Path.Combine(relativeParts)}.");

    internal static string ReadWorkspaceText(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    internal static string[] ReadWorkspaceLines(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllLines(relativeParts);

    internal static string ReadRepoText(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback(relativeParts);

    internal static string ReadCoreIoSource(string fileName) =>
        ReadWorkspaceText("src", "FreeX.Core.IO", fileName);

    internal static string ReadCoreIoRepoSource(string fileName) =>
        ReadRepoText("src", "FreeX.Core.IO", fileName);

    internal static string ReadCoreModelRepoSource(string fileName) =>
        ReadRepoText("src", "FreeX.Core.Model", fileName);
}
