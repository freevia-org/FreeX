namespace FreeX.App.Host.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindWithFailureMessage(message, relativeParts);

    public static string ReadAllText(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    public static string[] ReadAllLines(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllLines(relativeParts);
}
