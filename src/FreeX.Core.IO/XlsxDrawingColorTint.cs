using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDrawingColorTint
{
    private const int PercentageScale = 100000;

    public static void ApplyTo(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", ToPercentage(1 - tint))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", ToPercentage(tint))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(
                drawingNs + "lumMod",
                new XAttribute("val", ToPercentage(1 + tint))));
        }
    }

    public static double ReadFrom(XElement schemeColor, XNamespace drawingNs)
    {
        var lumMod = ReadPercentage(schemeColor.Element(drawingNs + "lumMod")?.Attribute("val")?.Value);
        var lumOff = ReadPercentage(schemeColor.Element(drawingNs + "lumOff")?.Attribute("val")?.Value);

        if (lumOff > 0)
            return Math.Round(lumOff.Value, 6);
        if (lumMod is > 0 and < 1)
            return Math.Round(lumMod.Value - 1, 6);

        // DrawingML <a:tint>/<a:shade> luminance modulation. Excel uses these on chart data-point
        // and series fills where lumMod/lumOff are absent.
        var tint = ReadPercentage(schemeColor.Element(drawingNs + "tint")?.Attribute("val")?.Value);
        if (tint is > 0 and < 1)
            return Math.Round(1 - tint.Value, 6);
        var shade = ReadPercentage(schemeColor.Element(drawingNs + "shade")?.Attribute("val")?.Value);
        if (shade is > 0 and < 1)
            return Math.Round(-(1 - shade.Value), 6);

        return 0;
    }

    private static int ToPercentage(double value) =>
        Math.Clamp((int)Math.Round(value * PercentageScale), 0, PercentageScale);

    private static double? ReadPercentage(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            ? Math.Clamp(integer / (double)PercentageScale, 0, 1)
            : null;
}
