using System.IO;

namespace FreeX.Core.Calc.Tests;

internal static class CalcSourceTestSupport
{
    public static string ReadCalcSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Calc", fileName));

    public static string ReadCalcSourceFromCurrentDirectoryOrFallback(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.FindFromCurrentDirectoryOrFallback("src", "FreeX.Core.Calc", fileName));

    public static string ReadFormulaSourceFromCurrentDirectoryOrFallback(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.FindFromCurrentDirectoryOrFallback("src", "FreeX.Core.Formula", fileName));
}
