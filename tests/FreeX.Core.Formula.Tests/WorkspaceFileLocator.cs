using System.Runtime.CompilerServices;

namespace FreeX.Core.Formula.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.Find(relativeParts);

    public static string ReadAllText(params string[] relativeParts) =>
        global::TestWorkspaceFileLocator.ReadAllText(relativeParts);

    public static string FindFromCurrentDirectoryOrSourceFile(
        string[] relativeParts,
        [CallerFilePath] string sourceFilePath = "") =>
        global::TestWorkspaceFileLocator.FindFromCurrentDirectoryOrSourceFile(relativeParts, sourceFilePath);
}
