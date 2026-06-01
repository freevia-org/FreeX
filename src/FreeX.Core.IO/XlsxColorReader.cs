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

        var tint = 0d;
        var tintText = element.Attribute("tint")?.Value;
        if (!string.IsNullOrWhiteSpace(tintText))
            double.TryParse(tintText, NumberStyles.Float, CultureInfo.InvariantCulture, out tint);

        color = theme.ResolveColor(slot, tint);
        return true;
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
