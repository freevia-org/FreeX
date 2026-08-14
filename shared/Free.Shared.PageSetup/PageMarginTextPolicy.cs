using System.Globalization;

namespace Free.Shared.PageSetup;

/// <summary>Why a page-setup measurement field failed to parse.</summary>
public enum PageMeasureParseFailure
{
    None,

    /// <summary>The field was blank (or whitespace) and blanks are not accepted here.</summary>
    Blank,

    /// <summary>The text is not a finite number in the supplied culture.</summary>
    NotANumber,

    /// <summary>The value parsed but is negative where a non-negative value is required.</summary>
    Negative,

    /// <summary>The value parsed but is zero or negative where a strictly positive value is required.</summary>
    NotPositive,
}

/// <summary>
/// Culture-aware parse/validate/format for the free-text measurement fields on the sibling page-setup
/// dialogs (margins, gutter, header/footer distance, custom page width and height). It owns only the
/// numeric rules; the user-facing wording stays app-side, because FreeX and FreeW deliberately word and
/// localize their validation messages differently.
/// </summary>
public static class PageMarginTextPolicy
{
    /// <summary>The dialog measurement format ("0.##") both apps use for these fields.</summary>
    public const string MeasureFormat = "0.##";

    public static string Format(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString(MeasureFormat, culture);
    }

    /// <summary>Parses a non-negative measurement. Blank input fails with <see cref="PageMeasureParseFailure.Blank"/>.</summary>
    public static bool TryParseNonNegative(string? text, CultureInfo culture, out double value) =>
        TryParseNonNegative(text, culture, out value, out _);

    public static bool TryParseNonNegative(
        string? text,
        CultureInfo culture,
        out double value,
        out PageMeasureParseFailure failure)
    {
        if (!TryParseFinite(text, culture, out value, out failure))
            return false;

        if (value < 0)
        {
            failure = PageMeasureParseFailure.Negative;
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>Parses a strictly positive measurement (page width/height).</summary>
    public static bool TryParsePositive(string? text, CultureInfo culture, out double value) =>
        TryParsePositive(text, culture, out value, out _);

    public static bool TryParsePositive(
        string? text,
        CultureInfo culture,
        out double value,
        out PageMeasureParseFailure failure)
    {
        if (!TryParseFinite(text, culture, out value, out failure))
        {
            value = 1;
            return false;
        }

        if (value <= 0)
        {
            failure = PageMeasureParseFailure.NotPositive;
            value = 1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parses a non-negative measurement, treating blank input as "leave unchanged" and yielding
    /// <paramref name="fallback"/>. Used by the surfaces where an empty box means "keep current".
    /// </summary>
    public static bool TryParseNonNegativeOrBlank(
        string? text,
        CultureInfo culture,
        double fallback,
        out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (IsBlank(text))
        {
            value = fallback;
            return true;
        }

        return TryParseNonNegative(text, culture, out value);
    }

    public static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);

    private static bool TryParseFinite(
        string? text,
        CultureInfo culture,
        out double value,
        out PageMeasureParseFailure failure)
    {
        ArgumentNullException.ThrowIfNull(culture);

        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            failure = PageMeasureParseFailure.Blank;
            return false;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, culture, out value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            value = 0;
            failure = PageMeasureParseFailure.NotANumber;
            return false;
        }

        failure = PageMeasureParseFailure.None;
        return true;
    }
}
