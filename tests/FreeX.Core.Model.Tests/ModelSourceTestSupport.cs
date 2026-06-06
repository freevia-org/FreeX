using System.IO;

namespace FreeX.Core.Model.Tests;

internal static class ModelSourceTestSupport
{
    public static string ReadCommandsSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Commands", fileName));

    public static string ReadCommandsSourceFromCurrentDirectoryOrFallback(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.FindFromCurrentDirectoryOrFallback("src", "FreeX.Core.Commands", fileName));

    public static string ReadCommandsSourcesMatching(string primaryFileName, string searchPattern)
    {
        var primaryFile = WorkspaceFileLocator.Find("src", "FreeX.Core.Commands", primaryFileName);
        var directory = Path.GetDirectoryName(primaryFile)!;
        var files = Directory.EnumerateFiles(directory, searchPattern)
            .OrderBy(static path => path, StringComparer.Ordinal);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    public static string ReadModelSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Model", fileName));
}
