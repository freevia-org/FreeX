using System.IO;

namespace FreeX.App.Host.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindWithFailureMessage(message, relativeParts);

    public static string ReadAllText(params string[] relativeParts) =>
        File.ReadAllText(Find(relativeParts));

    public static string[] ReadAllLines(params string[] relativeParts) =>
        File.ReadAllLines(Find(relativeParts));
}
