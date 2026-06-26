using FreeX.Core.Model;
using FreeX.ToolsShared;

namespace FreeX.FormatFidelity;

/// <summary>
/// Thin façade over <see cref="FidelityValueCompare"/> so callers in this tool keep their
/// <c>FidelityCompare.*</c> call sites unchanged. All logic now lives in
/// <c>tools/FreeX.ToolsShared/FidelityValueCompare.cs</c>.
/// </summary>
internal static class FidelityCompare
{
    public static bool ValuesMatch(ScalarValue a, ScalarValue b)
        => FidelityValueCompare.ValuesMatch(a, b);

    public static bool TryNumeric(ScalarValue v, out double value)
        => FidelityValueCompare.TryNumeric(v, out value);

    public static bool NumbersMatch(double a, double b)
        => FidelityValueCompare.NumbersMatch(a, b);

    public static string DisplayString(ScalarValue v)
        => FidelityValueCompare.DisplayString(v);

    public static string ScalarStr(ScalarValue v)
        => FidelityValueCompare.ScalarStr(v);

    public static string ColToLetter(uint col)
        => FidelityValueCompare.ColToLetter(col);
}
