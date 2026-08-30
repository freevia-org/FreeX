using System.Globalization;

namespace FreeX.App.Presentation;

public static class NumericInputParser
{
    public static bool TryParseInt32(string input, out int value) =>
        TryParseInt32(
            input,
            CultureInfo.CurrentCulture,
            CultureInfo.InvariantCulture,
            out value);

    public static bool TryParseInt32(
        string input,
        CultureInfo primaryCulture,
        CultureInfo fallbackCulture,
        out int value)
    {
        ArgumentNullException.ThrowIfNull(input);

        var trimmed = input.AsSpan().Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, primaryCulture, out value) ||
            int.TryParse(trimmed, NumberStyles.Integer, fallbackCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public static bool TryParseInt32InRange(
        string input,
        int min,
        int max,
        out int value) =>
        TryParseInt32InRange(
            input,
            min,
            max,
            CultureInfo.CurrentCulture,
            CultureInfo.InvariantCulture,
            out value);

    public static bool TryParseInt32InRange(
        string input,
        int min,
        int max,
        CultureInfo primaryCulture,
        CultureInfo fallbackCulture,
        out int value) =>
        TryParseInt32(input, primaryCulture, fallbackCulture, out value) &&
        value >= min &&
        value <= max;

    public static bool TryParseFiniteDouble(string input, CultureInfo culture, out double value)
        => TryParseFiniteDouble(input, NumberStyles.Float, culture, out value);

    public static bool TryParseFiniteDouble(string input, NumberStyles styles, CultureInfo culture, out double value)
    {
        if (double.TryParse(input.Trim(), styles, culture, out value) && double.IsFinite(value))
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
