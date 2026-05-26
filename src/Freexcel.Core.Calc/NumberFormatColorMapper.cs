using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

internal static class NumberFormatColorMapper
{
    private static readonly Regex LeadingColorDirectiveRegex = new(
        @"^\[([A-Za-z]+|Color\s*\d+)\]",
        RegexOptions.IgnoreCase);
    private static readonly Regex IndexedColorTokenRegex = new(
        @"^Color\s*(\d+)$",
        RegexOptions.IgnoreCase);

    public static (string? Color, string Format) ExtractColor(string section)
    {
        var match = LeadingColorDirectiveRegex.Match(section);
        if (!match.Success)
            return (null, section);

        return TryMapColor(match.Groups[1].Value, out var hex)
            ? (hex, section[match.Length..])
            : (null, section);
    }

    public static bool TryMapColor(string token, out string? color)
        => TryMapColor(token, null, out color);

    public static bool TryMapColor(string token, WorkbookIndexedColorPalette? indexedColors, out string? color)
    {
        token = token.Trim();

        if (TryMapIndexedColor(token, indexedColors, out color))
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

    private static bool TryMapIndexedColor(
        string token,
        WorkbookIndexedColorPalette? indexedColors,
        out string? color)
    {
        color = null;
        var match = IndexedColorTokenRegex.Match(token);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            !((indexedColors is not null && indexedColors.TryResolveColor(index, out var resolvedColor)) ||
              WorkbookIndexedColorPalette.TryGetDefaultColor(index, out resolvedColor)))
        {
            return false;
        }

        color = ToHex(resolvedColor);
        return true;
    }

    private static string ToHex(CellColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
