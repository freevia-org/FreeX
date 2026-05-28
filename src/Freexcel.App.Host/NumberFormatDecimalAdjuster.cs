using System.Text.RegularExpressions;

namespace Freexcel.App.Host;

public static partial class NumberFormatDecimalAdjuster
{
    public static string AddDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0.0";

        var decimalMatch = DecimalPlacesRegex().Match(format);
        if (decimalMatch.Success)
        {
            return format
                .Remove(decimalMatch.Index, decimalMatch.Length)
                .Insert(decimalMatch.Index, decimalMatch.Groups[1].Value + "." + decimalMatch.Groups[3].Value + "0");
        }

        var integerMatch = IntegerDigitsRegex().Match(format);
        if (integerMatch.Success)
            return format.Remove(integerMatch.Index, integerMatch.Length).Insert(integerMatch.Index, integerMatch.Value + ".0");

        return format + ".0";
    }

    public static string RemoveDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0";

        var decimalMatch = RemoveDecimalPlacesRegex().Match(format);
        if (!decimalMatch.Success)
            return format;

        if (decimalMatch.Groups[1].Value.Length <= 1)
            return format.Remove(decimalMatch.Index, decimalMatch.Length);

        return format.Remove(decimalMatch.Index + decimalMatch.Length - 1, 1);
    }

    [GeneratedRegex(@"(\d*)(\.(\d*))")]
    private static partial Regex DecimalPlacesRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex IntegerDigitsRegex();

    [GeneratedRegex(@"\.(\d+)")]
    private static partial Regex RemoveDecimalPlacesRegex();
}
