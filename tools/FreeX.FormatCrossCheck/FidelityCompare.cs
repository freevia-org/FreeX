using System;
using System.Globalization;
using FreeX.Core.Model;
using FreeX.ToolsShared;

namespace FreeX.FormatCrossCheck;

/// <summary>
/// Value-equivalence + address helpers for the cross-check tool. Numeric primitives and address
/// formatting are delegated verbatim to <see cref="FidelityValueCompare"/>. <see cref="ValuesMatch"/>
/// keeps a local override that also normalizes line-endings — an intentional cross-check-specific
/// divergence (an external tool may re-emit embedded newlines as \n vs \r\n; that is not a value
/// loss). <see cref="DisplayMatch"/> is also cross-check-only (used for lossy interchange formats).
/// </summary>
internal static class FidelityCompare
{
    public static bool ValuesMatch(ScalarValue a, ScalarValue b)
    {
        if (FidelityValueCompare.TryNumeric(a, out var an) && FidelityValueCompare.TryNumeric(b, out var bn))
            return FidelityValueCompare.NumbersMatch(an, bn);

        return (a, b) switch
        {
            (BlankValue, BlankValue) => true,
            // Normalize line endings: an external tool may re-emit embedded newlines as \n vs \r\n; the
            // text content is unchanged, so that is not a value loss.
            (TextValue ta, TextValue tb) => string.Equals(NormalizeNewlines(ta.Value), NormalizeNewlines(tb.Value), StringComparison.Ordinal),
            (ErrorValue, ErrorValue) => true,
            _ => false
        };
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    public static bool TryNumeric(ScalarValue v, out double value)
        => FidelityValueCompare.TryNumeric(v, out value);

    public static bool NumbersMatch(double a, double b)
        => FidelityValueCompare.NumbersMatch(a, b);

    /// <summary>
    /// Display-string form, used for the "compare by display, not raw type" rule: CSV/HTML/SpreadsheetML
    /// round-trips coerce text→typed (and back) heuristically, so a value matches when its display string
    /// matches even if the loaded scalar type differs.
    /// </summary>
    public static string DisplayString(ScalarValue v)
        => FidelityValueCompare.DisplayString(v);

    /// <summary>
    /// Looser display match: numbers compare numerically (so "1" vs "1.0" agree); everything else by
    /// case-insensitive trimmed string. Used for lossy interchange formats where LibreOffice may re-render
    /// a number's textual form differently from FreeX's source scalar.
    /// </summary>
    public static bool DisplayMatch(ScalarValue a, ScalarValue b)
    {
        if (ValuesMatch(a, b)) return true;
        // text-that-is-numeric vs number, etc.
        var sa = DisplayString(a).Trim();
        var sb = DisplayString(b).Trim();
        if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return true;
        if (double.TryParse(sa, NumberStyles.Any, CultureInfo.InvariantCulture, out var na) &&
            double.TryParse(sb, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
            return FidelityValueCompare.NumbersMatch(na, nb);
        return false;
    }

    public static string ScalarStr(ScalarValue v)
        => FidelityValueCompare.ScalarStr(v);

    public static string ColToLetter(uint col)
        => FidelityValueCompare.ColToLetter(col);
}
