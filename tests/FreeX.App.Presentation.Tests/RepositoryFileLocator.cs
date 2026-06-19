namespace FreeX.App.Presentation.Tests;

// Thin shim over the shared, app-neutral base-directory walker.
internal static class RepositoryFileLocator
{
    public static string FindDirectory(params string[] parts) =>
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory(parts);
}
