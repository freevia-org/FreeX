using System.Text;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Builds native OPC parts for a newly inserted Basic Process diagram.</summary>
internal static class SmartArtInsertionFactory
{
    private static readonly XNamespace Diagram = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace DrawingMl = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static SmartArtShape CreateBasicProcess(int partIndex, IReadOnlyList<string> labels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partIndex);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
            throw new ArgumentException("A SmartArt process requires at least one node.", nameof(labels));

        var ids = labels.Select(_ => $"{{{Guid.NewGuid()}}}").ToArray();
        var smart = new SmartArtShape
        {
            Data = new SmartArtData
            {
                Family = SmartArtFamily.Process,
                LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/process1",
                IsLiveLayoutSupported = true,
            },
        };

        var root = new SmartArtNode { ModelId = ids[0], Text = labels[0], Level = 0 };
        smart.Data.Nodes.Add(root);
        for (var index = 1; index < labels.Count; index++)
            root.Children.Add(new SmartArtNode { ModelId = ids[index], Text = labels[index], Level = 1 });

        var prefix = "ppt/diagrams/";
        var dataPath = $"{prefix}data{partIndex}.xml";
        var layoutPath = $"{prefix}layout{partIndex}.xml";
        var stylePath = $"{prefix}quickStyle{partIndex}.xml";
        var colorsPath = $"{prefix}colors{partIndex}.xml";
        var drawingPath = $"{prefix}drawing{partIndex}.xml";

        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml", dataPath,
            BuildDataXml(labels, ids));
        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml", layoutPath,
            new XDocument(new XElement(Diagram + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute("uniqueId", smart.Data.LayoutUniqueId))));
        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml", stylePath,
            new XDocument(new XElement(Diagram + "styleDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName))));
        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml", colorsPath,
            new XDocument(new XElement(Diagram + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName))));
        AddPart(smart, "application/vnd.ms-office.drawingml.diagramDrawing+xml", drawingPath,
            new XDocument(new XElement(DrawingMl + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", DrawingMl.NamespaceName),
                new XElement(DrawingMl + "spTree"))));

        smart.DiagramRelIds["dm"] = $"rIdDm{partIndex}";
        smart.DiagramRelIds["lo"] = $"rIdLo{partIndex}";
        smart.DiagramRelIds["qs"] = $"rIdQs{partIndex}";
        smart.DiagramRelIds["cs"] = $"rIdCs{partIndex}";
        smart.DrawingPartPath = drawingPath;
        smart.PartRels[dataPath] = SerializeRelationships((
            $"rIdDrawing{partIndex}",
            "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing",
            $"drawing{partIndex}.xml"));

        return smart;
    }

    private static XDocument BuildDataXml(IReadOnlyList<string> labels, IReadOnlyList<string> ids)
    {
        var points = labels.Select((label, index) => new XElement(Diagram + "pt",
            new XAttribute("modelId", ids[index]), new XAttribute("type", "node"),
            new XElement(Diagram + "t", new XElement(Drawing + "p",
                new XElement(Drawing + "r", new XElement(Drawing + "rPr", new XAttribute("lang", "en-US")),
                    new XElement(Drawing + "t", label)))))).ToArray();

        var connections = Enumerable.Range(1, labels.Count - 1).Select(index => new XElement(Diagram + "cxn",
            new XAttribute("type", "parOf"), new XAttribute("srcId", ids[0]),
            new XAttribute("destId", ids[index]))).ToArray();

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Diagram + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName),
                new XElement(Diagram + "ptLst", points), new XElement(Diagram + "cxnLst", connections)));
    }

    private static void AddPart(SmartArtShape smart, string contentType, string path, XDocument document) =>
        smart.Parts[path] = new DiagramPart
        {
            ContentType = contentType,
            PartPath = path,
            Bytes = Serialize(document),
        };

    private static byte[] Serialize(XDocument document) =>
        Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));

    private static byte[] SerializeRelationships(params (string id, string type, string target)[] relationships) =>
        Serialize(new XDocument(new XElement(Package + "Relationships", relationships.Select(relationship =>
            new XElement(Package + "Relationship", new XAttribute("Id", relationship.id),
                new XAttribute("Type", relationship.type), new XAttribute("Target", relationship.target))))));
}
