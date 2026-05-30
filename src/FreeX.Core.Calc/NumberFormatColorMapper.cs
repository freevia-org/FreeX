using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

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

        color = TryMapNamedColor(token);
        return color is not null;
    }

    public static bool IsThemeColorDirective(string token)
        => TokenStartsWithIgnoringWhitespace(token, "THEME");

    private static string? TryMapNamedColor(string token)
    {
        if (token.Equals("BLACK", StringComparison.OrdinalIgnoreCase))
            return "#000000";
        if (token.Equals("WHITE", StringComparison.OrdinalIgnoreCase))
            return "#FFFFFF";
        if (token.Equals("RED", StringComparison.OrdinalIgnoreCase))
            return "#FF0000";
        if (token.Equals("GREEN", StringComparison.OrdinalIgnoreCase))
            return "#00B050";
        if (token.Equals("BLUE", StringComparison.OrdinalIgnoreCase))
            return "#0070C0";
        if (token.Equals("YELLOW", StringComparison.OrdinalIgnoreCase))
            return "#FFFF00";
        if (token.Equals("CYAN", StringComparison.OrdinalIgnoreCase))
            return "#00FFFF";
        if (token.Equals("MAGENTA", StringComparison.OrdinalIgnoreCase))
            return "#FF00FF";

        return null;
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
        if (TokenEqualsIgnoringWhitespace(token, "THEMEDARK1"))
            slot = WorkbookThemeColorSlot.Dark1;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMELIGHT1"))
            slot = WorkbookThemeColorSlot.Light1;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEDARK2"))
            slot = WorkbookThemeColorSlot.Dark2;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMELIGHT2"))
            slot = WorkbookThemeColorSlot.Light2;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT1"))
            slot = WorkbookThemeColorSlot.Accent1;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT2"))
            slot = WorkbookThemeColorSlot.Accent2;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT3"))
            slot = WorkbookThemeColorSlot.Accent3;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT4"))
            slot = WorkbookThemeColorSlot.Accent4;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT5"))
            slot = WorkbookThemeColorSlot.Accent5;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEACCENT6"))
            slot = WorkbookThemeColorSlot.Accent6;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEHYPERLINK"))
            slot = WorkbookThemeColorSlot.Hyperlink;
        else if (TokenEqualsIgnoringWhitespace(token, "THEMEFOLLOWEDHYPERLINK"))
            slot = WorkbookThemeColorSlot.FollowedHyperlink;
        else
        {
            slot = default;
            return false;
        }

        return true;
    }

    private static bool TokenStartsWithIgnoringWhitespace(string token, string prefix)
    {
        var tokenIndex = 0;
        var prefixIndex = 0;
        while (tokenIndex < token.Length && prefixIndex < prefix.Length)
        {
            var current = token[tokenIndex++];
            if (char.IsWhiteSpace(current))
                continue;

            if (char.ToUpperInvariant(current) != prefix[prefixIndex++])
                return false;
        }

        return prefixIndex == prefix.Length;
    }

    private static bool TokenEqualsIgnoringWhitespace(string token, string expected)
    {
        var tokenIndex = 0;
        var expectedIndex = 0;
        while (tokenIndex < token.Length && expectedIndex < expected.Length)
        {
            var current = token[tokenIndex++];
            if (char.IsWhiteSpace(current))
                continue;

            if (char.ToUpperInvariant(current) != expected[expectedIndex++])
                return false;
        }

        while (tokenIndex < token.Length)
        {
            if (!char.IsWhiteSpace(token[tokenIndex++]))
                return false;
        }

        return expectedIndex == expected.Length;
    }

    private static string ToHex(CellColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
