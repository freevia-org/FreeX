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
        else if (!indexedColors.TryResolveColor(index + 1, out indexedColor))
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
}
