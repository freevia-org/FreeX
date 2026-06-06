namespace FreeX.Core.IO.Tests;

internal static class TestWorkspaceFiles
{
    internal static string FindWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    internal static string FindRepoFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindFromCurrentDirectoryOrFallback(relativeParts);

    internal static string ReadCoreIoSource(string fileName) =>
        File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", fileName));

    internal static string ReadCoreIoRepoSource(string fileName) =>
        File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", fileName));

    internal static string ReadCoreModelRepoSource(string fileName) =>
        File.ReadAllText(FindRepoFile("src", "FreeX.Core.Model", fileName));
}
