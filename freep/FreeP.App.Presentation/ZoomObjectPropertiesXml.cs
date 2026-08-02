using System.Xml.Linq;

namespace FreeP.App.Compositor;

/// <summary>
/// Creates the required Office 2016 zoom-object properties payload.
/// The preview image is intentionally left to PowerPoint; the shape remains valid and
/// slideshow navigation uses the serialized target metadata.
/// </summary>
internal static class ZoomObjectPropertiesXml
{
    internal static XElement Build(XNamespace p166, XNamespace a)
    {
        return new XElement(p166 + "zmPr",
            new XAttribute("id", Guid.NewGuid().ToString("B").ToUpperInvariant()),
            new XAttribute("returnToParent", "1"),
            new XAttribute("imageType", "preview"),
            new XAttribute("showBg", "1"),
            new XElement(p166 + "blipFill",
                new XElement(a + "stretch",
                    new XElement(a + "fillRect"))),
            new XElement(p166 + "spPr",
                new XElement(a + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(a + "avLst"))));
    }
}
