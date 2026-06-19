using System;
using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

/// <summary>
/// Shared value-equivalence + address helpers, lifted verbatim from
/// <c>tools/FreeX.SheetFidelity/Program.cs</c> so the two fidelity harnesses agree on what
/// "the same value" means. Keeping the semantics identical is the whole point of factoring these
/// out: a date-serial vs a plain number, a bool vs 1/0, and any-error vs any-error must score the
/// same way in both tools.
/// </summary>
internal static class FidelityCompare
{
    /// <summary>
    /// True when two scalars are value-equivalent. A date serial and a plain number with the same
    /// magnitude display identically (the number format, not the scalar type, decides date-vs-number
    /// rendering), so equal serials are a MATCH. Bools compare as 1/0. Any error vs any error is a
    /// MATCH — only error-vs-non-error diverges.
    /// </summary>
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

    /// <summary>Numeric view of a scalar: numbers, dates (serial) and bools (1/0) are comparable.</summary>
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

    /// <summary>Absolute-or-relative tolerant numeric equality (abs &lt; 1e-9 or rel &lt; 1e-6).</summary>
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
    /// Display-string form of a scalar, used for the CSV/text "compare by display, not raw type" rule
    /// (footnote 1): CSV coerces text→typed heuristically, so a round-tripped value matches when its
    /// display string matches even if the loaded scalar type differs.
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

    /// <summary>Diagnostic rendering of a scalar (matches SheetFidelity's ScalarStr).</summary>
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

    /// <summary>1-based column index → spreadsheet letter (1→A, 27→AA).</summary>
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
