using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDrawingColorWriter
{
    public static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        var colorElement = ToColorElement(themeColor, color, drawingNs);
        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    public static XElement? ToColorElement(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        if (themeColor is { } theme)
        {
            var colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToSchemeColorValue(theme.Slot)));
            XlsxDrawingColorTint.ApplyTo(colorElement, theme.Tint, drawingNs);
            return colorElement;
        }

        return color is { } concrete
            ? ToRgbColorElement(concrete, drawingNs)
            : null;
    }

    public static XElement ToRgbColorElement(CellColor color, XNamespace drawingNs) =>
        new(drawingNs + "srgbClr", new XAttribute("val", FormatRgb(color)));

    public static string FormatRgb(CellColor color) =>
        new DrawingMlRgbColor(color.R, color.G, color.B).ToHexRgb();

    private static string ToSchemeColorValue(WorkbookThemeColorSlot slot) =>
        XlsxDrawingThemeColorSlots.ToSchemeColorValue(slot);
}
