using System.IO;

namespace FreeX.App.UI.Tests;

internal static class AppUiSourceTestSupport
{
    public static string ReadAppUiSources(params string[] fileNames) =>
        ReadAppUiSourcesWithSeparator(Environment.NewLine, fileNames);

    public static string ReadAppUiSourcesWithSeparator(string separator, params string[] fileNames) =>
        string.Join(separator, fileNames.Select(ReadAppUiSource));

    private static string ReadAppUiSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.FindWithFailureMessage(
            "Unable to locate workspace file",
            "src",
            "FreeX.App.UI",
            fileName));
}
