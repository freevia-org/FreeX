using System.Runtime.CompilerServices;

namespace FreeX.Integration.Tests;

internal static class WorkspacePathLocator
{
    public static string FindDirectoryFromSourceOrCurrentDirectory(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "")
        => TestWorkspaceFileLocator.FindDirectoryFromSourceOrCurrentDirectory(relativeParts, sourceFilePath);
}
