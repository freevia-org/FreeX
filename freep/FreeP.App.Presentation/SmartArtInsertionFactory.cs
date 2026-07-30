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

    public static SmartArtShape CreateBasicProcess(int partIndex, IReadOnlyList<string> labels) =>
        Create(SmartArtLayoutPreset.BasicProcess, partIndex, labels);

    public static SmartArtShape Create(
        SmartArtLayoutPreset preset,
        int partIndex,
        IReadOnlyList<string> labels,
        IReadOnlyList<SlideObjectPicturePayload>? pictures = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partIndex);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
            throw new ArgumentException("A SmartArt process requires at least one node.", nameof(labels));

        var ids = labels.Select((_, index) => (index + 1).ToString()).ToArray();
        var nodePictures = NormalizePictures(preset, labels.Count, pictures);
        var (layoutId, family) = GetLayoutDefinition(preset);
        var smart = new SmartArtShape
        {
            Data = new SmartArtData
            {
                Family = family,
                LayoutUniqueId = layoutId,
                IsLiveLayoutSupported = true,
            },
        };

        var root = new SmartArtNode { ModelId = ids[0], Text = labels[0], Level = 0 };
        if (nodePictures is not null)
            root.Picture = ToImagePart(nodePictures[0]);
        smart.Data.Nodes.Add(root);
        for (var index = 1; index < labels.Count; index++)
        {
            var node = new SmartArtNode { ModelId = ids[index], Text = labels[index], Level = 1 };
            if (nodePictures is not null)
                node.Picture = ToImagePart(nodePictures[index]);
            root.Children.Add(node);
        }

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
                new XAttribute("uniqueId", smart.Data.LayoutUniqueId),
                new XElement(Diagram + "title", new XAttribute("val", "")),
                new XElement(Diagram + "desc", new XAttribute("val", "")),
                new XElement(Diagram + "catLst",
                    new XElement(Diagram + "cat", new XAttribute("type", "list"), new XAttribute("pri", "1000"))),
                new XElement(Diagram + "sampData",
                    new XElement(Diagram + "dataModel",
                        new XElement(Diagram + "ptLst"),
                        new XElement(Diagram + "bg"),
                        new XElement(Diagram + "whole"))),
                new XElement(Diagram + "styleData",
                    new XElement(Diagram + "dataModel",
                        new XElement(Diagram + "ptLst"),
                        new XElement(Diagram + "bg"),
                        new XElement(Diagram + "whole"))),
                new XElement(Diagram + "clrData",
                    new XElement(Diagram + "dataModel",
                        new XElement(Diagram + "ptLst"),
                        new XElement(Diagram + "bg"),
                        new XElement(Diagram + "whole"))),
                new XElement(Diagram + "layoutNode",
                    new XAttribute("name", "root"),
                    new XElement(Diagram + "alg", new XAttribute("type", "lin")),
                    new XElement(Diagram + "shape", new XElement(Diagram + "adjLst")),
                    new XElement(Diagram + "presOf"),
                    new XElement(Diagram + "constrLst"),
                    new XElement(Diagram + "ruleLst")))));
        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml", stylePath,
            new XDocument(new XElement(Diagram + "styleDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute("uniqueId", "urn:freep:smartart:style:default"),
                new XElement(Diagram + "title", new XAttribute("val", "Default")),
                new XElement(Diagram + "catLst"),
                new XElement(Diagram + "styleLbl", new XAttribute("name", "node0")))));
        AddPart(smart, "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml", colorsPath,
            new XDocument(new XElement(Diagram + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName),
                new XAttribute("uniqueId", "urn:freep:smartart:colors:default"),
                new XElement(Diagram + "styleLbl", new XAttribute("name", "node0")))));
        AddPart(smart, "application/vnd.ms-office.drawingml.diagramDrawing+xml", drawingPath,
            BuildDrawingXml(nodePictures));

        if (nodePictures is not null)
        {
            var drawingRelationships = new List<(string id, string type, string target)>();
            for (var index = 0; index < nodePictures.Count; index++)
            {
                var image = nodePictures[index];
                var extension = GetImageExtension(image.ContentType);
                var mediaPath = $"ppt/media/smartart{partIndex}_picture{index + 1}.{extension}";
                smart.Parts[mediaPath] = new DiagramPart
                {
                    ContentType = image.ContentType,
                    PartPath = mediaPath,
                    Bytes = image.Bytes.ToArray(),
                };
                drawingRelationships.Add((
                    $"rIdPic{index + 1}",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                    $"../media/smartart{partIndex}_picture{index + 1}.{extension}"));
            }

            smart.PartRels[drawingPath] = SerializeRelationships(drawingRelationships.ToArray());
        }

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

    private static IReadOnlyList<SlideObjectPicturePayload>? NormalizePictures(
        SmartArtLayoutPreset preset,
        int nodeCount,
        IReadOnlyList<SlideObjectPicturePayload>? pictures)
    {
        if (preset is not (SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid))
            return null;

        if (pictures is null || pictures.Count == 0)
            return null;

        if (pictures.Count == nodeCount)
            return pictures;

        if (pictures.Count == 1)
            return Enumerable.Repeat(pictures[0], nodeCount).ToArray();

        throw new ArgumentException(
            $"Picture SmartArt layouts require one image or exactly {nodeCount} images.",
            nameof(pictures));
    }

    private static ImagePart ToImagePart(SlideObjectPicturePayload payload) => new()
    {
        Bytes = payload.Bytes.ToArray(),
        ContentType = payload.ContentType,
    };

    private static XDocument BuildDrawingXml(IReadOnlyList<SlideObjectPicturePayload>? pictures)
    {
        var drawing = new XElement(DrawingMl + "drawing",
            new XAttribute(XNamespace.Xmlns + "dsp", DrawingMl.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
            new XElement(DrawingMl + "spTree",
                new XElement(DrawingMl + "nvGrpSpPr",
                    new XElement(DrawingMl + "cNvPr",
                        new XAttribute("id", "1"), new XAttribute("name", "SmartArt Picture Layout")),
                    new XElement(DrawingMl + "cNvGrpSpPr")),
                new XElement(DrawingMl + "grpSpPr",
                    new XElement(Drawing + "xfrm",
                        new XElement(Drawing + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(Drawing + "ext", new XAttribute("cx", "1"), new XAttribute("cy", "1")),
                        new XElement(Drawing + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(Drawing + "chExt", new XAttribute("cx", "1"), new XAttribute("cy", "1"))))));

        if (pictures is not null)
        {
            var tree = drawing.Element(DrawingMl + "spTree")!;
            for (var index = 0; index < pictures.Count; index++)
            {
                tree.Add(new XElement(DrawingMl + "sp",
                    new XAttribute("modelId", (index + 1).ToString()),
                    new XElement(DrawingMl + "nvSpPr",
                        new XElement(DrawingMl + "cNvPr",
                            new XAttribute("id", (index + 1).ToString()),
                            new XAttribute("name", $"Picture {index + 1}")),
                        new XElement(DrawingMl + "cNvSpPr")),
                    new XElement(DrawingMl + "spPr",
                        new XElement(Drawing + "xfrm",
                            new XElement(Drawing + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(Drawing + "ext", new XAttribute("cx", "1"), new XAttribute("cy", "1"))),
                        new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst")),
                        new XElement(Drawing + "blipFill",
                            new XElement(Drawing + "blip",
                                new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "embed",
                                    $"rIdPic{index + 1}")),
                            new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))))));
            }
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), drawing);
    }

    private static string GetImageExtension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            _ => "png",
        };

    private static (string layoutId, SmartArtFamily family) GetLayoutDefinition(SmartArtLayoutPreset preset) =>
        preset switch
        {
            SmartArtLayoutPreset.BasicProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/process1", SmartArtFamily.Process),
            SmartArtLayoutPreset.AccentProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/accentProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.AscendingProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/ascendingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.DescendingProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/descendingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BasicTimeline => ("urn:microsoft.com/office/officeart/2005/8/layout/basicTimeline", SmartArtFamily.Process),
            SmartArtLayoutPreset.PhasedProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/phasedProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.CircleAccentTimeline => ("urn:microsoft.com/office/officeart/2005/8/layout/circleAccentTimeline", SmartArtFamily.Process),
            SmartArtLayoutPreset.StepDownProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/StepDownProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ContinuousBlockProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/continuousBlockProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.SegmentedProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ChevronProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BasicChevronProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ClosedChevronProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BendingProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.AlternatingProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ArrowRibbon => ("urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon", SmartArtFamily.Process),
            SmartArtLayoutPreset.CircleProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/circleProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.FunnelProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.VerticalProcess => ("urn:microsoft.com/office/officeart/2005/8/layout/verticalProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.VerticalBoxList => ("urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList", SmartArtFamily.List),
            SmartArtLayoutPreset.VerticalChevronList => ("urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList", SmartArtFamily.List),
            SmartArtLayoutPreset.VerticalArrowList => ("urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList", SmartArtFamily.List),
            SmartArtLayoutPreset.VerticalBulletList => ("urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalBulletList => ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalBulletList", SmartArtFamily.List),
            SmartArtLayoutPreset.HorizontalBlockList => ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.TrapezoidList => ("urn:microsoft.com/office/officeart/2005/8/layout/trapezoidList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/basicCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.Cycle2 => ("urn:microsoft.com/office/officeart/2005/8/layout/cycle2", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.ContinuousCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/continuousCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.GearCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/gearCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.TextCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/textCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BlockCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/blockCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.NonDirectionalCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicList => ("urn:microsoft.com/office/officeart/2005/8/layout/list1", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicBlockList => ("urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.StackedList => ("urn:microsoft.com/office/officeart/2005/8/layout/stackedList", SmartArtFamily.List),
            SmartArtLayoutPreset.DescendingBlockList => ("urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicPyramid => ("urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid", SmartArtFamily.List),
            SmartArtLayoutPreset.PyramidList => ("urn:microsoft.com/office/officeart/2005/8/layout/pyramidList", SmartArtFamily.List),
            SmartArtLayoutPreset.InvertedPyramid => ("urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid", SmartArtFamily.List),
            SmartArtLayoutPreset.RadialCycle => ("urn:microsoft.com/office/officeart/2005/8/layout/radialCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicRadial => ("urn:microsoft.com/office/officeart/2005/8/layout/radial1", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.RadialList => ("urn:microsoft.com/office/officeart/2005/8/layout/radialList", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicMatrix => ("urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.TitledMatrix => ("urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.GridMatrix => ("urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.BasicRelationship => ("urn:microsoft.com/office/officeart/2005/8/layout/relationship1", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.OpposingIdeas => ("urn:microsoft.com/office/officeart/2005/8/layout/opposingIdeas", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.ConvergingRadial => ("urn:microsoft.com/office/officeart/2005/8/layout/convergingRadial", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.BasicVenn => ("urn:microsoft.com/office/officeart/2005/8/layout/basicVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.RadialVenn => ("urn:microsoft.com/office/officeart/2005/8/layout/radialVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.TargetList => ("urn:microsoft.com/office/officeart/2005/8/layout/targetList", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.StackedVenn => ("urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.InterlockingRings => ("urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.BasicHierarchy => ("urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.Hierarchy3 => ("urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalHierarchy => ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.OrgChart => ("urn:microsoft.com/office/officeart/2005/8/layout/orgChart", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.NameAndTitleOrgChart => ("urn:microsoft.com/office/officeart/2005/8/layout/nameAndTitleOrgChart", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.PictureCaptionList => ("urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureAccentList => ("urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentList", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureStack => ("urn:microsoft.com/office/officeart/2005/8/layout/pictureStack", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureLineup => ("urn:microsoft.com/office/officeart/2005/8/layout/pictureLineup", SmartArtFamily.List),
            SmartArtLayoutPreset.ContinuousPictureList => ("urn:microsoft.com/office/officeart/2005/8/layout/continuousPictureList", SmartArtFamily.List),
            SmartArtLayoutPreset.LabeledHierarchy => ("urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.TableHierarchy => ("urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.PictureGrid => ("urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid", SmartArtFamily.List),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

    private static XDocument BuildDataXml(IReadOnlyList<string> labels, IReadOnlyList<string> ids)
    {
        var points = labels.Select((label, index) => new XElement(Diagram + "pt",
            new XAttribute("modelId", ids[index]), new XAttribute("type", "node"),
            new XElement(Diagram + "t", new XElement(Drawing + "bodyPr"),
                new XElement(Drawing + "lstStyle"), new XElement(Drawing + "p",
                new XElement(Drawing + "r", new XElement(Drawing + "rPr", new XAttribute("lang", "en-US")),
                    new XElement(Drawing + "t", label)))))).ToArray();

        var connections = Enumerable.Range(1, labels.Count - 1).Select(index => new XElement(Diagram + "cxn",
            new XAttribute("modelId", (labels.Count + index).ToString()),
            new XAttribute("type", "parOf"), new XAttribute("srcId", ids[0]),
            new XAttribute("destId", ids[index]), new XAttribute("srcOrd", index - 1),
            new XAttribute("destOrd", 0))).ToArray();

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
