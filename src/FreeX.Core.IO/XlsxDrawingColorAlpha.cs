using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// R91-render-chart-series-format-5-4: reads/writes the DrawingML <c>&lt;a:alpha&gt;</c>
/// transparency child of a concrete color element (<c>&lt;a:srgbClr&gt;</c>/<c>&lt;a:schemeClr&gt;</c>),
/// mirroring the tint sibling helper <see cref="XlsxDrawingColorTint"/>. The value is modeled as a
/// 0..1 opacity fraction (1 = fully opaque = no authored <c>&lt;a:alpha&gt;</c>).
/// </summary>
internal static class XlsxDrawingColorAlpha
{
    private const int PercentageScale = 100000;

    /// <summary>Adds an <c>&lt;a:alpha&gt;</c> child when <paramref name="opacity"/> is less than fully opaque.</summary>
    public static void ApplyTo(XElement colorElement, double opacity, XNamespace drawingNs)
    {
        if (opacity < 1)
        {
            var percentage = Math.Clamp((int)Math.Round(opacity * PercentageScale), 0, PercentageScale);
            colorElement.Add(new XElement(drawingNs + "alpha", new XAttribute("val", percentage)));
        }
    }

    /// <summary>Reads the <c>&lt;a:alpha&gt;</c> child as a 0..1 opacity fraction, or null when absent.</summary>
    public static double? ReadFrom(XElement colorElement, XNamespace drawingNs)
    {
        var value = colorElement.Element(drawingNs + "alpha")?.Attribute("val")?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            ? Math.Clamp(integer / (double)PercentageScale, 0, 1)
            : null;
    }
}
