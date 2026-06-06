using System.IO;

namespace FreeX.Core.Calc.Tests;

internal static class FormulaSourceTestSupport
{
    public static string ReadFormulaSource(string fileName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Formula", fileName));
}
