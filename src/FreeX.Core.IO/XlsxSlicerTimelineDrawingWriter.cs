using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R83-io-slicer-timeline-5-1: authors the <c>xl/drawings/*.xml</c> mc:AlternateContent -&gt; mc:Choice
/// -&gt; graphicFrame anchor a slicer/timeline needs to be visible AT ALL. Excel (and FreeX's own
/// <see cref="XlsxSlicerTimelineMetadataReader"/>) locate a slicer/timeline's on-sheet shape
/// EXCLUSIVELY via this graphicFrame, keyed by control name (<c>&lt;sle:slicer name=".."/&gt;</c> /
/// <c>&lt;tsle:timeslicer name=".."/&gt;</c>) -- never via the slicer/slicerCache package relationship
/// graph that <see cref="XlsxSlicerTimelineWriter"/> and <see cref="XlsxSlicerTimelineStateRewriter"/>
/// otherwise author. Without this, a brand-new slicer/timeline round-trips its data/selection state but
/// has no shape at all after one save+reload -- it silently vanishes from the sheet.
/// <para>
/// Shared by both <see cref="XlsxSlicerTimelineWriter"/>'s fresh-save (no-source-package) path and
/// <see cref="XlsxSlicerTimelineStateRewriter"/>'s append-new-control (source-preserved) path, so a
/// control authored on EITHER path gets an anchor. Reuses the host worksheet's EXISTING drawing part
/// (appending alongside any chart/picture/shape anchors already written there earlier in the same save)
/// when one exists, or authors a brand-new minimal drawing part + worksheet relationship + <c>&lt;drawing
/// r:id=".."/&gt;</c> element otherwise. A strict no-op when the model carries no
/// <see cref="SlicerModel.DrawingAnchor"/>/<see cref="TimelineModel.DrawingAnchor"/> (never regresses a
/// control FreeX cannot yet place) or when an anchor for this control name already exists in the target
/// drawing part (idempotent re-save).
/// </para>
/// </summary>
internal static class XlsxSlicerTimelineDrawingWriter
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Excel's own drawingML namespaces for the slicer/timeline mc:Choice link element.
    private static readonly XNamespace SlicerLinkNs = "http://schemas.microsoft.com/office/drawing/2010/slicer";
    private static readonly XNamespace TimelineLinkNs = "http://schemas.microsoft.com/office/drawing/2012/timeslicer";

    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string DrawingContentType = "application/vnd.openxmlformats-officedocument.drawing+xml";

    public static void EnsureSlicerAnchor(ZipArchive archive, string? worksheetPath, SlicerModel slicer) =>
        EnsureAnchor(archive, worksheetPath, slicer.Name, slicer.DrawingShapeName, slicer.DrawingAnchor, isTimeline: false);

    public static void EnsureTimelineAnchor(ZipArchive archive, string? worksheetPath, TimelineModel timeline) =>
        EnsureAnchor(archive, worksheetPath, timeline.Name, timeline.DrawingShapeName, timeline.DrawingAnchor, isTimeline: true);

    private static void EnsureAnchor(
        ZipArchive archive,
        string? worksheetPath,
        string controlName,
        string? shapeName,
        DrawingAnchorRange? anchorRange,
        bool isTimeline)
    {
        if (string.IsNullOrWhiteSpace(worksheetPath) || anchorRange is null || string.IsNullOrWhiteSpace(controlName))
            return;

        if (archive.GetEntry(worksheetPath) is null)
            return;

        var (drawingPath, drawingXml, isNewDrawingPart) = ResolveOrCreateDrawingPart(archive, worksheetPath);
        var root = drawingXml.Root;
        if (root is null)
            return;

        // Idempotent: an anchor already linked to this control name (by the same name-keyed association
        // the reader uses) is left untouched so a repeat save of an already-anchored control never
        // duplicates its shape.
        var alreadyAnchored = root.Descendants().Any(element =>
            (HasLocalName(element, "slicer") || HasLocalName(element, "timeline") || HasLocalName(element, "timeslicer")) &&
            string.Equals(element.Attribute("name")?.Value, controlName, StringComparison.OrdinalIgnoreCase));
        if (alreadyAnchored)
            return;

        var nextId = root.Descendants()
            .Where(HasLocalNameCNvPr)
            .Select(ParseShapeId)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var resolvedShapeName = string.IsNullOrWhiteSpace(shapeName) ? controlName : shapeName;
        root.Add(BuildAnchor(controlName, resolvedShapeName, anchorRange, isTimeline, nextId));
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{drawingPath}", DrawingContentType);

        if (isNewDrawingPart)
            WireWorksheetDrawingRelationship(archive, worksheetPath, drawingPath);
    }

    private static bool HasLocalNameCNvPr(XElement element) => HasLocalName(element, "cNvPr");

    private static int ParseShapeId(XElement element) =>
        int.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static (string Path, XDocument Xml, bool IsNew) ResolveOrCreateDrawingPart(ZipArchive archive, string worksheetPath)
    {
        var existingPath = FindExistingWorksheetDrawingPath(archive, worksheetPath);
        if (existingPath is not null && archive.GetEntry(existingPath) is { } existingEntry)
            return (existingPath, XlsxPackageXmlEditor.LoadXml(existingEntry), false);

        var freshPath = AllocateFreshDrawingPath(archive);
        var freshXml = new XDocument(new XElement(SpreadsheetDrawingNs + "wsDr",
            new XAttribute(XNamespace.Xmlns + "xdr", SpreadsheetDrawingNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", DrawingNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", RelNs.NamespaceName)));
        return (freshPath, freshXml, true);
    }

    // Resolves the drawing part the worksheet's OWN <drawing r:id=".."/> element already targets (a
    // chart/picture/shape writer that ran earlier in this same save may have created it), so a
    // slicer/timeline anchor lands alongside any existing drawing content on that sheet instead of a
    // second, competing drawing part that Excel would only render one of.
    private static string? FindExistingWorksheetDrawingPath(ZipArchive archive, string worksheetPath)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return null;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var drawingRelId = worksheetXml.Root?.Element(WorksheetNs + "drawing")?.Attribute(RelNs + "id")?.Value;
        if (string.IsNullOrEmpty(drawingRelId))
            return null;

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var target = relsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, drawingRelId, StringComparison.OrdinalIgnoreCase))?
            .Attribute("Target")?.Value;
        if (string.IsNullOrEmpty(target))
            return null;

        var resolvedPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
        return archive.GetEntry(resolvedPath) is not null ? resolvedPath : null;
    }

    private static string AllocateFreshDrawingPath(ZipArchive archive)
    {
        var index = 1;
        while (archive.GetEntry($"xl/drawings/drawing{index}.xml") is not null)
            index++;
        return $"xl/drawings/drawing{index}.xml";
    }

    private static void WireWorksheetDrawingRelationship(ZipArchive archive, string worksheetPath, string drawingPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relsXml, PackageRelNs, worksheetPath, drawingPath, DrawingRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, relsXml);

        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
        XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(root, WorksheetNs, RelNs, relationshipId);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XElement BuildAnchor(string controlName, string shapeName, DrawingAnchorRange range, bool isTimeline, int id) =>
        new(SpreadsheetDrawingNs + "twoCellAnchor",
            ToAnchorPointXml("from", range.From),
            ToAnchorPointXml("to", range.To),
            BuildAlternateContent(controlName, shapeName, isTimeline, id),
            new XElement(SpreadsheetDrawingNs + "clientData"));

    private static XElement ToAnchorPointXml(string elementName, DrawingAnchorPoint point) =>
        new(SpreadsheetDrawingNs + elementName,
            new XElement(SpreadsheetDrawingNs + "col", point.Column.ToString(CultureInfo.InvariantCulture)),
            new XElement(SpreadsheetDrawingNs + "colOff", point.ColumnOffsetEmu.ToString(CultureInfo.InvariantCulture)),
            new XElement(SpreadsheetDrawingNs + "row", point.Row.ToString(CultureInfo.InvariantCulture)),
            new XElement(SpreadsheetDrawingNs + "rowOff", point.RowOffsetEmu.ToString(CultureInfo.InvariantCulture)));

    private static XElement BuildAlternateContent(string controlName, string shapeName, bool isTimeline, int id)
    {
        var prefix = isTimeline ? "tsle" : "sle";
        var linkNs = isTimeline ? TimelineLinkNs : SlicerLinkNs;

        return new XElement(MarkupCompatNs + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", MarkupCompatNs.NamespaceName),
            new XElement(MarkupCompatNs + "Choice",
                new XAttribute(XNamespace.Xmlns + prefix, linkNs.NamespaceName),
                new XAttribute("Requires", prefix),
                BuildGraphicFrame(controlName, shapeName, isTimeline, id, prefix, linkNs)),
            new XElement(MarkupCompatNs + "Fallback",
                BuildFallbackShape(shapeName, isTimeline)));
    }

    private static XElement BuildGraphicFrame(
        string controlName,
        string shapeName,
        bool isTimeline,
        int id,
        string prefix,
        XNamespace linkNs)
    {
        var linkElementName = isTimeline ? "timeslicer" : "slicer";
        var graphicDataUri = linkNs.NamespaceName;

        return new XElement(SpreadsheetDrawingNs + "graphicFrame",
            new XAttribute("macro", ""),
            new XElement(SpreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(SpreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", shapeName)),
                new XElement(SpreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(SpreadsheetDrawingNs + "xfrm"),
            new XElement(DrawingNs + "graphic",
                new XElement(DrawingNs + "graphicData",
                    new XAttribute("uri", graphicDataUri),
                    new XElement(linkNs + linkElementName,
                        new XAttribute(XNamespace.Xmlns + prefix, linkNs.NamespaceName),
                        new XAttribute("name", controlName)))));
    }

    // A minimal, schema-valid mc:Fallback placeholder shape -- mirrors Excel's own "not supported in
    // earlier versions" fallback shape. Never read back by XlsxSlicerTimelineMetadataReader (which
    // deliberately only looks at the mc:Choice branch), so its exact content is cosmetic only.
    private static XElement BuildFallbackShape(string shapeName, bool isTimeline)
    {
        var kind = isTimeline ? "timeline" : "slicer";
        return new XElement(SpreadsheetDrawingNs + "sp",
            new XAttribute("macro", ""),
            new XAttribute("textlink", ""),
            new XElement(SpreadsheetDrawingNs + "nvSpPr",
                new XElement(SpreadsheetDrawingNs + "cNvPr", new XAttribute("id", 0), new XAttribute("name", "")),
                new XElement(SpreadsheetDrawingNs + "cNvSpPr",
                    new XElement(DrawingNs + "spLocks", new XAttribute("noTextEdit", "1")))),
            new XElement(SpreadsheetDrawingNs + "spPr",
                new XElement(DrawingNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(DrawingNs + "avLst")),
                new XElement(DrawingNs + "solidFill", new XElement(DrawingNs + "prstClr", new XAttribute("val", "white"))),
                new XElement(DrawingNs + "ln", new XAttribute("w", "1"),
                    new XElement(DrawingNs + "solidFill", new XElement(DrawingNs + "prstClr", new XAttribute("val", "green"))))),
            new XElement(SpreadsheetDrawingNs + "txBody",
                new XElement(DrawingNs + "bodyPr", new XAttribute("vertOverflow", "clip"), new XAttribute("horzOverflow", "clip")),
                new XElement(DrawingNs + "lstStyle"),
                new XElement(DrawingNs + "p",
                    new XElement(DrawingNs + "r",
                        new XElement(DrawingNs + "rPr", new XAttribute("lang", "en-US")),
                        new XElement(DrawingNs + "t",
                            $"This shape represents the \"{shapeName}\" {kind}. {char.ToUpperInvariant(kind[0])}{kind[1..]}s were introduced in a later version of Excel and are not supported in earlier versions.")))));
    }

    private static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
