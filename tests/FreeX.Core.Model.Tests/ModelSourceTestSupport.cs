using System.IO;

namespace FreeX.Core.Model.Tests;

internal static class ModelSourceTestSupport
{
    public static string ReadCommandsSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Commands", fileName));

    public static string ReadModelSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Model", fileName));
}
