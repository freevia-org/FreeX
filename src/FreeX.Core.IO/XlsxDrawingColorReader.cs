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

    /// <summary>
    /// R91-render-chart-series-format-5-4: reads the <c>&lt;a:alpha&gt;</c> transparency child of
    /// whichever concrete color element (<c>&lt;a:srgbClr&gt;</c> or <c>&lt;a:schemeClr&gt;</c>) is
    /// present directly under a <c>&lt;a:solidFill&gt;</c>, as a 0..1 opacity fraction. Returns null
    /// when no color element or no <c>&lt;a:alpha&gt;</c> is present (fully opaque, the implicit
    /// default).
    /// </summary>
    public static double? TryReadFillAlpha(XElement solidFillElement, XNamespace drawingNs)
    {
        var colorElement = solidFillElement.Element(drawingNs + "srgbClr")
            ?? solidFillElement.Element(drawingNs + "schemeClr");
        return colorElement is null ? null : XlsxDrawingColorAlpha.ReadFrom(colorElement, drawingNs);
    }

    private static bool TryMapSchemeColor(string? value, out WorkbookThemeColorSlot slot)
        => XlsxDrawingThemeColorSlots.TryMapRole(value, out slot);
}
