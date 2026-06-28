using System.Xml.Linq;
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
            ApplyTint(colorElement, theme.Tint, drawingNs);
            return colorElement;
        }

        return color is { } concrete
            ? ToRgbColorElement(concrete, drawingNs)
            : null;
    }

    public static XElement ToRgbColorElement(CellColor color, XNamespace drawingNs) =>
        new(drawingNs + "srgbClr", new XAttribute("val", FormatRgb(color)));

    public static string FormatRgb(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static string ToSchemeColorValue(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };
}
