namespace FreeX.Core.Calc.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindFromCurrentDirectoryOrFallback(relativeParts);

    public static string FindContainingDirectory(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindContainingDirectory(relativeParts);

    public static string ReadAllText(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.ReadAllText(relativeParts);

    public static string ReadAllTextFromCurrentDirectoryOrFallback(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback(relativeParts);
}
