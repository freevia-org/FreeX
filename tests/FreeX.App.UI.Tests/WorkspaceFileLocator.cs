namespace FreeX.App.UI.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindWithFailureMessage(message, relativeParts);

    public static string ReadAllTextWithFailureMessage(string message, params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllTextWithFailureMessage(message, relativeParts);
}
