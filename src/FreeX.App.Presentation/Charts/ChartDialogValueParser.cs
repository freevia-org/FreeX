using System.Globalization;

namespace FreeX.App.Presentation.Charts;

public static class ChartDialogValueParser
{
    public static bool TryParseNullableDouble(string input, out double? value)
    {
        value = null;
        var text = input.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TryParseFiniteDouble(text, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    public static bool TryParseNullablePositiveDouble(string input, out double? value) =>
        TryParseNullableDouble(input, out value) && value is null or > 0;

    public static bool TryParsePositiveDouble(string input, out double value) =>
        TryParseFiniteDouble(input, out value) && value > 0;

    public static bool TryParseClampedDouble(string input, double min, double max, out double value) =>
        TryParseFiniteDouble(input, out value) && value >= min && value <= max;

    private static bool TryParseFiniteDouble(string text, out double value) =>
        NumericInputParser.TryParseFiniteDouble(text, CultureInfo.InvariantCulture, out value);
}
