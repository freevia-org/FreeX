using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal static class NumberFormatColorMapper
{
    private static readonly Regex LeadingColorDirectiveRegex = new(
        @"^\[([^\]]+)\]",
        RegexOptions.IgnoreCase);
    private static readonly Regex IndexedColorTokenRegex = new(
        @"^Color\s*(\d+)$",
        RegexOptions.IgnoreCase);

    public static (string? Color, string Format) ExtractColor(string section)
    {
        var match = LeadingColorDirectiveRegex.Match(section);
        if (!match.Success)
            return (null, section);

        var token = match.Groups[1].Value;
        if (TryMapColor(token, out var hex))
            return (hex, section[match.Length..]);

        return IsThemeColorDirective(token)
            ? (null, section[match.Length..])
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
        if (theme is null || !TryGetThemeColorReference(token, out var slot, out var tint))
            return false;

        color = ToHex(theme.ResolveColor(slot, tint));
        return true;
    }

    private static bool TryGetThemeColorReference(
        string token,
        out WorkbookThemeColorSlot slot,
        out double tint)
    {
        tint = 0;

        if (TryConsumeIgnoringWhitespace(token, "THEMEDARK1", 0, out var next))
            slot = WorkbookThemeColorSlot.Dark1;
        else if (TryConsumeIgnoringWhitespace(token, "THEMELIGHT1", 0, out next))
            slot = WorkbookThemeColorSlot.Light1;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEDARK2", 0, out next))
            slot = WorkbookThemeColorSlot.Dark2;
        else if (TryConsumeIgnoringWhitespace(token, "THEMELIGHT2", 0, out next))
            slot = WorkbookThemeColorSlot.Light2;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT1", 0, out next))
            slot = WorkbookThemeColorSlot.Accent1;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT2", 0, out next))
            slot = WorkbookThemeColorSlot.Accent2;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT3", 0, out next))
            slot = WorkbookThemeColorSlot.Accent3;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT4", 0, out next))
            slot = WorkbookThemeColorSlot.Accent4;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT5", 0, out next))
            slot = WorkbookThemeColorSlot.Accent5;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEACCENT6", 0, out next))
            slot = WorkbookThemeColorSlot.Accent6;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEHYPERLINK", 0, out next))
            slot = WorkbookThemeColorSlot.Hyperlink;
        else if (TryConsumeIgnoringWhitespace(token, "THEMEFOLLOWEDHYPERLINK", 0, out next))
            slot = WorkbookThemeColorSlot.FollowedHyperlink;
        else
        {
            slot = default;
            return false;
        }

        next = SkipWhitespace(token, next);
        if (next == token.Length)
            return true;

        return TryConsumeIgnoringWhitespace(token, "TINT", next, out next) &&
               TryParseThemeTint(token, next, out tint);
    }

    private static bool TryParseThemeTint(string token, int startIndex, out double tint)
    {
        tint = 0;
        startIndex = SkipWhitespace(token, startIndex);
        var endIndex = token.Length;
        while (endIndex > startIndex && char.IsWhiteSpace(token[endIndex - 1]))
            endIndex--;

        var hasPercentSuffix = endIndex > startIndex && token[endIndex - 1] == '%';
        if (hasPercentSuffix)
        {
            endIndex--;
            while (endIndex > startIndex && char.IsWhiteSpace(token[endIndex - 1]))
                endIndex--;
        }

        if (startIndex >= endIndex ||
            !double.TryParse(
                token.AsSpan(startIndex, endIndex - startIndex),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var tintPercent) ||
            tintPercent is < -100d or > 100d)
        {
            return false;
        }

        tint = tintPercent / 100d;
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

    private static bool TryConsumeIgnoringWhitespace(
        string token,
        string expected,
        int startIndex,
        out int nextIndex)
    {
        var tokenIndex = startIndex;
        var expectedIndex = 0;
        while (tokenIndex < token.Length && expectedIndex < expected.Length)
        {
            var current = token[tokenIndex++];
            if (char.IsWhiteSpace(current))
                continue;

            if (char.ToUpperInvariant(current) != expected[expectedIndex++])
            {
                nextIndex = startIndex;
                return false;
            }
        }

        if (expectedIndex != expected.Length)
        {
            nextIndex = startIndex;
            return false;
        }

        nextIndex = tokenIndex;
        return true;
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

    private static int SkipWhitespace(string token, int index)
    {
        while (index < token.Length && char.IsWhiteSpace(token[index]))
            index++;

        return index;
    }

    private static string ToHex(CellColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
