namespace FreeX.App.Host.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindWithFailureMessage(message, relativeParts);
}
