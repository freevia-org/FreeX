using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Calc;

internal static class NumberFormatColorMapper
{
    private static readonly string[] IndexedFormatColors =
    [
        "",
        "#000000",
        "#FFFFFF",
        "#FF0000",
        "#00B050",
        "#0070C0",
        "#FFFF00",
        "#FF00FF",
        "#00FFFF",
        "#800000",
        "#008000",
        "#000080",
        "#808000",
        "#800080",
        "#008080",
        "#C0C0C0",
        "#808080",
        "#9999FF",
        "#993366",
        "#FFFFCC",
        "#CCFFFF",
        "#660066",
        "#FF8080",
        "#0066CC",
        "#CCCCFF",
        "#000080",
        "#FF00FF",
        "#FFFF00",
        "#00FFFF",
        "#800080",
        "#800000",
        "#008080",
        "#0000FF",
        "#00CCFF",
        "#CCFFFF",
        "#CCFFCC",
        "#FFFF99",
        "#99CCFF",
        "#FF99CC",
        "#CC99FF",
        "#FFCC99",
        "#3366FF",
        "#33CCCC",
        "#99CC00",
        "#FFCC00",
        "#FF9900",
        "#FF6600",
        "#666699",
        "#969696",
        "#003366",
        "#339966",
        "#003300",
        "#333300",
        "#993300",
        "#333399",
        "#333333",
        "#333333"
    ];

    public static (string? Color, string Format) ExtractColor(string section)
    {
        var match = Regex.Match(section, @"^\[([A-Za-z]+|Color\d+)\]", RegexOptions.IgnoreCase);
        if (!match.Success)
            return (null, section);

        return TryMapColor(match.Groups[1].Value, out var hex)
            ? (hex, section[match.Length..])
            : (null, section);
    }

    public static bool TryMapColor(string token, out string? color)
    {
        token = token.Trim();

        if (TryMapIndexedColor(token, out color))
            return true;

        color = token.ToUpperInvariant() switch
        {
            "BLACK" => "#000000",
            "WHITE" => "#FFFFFF",
            "RED" => "#FF0000",
            "GREEN" => "#00B050",
            "BLUE" => "#0070C0",
            "YELLOW" => "#FFFF00",
            "CYAN" => "#00FFFF",
            "MAGENTA" => "#FF00FF",
            _ => null
        };
        return color is not null;
    }

    private static bool TryMapIndexedColor(string token, out string? color)
    {
        color = null;
        var match = Regex.Match(token, @"^Color(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index <= 0 ||
            index >= IndexedFormatColors.Length)
        {
            return false;
        }

        color = IndexedFormatColors[index];
        return true;
    }
}
