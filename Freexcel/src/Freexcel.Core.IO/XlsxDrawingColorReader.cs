using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static class XlsxDrawingColorReader
{
    public static bool TryReadThemeColorReference(
        XElement solidFillElement,
        XNamespace drawingNs,
        out WorkbookThemeColorReference reference)
    {
        reference = default;
        var schemeColor = solidFillElement.Element(drawingNs + "schemeClr");
        var value = schemeColor?.Attribute("val")?.Value;
        if (!TryMapSchemeColor(value, out var slot))
            return false;

        reference = new WorkbookThemeColorReference(slot, ReadTint(schemeColor!, drawingNs));
        return true;
    }

    public static bool TryReadConcreteColor(
        XElement solidFillElement,
        XNamespace drawingNs,
        out CellColor color)
    {
        color = default;
        var value = solidFillElement.Element(drawingNs + "srgbClr")?.Attribute("val")?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('#');
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    private static bool TryMapSchemeColor(string? value, out WorkbookThemeColorSlot slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        slot = value.Trim().ToLowerInvariant() switch
        {
            "dk1" or "tx1" => WorkbookThemeColorSlot.Dark1,
            "lt1" or "bg1" => WorkbookThemeColorSlot.Light1,
            "dk2" or "tx2" => WorkbookThemeColorSlot.Dark2,
            "lt2" or "bg2" => WorkbookThemeColorSlot.Light2,
            "accent1" => WorkbookThemeColorSlot.Accent1,
            "accent2" => WorkbookThemeColorSlot.Accent2,
            "accent3" => WorkbookThemeColorSlot.Accent3,
            "accent4" => WorkbookThemeColorSlot.Accent4,
            "accent5" => WorkbookThemeColorSlot.Accent5,
            "accent6" => WorkbookThemeColorSlot.Accent6,
            "hlink" => WorkbookThemeColorSlot.Hyperlink,
            "folhlink" => WorkbookThemeColorSlot.FollowedHyperlink,
            _ => default
        };

        return value.Trim().ToLowerInvariant() is
            "dk1" or "tx1" or
            "lt1" or "bg1" or
            "dk2" or "tx2" or
            "lt2" or "bg2" or
            "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or
            "hlink" or "folhlink";
    }

    private static double ReadTint(XElement schemeColor, XNamespace drawingNs)
    {
        var lumMod = ReadPercentage(schemeColor.Element(drawingNs + "lumMod")?.Attribute("val")?.Value);
        var lumOff = ReadPercentage(schemeColor.Element(drawingNs + "lumOff")?.Attribute("val")?.Value);

        if (lumOff > 0)
            return Math.Round(lumOff.Value, 6);
        if (lumMod is > 0 and < 1)
            return Math.Round(lumMod.Value - 1, 6);
        return 0;
    }

    private static double? ReadPercentage(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            ? Math.Clamp(integer / 100000.0, 0, 1)
            : null;
}
