using System.IO;

namespace FreeX.Core.Model.Tests;

internal static class ModelSourceTestSupport
{
    public static string ReadCommandsSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Commands", fileName);

    public static string ReadCommandsSourceFromCurrentDirectoryOrFallback(string fileName) =>
        WorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback("src", "FreeX.Core.Commands", fileName);

    public static string ReadCommandsSourcesMatching(string primaryFileName, string searchPattern)
    {
        var directory = WorkspaceFileLocator.FindContainingDirectory("src", "FreeX.Core.Commands", primaryFileName);
        var files = Directory.EnumerateFiles(directory, searchPattern)
            .OrderBy(static path => path, StringComparer.Ordinal);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    public static string ReadModelSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Model", fileName);
}
