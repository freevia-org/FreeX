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
    private static readonly Regex ColorTokenWhitespaceRegex = new(@"\s+");

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
        => TryMapColor(token, null, null, out color);

    public static bool TryMapColor(string token, WorkbookIndexedColorPalette? indexedColors, out string? color)
        => TryMapColor(token, indexedColors, null, out color);

    public static bool TryMapColor(
        string token,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        out string? color)
    {
        token = token.Trim();

        if (TryMapIndexedColor(token, indexedColors, out color))
            return true;

        if (TryMapThemeColor(token, theme, out color))
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

    public static bool IsThemeColorDirective(string token)
        => NormalizeToken(token).StartsWith("THEME", StringComparison.Ordinal);

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

    private static bool TryMapThemeColor(
        string token,
        WorkbookTheme? theme,
        out string? color)
    {
        color = null;
        if (theme is null || !TryGetThemeColorSlot(token, out var slot))
            return false;

        color = ToHex(theme.ResolveColor(slot));
        return true;
    }

    private static bool TryGetThemeColorSlot(string token, out WorkbookThemeColorSlot slot)
    {
        var mappedSlot = NormalizeToken(token) switch
        {
            "THEMEDARK1" => (WorkbookThemeColorSlot?)WorkbookThemeColorSlot.Dark1,
            "THEMELIGHT1" => WorkbookThemeColorSlot.Light1,
            "THEMEDARK2" => WorkbookThemeColorSlot.Dark2,
            "THEMELIGHT2" => WorkbookThemeColorSlot.Light2,
            "THEMEACCENT1" => WorkbookThemeColorSlot.Accent1,
            "THEMEACCENT2" => WorkbookThemeColorSlot.Accent2,
            "THEMEACCENT3" => WorkbookThemeColorSlot.Accent3,
            "THEMEACCENT4" => WorkbookThemeColorSlot.Accent4,
            "THEMEACCENT5" => WorkbookThemeColorSlot.Accent5,
            "THEMEACCENT6" => WorkbookThemeColorSlot.Accent6,
            "THEMEHYPERLINK" => WorkbookThemeColorSlot.Hyperlink,
            "THEMEFOLLOWEDHYPERLINK" => WorkbookThemeColorSlot.FollowedHyperlink,
            _ => null
        };

        slot = mappedSlot.GetValueOrDefault();
        return mappedSlot.HasValue;
    }

    private static string NormalizeToken(string token) =>
        ColorTokenWhitespaceRegex.Replace(token.Trim(), "").ToUpperInvariant();

    private static string ToHex(CellColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
