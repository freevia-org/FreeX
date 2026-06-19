using System;
using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.FormatCrossCheck;

/// <summary>
/// Value-equivalence + address helpers, kept byte-for-byte identical to
/// <c>tools/FreeX.FormatFidelity/FidelityCompare.cs</c> (which itself mirrors SheetFidelity) so the
/// cross-check tool scores "the same value" exactly the way the in-FreeX fidelity harness does: a
/// date serial vs a plain number, a bool vs 1/0, and any-error vs any-error all MATCH.
/// </summary>
internal static class FidelityCompare
{
    public static bool ValuesMatch(ScalarValue a, ScalarValue b)
    {
        if (TryNumeric(a, out var an) && TryNumeric(b, out var bn))
            return NumbersMatch(an, bn);

        return (a, b) switch
        {
            (BlankValue, BlankValue) => true,
            (TextValue ta, TextValue tb) => string.Equals(ta.Value, tb.Value, StringComparison.Ordinal),
            (ErrorValue, ErrorValue) => true,
            _ => false
        };
    }

    public static bool TryNumeric(ScalarValue v, out double value)
    {
        switch (v)
        {
            case NumberValue n: value = n.Value; return true;
            case DateTimeValue d: value = d.Value; return true;
            case BoolValue b: value = b.Value ? 1 : 0; return true;
            default: value = 0; return false;
        }
    }

    public static bool NumbersMatch(double a, double b)
    {
        if (a == b) return true;
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        var absDiff = Math.Abs(a - b);
        if (absDiff < 1e-9) return true;
        var magnitude = Math.Max(Math.Abs(a), Math.Abs(b));
        if (magnitude > 0 && absDiff / magnitude < 1e-6) return true;
        return false;
    }

    /// <summary>
    /// Display-string form, used for the "compare by display, not raw type" rule: CSV/HTML/SpreadsheetML
    /// round-trips coerce text→typed (and back) heuristically, so a value matches when its display string
    /// matches even if the loaded scalar type differs.
    /// </summary>
    public static string DisplayString(ScalarValue v) => v switch
    {
        BlankValue => "",
        NumberValue nv => nv.Value.ToString("R", CultureInfo.InvariantCulture),
        DateTimeValue dv => dv.Value.ToString("R", CultureInfo.InvariantCulture),
        BoolValue bv => bv.Value ? "TRUE" : "FALSE",
        TextValue tv => tv.Value,
        ErrorValue ev => ev.Code,
        _ => v.ToString() ?? ""
    };

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
            return NumbersMatch(na, nb);
        return false;
    }

    public static string ScalarStr(ScalarValue v) => v switch
    {
        BlankValue => "(blank)",
        NumberValue nv => nv.Value.ToString("G", CultureInfo.InvariantCulture),
        DateTimeValue dv => $"Date({dv.Value:G})",
        TextValue tv => $"\"{tv.Value}\"",
        BoolValue bv => bv.Value ? "TRUE" : "FALSE",
        ErrorValue ev => ev.ToString(),
        _ => v.ToString() ?? "?"
    };

    public static string ColToLetter(uint col)
    {
        var sb = new StringBuilder(3);
        while (col > 0)
        {
            col--;
            sb.Insert(0, (char)('A' + col % 26));
            col /= 26;
        }
        return sb.ToString();
    }
}
