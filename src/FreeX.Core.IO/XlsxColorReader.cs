using Free.Shared.Drawing;
using FreeX.Core.Model;
using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

public static class XlsxColorReader
{
    public static bool TryParseHexColor(string? text, out CellColor color)
    {
        color = default;
        if (!DrawingMlRgbColor.TryParseHexRgb(text, out var rgb))
            return false;

        color = new CellColor(rgb.R, rgb.G, rgb.B);
        return true;
    }

    public static bool TryReadRgbColor(XElement? element, out RgbColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        var normalized = NormalizeRgbAttribute(rgb);
        if (!TryParseHexColor(normalized, out var cellColor))
            return false;

        color = RgbColor.FromCellColor(cellColor);
        return true;
    }

    public static bool TryReadRgbColor(XElement? element, WorkbookTheme theme, out RgbColor color)
    {
        if (TryReadRgbColor(element, out color))
            return true;

        if (TryReadThemeColor(element, theme, out var cellColor))
        {
            color = RgbColor.FromCellColor(cellColor);
            return true;
        }

        color = default;
        return false;
    }

    public static bool TryReadRgbColor(
        XElement? element,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors,
        out RgbColor color)
    {
        if (TryReadRgbColor(element, theme, out color))
            return true;

        if (TryReadIndexedColor(element, indexedColors, out var cellColor))
        {
            color = RgbColor.FromCellColor(cellColor);
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>
    /// Like <see cref="TryReadRgbColor(XElement?,WorkbookTheme,WorkbookIndexedColorPalette,out RgbColor)"/>
    /// but also returns the raw OOXML theme index and tint via <paramref name="source"/> when the color
    /// was expressed as a theme reference (so callers can round-trip the original attributes).
    /// <paramref name="source"/> is <see langword="null"/> when the color was sRGB or indexed.
    /// </summary>
    public static bool TryReadRgbColorWithSource(
        XElement? element,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors,
        out RgbColor color,
        out CfColorStopSource? source)
    {
        source = null;

        if (TryReadRgbColor(element, out color))
            return true;

        if (TryReadThemeColorWithSource(element, theme, out var cellColor, out source))
        {
            color = RgbColor.FromCellColor(cellColor);
            return true;
        }

        if (TryReadIndexedColor(element, indexedColors, out cellColor))
        {
            color = RgbColor.FromCellColor(cellColor);
            return true;
        }

        color = default;
        return false;
    }

    public static bool TryReadCellColor(XElement? element, out CellColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        return TryParseHexColor(NormalizeRgbAttribute(rgb), out color);
    }

    public static bool TryReadCellColor(
        XElement? element,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors,
        out CellColor color)
    {
        if (TryReadCellColor(element, out color))
            return true;

        if (TryReadThemeColor(element, theme, out color))
            return true;

        if (TryReadIndexedColor(element, indexedColors, out color))
            return true;

        color = default;
        return false;
    }

    /// <summary>
    /// Like <see cref="TryReadCellColor(XElement?,WorkbookTheme,WorkbookIndexedColorPalette,out CellColor)"/>
    /// but also returns a <see cref="WorkbookThemeColorReference"/> (slot + tint) via
    /// <paramref name="themeColorReference"/> when the color was expressed as a theme reference, so callers
    /// can preserve the theme link instead of only keeping the baked RGB (see R80-border-theme-color-1:
    /// without this a theme-colored cell border loses its theme link on round-trip, unlike font/fill colors).
    /// <paramref name="themeColorReference"/> is <see langword="null"/> when the color was sRGB or indexed.
    /// </summary>
    public static bool TryReadCellColorWithThemeReference(
        XElement? element,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors,
        out CellColor color,
        out WorkbookThemeColorReference? themeColorReference)
    {
        themeColorReference = null;

        if (TryReadCellColor(element, out color))
            return true;

        if (TryReadThemeColorReference(element, theme, out color, out themeColorReference))
            return true;

        if (TryReadIndexedColor(element, indexedColors, out color))
            return true;

        color = default;
        return false;
    }

    private static string NormalizeRgbAttribute(string rgb)
    {
        var normalized = rgb.Trim().TrimStart('#');
        return normalized.Length == 8
            ? normalized[2..]
            : normalized;
    }

    private static bool TryReadThemeColor(XElement? element, WorkbookTheme theme, out CellColor color)
    {
        color = default;
        if (element is null)
            return false;

        var themeText = element.Attribute("theme")?.Value;
        if (string.IsNullOrWhiteSpace(themeText) ||
            !int.TryParse(themeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var themeIndex) ||
            !TryMapThemeColorSlot(themeIndex, out var slot))
        {
            return false;
        }

        color = theme.ResolveColor(slot, ReadTint(element));
        return true;
    }

    private static bool TryReadThemeColorReference(
        XElement? element,
        WorkbookTheme theme,
        out CellColor color,
        out WorkbookThemeColorReference? themeColorReference)
    {
        color = default;
        themeColorReference = null;
        if (element is null)
            return false;

        var themeText = element.Attribute("theme")?.Value;
        if (string.IsNullOrWhiteSpace(themeText) ||
            !int.TryParse(themeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var themeIndex) ||
            !TryMapThemeColorSlot(themeIndex, out var slot))
        {
            return false;
        }

        var tint = ReadTint(element);
        color = theme.ResolveColor(slot, tint);
        themeColorReference = new WorkbookThemeColorReference(slot, tint);
        return true;
    }

    private static bool TryReadThemeColorWithSource(
        XElement? element,
        WorkbookTheme theme,
        out CellColor color,
        out CfColorStopSource? source)
    {
        color = default;
        source = null;
        if (element is null)
            return false;

        var themeText = element.Attribute("theme")?.Value;
        if (string.IsNullOrWhiteSpace(themeText) ||
            !int.TryParse(themeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var themeIndex) ||
            !TryMapThemeColorSlot(themeIndex, out var slot))
        {
            return false;
        }

        var tint = ReadTint(element);
        color = theme.ResolveColor(slot, tint);
        source = new CfColorStopSource(themeIndex, tint);
        return true;
    }

    // OOXML reserves indexed=64 for "System Foreground" (black) and indexed=65 for
    // "System Background" (white); these lie outside the 56-entry standard palette
    // (indices 1-56) that WorkbookIndexedColorPalette resolves, so they must be
    // special-cased rather than forwarded to TryResolveColor.
    private const int SystemForegroundIndexedValue = 64;
    private const int SystemBackgroundIndexedValue = 65;

    // R80-io-theme-styles-5-1: the legacy indexed palette's low fixed range (0=black, 1=white,
    // 2=red, 3=green, 4=blue, 5=yellow, 6=magenta, 7=cyan) is a real, distinct part of the
    // OOXML/BIFF indexed-color model -- it is NOT reachable via the "index - 7" transform below
    // (that transform is only valid for the 8-63 range, where 8-15 duplicate these same eight
    // fixed colors before the 48 customizable "standard colors" begin at 16). Values 0-7 must be
    // resolved directly against their fixed RGB rather than forwarded to TryResolveColor, which
    // would receive a negative index and reject it outright.
    private static readonly CellColor[] LegacyFixedIndexedColors =
    [
        new(0x00, 0x00, 0x00), // 0 black
        new(0xFF, 0xFF, 0xFF), // 1 white
        new(0xFF, 0x00, 0x00), // 2 red
        new(0x00, 0xFF, 0x00), // 3 green
        new(0x00, 0x00, 0xFF), // 4 blue
        new(0xFF, 0xFF, 0x00), // 5 yellow
        new(0xFF, 0x00, 0xFF), // 6 magenta
        new(0x00, 0xFF, 0xFF), // 7 cyan
    ];

    private static bool TryReadIndexedColor(XElement? element, WorkbookIndexedColorPalette indexedColors, out CellColor color)
    {
        color = default;
        if (element is null)
            return false;

        var indexedText = element.Attribute("indexed")?.Value;
        // OOXML indexed colors are zero-based; WorkbookIndexedColorPalette stores Excel ColorIndex values one-based.
        if (string.IsNullOrWhiteSpace(indexedText) ||
            !int.TryParse(indexedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        CellColor indexedColor;
        if (index == SystemForegroundIndexedValue)
        {
            indexedColor = CellColor.Black;
        }
        else if (index == SystemBackgroundIndexedValue)
        {
            indexedColor = CellColor.White;
        }
        else if (index is >= 0 and <= 7)
        {
            indexedColor = LegacyFixedIndexedColors[index];
        }
        else if (!indexedColors.TryResolveColor(index - 7, out indexedColor))
        {
            return false;
        }

        color = WorkbookThemeTint.Apply(indexedColor, ReadTint(element));
        return true;
    }

    private static double ReadTint(XElement element)
    {
        var tintText = element.Attribute("tint")?.Value;
        return !string.IsNullOrWhiteSpace(tintText) &&
            double.TryParse(tintText, NumberStyles.Float, CultureInfo.InvariantCulture, out var tint)
            ? tint
            : 0d;
    }

    private static bool TryMapThemeColorSlot(int themeIndex, out WorkbookThemeColorSlot slot)
    {
        slot = themeIndex switch
        {
            0 => WorkbookThemeColorSlot.Light1,
            1 => WorkbookThemeColorSlot.Dark1,
            2 => WorkbookThemeColorSlot.Light2,
            3 => WorkbookThemeColorSlot.Dark2,
            4 => WorkbookThemeColorSlot.Accent1,
            5 => WorkbookThemeColorSlot.Accent2,
            6 => WorkbookThemeColorSlot.Accent3,
            7 => WorkbookThemeColorSlot.Accent4,
            8 => WorkbookThemeColorSlot.Accent5,
            9 => WorkbookThemeColorSlot.Accent6,
            10 => WorkbookThemeColorSlot.Hyperlink,
            11 => WorkbookThemeColorSlot.FollowedHyperlink,
            _ => default
        };
        return themeIndex is >= 0 and <= 11;
    }

    /// <summary>
    /// Inverse of the theme-index-to-slot mapping above -- the single source of truth for turning a
    /// <see cref="WorkbookThemeColorReference"/> (as preserved on <see cref="CellStyle"/>/<see cref="CellBorder"/>)
    /// back into the raw OOXML &lt;color theme="N"/&gt; index so writers that build &lt;color&gt; XML directly
    /// (not through ClosedXML) can round-trip a theme-referenced color without duplicating this table
    /// (R120-cf-theme-color-1: used by the conditional-format differential-style writer).
    /// </summary>
    public static int ThemeColorIndex(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Light1 => 0,
            WorkbookThemeColorSlot.Dark1 => 1,
            WorkbookThemeColorSlot.Light2 => 2,
            WorkbookThemeColorSlot.Dark2 => 3,
            WorkbookThemeColorSlot.Accent1 => 4,
            WorkbookThemeColorSlot.Accent2 => 5,
            WorkbookThemeColorSlot.Accent3 => 6,
            WorkbookThemeColorSlot.Accent4 => 7,
            WorkbookThemeColorSlot.Accent5 => 8,
            WorkbookThemeColorSlot.Accent6 => 9,
            WorkbookThemeColorSlot.Hyperlink => 10,
            WorkbookThemeColorSlot.FollowedHyperlink => 11,
            _ => 1
        };
}
