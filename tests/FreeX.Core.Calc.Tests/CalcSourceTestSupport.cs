using System.IO;

namespace FreeX.Core.Calc.Tests;

internal static class CalcSourceTestSupport
{
    public static string ReadCalcSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Calc", fileName);

    public static string ReadCalcSourceFromCurrentDirectoryOrFallback(string fileName) =>
        WorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback("src", "FreeX.Core.Calc", fileName);

    public static string ReadFormulaSourceFromCurrentDirectoryOrFallback(string fileName) =>
        WorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback("src", "FreeX.Core.Formula", fileName);

    public static string ReadCalcSourcesMatching(string primaryFileName, string searchPattern)
    {
        var primaryFile = WorkspaceFileLocator.Find("src", "FreeX.Core.Calc", primaryFileName);
        var directory = Path.GetDirectoryName(primaryFile)!;
        var files = Directory.GetFiles(directory, searchPattern)
            .OrderBy(static file => Path.GetFileName(file), StringComparer.Ordinal);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }
}
