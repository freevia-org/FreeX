using System.Globalization;

namespace FreeX.App.Host;

internal static class NumericInputParser
{
    public static bool TryParseFiniteDouble(string input, CultureInfo culture, out double value)
    {
        if (double.TryParse(input.Trim(), NumberStyles.Float, culture, out value) && double.IsFinite(value))
            return true;

        value = 0;
        return false;
    }

    public static bool TryParseFiniteDouble(
        string input,
        CultureInfo primaryCulture,
        CultureInfo fallbackCulture,
        out double value)
    {
        var trimmed = input.Trim();
        return TryParseFiniteDouble(trimmed, primaryCulture, out value) ||
               TryParseFiniteDouble(trimmed, fallbackCulture, out value);
    }
}
