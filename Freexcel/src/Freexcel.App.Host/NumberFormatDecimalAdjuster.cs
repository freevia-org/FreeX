using System.Text.RegularExpressions;

namespace Freexcel.App.Host;

public static class NumberFormatDecimalAdjuster
{
    public static string AddDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0.0";

        var decimalMatch = Regex.Match(format, @"(\d*)(\.(\d*))");
        if (decimalMatch.Success)
        {
            return format
                .Remove(decimalMatch.Index, decimalMatch.Length)
                .Insert(decimalMatch.Index, decimalMatch.Groups[1].Value + "." + decimalMatch.Groups[3].Value + "0");
        }

        var integerMatch = Regex.Match(format, @"(\d+)");
        if (integerMatch.Success)
            return format.Remove(integerMatch.Index, integerMatch.Length).Insert(integerMatch.Index, integerMatch.Value + ".0");

        return format + ".0";
    }

    public static string RemoveDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0";

        var decimalMatch = Regex.Match(format, @"\.(\d+)");
        if (!decimalMatch.Success)
            return format;

        if (decimalMatch.Groups[1].Value.Length <= 1)
            return format.Remove(decimalMatch.Index, decimalMatch.Length);

        return format.Remove(decimalMatch.Index + decimalMatch.Length - 1, 1);
    }
}
