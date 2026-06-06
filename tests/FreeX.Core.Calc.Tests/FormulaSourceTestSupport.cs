namespace FreeX.Core.Calc.Tests;

internal static class FormulaSourceTestSupport
{
    public static string ReadFormulaSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Formula", fileName);
}
