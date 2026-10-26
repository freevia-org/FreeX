using System.Globalization;

namespace Free.Shared.AppServices;

public static class DialogNumericTextPolicy
{
    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    public static string FormatNullableDouble(double? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value is double numeric
            ? numeric.ToString("G6", culture)
            : string.Empty;
    }

    public static bool TryParsePositiveDouble(string? text, CultureInfo culture, out double value) =>
        TryParseDouble(text, culture, out value) && value > 0;

    public static bool TryParseNonNegativeDouble(string? text, CultureInfo culture, out double value) =>
        TryParseDouble(text, culture, out value) && value >= 0;

    public static bool TryParseOptionalNonNegativeDouble(
        bool isChecked,
        string? text,
        CultureInfo culture,
        out double? value)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (!isChecked)
        {
            value = null;
            return true;
        }

        if (TryParseNonNegativeDouble(text, culture, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    public static double? ParseNullableDouble(object? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (value is double numericValue)
            return numericValue;

        if (value is not string text)
            return null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        // r571: IsFinite as well. NumberStyles.Any accepts the literal "NaN" and "Infinity", and
        // this overload has no range check at all, so every spelling reached the caller.
        return double.TryParse(trimmed, NumberStyles.Any, culture, out var numeric)
            && double.IsFinite(numeric)
            ? numeric
            : null;
    }

    private static bool TryParseDouble(string? text, CultureInfo culture, out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // r571: IsFinite as well, and the reason CORRECTS r551. That round recorded that the
        // ACCEPT form of a range check rejects both Infinity and NaN -- but its example carried
        // BOTH bounds ("width > 0 && width <= 12"). The wrappers over this helper are accept-form
        // with only a LOWER bound (value > 0, value >= 0), and Infinity satisfies those, so it
        // passed to 55 call sites as a chart size, column spacing or hyphenation zone. Only NaN
        // was caught, because NaN fails every comparison. An accept form excludes a non-finite
        // value only when it is bounded on the side Infinity would exceed.
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value)
            && double.IsFinite(value);
    }
}
