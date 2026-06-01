using FreeX.Core.Model;
using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

public static class XlsxColorReader
{
    public static bool TryParseHexColor(string? text, out CellColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().TrimStart('#');
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
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

    public static bool TryReadCellColor(XElement? element, out CellColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        return TryParseHexColor(NormalizeRgbAttribute(rgb), out color);
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

    private static bool TryReadIndexedColor(XElement? element, WorkbookIndexedColorPalette indexedColors, out CellColor color)
    {
        color = default;
        if (element is null)
            return false;

        var indexedText = element.Attribute("indexed")?.Value;
        // OOXML indexed colors are zero-based; WorkbookIndexedColorPalette stores Excel ColorIndex values one-based.
        if (string.IsNullOrWhiteSpace(indexedText) ||
            !int.TryParse(indexedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
            !indexedColors.TryResolveColor(index + 1, out var indexedColor))
        {
            return false;
        }

        color = ApplyTint(indexedColor, ReadTint(element));
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

    private static CellColor ApplyTint(CellColor color, double tint)
    {
        if (Math.Abs(tint) < 0.000001)
            return color;

        return new CellColor(
            ApplyTint(color.R, tint),
            ApplyTint(color.G, tint),
            ApplyTint(color.B, tint));
    }

    private static byte ApplyTint(byte channel, double tint)
    {
        var value = tint < 0
            ? channel * (1.0 + tint)
            : channel + ((255 - channel) * tint);
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
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
