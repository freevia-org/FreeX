namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core text functions are split into focused TextCore partial files.

    private static System.Text.RegularExpressions.Regex GetSearchRegex(string findText) =>
        FormulaWildcardHelper.GetOrCreateRegex(findText, ignoreCase: true, anchored: false);
}
