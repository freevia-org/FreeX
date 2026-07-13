using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    // Calibrated against Excel's canonical default-column-width example: at the real default
    // column width (8.43 units / 64px), ViewportService's generic average-character estimate
    // (EstimateCharacterWidth) yields 8, but Excel's General format actually displays up to 11
    // characters there (e.g. "0.333333333" for 1/3). See FormatNumberGeneral for the full
    // rationale.
    private const int GeneralFormatDigitBudgetBonus = 3;

    private static string FormatGeneral(ScalarValue value, bool uses1904DateSystem = false, int? targetWidthCharacters = null) => value switch
    {
        NumberValue n => FormatNumberGeneral(n.Value, targetWidthCharacters),
        DateTimeValue d => FormatGeneralDateTime(d.Value, uses1904DateSystem),
        TextValue t => StripLeadingForceTextApostrophe(t.Value),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        ErrorValue e => e.Code,
        BlankValue => "",
        _ => ""
    };

    /// <summary>
    /// Excel's "force text" entry gesture (typing a leading apostrophe, e.g. <c>'5</c>) is kept
    /// as part of the cell's literal text value so the formula bar can still show it, but the
    /// apostrophe itself is solely an edit-time marker -- the grid cell always renders the text
    /// with that single leading apostrophe stripped.
    /// </summary>
    private static string StripLeadingForceTextApostrophe(string text) =>
        text.Length > 0 && text[0] == '\'' ? text[1..] : text;

    private static string FormatGeneralDateTime(double value, bool uses1904DateSystem = false)
    {
        try
        {
            var dt = uses1904DateSystem
                ? ExcelDateSystem.SerialToDate(value, uses1904DateSystem)
                : DateTime.FromOADate(value);
            return dt.ToString("d", CultureInfo.InvariantCulture);
        }
        catch { return FormatNumberGeneral(value); }
    }

    private static string FormatNumberGeneral(double value, int? targetWidthCharacters = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return value.ToString(CultureInfo.InvariantCulture);

        // Excel never displays negative zero. -0.0 passes the truncation/magnitude checks
        // below unchanged, but (unlike the (long) cast used when no width is known) the
        // width-aware G-format loop calls .ToString() directly on the raw double and would
        // otherwise print .NET's own "-0" for the sign bit.
        if (value == 0)
            value = 0.0;

        if (targetWidthCharacters is not int width)
        {
            // No column-width context (e.g. formula-bar/text-coercion callers) -- keep the
            // original unconstrained General rendering: the full integer up to double's
            // ~15-digit precision ceiling, else a fixed 10-significant-digit decimal.
            if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("G10", CultureInfo.InvariantCulture);
        }

        // Column-width-aware General format: Excel fits as many significant digits (up to its
        // 15-digit double-precision ceiling) as the column can display, and falls back to
        // scientific notation once the fixed-point form no longer fits -- e.g. 10^14 shows as
        // the full 15-digit integer in a wide-enough column, but as "1E+14" at the default
        // column width. .NET's "G" format specifier already picks fixed-point vs. scientific
        // the same way (fixed when the value's exponent is within [-5, precision), else
        // scientific) and trims insignificant trailing zeros, so trying decreasing precisions
        // and keeping the first one that fits the available character budget reproduces
        // Excel's behavior (e.g. a budget of 11 yields "0.333333333" for 1/3, matching Excel's
        // canonical example).
        //
        // `width` itself is the caller's *generic* average-character estimate for the column's
        // pixel width (ViewportService.EstimateCharacterWidth, calibrated for things like
        // auto-fit text sizing). General-format numbers pack tighter than that generic estimate
        // assumes -- digits, the decimal point, sign, and the "E+" exponent marker are all
        // narrower than an average character -- so Excel displays noticeably more of them than
        // the raw character estimate suggests. Excel's own canonical example pins the ratio: at
        // the real default column width (8.43 units / 64px), the generic estimate is 8
        // characters, but Excel actually shows up to 11. Apply that calibrated bonus before
        // fitting so the real default-width path reproduces Excel's digit budget instead of the
        // generic (and too-narrow) text-sizing estimate.
        var digitBudget = width + GeneralFormatDigitBudgetBonus;
        for (var precision = 15; precision >= 1; precision--)
        {
            var candidate = value.ToString("G" + precision, CultureInfo.InvariantCulture);
            if (candidate.Length <= digitBudget)
                return candidate;
        }

        // Not even the narrowest representation (G1, e.g. scientific notation "1E+14") fits the
        // available column width -- match Excel's "value doesn't fit" indicator (a run of '#'
        // characters sized to the column) instead of silently returning the still-too-wide text,
        // which would otherwise just get clipped by the grid's cell clip geometry.
        return new string('#', Math.Max(width, 1));
    }
}
