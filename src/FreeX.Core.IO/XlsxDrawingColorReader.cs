using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

        reference = new WorkbookThemeColorReference(slot, XlsxDrawingColorTint.ReadFrom(schemeColor!, drawingNs));
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

        return XlsxColorReader.TryParseHexColor(value, out color);
    }

    private static bool TryMapSchemeColor(string? value, out WorkbookThemeColorSlot slot)
        => XlsxDrawingThemeColorSlots.TryMapRole(value, out slot);
}
