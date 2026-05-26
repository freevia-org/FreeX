using System.Globalization;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

internal static class NumberFormatColorMapper
{
    public static (string? Color, string Format) ExtractColor(string section)
    {
        var match = Regex.Match(section, @"^\[([A-Za-z]+|Color\s*\d+|ThemeColor\s+[^\]]+)\]", RegexOptions.IgnoreCase);
        if (!match.Success)
            return (null, section);

        return TryMapColor(match.Groups[1].Value, out var hex)
            ? (hex, section[match.Length..])
            : (null, section);
    }

    public static bool TryMapColor(string token, out string? color)
        => TryMapColor(token, null, out color);

    public static bool TryMapColor(string token, WorkbookIndexedColorPalette? indexedColors, out string? color)
        => TryMapColor(token, indexedColors, out color, out _);

    public static bool TryMapColor(
        string token,
        WorkbookIndexedColorPalette? indexedColors,
        out string? color,
        out WorkbookThemeColorReference? themeColor)
    {
        token = token.Trim();
        themeColor = null;

        if (TryMapThemeColor(token, out themeColor))
        {
            color = null;
            return true;
        }

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

    private static bool TryMapThemeColor(string token, out WorkbookThemeColorReference? themeColor)
    {
        themeColor = null;
        var match = Regex.Match(
            token,
            @"^ThemeColor\s+([A-Za-z0-9 _-]+?)(?:\s+Tint\s+([+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))))?$",
            RegexOptions.IgnoreCase);
        if (!match.Success ||
            !TryMapThemeSlot(match.Groups[1].Value.Trim(), out var slot))
        {
            return false;
        }

        var tint = 0d;
        if (match.Groups[2].Success &&
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tint))
        {
            return false;
        }

        themeColor = new WorkbookThemeColorReference(slot, Math.Clamp(tint, -1, 1));
        return true;
    }

    private static bool TryMapThemeSlot(string token, out WorkbookThemeColorSlot slot)
    {
        slot = NormalizeThemeSlot(token) switch
        {
            "dark1" or "dk1" or "text1" or "tx1" => WorkbookThemeColorSlot.Dark1,
            "light1" or "lt1" or "background1" or "bg1" => WorkbookThemeColorSlot.Light1,
            "dark2" or "dk2" or "text2" or "tx2" => WorkbookThemeColorSlot.Dark2,
            "light2" or "lt2" or "background2" or "bg2" => WorkbookThemeColorSlot.Light2,
            "accent1" => WorkbookThemeColorSlot.Accent1,
            "accent2" => WorkbookThemeColorSlot.Accent2,
            "accent3" => WorkbookThemeColorSlot.Accent3,
            "accent4" => WorkbookThemeColorSlot.Accent4,
            "accent5" => WorkbookThemeColorSlot.Accent5,
            "accent6" => WorkbookThemeColorSlot.Accent6,
            "hyperlink" or "hlink" => WorkbookThemeColorSlot.Hyperlink,
            "followedhyperlink" or "folhlink" => WorkbookThemeColorSlot.FollowedHyperlink,
            _ => default
        };

        return NormalizeThemeSlot(token) is
            "dark1" or "dk1" or "text1" or "tx1" or
            "light1" or "lt1" or "background1" or "bg1" or
            "dark2" or "dk2" or "text2" or "tx2" or
            "light2" or "lt2" or "background2" or "bg2" or
            "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or
            "hyperlink" or "hlink" or "followedhyperlink" or "folhlink";
    }

    private static string NormalizeThemeSlot(string token) =>
        token.Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static bool TryMapIndexedColor(
        string token,
        WorkbookIndexedColorPalette? indexedColors,
        out string? color)
    {
        color = null;
        var match = Regex.Match(token, @"^Color\s*(\d+)$", RegexOptions.IgnoreCase);
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
