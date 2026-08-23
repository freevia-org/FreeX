using System.Xml.Linq;

namespace FreeP.App.Compositor;

/// <summary>Creates native SmartArt package parts shared by insertion and authoring flows.</summary>
public static class SmartArtNativePartFactory
{
    private static readonly XNamespace Diagram =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    public static XDocument CreateLayoutDefinition(string uniqueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);

        return new(new XElement(
            Diagram + "layoutDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
            new XAttribute("uniqueId", uniqueId),
            new XElement(Diagram + "title", new XAttribute("val", "")),
            new XElement(Diagram + "desc", new XAttribute("val", "")),
            new XElement(Diagram + "catLst",
                new XElement(Diagram + "cat", new XAttribute("type", "list"), new XAttribute("pri", "1000"))),
            CreateDataContainer("sampData"),
            CreateDataContainer("styleData"),
            CreateDataContainer("clrData"),
            new XElement(Diagram + "layoutNode",
                new XAttribute("name", "root"),
                new XElement(Diagram + "alg", new XAttribute("type", "lin")),
                new XElement(Diagram + "shape", new XElement(Diagram + "adjLst")),
                new XElement(Diagram + "presOf"),
                new XElement(Diagram + "constrLst"),
                new XElement(Diagram + "ruleLst"))));
    }

    private static XElement CreateDataContainer(string name) =>
        new(Diagram + name,
            new XElement(Diagram + "dataModel",
                new XElement(Diagram + "ptLst"),
                new XElement(Diagram + "bg"),
                new XElement(Diagram + "whole")));
}
