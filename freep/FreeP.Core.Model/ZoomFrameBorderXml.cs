using System.Xml.Linq;

namespace FreeP.Core.Model;

/// <summary>Mutates supported outline color/width/dash inside native Zoom <c>zmPr/spPr</c>.</summary>
internal static class ZoomFrameBorderXml
{
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void Set(
        XElement zoomProperties,
        string? color,
        int? widthEmu,
        OutlineDash? dash)
    {
        // Null means the model did not understand the native line; preserve it verbatim.
        if (color is null && widthEmu is null && dash is null)
            return;

        var shapeProperties = zoomProperties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        if (shapeProperties is null)
            return;

        var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
        if (line is null && (widthEmu is not null || dash is not null))
        {
            line = new XElement(Drawing + "ln");
            shapeProperties.Add(line);
        }
        if (line is not null && widthEmu is not null)
            line.SetAttributeValue("w", widthEmu.Value);

        if (line is not null && dash is OutlineDash dashValue)
        {
            line.Elements(Drawing + "prstDash").Remove();
            if (dashValue != OutlineDash.Solid)
                line.Add(new XElement(Drawing + "prstDash",
                    new XAttribute("val", ToDashToken(dashValue))));
        }

        var solidFill = line?.Elements(Drawing + "solidFill").FirstOrDefault();
        if (color is { Length: 0 })
        {
            var rgb = solidFill?.Elements(Drawing + "srgbClr").FirstOrDefault();
            if (rgb is null)
                return;

            solidFill!.Remove();
            if (line!.Attributes().Count() == 0 && !line.Elements().Any())
                line.Remove();
            return;
        }

        if (color is null)
            return;

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

    private static string ToDashToken(OutlineDash dash) => dash switch
    {
        OutlineDash.Dash => "dash",
        OutlineDash.Dot => "dot",
        OutlineDash.DashDot => "dashDot",
        OutlineDash.LongDash => "lgDash",
        OutlineDash.LongDashDot => "lgDashDot",
        OutlineDash.LongDashDotDot => "lgDashDotDot",
        OutlineDash.SystemDash => "sysDash",
        OutlineDash.SystemDot => "sysDot",
        OutlineDash.SystemDashDot => "sysDashDot",
        _ => "solid",
    };
}
