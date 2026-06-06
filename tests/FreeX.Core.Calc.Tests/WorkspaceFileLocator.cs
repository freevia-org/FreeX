namespace FreeX.Core.Calc.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.Find(relativeParts);

    public static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindFromCurrentDirectoryOrFallback(relativeParts);
}
