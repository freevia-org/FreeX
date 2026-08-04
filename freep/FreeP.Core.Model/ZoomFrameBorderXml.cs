using System.Xml.Linq;

namespace FreeP.Core.Model;

/// <summary>Mutates only the supported solid RGB outline inside native Zoom <c>zmPr/spPr</c>.</summary>
internal static class ZoomFrameBorderXml
{
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void Set(XElement zoomProperties, string? color)
    {
        // Null means the model did not understand the native line; preserve it verbatim.
        if (color is null)
            return;

        var shapeProperties = zoomProperties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        if (shapeProperties is null)
            return;

        var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
        var solidFill = line?.Elements(Drawing + "solidFill").FirstOrDefault();
        if (color.Length == 0)
        {
            var rgb = solidFill?.Elements(Drawing + "srgbClr").FirstOrDefault();
            if (rgb is null)
                return;

            solidFill!.Remove();
            if (line!.Attributes().Count() == 0 && !line.Elements().Any())
                line.Remove();
            return;
        }

        line ??= new XElement(Drawing + "ln");
        foreach (var fill in line.Elements().Where(element =>
                     element.Name == Drawing + "solidFill"
                     || element.Name == Drawing + "gradFill"
                     || element.Name == Drawing + "pattFill"
                     || element.Name == Drawing + "noFill").ToArray())
            fill.Remove();
        line.AddFirst(new XElement(Drawing + "solidFill",
            new XElement(Drawing + "srgbClr", new XAttribute("val", color))));
        if (line.Parent is null)
            shapeProperties.Add(line);
    }
}
