namespace FreeX.App.Services.Tests;

// Thin shim over the shared, app-neutral base-directory walker.
internal static class RepositoryFileLocator
{
    public static string Find(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
