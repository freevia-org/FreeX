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

        return double.TryParse(trimmed, NumberStyles.Any, culture, out var numeric)
            ? numeric
            : null;
    }

    private static bool TryParseDouble(string? text, CultureInfo culture, out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value);
    }
}
