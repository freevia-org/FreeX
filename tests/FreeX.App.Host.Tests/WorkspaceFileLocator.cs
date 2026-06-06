using System.IO;

namespace FreeX.App.Host.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindWorkspaceRoot() =>
        Path.GetDirectoryName(Find("FreeX.slnx"))!;

    public static string FindDocsDirectory() =>
        Path.GetDirectoryName(Find("docs", "README.md"))!;

    public static string FindToolScript(string fileName) =>
        Find("tools", fileName);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindWithFailureMessage(message, relativeParts);

    public static string ReadAllText(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    public static string[] ReadAllLines(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllLines(relativeParts);
}
