internal static class FormulaSourceTestSupport
{
    public static string ReadFormulaSource(string fileName) =>
        TestWorkspaceFileLocator.ReadAllText("src", "FreeX.Core.Formula", fileName);
}
