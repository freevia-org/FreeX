using System.Xml.Linq;

namespace FreeP.Core.Model;

/// <summary>Mutates the supported native Zoom frame geometry in <c>zmPr/spPr</c>.</summary>
internal static class ZoomFrameGeometryXml
{
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void Set(XElement zoomProperties, string? geometry)
    {
        if (geometry is null)
            return;

        var shapeProperties = zoomProperties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        if (shapeProperties is null)
            return;

        var normalized = geometry.Trim();
        if (normalized.Length == 0)
            return;

        var preset = shapeProperties.Elements(Drawing + "prstGeom").FirstOrDefault();
        if (preset is null)
        {
            preset = new XElement(Drawing + "prstGeom", new XElement(Drawing + "avLst"));
            shapeProperties.AddFirst(preset);
        }

        preset.SetAttributeValue("prst", normalized);
        if (preset.Element(Drawing + "avLst") is null)
            preset.Add(new XElement(Drawing + "avLst"));
    }
}
