namespace FreeX.Core.Model.Tests;

internal static class WorkspaceFileLocator
{
    internal static string Find(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindWithFailureMessage(
            $"Could not find workspace file: {Path.Combine(relativeParts)}",
            relativeParts);

    internal static string FindFromCurrentDirectoryOrFallback(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindFromCurrentDirectoryOrFallback(relativeParts);

    internal static string FindFromWorkspaceRoot(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.FindFromWorkspaceRoot(relativeParts);
}
