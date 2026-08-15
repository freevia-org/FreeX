using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 7C: tests for SmartArt model, round-trip I/O preservation, and compositor dispatch.
/// </summary>
public sealed class SmartArtTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.SmartArtTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static void RemoveZipEntries(string path, params string[] entryPaths)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var entryPath in entryPaths)
            archive.GetEntry(entryPath)?.Delete();
    }

    private static void ReplaceZipEntry(string path, string entryPath, string content)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        archive.GetEntry(entryPath)?.Delete();
        var entry = archive.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>
    /// Builds a minimal but self-consistent in-memory .pptx archive with one SmartArt shape.
    /// Writes it to disk and returns the path.
    /// </summary>
    private string MakeSmartArtPptx(
        string[] nodeTexts,
        bool pictureAccentProcess = false,
        bool pictureCaptionList = false,
        bool pictureAccentList = false,
        bool pictureStack = false,
        bool pictureLineup = false,
        bool pictureStrips = false,
        bool verticalPictureList = false,
        bool continuousPictureList = false,
        bool pictureGrid = false,
        bool includeNodeImage = false,
        IReadOnlySet<int>? pictureNodeIndexes = null,
        bool includeColors = true,
        string? layoutUniqueId = null,
        bool groupedListUnmodeledRole = false,
        bool includeNodeOuterShadow = false,
        bool cycle2NodeAndArrowCache = false,
        bool relationship1NodeAndEllipseCache = false,
        long? relationship1HorizontalStepEmu = null,
        bool verticalArrowListNodeCache = false,
        bool process1NodeAndConnectorCache = false)
    {
        var path = Path.Combine(_tempDir, $"smartart_{Guid.NewGuid():N}.pptx");

        // Namespaces
        var pNs  = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var aNs  = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var rNs  = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var pkgNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        const string diagramDataCT    = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
        const string diagramLayoutCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
        const string diagramQsCT      = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
        const string diagramColorsCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
        const string diagramDrawingCT = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

        const string dmRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
        const string loRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
        const string qsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
        const string csRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
        const string dgmDrawRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

        // Build dsp:drawing XML with fallback shapes
        int shapeIdx = 1;
        var fallbackEls = new List<XElement>();
        var process1BoxX = new long[] { 329_184, 1_933_956, 3_538_728, 5_143_500, 6_748_272 };
        var process1ConnectorX = new long[] { 1_584_198, 3_188_970, 4_793_742, 6_398_514 };
        for (var nodeIndex = 0; nodeIndex < nodeTexts.Length; nodeIndex++)
        {
            var text = nodeTexts[nodeIndex];
            int idx = shapeIdx++;
            if ((pictureAccentProcess || pictureCaptionList || pictureAccentList || pictureStack || pictureLineup || pictureStrips || verticalPictureList || continuousPictureList || pictureGrid)
                && includeNodeImage
                && (pictureNodeIndexes is null || pictureNodeIndexes.Contains(nodeIndex)))
            {
                fallbackEls.Add(new XElement(dspNs + "pic",
                    new XAttribute("modelId", $"n{nodeIndex + 1}"),
                    new XElement(dspNs + "nvPicPr",
                        new XElement(dspNs + "cNvPr", new XAttribute("id", idx), new XAttribute("name", $"Picture{idx}")),
                        new XElement(dspNs + "cNvPicPr")),
                    new XElement(dspNs + "blipFill",
                        new XElement(aNs + "blip", new XAttribute(rNs + "embed", "rIdImg1")),
                        new XElement(aNs + "stretch", new XElement(aNs + "fillRect"))),
                    new XElement(dspNs + "spPr",
                        new XElement(aNs + "xfrm",
                            new XElement(aNs + "off", new XAttribute("x", (idx - 1) * 914400L), new XAttribute("y", "457200")),
                            new XElement(aNs + "ext", new XAttribute("cx", "457200"), new XAttribute("cy", "457200"))),
                        new XElement(aNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(aNs + "avLst")))));
                idx = shapeIdx++;
            }

            fallbackEls.Add(new XElement(dspNs + "sp",
                new XElement(dspNs + "nvSpPr",
                    new XElement(dspNs + "cNvPr", new XAttribute("id", idx), new XAttribute("name", $"Node{idx}")),
                    new XElement(dspNs + "cNvSpPr")),
                new XElement(dspNs + "spPr",
                    new XElement(aNs + "xfrm",
                        new XElement(aNs + "off", new XAttribute("x", process1NodeAndConnectorCache
                            ? process1BoxX[nodeIndex]
                            : verticalArrowListNodeCache
                            ? 329_184L
                            : relationship1NodeAndEllipseCache
                                ? 1_522_800L + nodeIndex * (relationship1HorizontalStepEmu ?? 1_392_000L)
                                : (idx - 1) * 914400L), new XAttribute("y", process1NodeAndConnectorCache
                            ? 689_376L
                            : verticalArrowListNodeCache
                            ? 229_792L + nodeIndex * 1_344_642L
                            : relationship1NodeAndEllipseCache ? 1_672_400L : 457200L)),
                        new XElement(aNs + "ext", new XAttribute("cx", process1NodeAndConnectorCache
                            ? 1_152_144L
                            : verticalArrowListNodeCache
                            ? 7_571_232L
                            : relationship1NodeAndEllipseCache ? 2_400_000L : 914400L), new XAttribute("cy", process1NodeAndConnectorCache
                            ? 4_366_048L
                            : verticalArrowListNodeCache
                            ? 1_251_289L
                            : relationship1NodeAndEllipseCache ? 2_400_000L : 457200L))),
                    new XElement(aNs + "prstGeom",
                        new XAttribute("prst", process1NodeAndConnectorCache
                            ? "roundRect"
                            : verticalArrowListNodeCache
                            ? "downArrow"
                            : cycle2NodeAndArrowCache || relationship1NodeAndEllipseCache ? "ellipse" : "rect"),
                        new XElement(aNs + "avLst")),
                    new XElement(aNs + "solidFill",
                        new XElement(aNs + "srgbClr", new XAttribute("val", "4472C4"))),
                includeNodeOuterShadow
                    ? new XElement(aNs + "effectLst",
                        new XElement(aNs + "outerShdw",
                            new XAttribute("blurRad", "76200"),
                            new XAttribute("dist", "12700"),
                            new XAttribute("dir", "2700000"),
                            new XElement(aNs + "srgbClr",
                                new XAttribute("val", "000000"),
                                new XElement(aNs + "alpha", new XAttribute("val", "50000")))))
                    : null),
                new XElement(dspNs + "style",
                    new XElement(aNs + "fontRef", new XAttribute("idx", "minor"),
                        new XElement(aNs + "schemeClr", new XAttribute("val", "lt1")))),
                new XElement(dspNs + "txBody",
                    new XElement(aNs + "bodyPr"),
                    new XElement(aNs + "lstStyle"),
                    new XElement(aNs + "p",
                        new XElement(aNs + "r",
                            new XElement(aNs + "rPr", new XAttribute("lang", "en-US")),
                        new XElement(aNs + "t", text))))));

            if (cycle2NodeAndArrowCache)
            {
                idx = shapeIdx++;
                fallbackEls.Add(new XElement(dspNs + "sp",
                    new XElement(dspNs + "nvSpPr",
                        new XElement(dspNs + "cNvPr", new XAttribute("id", idx), new XAttribute("name", $"Transition{idx}")),
                        new XElement(dspNs + "cNvSpPr")),
                    new XElement(dspNs + "spPr",
                        new XElement(aNs + "xfrm",
                            new XElement(aNs + "off", new XAttribute("x", (idx - 1) * 914400L), new XAttribute("y", "685800")),
                            new XElement(aNs + "ext", new XAttribute("cx", "457200"), new XAttribute("cy", "228600"))),
                        new XElement(aNs + "prstGeom", new XAttribute("prst", "rightArrow"), new XElement(aNs + "avLst")),
                        new XElement(aNs + "solidFill",
                            new XElement(aNs + "srgbClr", new XAttribute("val", "AAB6C1"))))));
            }

            if (process1NodeAndConnectorCache && nodeIndex < nodeTexts.Length - 1)
            {
                idx = shapeIdx++;
                fallbackEls.Add(new XElement(dspNs + "sp",
                    new XElement(dspNs + "nvSpPr",
                        new XElement(dspNs + "cNvPr", new XAttribute("id", idx), new XAttribute("name", $"Process1 connector {idx}")),
                        new XElement(dspNs + "cNvSpPr")),
                    new XElement(dspNs + "spPr",
                        new XElement(aNs + "xfrm",
                            new XElement(aNs + "off", new XAttribute("x", process1ConnectorX[nodeIndex]), new XAttribute("y", "2872400")),
                            new XElement(aNs + "ext", new XAttribute("cx", "246888"), new XAttribute("cy", "914"))),
                        new XElement(aNs + "ln",
                            new XElement(aNs + "solidFill",
                                new XElement(aNs + "srgbClr", new XAttribute("val", "0E4B66"))),
                            new XElement(aNs + "prstDash", new XAttribute("val", "solid"))))));
            }
        }

        if (groupedListUnmodeledRole)
        {
            fallbackEls.Add(new XElement(dspNs + "sp",
                new XElement(dspNs + "nvSpPr",
                    new XElement(dspNs + "cNvPr", new XAttribute("id", shapeIdx++), new XAttribute("name", "GroupedList Cached Role")),
                    new XElement(dspNs + "cNvSpPr")),
                new XElement(dspNs + "spPr",
                    new XElement(aNs + "xfrm",
                        new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(aNs + "ext", new XAttribute("cx", "457200"), new XAttribute("cy", "457200"))),
                    new XElement(aNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(aNs + "avLst")))));
        }

        var dspDrawingXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dspNs + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", dspNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(dspNs + "spTree", fallbackEls)));

        var process1Connections = process1NodeAndConnectorCache
            ? new XElement(dgmNs + "cxnLst",
                Enumerable.Range(1, nodeTexts.Length - 1).Select(i =>
                    new XElement(dgmNs + "cxn",
                        new XAttribute("type", "parOf"),
                        new XAttribute("srcId", $"n{i}"),
                        new XAttribute("destId", $"n{i + 1}"))))
            : null;

        // Build minimal diagram data XML (just a root element)
        var dataXml = layoutUniqueId is not null || pictureAccentProcess || pictureCaptionList || pictureAccentList || pictureStack || pictureLineup || pictureStrips || verticalPictureList || continuousPictureList || pictureGrid
            ? new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(dgmNs + "dataModel",
                    new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                    new XElement(dgmNs + "ptLst",
                        nodeTexts.Select((text, i) =>
                            new XElement(dgmNs + "pt",
                                new XAttribute("modelId", $"n{i + 1}"),
                                new XAttribute("type", "node"),
                                new XElement(dgmNs + "t",
                                    new XElement(aNs + "p",
                                        new XElement(aNs + "r",
                                            new XElement(aNs + "t", text))))))),
                    process1Connections))
            : new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(dgmNs + "dataModel",
                    new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));

        // Minimal layout, quickStyle, colors XML
        var layoutXml  = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                layoutUniqueId is not null || pictureAccentProcess || pictureCaptionList || pictureAccentList || pictureStack || pictureLineup || pictureStrips || verticalPictureList || continuousPictureList || pictureGrid
                    ? new XAttribute("uniqueId", layoutUniqueId ?? (pictureGrid
                        ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid"
                        : pictureAccentProcess
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentProcess"
                        : continuousPictureList
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/continuousPictureList"
                        : pictureStrips
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureStrips"
                        : verticalPictureList
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/verticalPictureList"
                        : pictureLineup
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureLineup"
                        : pictureStack
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureStack"
                        : pictureAccentList
                            ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentList"
                            : "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList"))
                    : null));
        var qsXml      = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(dgmNs + "styleDef",   new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));
        var colorsXml  = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(dgmNs + "colorsDef", new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));

        static byte[] ToBytes(XDocument doc)
        {
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        static byte[] MakeRels(XNamespace pkgNs, params (string id, string type, string target)[] rels)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(pkgNs + "Relationships",
                    rels.Select(r => new XElement(pkgNs + "Relationship",
                        new XAttribute("Id", r.id),
                        new XAttribute("Type", r.type),
                        new XAttribute("Target", r.target)))));
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        // Slide XML referencing the SmartArt via dgm:relIds
        var slideXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sld",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        // Required grpSp header
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))),
                        // The SmartArt graphicFrame
                        new XElement(pNs + "graphicFrame",
                            new XElement(pNs + "nvGraphicFramePr",
                                new XElement(pNs + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "SmartArt 1")),
                                new XElement(pNs + "cNvGraphicFramePr"),
                                new XElement(pNs + "nvPr")),
                            new XElement(pNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "914400"), new XAttribute("y", "457200")),
                                new XElement(aNs + "ext",
                                    new XAttribute("cx", process1NodeAndConnectorCache ? "8229600" : "7315200"),
                                    new XAttribute("cy", process1NodeAndConnectorCache ? "5744800" : "3657600"))),
                            new XElement(aNs + "graphic",
                                new XElement(aNs + "graphicData",
                                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/diagram"),
                                    new XElement(dgmNs + "relIds",
                                        new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                                        new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                                        new XAttribute(rNs + "dm", "rIdDm1"),
                                        new XAttribute(rNs + "lo", "rIdLo1"),
                                        new XAttribute(rNs + "qs", "rIdQs1"),
                                        (includeColors ? new XAttribute(rNs + "cs", "rIdCs1") : null)))))))));

        using var zipStream = File.Create(path);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        void WriteEntry(string entryPath, byte[] bytes)
        {
            var e = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
            using var s = e.Open();
            s.Write(bytes);
        }
        void WriteXml(string entryPath, XDocument doc) => WriteEntry(entryPath, ToBytes(doc));

        // [Content_Types].xml
        var ctNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        WriteXml("[Content_Types].xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ctNs + "Types",
                new XElement(ctNs + "Default", new XAttribute("Extension", "rels"),  new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default", new XAttribute("Extension", "xml"),   new XAttribute("ContentType", "application/xml")),
                includeNodeImage
                    ? new XElement(ctNs + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png"))
                    : null,
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/presentation.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slides/slide1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/theme/theme1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slideLayouts/slideLayout1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slideMasters/slideMaster1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/data1.xml"),      new XAttribute("ContentType", diagramDataCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/layout1.xml"),    new XAttribute("ContentType", diagramLayoutCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/quickStyle1.xml"), new XAttribute("ContentType", diagramQsCT)),
                includeColors
                    ? new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/colors1.xml"), new XAttribute("ContentType", diagramColorsCT))
                    : null,
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/drawing1.xml"),   new XAttribute("ContentType", diagramDrawingCT)))));

        // Root rels
        WriteEntry("_rels/.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "ppt/presentation.xml")));

        // Presentation
        var presNs = pNs;
        WriteXml("ppt/presentation.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(presNs + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(presNs + "sldMasterIdLst",
                    new XElement(presNs + "sldMasterId", new XAttribute("id", 2147483648u), new XAttribute(rNs + "id", "rId2"))),
                new XElement(presNs + "sldIdLst",
                    new XElement(presNs + "sldId", new XAttribute("id", 256), new XAttribute(rNs + "id", "rId1"))),
                new XElement(presNs + "sldSz", new XAttribute("cx", 9144000), new XAttribute("cy", 6858000), new XAttribute("type", "screen16x9")),
                new XElement(presNs + "notesSz", new XAttribute("cx", 6858000), new XAttribute("cy", 9144000)))));

        WriteEntry("ppt/_rels/presentation.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide", "slides/slide1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "slideMasters/slideMaster1.xml")));

        // Theme (minimal)
        var aNsTheme = aNs;
        WriteXml("ppt/theme/theme1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(aNsTheme + "theme", new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute("name", "Office Theme"),
                new XElement(aNsTheme + "themeElements",
                    new XElement(aNsTheme + "clrScheme", new XAttribute("name", "Office")),
                    new XElement(aNsTheme + "fontScheme", new XAttribute("name", "Office"),
                        new XElement(aNsTheme + "majorFont", new XElement(aNsTheme + "latin", new XAttribute("typeface", "Calibri Light"))),
                        new XElement(aNsTheme + "minorFont", new XElement(aNsTheme + "latin", new XAttribute("typeface", "Calibri")))),
                    new XElement(aNsTheme + "fmtScheme", new XAttribute("name", "Office"))))));

        // Slide Master (minimal)
        WriteXml("ppt/slideMasters/slideMaster1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sldMaster",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))))),
                new XElement(pNs + "clrMap",
                    new XAttribute("bg1", "lt1"), new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"), new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink")))));

        WriteEntry("ppt/slideMasters/_rels/slideMaster1.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "../theme/theme1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml")));

        // Slide layout (minimal)
        WriteXml("ppt/slideLayouts/slideLayout1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sldLayout",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XAttribute("type", "blank"),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))))))));

        WriteEntry("ppt/slideLayouts/_rels/slideLayout1.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "../slideMasters/slideMaster1.xml")));

        // Slide
        WriteXml("ppt/slides/slide1.xml", slideXml);

        // Slide rels: point to layout + the diagram parts present in the fixture.
        var slideRels = new List<(string id, string type, string target)>
        {
            ("rId1",   "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml"),
            ("rIdDm1", dmRelType, "../diagrams/data1.xml"),
            ("rIdLo1", loRelType, "../diagrams/layout1.xml"),
            ("rIdQs1", qsRelType, "../diagrams/quickStyle1.xml"),
        };
        if (includeColors)
            slideRels.Add(("rIdCs1", csRelType, "../diagrams/colors1.xml"));
        WriteEntry("ppt/slides/_rels/slide1.xml.rels", MakeRels(pkgNs, slideRels.ToArray()));

        // Diagram parts
        WriteXml("ppt/diagrams/data1.xml",       dataXml);
        WriteXml("ppt/diagrams/layout1.xml",     layoutXml);
        WriteXml("ppt/diagrams/quickStyle1.xml", qsXml);
        if (includeColors)
            WriteXml("ppt/diagrams/colors1.xml", colorsXml);
        WriteXml("ppt/diagrams/drawing1.xml",    dspDrawingXml);
        if (includeNodeImage)
        {
            WriteEntry("ppt/media/image1.png", Minimal1x1Png());
            WriteEntry("ppt/diagrams/_rels/drawing1.xml.rels", MakeRels(pkgNs,
                ("rIdImg1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/image1.png")));
        }

        // data1.xml rels: points to drawing1.xml
        WriteEntry("ppt/diagrams/_rels/data1.xml.rels", MakeRels(pkgNs,
            ("rIdDraw1", dgmDrawRelType, "drawing1.xml")));

        return path;
    }

    private static byte[] Minimal1x1Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, 0x33, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    // ── Model unit tests ─────────────────────────────────────────────────────────

    [Fact]
    public void SmartArtShape_DefaultsEmpty()
    {
        var sa = new SmartArtShape();
        sa.FallbackShapes.Should().BeEmpty();
        sa.DiagramRelIds.Should().BeEmpty();
        sa.Parts.Should().BeEmpty();
        sa.DrawingPartPath.Should().BeNull();
    }

    [Fact]
    public void SlideShape_SmartArtKind_IsSmartArt()
    {
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.SmartArt,
            SmartArt = new SmartArtShape()
        };
        shape.Kind.Should().Be(SlideShapeKind.SmartArt);
        shape.SmartArt.Should().NotBeNull();
    }

    // ── Reader: parse real SmartArt archive ──────────────────────────────────────

    [Fact]
    public void Reader_SmartArtGraphicFrame_IsRecognizedAsSmartArt()
    {
        var pptxPath = MakeSmartArtPptx(["Step A", "Step B", "Step C"]);
        var pres = PptxPackageReader.Read(pptxPath);

        pres.Slides.Should().HaveCount(1);
        var smartShape = pres.Slides[0].Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.SmartArt);
        smartShape.Should().NotBeNull("diagram graphicFrame should be detected as SmartArt");
    }

    [Fact]
    public void Reader_SmartArt_HasCorrectFramePosition()
    {
        var pptxPath = MakeSmartArtPptx(["A"]);
        var pres = PptxPackageReader.Read(pptxPath);

        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        sa.OffsetXEmu.Should().Be(914400L);
        sa.OffsetYEmu.Should().Be(457200L);
        sa.ExtentCxEmu.Should().Be(7315200L);
        sa.ExtentCyEmu.Should().Be(3657600L);
    }

    [Fact]
    public void Reader_SmartArt_HasDiagramRelIds()
    {
        var pptxPath = MakeSmartArtPptx(["A", "B"]);
        var pres = PptxPackageReader.Read(pptxPath);

        var sa = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        sa.DiagramRelIds.Should().ContainKey("dm");
        sa.DiagramRelIds.Should().ContainKey("lo");
        sa.DiagramRelIds.Should().ContainKey("qs");
        sa.DiagramRelIds.Should().ContainKey("cs");
    }

    [Fact]
    public void Reader_SmartArt_ParsesFallbackShapesFromDspDrawing()
    {
        var nodeTexts = new[] { "Step A", "Step B", "Step C" };
        var pptxPath = MakeSmartArtPptx(nodeTexts);
        var pres = PptxPackageReader.Read(pptxPath);

        var sa = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        sa.FallbackShapes.Should().HaveCount(nodeTexts.Length,
            "each dsp:sp in the drawing should produce one fallback shape");

        foreach (var (text, shape) in nodeTexts.Zip(sa.FallbackShapes))
        {
            shape.Kind.Should().Be(SlideShapeKind.AutoShape);
            shape.PlainText.Should().Be(text);
            shape.TextBody!.Paragraphs[0].Runs[0].Color!.Resolved.Should().Be(SrgbColor.White,
                "the cached drawing's dsp:style fontRef supplies its default text color");
        }
    }

    [Fact]
    public void Reader_SmartArt_GroupedList_UsesImportedDrawingCacheBoundary()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Group A", "Group B"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/groupedList",
            groupedListUnmodeledRole: true);
        var presentation = PptxPackageReader.Read(pptxPath);

        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/groupedList");
        smartArt.Data.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.IsLiveLayoutSupported.Should().BeFalse(
            "authoring can use Grouped List live geometry, but imported PowerPoint drawing caches may contain roles that geometry does not model");
        smartArt.FallbackShapes.Should().NotBeEmpty();
    }

    [Fact]
    public void Reader_SmartArt_GroupedList_AdmitsSimpleNodeCacheToLiveLayout()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Group A", "Group B"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/groupedList");
        var presentation = PptxPackageReader.Read(pptxPath);

        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "a cache with exactly one matching text shape per parsed node has no unmodeled grouped-list role");
        smartArt.FallbackShapes.Should().HaveCount(2);
    }

    [Fact]
    public void Reader_ImportedSmartArtGroupedList_AdmitsBoundedBandCacheToSharedLiveLayout()
    {
        var corpusPath = FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);
        var slide = presentation.Slides[5];
        var smartArtShape = slide.Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartArtShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/groupedList");
        smartArt.Data.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the generated grouped-list cache contains only two group bands and six matching node boxes");
        smartArt.Data.UsesGroupedListBands.Should().BeTrue();
        smartArt.FallbackShapes.Should().HaveCount(8,
            "the cache contains two empty group bands, two headers, and four child boxes");
        smartArt.FallbackShapes.Count(shape => string.IsNullOrWhiteSpace(shape.PlainText))
            .Should().Be(2);
        smartArt.FallbackShapes.Count(shape => !string.IsNullOrWhiteSpace(shape.PlainText))
            .Should().Be(6);

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Count.Should().Be(8,
            "the shared grouped-list plan preserves both imported group bands behind the six editable nodes");
        live.Count(shape => string.IsNullOrWhiteSpace(shape.PlainText)).Should().Be(2);

        var composed = SlideCompositor.Compose(presentation, slide)
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        composed.Where(shape => shape.ShapeId is >= 2250 and <= 2257)
            .Should().HaveCount(8,
                "the compositor must consume the eight-shape shared live plan instead of the cached drawing");
    }

    [Fact]
    public void Reader_SmartArt_IncreasingCircleProcess_UnprovenCachePreservesFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Phase A", "Phase B", "Phase C"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/increasingCircleProcess");
        var presentation = PptxPackageReader.Read(pptxPath);

        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.Process);
        smartArt.Data.IsLiveLayoutSupported.Should().BeFalse(
            "a synthetic cache without the audited seven-shape grammar must remain on fallback");
    }

    [Fact]
    public void Reader_SmartArt_ImportedAccentProcessPreservesCachedFallbackAcrossRoundTrip()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Imported A", "Imported B", "Imported C"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/accentProcess");
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartShape = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "an imported Accent Process cache without the authored main/accent topology stays cached");
        smartArt.FallbackShapes.Should().HaveCount(3);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Imported A", "Imported B", "Imported C");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(presentation, saved);
        var reopened = PptxPackageReader.Read(new MemoryStream(saved.ToArray()));
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        reopenedSmartArt.Data!.LayoutUniqueId.Should().Contain("/layout/accentProcess");
        reopenedSmartArt.Data.IsLiveLayoutSupported.Should().BeFalse();
        reopenedSmartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Imported A", "Imported B", "Imported C");
    }

    [Fact]
    public void Reader_SmartArt_PreservesNativeDspConnectorFallback()
    {
        var pptxPath = MakeSmartArtPptx(["Node"]);
        var dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
        var a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var drawing = $"""
            <dsp:drawing xmlns:dsp="{dsp}" xmlns:a="{a}" xmlns:r="{r}">
              <dsp:spTree>
                <dsp:nvGrpSpPr><dsp:cNvPr id="1" name="SmartArt Cache" /><dsp:cNvGrpSpPr /></dsp:nvGrpSpPr>
                <dsp:grpSpPr />
                <dsp:cxnSp modelId="connector-1">
                  <dsp:nvCxnSpPr>
                    <dsp:cNvPr id="7" name="Native connector" />
                    <dsp:cNvCxnSpPr>
                      <a:stCxn id="3" idx="2" />
                      <a:endCxn id="4" idx="0" />
                    </dsp:cNvCxnSpPr>
                    <dsp:nvPr />
                  </dsp:nvCxnSpPr>
                  <dsp:spPr>
                    <a:xfrm flipV="1">
                      <a:off x="100" y="200" />
                      <a:ext cx="300" cy="400" />
                    </a:xfrm>
                    <a:prstGeom prst="line"><a:avLst /></a:prstGeom>
                    <a:ln w="12700"><a:solidFill><a:srgbClr val="4472C4" /></a:solidFill></a:ln>
                  </dsp:spPr>
                </dsp:cxnSp>
              </dsp:spTree>
            </dsp:drawing>
            """;
        ReplaceZipEntry(pptxPath, "ppt/diagrams/drawing1.xml", drawing);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        var connector = smartArt.FallbackShapes.Should().ContainSingle().Subject;

        connector.Kind.Should().Be(SlideShapeKind.Connector);
        connector.AutoShapeKind.Should().Be(DrawingShapeKind.Line);
        connector.OffsetXEmu.Should().Be(100);
        connector.OffsetYEmu.Should().Be(200);
        connector.ExtentCxEmu.Should().Be(300);
        connector.ExtentCyEmu.Should().Be(400);
        connector.FlipV.Should().BeTrue();
        connector.ConnectionStart.Should().NotBeNull();
        connector.ConnectionStart!.ShapeId.Should().Be(3);
        connector.ConnectionStart.SiteIndex.Should().Be(2);
        connector.ConnectionEnd.Should().NotBeNull();
        connector.ConnectionEnd!.ShapeId.Should().Be(4);
        connector.ConnectionEnd.SiteIndex.Should().Be(0);
    }

    [Fact]
    public void Reader_SmartArt_HierarchyCachedConnectorSegmentsUseLineGeometry()
    {
        var corpusPath = FindRenderCompareCorpusFile("14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);

        var fallbackShapes = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SelectMany(shape => shape.SmartArt?.FallbackShapes ?? [])
            .ToList();

        var connectorSegments = fallbackShapes
            .Where(shape => string.IsNullOrWhiteSpace(shape.PlainText)
                && (shape.ExtentCxEmu >= shape.ExtentCyEmu * 4
                    || shape.ExtentCyEmu >= shape.ExtentCxEmu * 4))
            .ToList();

        connectorSegments.Should().NotBeEmpty("the live SmartArt corpus contains cached connector segments");
        connectorSegments.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Line,
            "geometry-less, textless cached SmartArt shapes are connector line segments, not rectangles");

        var clonedConnector = SlideCloner.CloneShape(connectorSegments[0]);
        clonedConnector.CustomGeometry.Should().HaveCount(connectorSegments[0].CustomGeometry.Count,
            "the compositor clones cached SmartArt shapes before rendering them");
        clonedConnector.CustomGeometry[0].PathW.Should().BeGreaterThan(0);
        clonedConnector.CustomGeometry[0].PathH.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Reader_ImportedSmartArtHierarchy3_AdmitsObservedOrthogonalCacheToSharedLiveLayout()
    {
        var corpusPath = FindRenderCompareCorpusFile("14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);
        var smartArtShape = presentation.Slides[1].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartArtShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/hierarchy3");
        smartArt.Data.Family.Should().Be(SmartArtFamily.Hierarchy);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the real hierarchy3 cache has four visible node boxes, two empty template leaves, and four orthogonal connector segments");
        smartArt.FallbackShapes.Should().NotBeEmpty();
        smartArt.FallbackShapes.Should().HaveCount(10,
            "the corpus cache contains four node boxes, two empty template leaves, and four orthogonal connector segments");
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Line)
            .Should().Be(4);
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle
            && string.IsNullOrWhiteSpace(shape.PlainText)).Should().Be(2);

        var boxes = smartArt.FallbackShapes
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle
                && !string.IsNullOrWhiteSpace(shape.PlainText))
            .ToDictionary(shape => shape.PlainText, StringComparer.Ordinal);
        boxes.Keys.Should().Contain(["CEO", "VP Sales", "VP Engineering", "VP Marketing"]);
        boxes["VP Sales"].OffsetXEmu.Should().BeGreaterThan(boxes["CEO"].OffsetXEmu,
            "the imported PowerPoint cache owns the visible hierarchy geometry");

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            presentation.Theme,
            presentation.Slides[1].ColorMapOverride,
            smartArt.QuickStyle,
            smartArt.Colors);
        live.Should().NotBeNull();
        live!.Count.Should().Be(6,
            "the shared hierarchy3 plan emits four editable boxes and one elbow connector per modeled parent edge");

        var liveBoxes = live
            .Where(shape => shape.TextBody?.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                .Any(run => !string.IsNullOrWhiteSpace(run.Text)) == true)
            .ToDictionary(
                shape => shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                    .First(run => !string.IsNullOrWhiteSpace(run.Text)).Text,
                StringComparer.Ordinal);
        var cachedBoxes = smartArt.FallbackShapes
            .Where(shape => !string.IsNullOrWhiteSpace(shape.PlainText))
            .ToDictionary(shape => shape.PlainText, StringComparer.Ordinal);
        foreach (var text in cachedBoxes.Keys)
        {
            var liveBox = liveBoxes[text];
            var cachedBox = cachedBoxes[text];
            (liveBox.OffsetXEmu - smartArtShape.OffsetXEmu)
                .Should().BeInRange(cachedBox.OffsetXEmu - 2000, cachedBox.OffsetXEmu + 2000, text);
            (liveBox.OffsetYEmu - smartArtShape.OffsetYEmu)
                .Should().BeInRange(cachedBox.OffsetYEmu - 2000, cachedBox.OffsetYEmu + 2000, text);
            liveBox.ExtentCxEmu.Should().BeInRange(cachedBox.ExtentCxEmu - 2000, cachedBox.ExtentCxEmu + 2000, text);
            liveBox.ExtentCyEmu.Should().BeInRange(cachedBox.ExtentCyEmu - 2000, cachedBox.ExtentCyEmu + 2000, text);

            var run = liveBox.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                .Single(run => !string.IsNullOrWhiteSpace(run.Text));
            run.Bold.Should().BeFalse(text);
            run.FontSizePt.Should().Be(text is "CEO" or "VP Marketing" ? 42.0 : 31.0, text);
        }

        live.Count(shape => shape.CustomGeometry.Count == 1)
            .Should().Be(2, "each visible hierarchy3 parent edge uses one authored elbow path");
        live.Where(shape => shape.CustomGeometry.Count == 1)
            .Should().OnlyContain(shape => shape.CustomGeometry[0].Segments.Select(segment => segment.Kind)
                .SequenceEqual(new[] { CustomSegmentKind.MoveTo, CustomSegmentKind.LineTo, CustomSegmentKind.LineTo }));

        var composed = SlideCompositor.Compose(presentation, presentation.Slides[1])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        composed.Count.Should().BeLessThan(smartArt.FallbackShapes.Count,
            "the admitted live plan must replace the ten-shape cached orthogonal drawing");
    }

    [Fact]
    public void Reader_ImportedGridMatrix_AdmitsExactFourCellCacheToSharedLiveLayout()
    {
        var corpusPath = FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);
        var slide = presentation.Slides[7];
        var smartArtShape = slide.Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartArtShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/gridMatrix");
        smartArt.Data.Family.Should().Be(SmartArtFamily.Matrix);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the deterministic gridMatrix package has four ordered square rectangle cells with the shared 2.5% gap");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Should().OnlyContain(shape =>
            shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.Rectangle
            && shape.ExtentCxEmu == shape.ExtentCyEmu);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Axis", "Speed", "Quality", "Cost");

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Should().HaveCount(4);

        var composed = SlideCompositor.Compose(presentation, slide)
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .Where(shape => shape.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text
                is "Axis" or "Speed" or "Quality" or "Cost")
            .ToList();
        composed.Should().HaveCount(4,
            "the admitted shared Grid Matrix plan replaces the four-shape cache without adding renderer-local geometry");
        composed.Select(shape => shape.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Axis", "Speed", "Quality", "Cost");
    }

    [Fact]
    public void Reader_GridMatrix_WithNonSquareCachePreservesCachedDrawingFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Axis", "Speed", "Quality", "Cost"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix");

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "a non-square generic rectangle cache is outside the proven Grid Matrix grammar");
        smartArt.FallbackShapes.Should().HaveCount(4);
    }

    [Fact]
    public void Reader_ImportedIncreasingCircleProcess_AdmitsExactGrowingEllipseAndLineCache()
    {
        var presentation = PptxPackageReader.Read(
            FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"));
        var smartShape = presentation.Slides[8].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/increasingCircleProcess");
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the checked-in cache is exactly four ordered growing ellipse nodes plus three empty line roles");
        smartArt.FallbackShapes.Should().HaveCount(7);
        smartArt.FallbackShapes.Take(4).Should().OnlyContain(shape =>
            shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        smartArt.FallbackShapes.Skip(4).Should().OnlyContain(shape =>
            shape.AutoShapeKind == DrawingShapeKind.Line
            && string.IsNullOrWhiteSpace(shape.PlainText));

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartShape.OffsetXEmu,
            smartShape.OffsetYEmu,
            smartShape.ExtentCxEmu,
            smartShape.ExtentCyEmu,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Count.Should().Be(7);
        live.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.ExtentCxEmu)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Reader_ImportedIncreasingCircleProcess_RichPowerPointCacheRemainsCached()
    {
        var presentation = PptxPackageReader.Read(
            FindRenderCompareCorpusFile("14-smartart-live.pptx"));
        var smartArt = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.LayoutUniqueId.Should().EndWith("/IncreasingCircleProcess");
        smartArt.Data.IsLiveLayoutSupported.Should().BeFalse(
            "the richer PowerPoint background/chord/rectangle cache is outside the seven-shape grammar");
        smartArt.FallbackShapes.Should().HaveCount(12);
    }

    [Fact]
    public void Reader_ImportedProcess1_AdmitsExactFiveStageNodeAndConnectorCache()
    {
        var presentation = PptxPackageReader.Read(
            FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"));
        var slide = presentation.Slides[0];
        var smartShape = slide.Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.Process);
        smartArt.Data.LayoutUniqueId.Should().EndWith("/process1");
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.Data.Nodes.SelectMany(FlattenNodes).Select(node => node.Text)
            .Should().Equal("Plan", "Design", "Build", "Test", "Deploy");
        smartArt.FallbackShapes.Should().HaveCount(9);
        smartArt.FallbackShapes.Where((_, index) => index % 2 == 0)
            .Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle);
        smartArt.FallbackShapes.Where((_, index) => index % 2 == 1)
            .Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Line);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Plan", "Design", "Build", "Test", "Deploy");

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartShape.OffsetXEmu,
            smartShape.OffsetYEmu,
            smartShape.ExtentCxEmu,
            smartShape.ExtentCyEmu,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Should().HaveCount(9);
        live.Select(shape => (shape.OffsetXEmu - smartShape.OffsetXEmu, shape.OffsetYEmu - smartShape.OffsetYEmu))
            .Should().Equal(smartArt.FallbackShapes.Select(shape => (shape.OffsetXEmu, shape.OffsetYEmu)));

        var composed = SlideCompositor.Compose(presentation, slide)
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .Where(shape => shape.Text is null
                || shape.Text.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text
                    is "Plan" or "Design" or "Build" or "Test" or "Deploy")
            .ToArray();
        composed.Should().HaveCount(9,
            "the imported process1 cache is replaced by the shared node-and-connector plan");
        composed.Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Plan", "Design", "Build", "Test", "Deploy");
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("order")]
    [InlineData("effect")]
    [InlineData("extra-role")]
    [InlineData("picture")]
    public void Reader_Process1_NearMissesPreserveCachedDrawingFallback(string mutation)
    {
        var pptxPath = MakeSmartArtPptx(
            ["Plan", "Design", "Build", "Test", "Deploy"],
            pictureAccentProcess: mutation == "picture",
            includeNodeImage: mutation == "picture",
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            groupedListUnmodeledRole: mutation == "extra-role",
            includeNodeOuterShadow: mutation == "effect",
            process1NodeAndConnectorCache: true);
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        if (mutation == "geometry")
        {
            RewriteSmartArtDrawing(pptxPath, document =>
            {
                document.Descendants(dspNs + "sp").First()
                    .Element(dspNs + "spPr")!
                    .Element(aNs + "xfrm")!
                    .Element(aNs + "off")!
                    .SetAttributeValue("x", "329185");
            });
        }
        else if (mutation == "order")
        {
            RewriteSmartArtDrawing(pptxPath, document =>
            {
                var texts = document.Descendants(dspNs + "sp")
                    .SelectMany(shape => shape.Descendants(aNs + "t"))
                    .ToList();
                (texts[0].Value, texts[1].Value) = (texts[1].Value, texts[0].Value);
            });
        }

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Nodes.Should().HaveCount(1, mutation);
        smartArt.Data.IsLiveLayoutSupported.Should().BeFalse(mutation);
        smartArt.FallbackShapes.Should().NotBeEmpty(mutation);
    }

    [Fact]
    public void Reader_Process1_MalformedHierarchyConnectionsPreserveCachedDrawingFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Plan", "Design", "Build", "Test", "Deploy"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            process1NodeAndConnectorCache: true);
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");

        RewriteSmartArtData(pptxPath, document =>
        {
            document.Descendants(dgmNs + "cxn").First()
                .SetAttributeValue("destId", "missing-node");
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse("malformed process1 hierarchy");
        smartArt.FallbackShapes.Should().HaveCount(9,
            "malformed data connections must preserve the complete cached drawing");
    }

    [Theory]
    [InlineData("node")]
    [InlineData("connector")]
    public void Reader_Process1_MissingCachedShapeOrConnectorPreservesFallback(string missingRole)
    {
        var pptxPath = MakeSmartArtPptx(
            ["Plan", "Design", "Build", "Test", "Deploy"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            process1NodeAndConnectorCache: true);
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");

        RewriteSmartArtDrawing(pptxPath, document =>
        {
            var shapes = document.Descendants(dspNs + "sp").ToList();
            shapes[missingRole == "node" ? 0 : 1].Remove();
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse($"missing process1 {missingRole} cache");
        smartArt.FallbackShapes.Should().HaveCount(8,
            "a missing cached role must retain the remaining cached drawing");
    }

    [Fact]
    public void Reader_ImportedList1_AdmitsExactFourSlotCacheAndSharedComposition()
    {
        var presentation = PptxPackageReader.Read(
            FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"));
        var slide = presentation.Slides.Single(candidate => candidate.Shapes.Any(shape =>
            shape.Kind == SlideShapeKind.SmartArt
            && shape.SmartArt?.Data?.LayoutUniqueId.EndsWith("/list1", StringComparison.OrdinalIgnoreCase) == true));
        var smartArt = slide.Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.Data.Nodes.Select(node => node.Text)
            .Should().Equal("Requirement 1", "Requirement 2", "Requirement 3", "Requirement 4");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Should().OnlyContain(shape =>
            shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle
            && shape.Effects == null);
        smartArt.FallbackShapes.Select(shape => shape.OffsetXEmu)
            .Should().OnlyContain(value => value == 329_184L);
        smartArt.FallbackShapes.Select(shape => shape.OffsetYEmu)
            .Should().Equal(229_792L, 1_587_001L, 2_944_210L, 4_301_419L);
        smartArt.FallbackShapes.Select(shape => (shape.ExtentCxEmu, shape.ExtentCyEmu))
            .Should().OnlyContain(value => value.Item1 == 7_571_232L && value.Item2 == 1_213_589L);

        var composed = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.First().Runs.First().Text)
            .Where(text => text.StartsWith("Requirement ", StringComparison.Ordinal))
            .ToArray();
        composed.Should().Equal("Requirement 1", "Requirement 2", "Requirement 3", "Requirement 4");
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("text")]
    [InlineData("order")]
    [InlineData("missing-role")]
    [InlineData("effect")]
    [InlineData("picture")]
    [InlineData("richer-role")]
    public void Reader_List1_NearMissesPreserveCachedDrawingFallback(string mutation)
    {
        var pptxPath = Path.Combine(_tempDir, $"list1-{mutation}.pptx");
        File.Copy(FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"), pptxPath);
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");

        RewriteList1Drawing(pptxPath, document =>
        {
            var shapes = document.Descendants(dspNs + "sp").ToList();
            switch (mutation)
            {
                case "geometry":
                    shapes[0].Element(dspNs + "spPr")!
                        .Element(aNs + "xfrm")!
                        .Element(aNs + "off")!
                        .SetAttributeValue("x", "329185");
                    break;
                case "text":
                    shapes[2].Descendants(aNs + "t").Single().Value = "Requirement X";
                    break;
                case "order":
                    shapes[0].AddBeforeSelf(shapes[3]);
                    break;
                case "missing-role":
                    shapes[3].Remove();
                    break;
                case "effect":
                    shapes[0].Element(dspNs + "spPr")!.Add(new XElement(aNs + "effectLst",
                        new XElement(aNs + "outerShdw", new XAttribute("blurRad", "25400"))));
                    break;
                case "picture":
                    document.Root!.Element(dspNs + "spTree")!.Add(new XElement(dspNs + "pic",
                        new XElement(dspNs + "nvPicPr",
                            new XElement(dspNs + "cNvPr", new XAttribute("id", "999"), new XAttribute("name", "List1 picture")),
                            new XElement(dspNs + "cNvPicPr")),
                        new XElement(dspNs + "blipFill",
                            new XElement(aNs + "blip", new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "embed", "rIdMissing"))),
                        new XElement(dspNs + "spPr")));
                    break;
                case "richer-role":
                    document.Root!.Element(dspNs + "spTree")!.Add(new XElement(dspNs + "sp",
                        new XElement(dspNs + "nvSpPr",
                            new XElement(dspNs + "cNvPr", new XAttribute("id", "998"), new XAttribute("name", "List1 extra role"))),
                        new XElement(dspNs + "spPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "1"), new XAttribute("cy", "1"))),
                            new XElement(aNs + "prstGeom", new XAttribute("prst", "ellipse")))));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        });

        if (mutation == "order")
        {
            RewriteList1Data(pptxPath, document =>
            {
                var points = document.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram") + "pt")
                    .ToList();
                points[0].AddBeforeSelf(points[3]);
            });
        }

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[4].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(mutation);
        smartArt.FallbackShapes.Should().NotBeEmpty(mutation);
    }

    [Fact]
    public void Reader_List1_MalformedHierarchyPreservesCachedDrawingFallback()
    {
        var pptxPath = Path.Combine(_tempDir, "list1-malformed-hierarchy.pptx");
        File.Copy(FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"), pptxPath);
        RewriteList1Data(pptxPath, document =>
        {
            var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
            var nodeIds = document.Descendants(dgmNs + "pt")
                .Where(point => (string?)point.Attribute("type") != "doc")
                .Select(point => (string)point.Attribute("modelId")!)
                .Take(2)
                .ToArray();
            document.Root!.Element(dgmNs + "cxnLst")!.Add(new XElement(dgmNs + "cxn",
                new XAttribute("modelId", "{00000000-0000-0000-0000-000000000138}"),
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", nodeIds[0]),
                new XAttribute("destId", nodeIds[1]),
                new XAttribute("srcOrd", "0"),
                new XAttribute("destOrd", "0")));
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[4].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse("malformed list1 hierarchy");
        smartArt.FallbackShapes.Should().HaveCount(4);
    }

    private static IEnumerable<SmartArtNode> FlattenNodes(SmartArtNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in FlattenNodes(child))
                yield return descendant;
        }
    }

    [Fact]
    public void ReaderWriter_ImportedVerticalArrowList_AdmitsOnlyTheAuditedFourSlotCache()
    {
        var corpusPath = FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);
        var slide = presentation.Slides.Single(candidate => candidate.Shapes.Any(shape =>
            shape.Kind == SlideShapeKind.SmartArt
            && shape.SmartArt?.Data?.LayoutUniqueId.EndsWith("/verticalArrowList", StringComparison.OrdinalIgnoreCase) == true));
        var smartArt = slide.Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.LayoutUniqueId.Should().EndWith("/verticalArrowList");
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.Data.Nodes.Select(node => node.Text)
            .Should().Equal("Collect", "Shape", "Review", "Share");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Should().OnlyContain(shape =>
            shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.DownArrow
            && shape.Effects == null);
        smartArt.FallbackShapes.Select(shape => shape.OffsetXEmu)
            .Should().OnlyContain(value => value == 329_184L);
        smartArt.FallbackShapes.Select(shape => shape.OffsetYEmu)
            .Should().Equal(229_792L, 1_574_434L, 2_919_076L, 4_263_718L);

        var liveShapes = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Where(shape => shape.Text is not null)
            .ToArray();
        liveShapes.Select(shape => shape.Text!.Paragraphs.First().Runs.First().Text)
            .Where(text => text is "Collect" or "Shape" or "Review" or "Share")
            .Should().Equal("Collect", "Shape", "Review", "Share");

        var reopened = PptxPackageReader.Read(WriteToPptx(presentation));
        var reopenedSmartArt = reopened.Slides.SelectMany(candidate => candidate.Shapes)
            .First(shape => shape.Kind == SlideShapeKind.SmartArt
                && shape.SmartArt?.Data?.LayoutUniqueId.EndsWith("/verticalArrowList", StringComparison.OrdinalIgnoreCase) == true)
            .SmartArt!;
        reopenedSmartArt.Data!.IsLiveLayoutSupported.Should().BeTrue();
    }

    [Fact]
    public void Reader_VerticalArrowList_WithNonArrowRole_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Collect", "Shape", "Review", "Share"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList");

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "a rectangular imported role is outside the proven vertical-arrow cache grammar");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle);
    }

    [Fact]
    public void Reader_VerticalArrowList_WithAlteredSlotGeometry_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Collect", "Shape", "Review", "Share"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
            verticalArrowListNodeCache: true);
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        RewriteSmartArtDrawing(pptxPath, document =>
        {
            document.Descendants(dspNs + "sp").First()
                .Element(dspNs + "spPr")!
                .Element(aNs + "xfrm")!
                .Element(aNs + "off")!
                .SetAttributeValue("x", "329185");
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "one slot moved outside the exact vertical-arrow geometry contract");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Select(shape => shape.OffsetXEmu)
            .Should().Equal(329185L, 329184L, 329184L, 329184L);
    }

    [Fact]
    public void Reader_VerticalArrowList_WithReorderedCacheText_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Collect", "Shape", "Review", "Share"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
            verticalArrowListNodeCache: true);
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        RewriteSmartArtDrawing(pptxPath, document =>
        {
            var texts = document.Descendants(dspNs + "sp")
                .SelectMany(shape => shape.Descendants(aNs + "t"))
                .ToList();
            (texts[0].Value, texts[1].Value) = (texts[1].Value, texts[0].Value);
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "cache text order no longer matches the SmartArt node order");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Shape", "Collect", "Review", "Share");
    }

    [Fact]
    public void Reader_VerticalArrowList_WithPictureRole_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Collect", "Shape", "Review", "Share"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
            verticalArrowListNodeCache: true);
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        RewriteSmartArtDrawing(pptxPath, document =>
        {
            var shape = document.Descendants(dspNs + "sp").First();
            var nonVisualShape = shape.Element(dspNs + "nvSpPr")!;
            var picture = new XElement(
                dspNs + "pic",
                new XElement(
                    dspNs + "nvPicPr",
                    new XElement(
                        dspNs + "cNvPr",
                        nonVisualShape.Element(dspNs + "cNvPr")!.Attributes()),
                    new XElement(dspNs + "cNvPicPr")),
                new XElement(
                    dspNs + "blipFill",
                    new XElement(aNs + "stretch", new XElement(aNs + "fillRect"))),
                new XElement(shape.Element(dspNs + "spPr")!));
            shape.ReplaceWith(picture);
        });

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "a picture role is outside the autoshape-only vertical-arrow grammar");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Count(shape => shape.Kind == SlideShapeKind.Picture)
            .Should().Be(1);
        smartArt.FallbackShapes.Skip(1).Should().OnlyContain(shape =>
            shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.DownArrow);
    }

    [Fact]
    public void Reader_VerticalArrowList_WithEffectExtraRoleOrDuplicateText_PreservesCachedFallback()
    {
        var cases = new[]
        {
            (Name: "effect", Path: MakeSmartArtPptx(
                ["Collect", "Shape", "Review", "Share"],
                layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
                includeNodeOuterShadow: true,
                verticalArrowListNodeCache: true)),
            (Name: "extra-role", Path: MakeSmartArtPptx(
                ["Collect", "Shape", "Review", "Share"],
                layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
                groupedListUnmodeledRole: true,
                verticalArrowListNodeCache: true)),
            (Name: "duplicate-text", Path: MakeSmartArtPptx(
                ["Collect", "Shape", "Review", "Collect"],
                layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
                verticalArrowListNodeCache: true))
        };

        foreach (var testCase in cases)
        {
            var smartArt = PptxPackageReader.Read(testCase.Path).Slides[0].Shapes
                .First(shape => shape.Kind == SlideShapeKind.SmartArt)
                .SmartArt!;

            smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(testCase.Name);
            smartArt.FallbackShapes.Should().NotBeEmpty(testCase.Name);
        }
    }

    [Fact]
    public void ReaderWriter_Cycle2_AdmitsLiveGeometryAndPreservesNativeIdentity()
    {
        var corpusPath = FindRenderCompareCorpusFile("14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(corpusPath);
        var slide = presentation.Slides.Single(candidate => candidate.Shapes.Any(shape =>
            shape.Kind == SlideShapeKind.SmartArt
            && shape.SmartArt?.Data?.LayoutUniqueId.EndsWith("/cycle2", StringComparison.OrdinalIgnoreCase) == true));
        var smartShape = slide.Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        smartArt.Data.LayoutUniqueId.Should().EndWith("/cycle2");
        smartArt.FallbackShapes.Should().HaveCount(10,
            "the real PowerPoint cycle2 cache contains five ellipse nodes and five transition arrows");
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().Be(5);
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().Be(5);
        smartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.PlainText)
            .Should().Equal("Idea", "Plan", "Execute", "Review", "Improve");
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "cycle2 is admitted only for the repository-proven ellipse-plus-arrow cache contract");
        smartArt.Data.Nodes.Select(node => node.Text)
            .Should().Equal("Idea", "Plan", "Execute", "Review", "Improve");

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartShape.OffsetXEmu,
            smartShape.OffsetYEmu,
            smartShape.ExtentCxEmu,
            smartShape.ExtentCyEmu,
            presentation.Theme,
            slide.ColorMapOverride,
            smartArt.QuickStyle,
            smartArt.Colors);
        live.Should().NotBeNull();
        var liveEllipses = live!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .ToDictionary(
                shape => shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
                    .Single(run => !string.IsNullOrWhiteSpace(run.Text)).Text,
                StringComparer.Ordinal);
        var cachedEllipses = smartArt.FallbackShapes
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .ToDictionary(shape => shape.PlainText, StringComparer.Ordinal);
        foreach (var text in cachedEllipses.Keys)
        {
            var liveEllipse = liveEllipses[text];
            var cachedEllipse = cachedEllipses[text];
            (liveEllipse.OffsetXEmu - smartShape.OffsetXEmu)
                .Should().BeInRange(cachedEllipse.OffsetXEmu - 2000, cachedEllipse.OffsetXEmu + 2000, text);
            (liveEllipse.OffsetYEmu - smartShape.OffsetYEmu)
                .Should().BeInRange(cachedEllipse.OffsetYEmu - 2000, cachedEllipse.OffsetYEmu + 2000, text);
            liveEllipse.ExtentCxEmu.Should().BeInRange(cachedEllipse.ExtentCxEmu - 2000, cachedEllipse.ExtentCxEmu + 2000, text);
            liveEllipse.ExtentCyEmu.Should().BeInRange(cachedEllipse.ExtentCyEmu - 2000, cachedEllipse.ExtentCyEmu + 2000, text);
            var run = liveEllipse.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs).Single();
            run.FontSizePt.Should().Be(21.0, text);
            run.Bold.Should().BeFalse(text);
            liveEllipse.TextBody.InsetTopPt.Should().Be(2.1, text);
            liveEllipse.TextBody.Anchor.Should().Be(VerticalAnchor.Middle, text);
        }

        var liveShapes = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Idea", "Plan", "Execute", "Review", "Improve"]);
        liveShapes.Count(shape => Math.Abs(shape.RotationDeg) > 0.1)
            .Should().Be(5, "the five live cycle2 arrows carry tangent rotations");

        var savedPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(savedPath);
        var reopenedSmartArt = reopened.Slides.SelectMany(candidate => candidate.Shapes)
            .First(shape => shape.Kind == SlideShapeKind.SmartArt
                && shape.SmartArt?.Data?.LayoutUniqueId.EndsWith("/cycle2", StringComparison.OrdinalIgnoreCase) == true)
            .SmartArt!;
        reopenedSmartArt.Data.Should().NotBeNull();
        reopenedSmartArt.Data!.LayoutUniqueId.Should().EndWith("/cycle2");
        reopenedSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        reopenedSmartArt.Data.Nodes.Select(node => node.Text)
            .Should().Equal("Idea", "Plan", "Execute", "Review", "Improve");
    }

    [Fact]
    public void Reader_Cycle2_WithUnmodeledCacheRole_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Idea", "Plan"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/cycle2",
            groupedListUnmodeledRole: true);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.LayoutUniqueId.Should().EndWith("/cycle2");
        smartArt.Data.IsLiveLayoutSupported.Should().BeFalse(
            "an imported cycle2 cache with an extra visible role is outside the proven planner contract");
        smartArt.FallbackShapes.Should().HaveCount(3);
        smartArt.FallbackShapes.Should().Contain(shape => string.IsNullOrEmpty(shape.PlainText),
            "the extra cached role must remain available to the fallback compositor");
    }

    [Fact]
    public void Reader_Cycle2_ExactNodeAndArrowCacheWithoutEffects_IsAdmittedLive()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Idea", "Plan"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/cycle2",
            cycle2NodeAndArrowCache: true);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "the otherwise identical effect-free ellipse-and-arrow cache satisfies every Wave117 admission gate");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().Be(2);
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().Be(2);
        smartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.PlainText)
            .Should().Equal("Idea", "Plan");
        smartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().OnlyContain(shape => string.IsNullOrWhiteSpace(shape.PlainText));
        smartArt.FallbackShapes.Should().OnlyContain(shape => shape.Effects == null);
    }

    [Fact]
    public void Reader_Cycle2_WithAuthoredEffects_PreservesCachedFallbackThroughSaveReopen()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Idea", "Plan"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/cycle2",
            includeNodeOuterShadow: true,
            cycle2NodeAndArrowCache: true);

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "the shared cycle2 planner does not reproduce authored node effects");
        smartArt.FallbackShapes.Should().HaveCount(4);
        smartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.Effects is not null && shape.Effects.HasOuterShadow)
            .Should().OnlyContain(value => value);
        smartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().OnlyContain(shape => string.IsNullOrWhiteSpace(shape.PlainText) && shape.Effects == null);

        var composed = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .ToArray();
        composed
            .Select(shape => shape.Effects is not null && shape.Effects.HasOuterShadow)
            .Should().Contain(true,
                "the effect-bearing cache must remain the render source");

        var reopened = PptxPackageReader.Read(WriteToPptx(presentation));
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        reopenedSmartArt.Data.Should().NotBeNull();
        reopenedSmartArt.Data!.IsLiveLayoutSupported.Should().BeFalse();
        reopenedSmartArt.FallbackShapes.Should().HaveCount(4);
        reopenedSmartArt.FallbackShapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.Effects is not null && shape.Effects.HasOuterShadow)
            .Should().OnlyContain(value => value,
                "save/reopen must retain the authoritative effect-bearing nodes");
        reopenedSmartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().Be(2);
    }

    [Fact]
    public void Reader_SmartArt_PictureCaptionList_ImportsNodePictures()
    {
        var nodeTexts = new[] { "Alpha caption", "Beta caption" };
        var pptxPath = MakeSmartArtPptx(nodeTexts, pictureCaptionList: true, includeNodeImage: true);
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.LayoutUniqueId.Should().EndWith("/pictureCaptionList");
        smart.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the fixture has a deterministic one-to-one ordered node/picture mapping");
        smart.Data.Nodes.Should().HaveCount(nodeTexts.Length);
        smart.Data.Nodes.Select(n => n.Text).Should().Equal(nodeTexts);
        smart.Data.Nodes.Select(n => n.Picture?.ContentType).Should().OnlyContain(contentType => contentType == "image/png");
        smart.Data.Nodes.Select(n => n.Picture?.Bytes.Length ?? 0).Should().OnlyContain(length => length > 0);
        smart.FallbackShapes.Should().Contain(s => s.Kind == SlideShapeKind.Picture,
            "the cached dsp:pic is still parsed as an ordinary fallback picture shape");
        smart.Parts.Should().ContainKey("ppt/media/image1.png",
            "diagram-owned image media must be retained in the SmartArt part bag for save/edit workflows");
        smart.Parts["ppt/media/image1.png"].Bytes.Should().Equal(Minimal1x1Png());
    }

    [Fact]
    public void Reader_SmartArt_PictureCaptionList_WithoutImage_UsesLivePlaceholders()
    {
        var pptxPath = MakeSmartArtPptx(["Caption only"], pictureCaptionList: true, includeNodeImage: false);
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "PowerPoint exposes an empty picture layout as editable Add picture placeholders");
        smart.Data.Nodes.Should().ContainSingle();
        smart.Data.Nodes[0].Picture.Should().BeNull();

        var liveText = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .SelectMany(shape => shape.Text?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .ToArray();
        liveText.Should().Contain("Add picture");
    }

    [Fact]
    public void Reader_SmartArt_PictureCaptionList_PartialTaggedPictures_UsesPlaceholdersForMissingNodes()
    {
        var pptxPath = MakeSmartArtPptx(
            ["First caption", "Second caption"],
            pictureCaptionList: true,
            includeNodeImage: true,
            pictureNodeIndexes: new HashSet<int> { 1 });
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "modelId tags identify the populated node without making the missing node fall back to cached SmartArt");
        smart.Data.Nodes[0].Picture.Should().BeNull();
        smart.Data.Nodes[1].Picture.Should().NotBeNull();

        var liveText = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .SelectMany(shape => shape.Text?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .ToArray();
        liveText.Should().Contain("Add picture");
    }

    [Fact]
    public void Reader_SmartArt_PictureGrid_ImportsNodePictures()
    {
        var nodeTexts = new[] { "Alpha caption", "Beta caption" };
        var pptxPath = MakeSmartArtPptx(nodeTexts, pictureGrid: true, includeNodeImage: true);
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.LayoutUniqueId.Should().EndWith("/pictureGrid");
        smart.Data.IsLiveLayoutSupported.Should().BeTrue(
            "Picture Grid is a supported live layout when every node has a deterministic image relationship");
        smart.Data.Nodes.Should().HaveCount(nodeTexts.Length);
        smart.Data.Nodes.Select(n => n.Text).Should().Equal(nodeTexts);
        smart.Data.Nodes.Select(n => n.Picture?.ContentType).Should().OnlyContain(contentType => contentType == "image/png");
        smart.Data.Nodes.Select(n => n.Picture?.Bytes.Length ?? 0).Should().OnlyContain(length => length > 0);
    }

    [Fact]
    public void Reader_SmartArt_PictureStrips_ImportsNodePictures()
    {
        var nodeTexts = new[] { "Alpha caption", "Beta caption" };
        var pptxPath = MakeSmartArtPptx(nodeTexts, pictureStrips: true, includeNodeImage: true);
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.LayoutUniqueId.Should().EndWith("/pictureStrips");
        smart.Data.IsLiveLayoutSupported.Should().BeTrue(
            "Picture Strips is a supported live layout when node picture relationships are mapped");
        smart.Data.Nodes.Should().HaveCount(nodeTexts.Length);
        smart.Data.Nodes.Select(n => n.Text).Should().Equal(nodeTexts);
        smart.Data.Nodes.Select(n => n.Picture?.ContentType).Should().OnlyContain(contentType => contentType == "image/png");
        smart.Data.Nodes.Select(n => n.Picture?.Bytes.Length ?? 0).Should().OnlyContain(length => length > 0);

        var reopened = PptxPackageReader.Read(WriteToPptx(pres));
        var reopenedSmart = reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopenedSmart.Data!.IsLiveLayoutSupported.Should().BeTrue();
        reopenedSmart.Data.Nodes.Select(n => n.Picture?.Bytes.Length ?? 0)
            .Should().OnlyContain(length => length > 0);
    }

    [Fact]
    public void EditingSession_ReplaceSmartArtNodePicture_IsUndoableAndRoundTripsMedia()
    {
        var sourcePath = MakeSmartArtPptx(["Alpha caption", "Beta caption"], pictureCaptionList: true, includeNodeImage: true);
        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var originalBytes = shape.SmartArt!.Data!.Nodes[0].Picture!.Bytes.ToArray();
        shape.SmartArt.DrawingPartPath.Should().NotBeNull();
        shape.SmartArt.PartRels.Should().ContainKey(shape.SmartArt.DrawingPartPath!);
        var replacement = originalBytes.ToArray();
        replacement[^1] ^= 0x01;
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var result = session.ReplaceSmartArtNodePicture(
            shape.Id,
            shape.SmartArt.Data.Nodes[0].ModelId,
            replacement,
            "image/png");

        result.Applied.Should().BeTrue(result.Message);
        shape.SmartArt.Data.Nodes[0].Picture!.Bytes.Should().Equal(replacement);
        session.Bus.CanUndo.Should().BeTrue();

        session.Bus.Undo();
        shape.SmartArt.Data.Nodes[0].Picture!.Bytes.Should().Equal(originalBytes);
        session.Bus.Redo();
        shape.SmartArt.Data.Nodes[0].Picture!.Bytes.Should().Equal(replacement);

        var roundTripPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(roundTripPath);
        reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!.Data!.Nodes[0].Picture!.Bytes
            .Should().Equal(replacement);
    }

    [Fact]
    public void EditingSession_ClearSmartArtNodePicture_RestoresPlaceholderAndIsUndoable()
    {
        var sourcePath = MakeSmartArtPptx(["Alpha caption", "Beta caption"], pictureCaptionList: true, includeNodeImage: true);
        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;
        var firstId = smartArt.Data!.Nodes[0].ModelId;
        var secondId = smartArt.Data.Nodes[1].ModelId;
        var firstBytes = smartArt.Data.Nodes[0].Picture!.Bytes.ToArray();
        var secondBytes = smartArt.Data.Nodes[1].Picture!.Bytes.ToArray();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var clearFirst = session.ClearSmartArtNodePicture(shape.Id, firstId);
        clearFirst.Applied.Should().BeTrue(clearFirst.Message);
        var clearSecond = session.ClearSmartArtNodePicture(shape.Id, secondId);
        clearSecond.Applied.Should().BeTrue(clearSecond.Message);
        smartArt.Data.Nodes.Select(node => node.Picture).Should().OnlyContain(picture => picture == null);
        smartArt.FallbackShapes.Select(shape => shape.PlainText).Should().Contain("Add picture");
        Encoding.UTF8.GetString(smartArt.PartRels[smartArt.DrawingPartPath!])
            .Should().NotContain("/image");

        session.Bus.Undo();
        smartArt.Data.Nodes[0].Picture.Should().BeNull();
        smartArt.Data.Nodes[1].Picture!.Bytes.Should().Equal(secondBytes);
        session.Bus.Undo();
        smartArt.Data.Nodes[0].Picture!.Bytes.Should().Equal(firstBytes);
        smartArt.Data.Nodes[1].Picture!.Bytes.Should().Equal(secondBytes);

        session.Bus.Redo();
        session.Bus.Redo();
        var roundTripPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(roundTripPath);
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopenedSmartArt.Data!.Nodes.Select(node => node.Picture)
            .Should().OnlyContain(picture => picture == null);
        reopenedSmartArt.FallbackShapes.Select(shape => shape.PlainText).Should().Contain("Add picture");
    }

    [Fact]
    public void EditingSession_SmartArtEdit_RecreatesMissingDrawingCacheAndRoundTrips()
    {
        var sourcePath = MakeSmartArtPptxWithNodeTree(
            "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess",
            [("n1", "Alpha"), ("n2", "Beta")],
            [("n1", "n2")]);
        RemoveZipEntries(sourcePath, "ppt/diagrams/drawing1.xml", "ppt/diagrams/_rels/drawing1.xml.rels");

        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;
        smartArt.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
        smartArt.Parts.Should().NotContainKey(smartArt.DrawingPartPath);

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        session.EditSmartArt(shape.Id, candidate =>
        {
            var edit = SmartArtEditingPlanner.Apply(
                candidate.Data,
                SmartArtNodeEditIntent.ChangeText(candidate.Data!.Nodes[0].ModelId, "Alpha revised"));
            edit.Applied.Should().BeTrue(edit.Message);
            var dataRewrite = SmartArtEditingPlanner.RewriteDataPart(candidate);
            dataRewrite.Applied.Should().BeTrue(dataRewrite.Message);
            var cacheRefresh = SmartArtEditingPlanner.RegenerateDrawingCache(
                candidate,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                presentation.Theme);
            cacheRefresh.Applied.Should().BeTrue(cacheRefresh.Message);
            return true;
        }).Should().BeTrue();

        var updated = presentation.Slides[0].Shapes.First(s => s.Id == shape.Id).SmartArt!;
        updated.Parts.Should().ContainKey("ppt/diagrams/drawing1.xml");
        updated.FallbackShapes.Should().NotBeEmpty();

        session.Bus.Undo();
        presentation.Slides[0].Shapes.First(s => s.Id == shape.Id).SmartArt!
            .Parts.Should().NotContainKey("ppt/diagrams/drawing1.xml");
        session.Bus.Redo();
        presentation.Slides[0].Shapes.First(s => s.Id == shape.Id).SmartArt!
            .Parts.Should().ContainKey("ppt/diagrams/drawing1.xml");

        var roundTripPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(roundTripPath);
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopenedSmartArt.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
        reopenedSmartArt.FallbackShapes.Should().NotBeEmpty();
    }

    [Fact]
    public void EditingSession_AddsPictureSmartArtNodeWithPlaceholderAndKeepsExistingMedia()
    {
        var sourcePath = MakeSmartArtPptx(
            ["Alpha caption", "Beta caption"],
            pictureCaptionList: true,
            includeNodeImage: true);
        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.EditSmartArt(shape.Id, candidate =>
        {
            var edit = SmartArtEditingPlanner.Apply(
                candidate.Data,
                SmartArtNodeEditIntent.AddSiblingAfter(
                    candidate.Data!.Nodes[0].ModelId,
                    "Gamma caption"));
            if (!edit.Applied)
                return false;

            if (!SmartArtEditingPlanner.RewriteDataPart(candidate).Applied)
                return false;

            return SmartArtEditingPlanner.RegenerateDrawingCache(
                candidate,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                presentation.Theme).Applied;
        }).Should().BeTrue();

        var updated = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        updated.Data!.Nodes.Should().HaveCount(3);
        updated.Data.Nodes[1].Picture.Should().BeNull("new picture nodes start as an explicit add-picture slot");
        updated.FallbackShapes.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(2);
        updated.FallbackShapes.Select(s => s.PlainText).Should().Contain("Add picture");
        updated.PartRels[updated.DrawingPartPath!].Count(value => value == (byte)'i')
            .Should().BeGreaterThan(0, "existing picture relationships remain in the cache");

        var roundTripPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(roundTripPath);
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopenedSmartArt.Data!.Nodes.Should().HaveCount(3);
        reopenedSmartArt.Data.Nodes[1].Picture.Should().BeNull();
        reopenedSmartArt.FallbackShapes.Select(s => s.PlainText).Should().Contain("Add picture");
    }

    [Fact]
    public void Reader_SmartArt_StoresDiagramPartBytes()
    {
        var pptxPath = MakeSmartArtPptx(["A"]);
        var pres = PptxPackageReader.Read(pptxPath);

        var sa = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        // Data part should be captured
        sa.Parts.Should().ContainKey("ppt/diagrams/data1.xml");
        sa.Parts["ppt/diagrams/data1.xml"].Bytes.Length.Should().BeGreaterThan(0);

        // Drawing part path should be resolved
        sa.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
    }

    // ── Round-trip ──────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SmartArt_DiagramPartsPreserved()
    {
        // Read a hand-crafted SmartArt pptx
        var pptxPath = MakeSmartArtPptx(["Alpha", "Beta"]);
        var pres = PptxPackageReader.Read(pptxPath);

        // Write to a NEW path
        var roundTripPath = WriteToPptx(pres);

        // Open the round-tripped archive and verify diagram parts exist
        using var archive = new ZipArchive(File.OpenRead(roundTripPath), ZipArchiveMode.Read);
        archive.GetEntry("ppt/diagrams/data1.xml").Should().NotBeNull(
            "data part must be re-emitted in round-trip");
        archive.GetEntry("ppt/diagrams/layout1.xml").Should().NotBeNull(
            "layout part must be re-emitted");
        archive.GetEntry("ppt/diagrams/quickStyle1.xml").Should().NotBeNull(
            "quickStyle part must be re-emitted");
        archive.GetEntry("ppt/diagrams/colors1.xml").Should().NotBeNull(
            "colors part must be re-emitted");
        archive.GetEntry("ppt/diagrams/drawing1.xml").Should().NotBeNull(
            "drawing cache part must be re-emitted");
    }

    [Fact]
    public void RoundTrip_SmartArt_ShapeKindPreserved()
    {
        var pptxPath = MakeSmartArtPptx(["X"]);
        var pres     = PptxPackageReader.Read(pptxPath);
        var path2    = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path2);

        reloaded.Slides[0].Shapes
            .Should().Contain(s => s.Kind == SlideShapeKind.SmartArt,
                "SmartArt shape kind must survive read→write→read");
    }

    [Fact]
    public void RoundTrip_SmartArt_FallbackShapesPreserved()
    {
        var nodeTexts = new[] { "One", "Two", "Three" };
        var pptxPath  = MakeSmartArtPptx(nodeTexts);
        var pres      = PptxPackageReader.Read(pptxPath);
        var path2     = WriteToPptx(pres);
        var reloaded  = PptxPackageReader.Read(path2);

        var sa = reloaded.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        sa.FallbackShapes.Should().HaveCount(nodeTexts.Length);
    }

    [Fact]
    public void SmartArtColorPreset_WithoutNativeColorsPart_CreatesAndRoundTripsColorsPart()
    {
        var pptxPath = MakeSmartArtPptx(["One", "Two"], includeColors: false);
        var savedPath = Path.Combine(_tempDir, "smartart-created-colors.pptx");
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Parts.Values.Should().NotContain(part =>
            part.ContentType.Contains("diagramColors", StringComparison.OrdinalIgnoreCase));

        var result = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt,
            SmartArtColorPreset.SingleAccent,
            presentation.Theme!);

        result.Applied.Should().BeTrue();
        result.PartPath.Should().NotBeNull();
        smartArt.DiagramRelIds.Should().ContainKey("cs");

        PptxPackageWriter.Write(presentation, savedPath);

        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.Entries.SingleOrDefault(candidate =>
                candidate.FullName.Contains("colors-freep-", StringComparison.OrdinalIgnoreCase));
            entry.Should().NotBeNull("a missing source colors part must be materialized on save");
            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var document = XDocument.Parse(reader.ReadToEnd());
            var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
            document.Descendants(dgm + "fillClrLst").Should().ContainSingle();
        }

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reread.Colors.Should().NotBeNull();
        reread.Colors!.Palette.Should().ContainSingle();
        reread.DiagramRelIds.Should().ContainKey("cs");
    }

    [Theory]
    [InlineData(SmartArtLayoutPreset.BasicProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AccentProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AscendingProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.DescendingProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BasicTimeline, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.PhasedProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleAccentTimeline, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.StepDownProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ContinuousBlockProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.SegmentedProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ChevronProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BasicChevronProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ClosedChevronProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BendingProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AlternatingProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ArrowRibbon, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleArrowProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.IncreasingCircleProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.FunnelProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.PictureAccentProcess, SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalBoxList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalBlockList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalArrowList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.TrapezoidList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.GroupedList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalBulletList, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.VerticalPictureList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.MultidirectionalCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.ContinuousCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.GearCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.TextCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BlockCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.NonDirectionalCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.List2, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicBlockList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.StackedList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.DescendingBlockList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicPyramid, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PyramidList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.InvertedPyramid, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.RadialCycle, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicRadial, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.RadialCluster, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.RadialList, SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicMatrix, SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.TitledMatrix, SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.BasicRelationship, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.OpposingIdeas, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.ConvergingRadial, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.DivergingRadial, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.BasicVenn, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.RadialVenn, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.TargetList, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.StackedVenn, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.InterlockingRings, SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.BasicHierarchy, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.Hierarchy3, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.HorizontalHierarchy, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.OrgChart, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.NameAndTitleOrgChart, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.PictureCaptionList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureAccentList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureStack, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureLineup, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureStrips, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.ContinuousPictureList, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureGrid, SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.LabeledHierarchy, SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.TableHierarchy, SmartArtFamily.Hierarchy)]
    public void SmartArtLayoutPreset_PersistsNativeLayoutAndRereads(
        SmartArtLayoutPreset preset,
        SmartArtFamily expectedFamily)
    {
        var sourcePath = MakeSmartArtPptx(
            ["One", "Two"],
            pictureAccentProcess: preset == SmartArtLayoutPreset.PictureAccentProcess,
            pictureCaptionList: preset == SmartArtLayoutPreset.PictureCaptionList,
            pictureAccentList: preset == SmartArtLayoutPreset.PictureAccentList,
            pictureStack: preset == SmartArtLayoutPreset.PictureStack,
            pictureLineup: preset == SmartArtLayoutPreset.PictureLineup,
            pictureStrips: preset == SmartArtLayoutPreset.PictureStrips,
            verticalPictureList: preset == SmartArtLayoutPreset.VerticalPictureList,
            continuousPictureList: preset == SmartArtLayoutPreset.ContinuousPictureList,
            pictureGrid: preset == SmartArtLayoutPreset.PictureGrid,
            includeNodeImage: preset is (SmartArtLayoutPreset.PictureAccentProcess or SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.PictureStrips or SmartArtLayoutPreset.VerticalPictureList or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid));
        var savedPath = Path.Combine(_tempDir, $"smartart-layout-{preset}.pptx");
        var presentation = PptxPackageReader.Read(sourcePath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset);

        result.Applied.Should().BeTrue(result.Message);
        result.Family.Should().Be(expectedFamily);
        PptxPackageWriter.Write(presentation, savedPath);

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reread.Data.Should().NotBeNull();
        reread.Data!.Family.Should().Be(expectedFamily);
        reread.Data.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
    }

    [Fact]
    public void SmartArtLayoutPreset_PersistsNativeLayoutWhenLiveDataIsUnavailable()
    {
        var sourcePath = MakeSmartArtPptx(["One", "Two"]);
        var savedPath = Path.Combine(_tempDir, "smartart-layout-cached-only.pptx");
        var presentation = PptxPackageReader.Read(sourcePath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        smartArt.Data = null;
        var originalFallbackCount = smartArt.FallbackShapes.Count;

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.BasicProcess);

        result.Applied.Should().BeTrue(result.Message);
        smartArt.Data.Should().BeNull();
        smartArt.FallbackShapes.Should().HaveCount(originalFallbackCount);
        PptxPackageWriter.Write(presentation, savedPath);

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reread.Data.Should().NotBeNull();
        reread.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        reread.FallbackShapes.Should().HaveCount(originalFallbackCount);
    }

    [Theory]
    [InlineData(SmartArtQuickStylePreset.SimpleFill, "simple1", "Simple Fill")]
    [InlineData(SmartArtQuickStylePreset.WhiteOutline, "simple2", "White Outline")]
    [InlineData(SmartArtQuickStylePreset.SubtleEffect, "simple3", "Subtle Effect")]
    [InlineData(SmartArtQuickStylePreset.ModerateEffect, "simple4", "Moderate Effect")]
    [InlineData(SmartArtQuickStylePreset.IntenseEffect, "simple5", "Intense Effect")]
    [InlineData(SmartArtQuickStylePreset.Polished, "3d1", "Polished")]
    [InlineData(SmartArtQuickStylePreset.Inset, "3d2", "Inset")]
    [InlineData(SmartArtQuickStylePreset.Cartoon, "3d3", "Cartoon")]
    [InlineData(SmartArtQuickStylePreset.Powder, "3d4", "Powder")]
    [InlineData(SmartArtQuickStylePreset.BrickScene, "3d5", "Brick Scene")]
    [InlineData(SmartArtQuickStylePreset.FlatScene, "3d6", "Flat Scene")]
    [InlineData(SmartArtQuickStylePreset.MetallicScene, "3d7", "Metallic Scene")]
    [InlineData(SmartArtQuickStylePreset.SunsetScene, "3d8", "Sunset Scene")]
    [InlineData(SmartArtQuickStylePreset.BirdsEyeScene, "3d9", "Bird's Eye Scene")]
    public void SmartArtQuickStylePreset_PersistsNativeStyleAndRereads(
        SmartArtQuickStylePreset preset,
        string expectedStyle,
        string expectedTitle)
    {
        var sourcePath = MakeSmartArtPptx(["One", "Two"]);
        var savedPath = Path.Combine(_tempDir, $"smartart-style-{preset}.pptx");
        var presentation = PptxPackageReader.Read(sourcePath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset);

        result.Applied.Should().BeTrue(result.Message);
        result.StyleUniqueId.Should().EndWith($"/quickstyle/{expectedStyle}");
        PptxPackageWriter.Write(presentation, savedPath);

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reread.QuickStyle.Should().NotBeNull();
        reread.QuickStyle!.UniqueId.Should().Be(result.StyleUniqueId);
        reread.QuickStyle.Title.Should().Be(expectedTitle);
        reread.QuickStyle.Category.Should().Be(
            preset is SmartArtQuickStylePreset.Polished
                or SmartArtQuickStylePreset.Inset
                or SmartArtQuickStylePreset.Cartoon
                or SmartArtQuickStylePreset.Powder
                or SmartArtQuickStylePreset.BrickScene
                or SmartArtQuickStylePreset.FlatScene
                or SmartArtQuickStylePreset.MetallicScene
                or SmartArtQuickStylePreset.SunsetScene
                or SmartArtQuickStylePreset.BirdsEyeScene
                ? "3D"
                : "simple");
        reread.QuickStyle.StyleLabels.Should().Contain("node0");
    }

    [Fact]
    public void SmartArtQuickStylePreset_RefreshesUnsupportedLiveLayoutFallback()
    {
        var sourcePath = MakeSmartArtPptx(
            ["Imported A", "Imported B", "Imported C"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/accentProcess");
        var savedPath = Path.Combine(_tempDir, "smartart-unsupported-style-cache.pptx");
        var presentation = PptxPackageReader.Read(sourcePath);
        var smartArt = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse();
        var before = smartArt.FallbackShapes
            .Select(shape => (shape.Fill as ShapeFill.Solid)?.Color.Resolved)
            .ToArray();

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(
            smartArt,
            SmartArtQuickStylePreset.SimpleFill,
            presentation.Theme);

        result.Applied.Should().BeTrue(result.Message);
        var after = smartArt.FallbackShapes
            .Select(shape => (shape.Fill as ShapeFill.Solid)?.Color.Resolved)
            .ToArray();
        after.Should().NotEqual(before);

        PptxPackageWriter.Write(presentation, savedPath);
        PptxPackageReader.Read(savedPath).Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!.QuickStyle!.UniqueId
            .Should().Be(result.StyleUniqueId);
    }

    [Fact]
    public void SmartArtColorPreset_RefreshesUnsupportedLiveLayoutFallback()
    {
        var sourcePath = MakeSmartArtPptx(
            ["Imported A", "Imported B", "Imported C"],
            includeColors: false);
        var presentation = PptxPackageReader.Read(sourcePath);
        var smartArt = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported = false;
        var before = smartArt.FallbackShapes
            .Select(shape => (shape.Fill as ShapeFill.Solid)?.Color.Resolved)
            .ToArray();

        var result = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt,
            SmartArtColorPreset.MonochromaticAccent2,
            presentation.Theme!);

        result.Applied.Should().BeTrue(result.Message);
        smartArt.FallbackShapes
            .Select(shape => (shape.Fill as ShapeFill.Solid)?.Color.Resolved)
            .Should().NotEqual(before);
    }

    // ── Compositor ───────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SmartArt_SharedDataPartRewritePersistsEditedOutline()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-edited-outline.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        SmartArtEditingPlanner.Apply(smartArt.Data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(smartArt.Data, SmartArtNodeEditIntent.Demote("manager"))
            .Applied.Should().BeTrue();
        var rewrite = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        rewrite.Applied.Should().BeTrue();
        rewrite.ConnectionCount.Should().Be(1);

        PptxPackageWriter.Write(presentation, savedPath);

        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.GetEntry("ppt/diagrams/data1.xml");
            entry.Should().NotBeNull("the shared SmartArt data-part rewrite feeds the existing PPTX writer");

            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var doc = XDocument.Parse(reader.ReadToEnd());
            var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
            var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

            doc.Descendants(a + "t").Select(t => t.Value)
                .Should().Contain("Delivery Lead");
            doc.Descendants(dgm + "cxn")
                .Select(cxn => (
                    Type: (string?)cxn.Attribute("type"),
                    Source: (string?)cxn.Attribute("srcId"),
                    Destination: (string?)cxn.Attribute("destId")))
                .Should().ContainSingle()
                .Which.Should().Be(("parOf", "root", "manager"));
        }

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reread.Data!.Nodes.Should().ContainSingle("the saved parOf connection rebuilds one hierarchy root");
        reread.Data.Nodes[0].Text.Should().Be("Leader");
        reread.Data.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Delivery Lead");
    }

    [Fact]
    public void RoundTrip_SmartArt_SharedDrawingCacheRegenerationPersistsEditedOutline()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-regenerated-cache.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;

        SmartArtEditingPlanner.Apply(smartArt.Data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(smartArt.Data, SmartArtNodeEditIntent.Demote("manager"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            presentation.Theme!);
        cache.Applied.Should().BeTrue();
        cache.ShapeCount.Should().Be(3, "the shared hierarchy planner emits two node boxes plus one connector");
        smartArt.FallbackShapes.Where(s => s.TextBody is not null)
            .Should().OnlyContain(s => s.Name.StartsWith("SmartArt_Hierarchy1_", StringComparison.Ordinal));
        smartArt.FallbackShapes.Where(s => s.TextBody is null)
            .Should().ContainSingle(s => s.Name.StartsWith("SmartArt_Hierarchy1_Connector_", StringComparison.Ordinal));

        PptxPackageWriter.Write(presentation, savedPath);

        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.GetEntry("ppt/diagrams/drawing1.xml");
            entry.Should().NotBeNull("the regenerated SmartArt drawing cache feeds the existing PPTX writer");

            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var doc = XDocument.Parse(reader.ReadToEnd());
            var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
            var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

            doc.Root!.Name.Should().Be(dsp + "drawing");
            doc.Descendants(dsp + "sp").Should().HaveCount(3);
            doc.Descendants(a + "t").Select(t => t.Value)
                .Should().Contain(["Leader", "Delivery Lead"])
                .And.NotContain("Manager");
        }

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reread.FallbackShapes.Select(s => s.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Leader", "Delivery Lead");
    }

    [Fact]
    public void RoundTrip_SmartArt_BendingProcessCachePreservesConnectorFlipDirections()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-bending-process-cache.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;

        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            presentation.Theme!);
        cache.Applied.Should().BeTrue();
        cache.ShapeCount.Should().Be(5, "three bending-process boxes plus two diagonal connectors are cached");

        PptxPackageWriter.Write(presentation, savedPath);

        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.GetEntry("ppt/diagrams/drawing1.xml");
            entry.Should().NotBeNull();
            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var document = XDocument.Parse(reader.ReadToEnd());
            var lineTransforms = document.Descendants(dsp + "sp")
                .Where(element => element.Descendants(a + "prstGeom")
                    .Any(geometry => geometry.Attribute("prst")?.Value == "line"))
                .Select(element => element.Descendants(a + "xfrm").First())
                .ToList();

            lineTransforms.Should().HaveCount(2);
            lineTransforms[0].Attribute("flipH")?.Value.Should().BeNull();
            lineTransforms[0].Attribute("flipV")?.Value.Should().BeNull();
            lineTransforms[1].Attribute("flipH")?.Value.Should().BeNull();
            lineTransforms[1].Attribute("flipV")?.Value.Should().Be("1");
        }

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reread.FallbackShapes
            .Where(fallback => fallback.AutoShapeKind == DrawingShapeKind.Line)
            .Select(fallback => (fallback.FlipH, fallback.FlipV))
            .Should().Equal((false, false), (false, true));
    }

    [Fact]
    public void SmartArtDataRewrite_PreservesAuthoredDataModelMetadataAndExtensions()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: []);
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        var dataPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var extension = XNamespace.Get("urn:freep:smartart-test");

        using var sourceStream = new MemoryStream(dataPart.Bytes, writable: false);
        var source = XDocument.Load(sourceStream);
        source.Root!.SetAttributeValue("modelId", "authored-model-id");
        source.Root.Add(new XElement(dgm + "extLst",
            new XElement(dgm + "ext",
                new XAttribute("uri", "urn:freep:preserve-me"),
                new XElement(extension + "authoredState", new XAttribute("value", "keep")))));
        using (var stream = new MemoryStream())
        {
            source.Save(stream);
            dataPart.Bytes = stream.ToArray();
        }

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        using var rewrittenStream = new MemoryStream(dataPart.Bytes, writable: false);
        var rewritten = XDocument.Load(rewrittenStream);
        rewritten.Root!.Attribute("modelId")?.Value.Should().Be("authored-model-id");
        rewritten.Descendants(extension + "authoredState")
            .Single().Attribute("value")?.Value.Should().Be("keep");
        rewritten.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "t")
            .Select(element => element.Value)
            .Should().Contain("Delivery Lead");

        var savedPath = Path.Combine(_tempDir, "smartart-data-model-metadata-roundtrip.pptx");
        PptxPackageWriter.Write(presentation, savedPath);
        var rereadPart = PptxPackageReader.Read(savedPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!.Parts.Values
            .Single(part => part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        using var rereadStream = new MemoryStream(rereadPart.Bytes, writable: false);
        var reread = XDocument.Load(rereadStream);
        reread.Root!.Attribute("modelId")?.Value.Should().Be("authored-model-id");
        reread.Descendants(extension + "authoredState")
            .Single().Attribute("value")?.Value.Should().Be("keep");
    }

    [Fact]
    public void SmartArtDataRewrite_PreservesAuthoredNodePayloadWhenTextChanges()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: [("root", "manager")]);
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        var dataPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var extension = XNamespace.Get("urn:freep:smartart-node-test");

        using var sourceStream = new MemoryStream(dataPart.Bytes, writable: false);
        var source = XDocument.Load(sourceStream);
        var managerPoint = source.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "manager");
        var managerConnection = source.Descendants(dgm + "cxn")
            .Single(connection => (string?)connection.Attribute("destId") == "manager");
        managerPoint.SetAttributeValue("phldr", "1");
        managerPoint.Add(new XElement(dgm + "prSet",
            new XAttribute("phldr", "1"),
            new XElement(dgm + "extLst",
                new XElement(dgm + "ext",
                    new XAttribute("uri", "urn:freep:node-payload"),
                    new XElement(extension + "nodeState", new XAttribute("value", "keep"))))));
        managerConnection.SetAttributeValue("modelId", "authored-connection-id");
        managerConnection.Add(new XElement(extension + "connectionState",
            new XAttribute("value", "keep")));
        dataPart.Bytes = Encoding.UTF8.GetBytes(source.ToString(SaveOptions.DisableFormatting));

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        var rewritten = XDocument.Parse(Encoding.UTF8.GetString(dataPart.Bytes));
        var rewrittenManager = rewritten.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "manager");
        rewrittenManager.Attribute("phldr")?.Value.Should().Be("1");
        rewrittenManager.Element(dgm + "prSet")
            ?.Element(dgm + "extLst")
            ?.Descendants(extension + "nodeState")
            .Single().Attribute("value")?.Value.Should().Be("keep");
        rewrittenManager.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "t")
            .Single().Value.Should().Be("Delivery Lead");
        var rewrittenConnection = rewritten.Descendants(dgm + "cxn")
            .Single(connection => (string?)connection.Attribute("destId") == "manager");
        rewrittenConnection.Attribute("modelId")?.Value.Should().Be("authored-connection-id");
        rewrittenConnection.Descendants(extension + "connectionState")
            .Single().Attribute("value")?.Value.Should().Be("keep");

        var savedPath = Path.Combine(_tempDir, "smartart-node-payload-roundtrip.pptx");
        PptxPackageWriter.Write(presentation, savedPath);
        var rereadPart = PptxPackageReader.Read(savedPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!.Parts.Values
            .Single(part => part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var reread = XDocument.Parse(Encoding.UTF8.GetString(rereadPart.Bytes));
        var rereadManager = reread.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "manager");
        rereadManager.Attribute("phldr")?.Value.Should().Be("1");
        rereadManager.Descendants(extension + "nodeState")
            .Single().Attribute("value")?.Value.Should().Be("keep");
        var rereadConnection = reread.Descendants(dgm + "cxn")
            .Single(connection => (string?)connection.Attribute("destId") == "manager");
        rereadConnection.Attribute("modelId")?.Value.Should().Be("authored-connection-id");
        rereadConnection.Descendants(extension + "connectionState")
            .Single().Attribute("value")?.Value.Should().Be("keep");
    }

    [Fact]
    public void SmartArtDataRewrite_PreservesAuthoredNodeTextBodyPropertiesWhenTextChanges()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: [("root", "manager")]);
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        var dataPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        using var sourceStream = new MemoryStream(dataPart.Bytes, writable: false);
        var source = XDocument.Load(sourceStream);
        var managerPoint = source.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "manager");
        var text = managerPoint.Element(dgm + "t")!;
        var bodyPr = text.Element(a + "bodyPr");
        if (bodyPr is null)
        {
            bodyPr = new XElement(a + "bodyPr");
            text.AddFirst(bodyPr);
        }
        bodyPr.SetAttributeValue("wrap", "square");
        text.Element(a + "p")!.AddFirst(new XElement(a + "pPr", new XAttribute("lvl", "2")));
        text.Element(a + "p")!.Element(a + "r")!.AddFirst(
            new XElement(a + "rPr", new XAttribute("lang", "fr-FR"), new XAttribute("sz", "2400")));
        using (var rewrittenSourceStream = new MemoryStream())
        {
            source.Save(rewrittenSourceStream, SaveOptions.DisableFormatting);
            dataPart.Bytes = rewrittenSourceStream.ToArray();
        }

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        using var rewrittenStream = new MemoryStream(dataPart.Bytes, writable: false);
        var rewritten = XDocument.Load(rewrittenStream);
        var rewrittenText = rewritten.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "manager")
            .Element(dgm + "t")!;
        rewrittenText.Element(a + "bodyPr")!.Attribute("wrap")?.Value.Should().Be("square");
        rewrittenText.Element(a + "p")!.Element(a + "pPr")!.Attribute("lvl")?.Value.Should().Be("2");
        rewrittenText.Element(a + "p")!.Element(a + "r")!.Element(a + "rPr")!
            .Attribute("lang")?.Value.Should().Be("fr-FR");
        rewrittenText.Element(a + "p")!.Element(a + "r")!.Element(a + "rPr")!
            .Attribute("sz")?.Value.Should().Be("2400");
        rewrittenText.Descendants(a + "t").Single().Value.Should().Be("Delivery Lead");
    }

    [Fact]
    public void SmartArtDataRewrite_PreservesRichRunsOnUnchangedNodes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: [("root", "manager")]);
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        var dataPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        using var sourceStream = new MemoryStream(dataPart.Bytes, writable: false);
        var source = XDocument.Load(sourceStream);
        var rootText = source.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "root")
            .Element(dgm + "t")!;
        var rootParagraph = rootText.Element(a + "p")!;
        rootParagraph.Element(a + "r")!.ReplaceWith(
            new XElement(a + "r",
                new XElement(a + "rPr", new XAttribute("b", "1")),
                new XElement(a + "t", "Lead")),
            new XElement(a + "r",
                new XElement(a + "rPr", new XAttribute("i", "1")),
                new XElement(a + "t", "er")));
        using (var rewrittenSourceStream = new MemoryStream())
        {
            source.Save(rewrittenSourceStream, SaveOptions.DisableFormatting);
            dataPart.Bytes = rewrittenSourceStream.ToArray();
        }

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        using var rewrittenStream = new MemoryStream(dataPart.Bytes, writable: false);
        var rewritten = XDocument.Load(rewrittenStream);
        var rewrittenRuns = rewritten.Descendants(dgm + "pt")
            .Single(point => (string?)point.Attribute("modelId") == "root")
            .Element(dgm + "t")!
            .Element(a + "p")!
            .Elements(a + "r")
            .ToArray();

        rewrittenRuns.Should().HaveCount(2);
        rewrittenRuns[0].Element(a + "rPr")!.Attribute("b")?.Value.Should().Be("1");
        rewrittenRuns[1].Element(a + "rPr")!.Attribute("i")?.Value.Should().Be("1");
        rewrittenRuns.Select(run => run.Element(a + "t")!.Value)
            .Should().Equal("Lead", "er");
    }

    [Fact]
    public void SmartArtDrawingCacheRegeneration_PreservesAuthoredShellMetadataAndExtensions()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-drawing-shell-roundtrip.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArtShape = presentation.Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartArtShape.SmartArt!;
        var drawingPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramDrawing", StringComparison.OrdinalIgnoreCase));
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var extension = XNamespace.Get("urn:freep:smartart-drawing-test");

        using (var sourceStream = new MemoryStream(drawingPart.Bytes, writable: false))
        {
            var source = XDocument.Load(sourceStream);
            source.Root!.SetAttributeValue("cacheId", "authored-cache-id");
            source.Root.Add(new XElement(dsp + "extLst",
                new XElement(dsp + "ext",
                    new XAttribute("uri", "urn:freep:preserve-drawing-me"),
                    new XElement(extension + "cacheHint", new XAttribute("value", "keep")))));

            using var rewrittenStream = new MemoryStream();
            source.Save(rewrittenStream);
            drawingPart.Bytes = rewrittenStream.ToArray();
        }

        SmartArtEditingPlanner.Apply(smartArt.Data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            presentation.Theme!).Applied.Should().BeTrue();

        PptxPackageWriter.Write(presentation, savedPath);

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        var rereadDrawing = reread.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramDrawing", StringComparison.OrdinalIgnoreCase));
        using var rereadStream = new MemoryStream(rereadDrawing.Bytes, writable: false);
        var rereadDocument = XDocument.Load(rereadStream);
        rereadDocument.Root!.Attribute("cacheId")?.Value.Should().Be("authored-cache-id");
        rereadDocument.Descendants(extension + "cacheHint")
            .Single().Attribute("value")?.Value.Should().Be("keep");
        rereadDocument.Descendants(XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram") + "sp")
            .SelectMany(shape => shape.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "t"))
            .Select(text => text.Value)
            .Should().Contain("Delivery Lead")
            .And.NotContain("Manager");
    }

    [Fact]
    public void RoundTrip_SmartArt_TextPaneOutlineRegeneratesDataPartAndDrawingCache()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("root", "Leader"), ("manager", "Manager"), ("legacy", "Legacy")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-text-pane-outline-cache.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;

        var textPane = SmartArtEditingPlanner.ApplyTextPaneOutline(smartArt.Data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Delivery Lead", 1, ModelId: "manager"),
            new("Release", 2)
        ]);
        textPane.Applied.Should().BeTrue();
        textPane.Outline.Select(item => (item.Text, item.Level))
            .Should().Equal(("Executive", 0), ("Delivery Lead", 1), ("Release", 2));

        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            presentation.Theme!).Applied.Should().BeTrue();

        PptxPackageWriter.Write(presentation, savedPath);

        var reread = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        reread.Data!.Nodes.Should().ContainSingle();
        reread.Data.Nodes[0].Text.Should().Be("Executive");
        reread.Data.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Delivery Lead");
        reread.Data.Nodes[0].Children[0].Children.Should().ContainSingle().Which.Text.Should().Be("Release");
        reread.FallbackShapes.Select(s => s.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Executive", "Delivery Lead", "Release");
    }

    [Fact]
    public void Compositor_SmartArt_WithFallbackShapes_EmitsShapeOps()
    {
        var smart = new SmartArtShape();
        for (int i = 0; i < 3; i++)
        {
            smart.FallbackShapes.Add(new SlideShape
            {
                Id            = (uint)(i + 1),
                Kind          = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu    = i * 914400L,
                OffsetYEmu    = 457200L,
                ExtentCxEmu   = 914400L,
                ExtentCyEmu   = 457200L
            });
        }

        var container = new SlideShape
        {
            Id          = 10,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 9144000L,
            ExtentCyEmu = 6858000L,
            SmartArt    = smart
        };

        var pres = FreeP.Core.Model.Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        // Background + 3 shape ops
        ops.Should().HaveCount(1 + 3, "one DrawOp.Shape per fallback shape");
        ops.Skip(1).Should().AllBeOfType<DrawOp.Shape>("each fallback shape renders as a DrawOp.Shape");
    }

    [Fact]
    public void Compositor_SmartArt_CachedDrawing_UsesGraphicFrameOffset()
    {
        var smart = new SmartArtShape();
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400
        });

        var pres = FreeP.Core.Model.Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 2_743_200,
            OffsetYEmu = 1_371_600,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
            SmartArt = smart
        });

        var op = SlideCompositor.Compose(pres, pres.Slides[0]).OfType<DrawOp.Shape>().Single();
        op.BoundsDip.X.Should().Be(384.0);
        op.BoundsDip.Y.Should().Be(192.0);
    }

    [Fact]
    public void Compositor_SmartArt_WithNoFallbackShapes_EmitsPlaceholderRect()
    {
        var smart = new SmartArtShape(); // empty FallbackShapes

        var container = new SlideShape
        {
            Id          = 20,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = 914400L,
            OffsetYEmu  = 457200L,
            ExtentCxEmu = 4572000L,
            ExtentCyEmu = 1371600L,
            SmartArt    = smart
        };

        var pres = FreeP.Core.Model.Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        // Background + placeholder rectangle
        ops.Should().HaveCount(2);
        ops[1].Should().BeOfType<DrawOp.Shape>("no fallback shapes → grey placeholder rectangle");
    }

    // ── S1: correct quickStyle content type ──────────────────────────────────────

    /// <summary>
    /// S1: After a round-trip the writer must emit the ECMA-376-correct content type
    /// "...diagramStyle+xml" (not the incorrect "...diagramQuickStyle+xml") for the
    /// quickStyle (qs) diagram part.
    /// </summary>
    [Fact]
    public void RoundTrip_SmartArt_QuickStyleContentType_IsCorrect()
    {
        const string correctQsCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
        const string wrongQsCT    = "application/vnd.openxmlformats-officedocument.drawingml.diagramQuickStyle+xml";

        var pptxPath      = MakeSmartArtPptx(["Node1"]);
        var pres          = PptxPackageReader.Read(pptxPath);
        var roundTripPath = WriteToPptx(pres);

        // Inspect [Content_Types].xml inside the written archive
        using var archive = new ZipArchive(File.OpenRead(roundTripPath), ZipArchiveMode.Read);
        var ctEntry = archive.GetEntry("[Content_Types].xml");
        ctEntry.Should().NotBeNull("[Content_Types].xml must be present");

        using var stream = ctEntry!.Open();
        var ctDoc = XDocument.Load(stream);
        var ctNs  = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");

        var allCTs = ctDoc.Descendants(ctNs + "Override")
            .Select(e => e.Attribute("ContentType")?.Value ?? "")
            .ToList();

        allCTs.Should().Contain(correctQsCT,
            "the quickStyle part must carry the ECMA-376-correct diagramStyle+xml content type");
        allCTs.Should().NotContain(wrongQsCT,
            "the incorrect diagramQuickStyle+xml content type must not appear");
    }

    // ── S4: no duplicate relIds when source diagram relId collides with rId1 ─────

    /// <summary>
    /// S4: If the source .pptx uses "rId1" as a diagram relId (which collides with the
    /// slide layout rel), the writer must remap it to a fresh id so the slide rels XML
    /// contains no duplicate Id attributes.
    /// </summary>
    [Fact]
    public void RoundTrip_SmartArt_DiagramRelId_CollisionWithRId1_Remapped()
    {
        // MakeSmartArtPptx uses rIdDm1/rIdLo1/rIdQs1/rIdCs1 in the source.
        // After round-trip we must verify NO duplicate Relationship/@Id values appear.
        var pptxPath      = MakeSmartArtPptx(["A", "B"]);
        var pres          = PptxPackageReader.Read(pptxPath);

        // Manually force a relId collision: rename one diagram relId to "rId1"
        var smartArt = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        // Change dm relId to rId1 so it would collide with the layout rel
        if (smartArt.DiagramRelIds.ContainsKey("dm"))
            smartArt.DiagramRelIds["dm"] = "rId1";

        var roundTripPath = WriteToPptx(pres);

        using var archive = new ZipArchive(File.OpenRead(roundTripPath), ZipArchiveMode.Read);
        var relsEntry = archive.GetEntry("ppt/slides/_rels/slide1.xml.rels");
        relsEntry.Should().NotBeNull("slide rels must be present");

        using var stream = relsEntry!.Open();
        var relsDoc = XDocument.Load(stream);
        var pkgNs   = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        var ids     = relsDoc.Descendants(pkgNs + "Relationship")
            .Select(e => e.Attribute("Id")?.Value)
            .ToList();

        ids.Should().OnlyHaveUniqueItems("slide rels must not contain duplicate relationship Ids");
    }

    // ── S2: SmartArt with missing data part is dropped (no dangling frame) ───────

    /// <summary>
    /// S2: If the SmartArt's data (dm) part is absent (e.g. bytes were unreadable at
    /// read time), the writer must NOT emit a graphicFrame with a dangling r:dm attribute.
    /// The shape should be omitted entirely from the slide XML.
    /// </summary>
    [Fact]
    public void RoundTrip_SmartArt_MissingDataPart_DropsDanglingGraphicFrame()
    {
        var pptxPath = MakeSmartArtPptx(["X"]);
        var pres     = PptxPackageReader.Read(pptxPath);

        // Remove the data (dm) part from the SmartArt model to simulate an unreadable part
        var smartArt = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        var dmPath = smartArt.Parts.Keys
            .FirstOrDefault(k => k.Contains("data", StringComparison.OrdinalIgnoreCase));
        if (dmPath is not null)
            smartArt.Parts.Remove(dmPath);

        var roundTripPath = WriteToPptx(pres);

        using var archive = new ZipArchive(File.OpenRead(roundTripPath), ZipArchiveMode.Read);
        var slideEntry = archive.GetEntry("ppt/slides/slide1.xml");
        slideEntry.Should().NotBeNull();

        using var stream = slideEntry!.Open();
        var slideDoc = XDocument.Load(stream);
        var pNs      = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var rNs      = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        // No graphicFrame element should be present (SmartArt without data must be dropped)
        var graphicFrames = slideDoc.Descendants(pNs + "graphicFrame").ToList();
        graphicFrames.Should().BeEmpty(
            "SmartArt with no data part must not produce a graphicFrame with a dangling r:dm attribute");

        // Also verify the slide rels don't reference a non-existent diagram data part
        var relsEntry = archive.GetEntry("ppt/slides/_rels/slide1.xml.rels");
        if (relsEntry is not null)
        {
            using var rStream  = relsEntry.Open();
            var relsDoc        = XDocument.Load(rStream);
            var pkgNs          = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var diagramDataRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
            var dmRels         = relsDoc.Descendants(pkgNs + "Relationship")
                .Where(e => e.Attribute("Type")?.Value == diagramDataRel)
                .ToList();
            dmRels.Should().BeEmpty(
                "when data part is missing no diagramData relationship should be written to slide rels");
        }
    }

    // ── Theme 17: SmartArtData parse tests ─────────────────────────────────────────────

    /// <summary>
    /// Helper that builds a minimal pptx with a SmartArt whose data1.xml has
    /// a real node tree (ptLst + cxnLst parOf connections) and layout1.xml has
    /// a recognisable uniqueId.
    /// </summary>
    private string MakeSmartArtPptxWithNodeTree(
        string layoutUniqueId,
        (string id, string text)[] nodes,
        (string srcId, string destId)[] parOfConnections,
        XDocument? quickStyleXml = null,
        XDocument? colorsXml = null,
        string[]? assistantNodeIds = null,
        bool includeCachedHierarchy3Connectors = false)
    {
        var path = Path.Combine(_tempDir, $"smartart_tree_{Guid.NewGuid():N}.pptx");

        var pNs   = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var aNs   = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var rNs   = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var pkgNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        const string diagramDataCT    = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
        const string diagramLayoutCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
        const string diagramQsCT      = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
        const string diagramColorsCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
        const string diagramDrawingCT = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

        const string dmRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
        const string loRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
        const string qsRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
        const string csRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
        const string dgmDrawRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

        var assistantIds = new HashSet<string>(assistantNodeIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Build data1.xml with ptLst + cxnLst
        var ptElems = nodes.Select(n =>
            new XElement(dgmNs + "pt",
                new XAttribute("modelId", n.id),
                new XAttribute("type", assistantIds.Contains(n.id) ? "asst" : "node"),
                new XElement(dgmNs + "t",
                    n.text.Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Replace('\r', '\n')
                        .Split('\n')
                        .Select(line => new XElement(aNs + "p",
                            new XElement(aNs + "r",
                                new XElement(aNs + "t", line)))))));

        var cxnElems = parOfConnections.Select(c =>
            new XElement(dgmNs + "cxn",
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", c.srcId),
                new XAttribute("destId", c.destId)));

        var dataXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XElement(dgmNs + "ptLst", ptElems),
                new XElement(dgmNs + "cxnLst", cxnElems)));

        // Build layout1.xml with the given uniqueId
        var layoutXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute("uniqueId", layoutUniqueId)));

        var qsXml = quickStyleXml ?? new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(dgmNs + "styleDef",   new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));
        var colorsPartXml = colorsXml ?? new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(dgmNs + "colorsDef", new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));

        var fallbackEls = new List<XElement>();
        if (includeCachedHierarchy3Connectors)
        {
            layoutUniqueId.Should().EndWith("/hierarchy3");

            var shapeId = 1;
            foreach (var node in nodes)
            {
                fallbackEls.Add(new XElement(dspNs + "sp",
                    new XElement(dspNs + "nvSpPr",
                        new XElement(dspNs + "cNvPr",
                            new XAttribute("id", shapeId++),
                            new XAttribute("name", $"Node {node.id}")),
                        new XElement(dspNs + "cNvSpPr")),
                    new XElement(dspNs + "spPr",
                        new XElement(aNs + "xfrm",
                            new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(aNs + "ext", new XAttribute("cx", "914400"), new XAttribute("cy", "457200"))),
                        new XElement(aNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(aNs + "avLst"))),
                    new XElement(dspNs + "txBody",
                        new XElement(aNs + "bodyPr"),
                        new XElement(aNs + "lstStyle"),
                        new XElement(aNs + "p",
                            new XElement(aNs + "r",
                                new XElement(aNs + "rPr", new XAttribute("lang", "en-US")),
                                new XElement(aNs + "t", node.text))))));
            }

            foreach (var connection in parOfConnections)
            {
                fallbackEls.Add(new XElement(dspNs + "sp",
                    new XElement(dspNs + "nvSpPr",
                        new XElement(dspNs + "cNvPr",
                            new XAttribute("id", shapeId++),
                            new XAttribute("name", $"Connector {connection.srcId}-{connection.destId}")),
                        new XElement(dspNs + "cNvSpPr")),
                    new XElement(dspNs + "spPr",
                        new XElement(aNs + "xfrm",
                            new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(aNs + "ext", new XAttribute("cx", "914400"), new XAttribute("cy", "914400"))))));
            }
        }

        var dspXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dspNs + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", dspNs.NamespaceName),
                new XElement(dspNs + "spTree", fallbackEls)));

        static byte[] ToBytes(XDocument doc)
        {
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        static byte[] MakeRels(XNamespace ns, params (string id, string type, string target)[] rels)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ns + "Relationships",
                    rels.Select(r => new XElement(ns + "Relationship",
                        new XAttribute("Id", r.id),
                        new XAttribute("Type", r.type),
                        new XAttribute("Target", r.target)))));
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        // Reuse the same slide XML structure as in MakeSmartArtPptx
        var slideXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sld",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))),
                        new XElement(pNs + "graphicFrame",
                            new XElement(pNs + "nvGraphicFramePr",
                                new XElement(pNs + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "SmartArt 1")),
                                new XElement(pNs + "cNvGraphicFramePr"),
                                new XElement(pNs + "nvPr")),
                            new XElement(pNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "914400"), new XAttribute("y", "457200")),
                                new XElement(aNs + "ext", new XAttribute("cx", "7315200"), new XAttribute("cy", "3657600"))),
                            new XElement(aNs + "graphic",
                                new XElement(aNs + "graphicData",
                                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/diagram"),
                                    new XElement(dgmNs + "relIds",
                                        new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                                        new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                                        new XAttribute(rNs + "dm", "rIdDm1"),
                                        new XAttribute(rNs + "lo", "rIdLo1"),
                                        new XAttribute(rNs + "qs", "rIdQs1"),
                                        new XAttribute(rNs + "cs", "rIdCs1")))))))));

        using var zipStream = File.Create(path);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        void WriteEntry(string entryPath, byte[] bytes)
        {
            var e = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
            using var s = e.Open();
            s.Write(bytes);
        }
        void WriteXml(string entryPath, XDocument doc) => WriteEntry(entryPath, ToBytes(doc));

        // [Content_Types].xml
        var ctNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        WriteXml("[Content_Types].xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ctNs + "Types",
                new XElement(ctNs + "Default", new XAttribute("Extension", "rels"),  new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default", new XAttribute("Extension", "xml"),   new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/presentation.xml"),                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slides/slide1.xml"),               new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/theme/theme1.xml"),                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slideLayouts/slideLayout1.xml"),   new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/slideMasters/slideMaster1.xml"),   new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/data1.xml"),      new XAttribute("ContentType", diagramDataCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/layout1.xml"),    new XAttribute("ContentType", diagramLayoutCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/quickStyle1.xml"), new XAttribute("ContentType", diagramQsCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/colors1.xml"),    new XAttribute("ContentType", diagramColorsCT)),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/drawing1.xml"),   new XAttribute("ContentType", diagramDrawingCT)))));

        WriteEntry("_rels/.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "ppt/presentation.xml")));

        var presNs = pNs;
        WriteXml("ppt/presentation.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(presNs + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(presNs + "sldMasterIdLst",
                    new XElement(presNs + "sldMasterId", new XAttribute("id", 2147483648u), new XAttribute(rNs + "id", "rId2"))),
                new XElement(presNs + "sldIdLst",
                    new XElement(presNs + "sldId", new XAttribute("id", 256), new XAttribute(rNs + "id", "rId1"))),
                new XElement(presNs + "sldSz", new XAttribute("cx", 9144000), new XAttribute("cy", 6858000)),
                new XElement(presNs + "notesSz", new XAttribute("cx", 6858000), new XAttribute("cy", 9144000)))));

        WriteEntry("ppt/_rels/presentation.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide", "slides/slide1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "slideMasters/slideMaster1.xml")));

        var aNsTheme = aNs;
        WriteXml("ppt/theme/theme1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(aNsTheme + "theme", new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName), new XAttribute("name", "Office Theme"),
                new XElement(aNsTheme + "themeElements",
                    new XElement(aNsTheme + "clrScheme", new XAttribute("name", "Office")),
                    new XElement(aNsTheme + "fontScheme", new XAttribute("name", "Office"),
                        new XElement(aNsTheme + "majorFont", new XElement(aNsTheme + "latin", new XAttribute("typeface", "Calibri Light"))),
                        new XElement(aNsTheme + "minorFont", new XElement(aNsTheme + "latin", new XAttribute("typeface", "Calibri")))),
                    new XElement(aNsTheme + "fmtScheme", new XAttribute("name", "Office"))))));

        WriteXml("ppt/slideMasters/slideMaster1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sldMaster",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))))),
                new XElement(pNs + "clrMap",
                    new XAttribute("bg1", "lt1"), new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"), new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink")))));

        WriteEntry("ppt/slideMasters/_rels/slideMaster1.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "../theme/theme1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml")));

        WriteXml("ppt/slideLayouts/slideLayout1.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(pNs + "sldLayout",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XAttribute("type", "blank"),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))))))));

        WriteEntry("ppt/slideLayouts/_rels/slideLayout1.xml.rels", MakeRels(pkgNs,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "../slideMasters/slideMaster1.xml")));

        WriteXml("ppt/slides/slide1.xml", slideXml);

        WriteEntry("ppt/slides/_rels/slide1.xml.rels", MakeRels(pkgNs,
            ("rId1",   "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml"),
            ("rIdDm1", dmRelType, "../diagrams/data1.xml"),
            ("rIdLo1", loRelType, "../diagrams/layout1.xml"),
            ("rIdQs1", qsRelType, "../diagrams/quickStyle1.xml"),
            ("rIdCs1", csRelType, "../diagrams/colors1.xml")));

        WriteXml("ppt/diagrams/data1.xml",       dataXml);
        WriteXml("ppt/diagrams/layout1.xml",     layoutXml);
        WriteXml("ppt/diagrams/quickStyle1.xml", qsXml);
        WriteXml("ppt/diagrams/colors1.xml",     colorsPartXml);
        WriteXml("ppt/diagrams/drawing1.xml",    dspXml);

        WriteEntry("ppt/diagrams/_rels/data1.xml.rels", MakeRels(pkgNs,
            ("rIdDraw1", dgmDrawRelType, "drawing1.xml")));

        return path;
    }

    // ── T17: data parse round-trip tests ───────────────────────────────────────────

    [Fact]
    public void Reader_ParsesSmartArtData_FlatProcessNodes()
    {
        // Three flat nodes with no connections → all roots
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes: [("id1", "Step 1"), ("id2", "Step 2"), ("id3", "Step 3")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa   = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull("data1.xml + layout1.xml were present");
        sa.Data!.Family.Should().Be(SmartArtFamily.Process, "uniqueId contains 'process'");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue("process1 is in the bounded shared live-layout planner");
        sa.Data.Nodes.Should().HaveCount(3, "three root-level nodes with no parOf connections");

        var nodeTexts = sa.Data.Nodes.Select(n => n.Text).ToList();
        nodeTexts.Should().BeEquivalentTo(new[] { "Step 1", "Step 2", "Step 3" });
    }

    [Fact]
    public void Reader_ParsesContinuousBlockProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/continuousBlockProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "the model should still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "continuousBlockProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesBasicProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "basicProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesSegmentedProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "segmentedProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "segmentedProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesChevronProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "chevronProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "chevronProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesBasicChevronProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "basicChevronProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicChevronProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesClosedChevronProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "closedChevronProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "closedChevronProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesBendingProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "bendingProcess reuses the shared process-family geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "bendingProcess is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
    }

    [Fact]
    public void Reader_ParsesCircleProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/circleProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2"), ("id3", "Stage 3"), ("id4", "Stage 4")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "circleProcess remains a process-family SmartArt layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "circleProcess is in the bounded shared circular process planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2", "Stage 3", "Stage 4");
    }

    [Fact]
    public void Reader_ParsesFunnelProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2"), ("id3", "Stage 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "funnelProcess stays in the process-family model while using layout-specific geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "funnelProcess is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2", "Stage 3");
    }

    [Fact]
    public void Reader_ParsesBasicListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/list1",
            nodes: [("id1", "Item 1"), ("id2", "Item 2"), ("id3", "Item 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue("list1 is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2", "Item 3");
    }

    [Fact]
    public void Reader_ParsesBasicBlockListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList",
            nodes: [("id1", "Item 1"), ("id2", "Item 2"), ("id3", "Item 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "basicBlockList is a list-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicBlockList is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2", "Item 3");
    }

    [Fact]
    public void Reader_ParsesVerticalBoxListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList",
            nodes: [("id1", "Item 1"), ("id2", "Item 2"), ("id3", "Item 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "verticalBoxList is a list-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "verticalBoxList is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2", "Item 3");
    }

    [Fact]
    public void Reader_ParsesVerticalChevronListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList",
            nodes: [("id1", "Step 1"), ("id2", "Step 2"), ("id3", "Step 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "verticalChevronList is admitted to the shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public void Reader_ParsesVerticalArrowListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList",
            nodes: [("id1", "Step 1"), ("id2", "Step 2"), ("id3", "Step 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "verticalArrowList is admitted to the shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public void Compositor_VerticalChevronListSmartArt_RendersAllNodesBeyondOriginalTwelveItemCutoff()
    {
        var nodes = Enumerable.Range(1, 13)
            .Select(index => ($"id{index}", $"Step {index}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(13, "all 13 vertical chevron nodes should remain live");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, 13).Select(index => $"Step {index}"));
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("the shared vertical list plan preserves authored order");
    }

    [Fact]
    public void Reader_ParsesDescendingBlockListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList",
            nodes: [("id1", "Item 1"), ("id2", "Item 2"), ("id3", "Item 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "descendingBlockList is a list-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "descendingBlockList is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2", "Item 3");
    }

    [Fact]
    public void Reader_ParsesBasicPyramidAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid",
            nodes: [("id1", "Vision"), ("id2", "Strategy"), ("id3", "Execution"), ("id4", "Proof")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "basicPyramid stays in the broad list-family model while using layout-specific geometry");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicPyramid is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Vision", "Strategy", "Execution", "Proof");
    }

    [Fact]
    public void Reader_ParsesPyramidListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/pyramidList",
            nodes: [("id1", "Foundation"), ("id2", "Growth"), ("id3", "Vision")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "pyramidList has a shared live geometry planner and must not fall back to the cached drawing on import");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Foundation", "Growth", "Vision");
    }

    [Fact]
    public void Reader_ParsesInvertedPyramidAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid",
            nodes: [("id1", "Market"), ("id2", "Product"), ("id3", "Team"), ("id4", "Task")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Market", "Product", "Team", "Task");
    }

    [Fact]
    public void Reader_ParsesBasicVennAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicVenn",
            nodes: [("id1", "Audience"), ("id2", "Need"), ("id3", "Offer")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "basicVenn is tracked as a relationship-family SmartArt layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicVenn now has bounded shared overlapping-ellipse geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Audience", "Need", "Offer");
    }

    [Fact]
    public void Reader_ParsesRadialVennAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn",
            nodes: [("id1", "Customer"), ("id2", "Product"), ("id3", "Market"), ("id4", "Proof")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "radialVenn is tracked as a relationship-family SmartArt layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "radialVenn now has bounded shared radial overlapping-ellipse geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Customer", "Product", "Market", "Proof");
    }

    [Fact]
    public void Reader_ParsesDivergingRadialAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/divergingRadial",
            nodes: [("id1", "Central"), ("id2", "North"), ("id3", "East"), ("id4", "South")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "divergingRadial is a relationship-family layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "divergingRadial must use the editable shared relationship planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Central", "North", "East", "South");
    }

    [Fact]
    public void Reader_ParsesTargetListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/targetList",
            nodes: [("id1", "Market"), ("id2", "Segment"), ("id3", "Account"), ("id4", "Champion")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "targetList is tracked as a relationship-family SmartArt layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "targetList now has bounded shared concentric-ellipse geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Market", "Segment", "Account", "Champion");
    }

    [Fact]
    public void Reader_ParsesBasicCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicCycle",
            nodes: [("id1", "Discover"), ("id2", "Plan"), ("id3", "Build"), ("id4", "Review")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "basicCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicCycle is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Discover", "Plan", "Build", "Review");
    }

    [Fact]
    public void Reader_ParsesRadialCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialCycle",
            nodes: [("id1", "Identify"), ("id2", "Analyze"), ("id3", "Act"), ("id4", "Review")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "radialCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "radialCycle is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Identify", "Analyze", "Act", "Review");
    }

    [Fact]
    public void Reader_ParsesRadialListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialList",
            nodes: [("id1", "Discover"), ("id2", "Plan"), ("id3", "Build"), ("id4", "Review")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "radialList is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "radialList is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Discover", "Plan", "Build", "Review");
    }

    [Fact]
    public void Reader_ParsesBasicRadialAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radial1",
            nodes: [("id1", "Core"), ("id2", "Branch A"), ("id3", "Branch B")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "radial1 is a cycle-family hub-and-spoke layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "radial1 is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Core", "Branch A", "Branch B");
    }

    [Fact]
    public void Reader_ParsesRadialClusterAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2008/layout/RadialCluster",
            nodes: [("id1", "Theme"), ("id2", "North"), ("id3", "East"), ("id4", "South")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "RadialCluster is a cycle-family central-idea layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "RadialCluster is admitted through the shared hub-and-spoke planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Theme", "North", "East", "South");
    }

    [Fact]
    public void Reader_ParsesGearCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/gearCycle",
            nodes: [("id1", "Initiate"), ("id2", "Coordinate"), ("id3", "Deliver"), ("id4", "Improve")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "gearCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "gearCycle is in the bounded shared live-layout planner as a cycle approximation");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Initiate", "Coordinate", "Deliver", "Improve");
    }

    [Fact]
    public void Reader_ParsesBasicHierarchyAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy",
            nodes: [("R", "Company"), ("C1", "Product"), ("G1", "Platform"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("C1", "G1")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "basicHierarchy is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicHierarchy is in the bounded shared live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("Company");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Product", "Operations" });
        sa.Data.Nodes[0].Children[0].Children.Should().ContainSingle().Which.Text.Should().Be("Platform");
    }

    [Fact]
    public void Reader_ParsesOrgChartAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
            nodes: [("R", "CEO"), ("C1", "Sales"), ("C2", "Engineering")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "orgChart is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "orgChart is in the bounded shared live-layout planner as a generic tree approximation");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("CEO");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Sales", "Engineering" });
    }

    [Fact]
    public void Reader_ParsesVerticalBulletListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList",
            nodes: [("R", "Project"), ("C1", "Scope"), ("C2", "Timeline"), ("C3", "Risks")],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("R", "C3")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "verticalBulletList is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "verticalBulletList is in the bounded shared live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("Project");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Scope", "Timeline", "Risks" });
    }

    [Fact]
    public void Reader_ParsesStackedListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/stackedList",
            nodes: [("id1", "Item 1"), ("id2", "Item 2"), ("id3", "Item 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "stackedList is a list-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "stackedList is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2", "Item 3");
    }

    [Fact]
    public void Reader_ParsesBasicMatrixAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix",
            nodes: [("id1", "People"), ("id2", "Process"), ("id3", "Platform"), ("id4", "Proof")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Matrix,
            "basicMatrix is a matrix-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicMatrix is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("People", "Process", "Platform", "Proof");
    }

    [Fact]
    public void Reader_AdmitsMatrix1ToTheSharedBasicMatrixLayout()
    {
        var pptxPath = MakeSmartArtPptx(
            ["A", "B", "C", "D"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/matrix1");

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.Matrix);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "matrix1 is the native Basic Matrix layout and uses the shared four-quadrant plan");
        smartArt.FallbackShapes.Should().NotBeEmpty();
        var liveShapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().HaveCount(5, "the imported matrix1 should use the shared whole-plus-four-quadrant plan");
        liveShapes.Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C", "D");

        var savedPath = Path.Combine(_tempDir, "matrix1-live-roundtrip.pptx");
        PptxPackageWriter.Write(presentation, savedPath);
        var reopened = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopened.Data.Should().NotBeNull();
        reopened.Data!.LayoutUniqueId.Should().EndWith("/matrix1");
        reopened.Data.IsLiveLayoutSupported.Should().BeTrue();
    }

    [Fact]
    public void ReaderWriter_PreservesBasicMatrixSemanticsAndRegeneratesItsFiveShapeCache()
    {
        var nodeTexts = Enumerable.Range(0, 9).Select(i => $"Node {i + 1}").ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix",
            nodes: nodeTexts.Select((text, index) => ($"id{index + 1}", text)).ToArray(),
            parOfConnections: []);

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArtShape = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartArtShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.Data.Nodes.Select(node => node.Text).Should().Equal(nodeTexts);

        var liveShapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().HaveCount(5, "Basic Matrix renders one whole plus its first four Level 1 ideas");
        liveShapes.Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(nodeTexts[..4]);
        liveShapes.Where(shape => shape.Text is null)
            .Should().ContainSingle("the whole is a background diamond and Basic Matrix has no connectors");

        smartArt.Data.Nodes[^1].Text = "Node 9 edited";
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            presentation.Theme!);
        cache.Applied.Should().BeTrue(cache.Message);
        cache.ShapeCount.Should().Be(5);
        var savedPath = WriteToPptx(presentation);

        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.GetEntry("ppt/diagrams/drawing1.xml");
            entry.Should().NotBeNull();
            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var document = XDocument.Parse(reader.ReadToEnd());
            var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
            var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
            document.Descendants(dsp + "sp").Should().HaveCount(5);
            document.Descendants(dsp + "cxnSp").Should().BeEmpty();
            document.Descendants(dsp + "sp")
                .Select(element => element.Descendants(a + "prstGeom").Attributes("prst").FirstOrDefault()?.Value)
                .Should().Contain("diamond");
            document.Descendants(dsp + "cNvPr").Select(element => (string?)element.Attribute("name"))
                .Should().Contain("SmartArt_BasicMatrix_Whole")
                .And.Contain("SmartArt_BasicMatrix_Quadrant_TopLeft_1")
                .And.Contain("SmartArt_BasicMatrix_Quadrant_BottomRight_4");
        }

        var reopened = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        reopened.Data.Should().NotBeNull();
        reopened.Data!.IsLiveLayoutSupported.Should().BeTrue();
        reopened.Data.Nodes.Select(node => node.Text).Should().Equal(
            nodeTexts[..^1].Append("Node 9 edited"));
        reopened.FallbackShapes.Should().HaveCount(5);
        reopened.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal(nodeTexts[..4]);
    }

    [Fact]
    public void ReaderWriter_PreservesTitleOnlyTitledMatrixAsLiveEditableState()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix",
            nodes: [("id1", "Title only")],
            parOfConnections: []);

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue();
        var liveShapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().ContainSingle();
        liveShapes[0].Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text
            .Should().Be("Title only");

        smartArt.Data.Nodes[0].Text = "Edited title";
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var savedPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        reopened.Data.Should().NotBeNull();
        reopened.Data!.IsLiveLayoutSupported.Should().BeTrue();
        reopened.Data.Nodes.Select(node => node.Text).Should().Equal("Edited title");
    }

    [Fact]
    public void Reader_ParsesTitledMatrixAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix",
            nodes: [("id1", "Title"), ("id2", "North"), ("id3", "East"), ("id4", "South")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Matrix,
            "titledMatrix is a matrix-family layout with shared title-band semantics");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "titledMatrix is in the bounded shared titled-matrix live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Title", "North", "East", "South");
    }

    [Fact]
    public void Compositor_TitledMatrix_UsesSharedTitleBandAndBodyCells()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix",
            nodes: [("id1", "Title"), ("id2", "North"), ("id3", "East"), ("id4", "South")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4, "the shared WPF/Avalonia compositor should emit one title and three body cells");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Title", "North", "East", "South");
        liveShapes[0].BoundsDip.Width.Should().BeGreaterThan(liveShapes[1].BoundsDip.Width,
            "the title band should span the full matrix width");
        liveShapes[1].BoundsDip.Y.Should().BeGreaterThan(liveShapes[0].BoundsDip.Y,
            "body cells should render below the title band");
    }

    [Fact]
    public void Compositor_TitledMatrix_RendersAllBodyNodesBeyondOriginalNineItemCutoff()
    {
        var nodes = Enumerable.Range(0, 10)
            .Select(index => ($"id{index}", index == 0 ? "Title" : $"Node{index}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();

        liveShapes.Should().HaveCount(10,
            "the shared WPF/Avalonia compositor should emit one title and nine body cells");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(nodes.Select(node => node.Item2));
        liveShapes[0].BoundsDip.Width.Should().BeGreaterThan(liveShapes[1].BoundsDip.Width,
            "the title band should continue to span the complete matrix width");
    }

    [Fact]
    public void Reader_ParsesList2AsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/list2",
            nodes: [("id1", "Item 1"), ("id2", "Item 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "list2 remains a list-family layout for shared live regeneration");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "list2 uses the existing shared vertical-list geometry and should remain editable");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2");

        var presentation = PptxPackageReader.Read(pptxPath);
        var liveShapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().HaveCount(2);
        liveShapes.Select(shape => shape.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Item 1", "Item 2");

        var savedPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopened.Data.Should().NotBeNull();
        reopened.Data!.LayoutUniqueId.Should().EndWith("/list2");
        reopened.Data.IsLiveLayoutSupported.Should().BeTrue();
    }

    [Fact]
    public void Reader_ParsesHorizontalBlockListAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/horizontalBlockList",
            nodes: [("id1", "One"), ("id2", "Two"), ("id3", "Three")],
            parOfConnections: []);
        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the imported ID should reach the existing horizontal block-list planner");

        var liveShapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().HaveCount(3);
        liveShapes.Select(shape => shape.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("One", "Two", "Three");
        liveShapes.Select(shape => shape.BoundsDip.X)
            .Should().BeInAscendingOrder();

        var savedPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(savedPath)
            .Slides[0].Shapes.First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopened.Data.Should().NotBeNull();
        reopened.Data!.LayoutUniqueId.Should().EndWith("/horizontalBlockList");
        reopened.Data.Family.Should().Be(SmartArtFamily.List);
        reopened.Data.IsLiveLayoutSupported.Should().BeTrue();
    }

    [Fact]
    public void Reader_ParsesBlockCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/blockCycle",
            nodes: [("id1", "Sense"), ("id2", "Decide"), ("id3", "Act"), ("id4", "Learn")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "blockCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "blockCycle is in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Sense", "Decide", "Act", "Learn");
    }

    [Fact]
    public void Reader_ParsesContinuousCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/continuousCycle",
            nodes: [("id1", "Phase 1"), ("id2", "Phase 2"), ("id3", "Phase 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "unsupported cycle siblings still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "continuousCycle now uses the shared cycle-family live-layout path");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Phase 1", "Phase 2", "Phase 3");
    }

    [Fact]
    public void Reader_ParsesNonDirectionalCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle",
            nodes: [("id1", "Observe"), ("id2", "Align"), ("id3", "Deliver"), ("id4", "Adapt")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "nonDirectionalCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "nonDirectionalCycle is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Observe", "Align", "Deliver", "Adapt");
    }

    [Fact]
    public void Reader_ParsesTextCycleAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/textCycle",
            nodes: [("id1", "Plan"), ("id2", "Draft"), ("id3", "Review"), ("id4", "Publish")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "textCycle is a cycle-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "textCycle is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Plan", "Draft", "Review", "Publish");
    }

    [Fact]
    public void Reader_ParsesHorizontalHierarchyAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy",
            nodes: [("R", "Portfolio"), ("C1", "Product"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "horizontalHierarchy is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "horizontalHierarchy is now in the bounded shared live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("Portfolio");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Product", "Operations" });
    }

    [Fact]
    public void Reader_ParsesOrgChartAsLiveLayoutAndWpfConsumesDedicatedSharedPlan()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
            nodes:
            [
                ("R", "CEO"),
                ("A", "Assistant"),
                ("C", "Director")
            ],
            parOfConnections: [("R", "A"), ("R", "C")],
            assistantNodeIds: ["A"]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "orgChart is admitted by the bounded reader allow-list");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Children.Should().HaveCount(2);
        sa.Data.Nodes[0].Children.Single(node => node.ModelId == "A").IsAssistant.Should().BeTrue();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();
        liveShapes.Should().HaveCount(7,
            "WPF composes three dedicated shared org-chart boxes, three assistant segments, and one report connector");
        liveShapes.Where(op => op.Text is not null)
            .Should().OnlyContain(op => op.Text!.Paragraphs.Count == 1);
        liveShapes.SelectMany(op => op.Text?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Contain(["CEO", "Assistant", "Director"]);
    }

    [Fact]
    public void Reader_PreservesSmartArtNodeParagraphBoundaries()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/nameAndTitleOrgChart",
            nodes: [("R", "Jane Doe\nChief Executive Officer")],
            parOfConnections: []);
        var savedPath = Path.Combine(_tempDir, "smartart-node-paragraphs-saved.pptx");

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smartArt.Data!.Nodes.Single().Text.Should().Be("Jane Doe\nChief Executive Officer");

        var rewrite = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        rewrite.Applied.Should().BeTrue();
        PptxPackageWriter.Write(presentation, savedPath);

        using var archive = ZipFile.OpenRead(savedPath);
        var entry = archive.GetEntry("ppt/diagrams/data1.xml");
        entry.Should().NotBeNull();
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        var dataXml = XDocument.Parse(reader.ReadToEnd());
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        dataXml.Descendants(aNs + "p")
            .Select(p => string.Concat(p.Descendants(aNs + "t").Select(t => t.Value)))
            .Should().ContainInOrder("Jane Doe", "Chief Executive Officer");
    }

    [Fact]
    public void Reader_ParsesKnownHierarchyFamilyButDisablesLiveLayoutForUnsupportedSibling()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/unknownHierarchy",
            nodes: [("R", "Root"), ("C", "Child")],
            parOfConnections: [("R", "C")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "unsupported hierarchy siblings still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeFalse(
            "hierarchy-family layouts outside the bounded allow-list should keep cached-drawing fallback");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Child");
    }

    [Fact]
    public void Reader_ParsesHierarchy3AsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
            nodes: [("R", "Portfolio"), ("C1", "Product"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "hierarchy3 has a bounded shared left-to-right layout plan");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Product", "Operations" });
    }

    [Fact]
    public void Reader_Hierarchy3_AdmitsSimpleNodeCacheToLiveLayout()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Portfolio", "Product"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3");
        var presentation = PptxPackageReader.Read(pptxPath);

        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "a cache with exactly one matching text shape per parsed hierarchy node has no unmodeled role");
        smartArt.FallbackShapes.Should().HaveCount(2);
    }

    [Fact]
    public void Reader_Hierarchy3_AdmitsExactNodeAndConnectorCacheToLiveLayout()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
            nodes: [("R", "Portfolio"), ("C1", "Product"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2")],
            includeCachedHierarchy3Connectors: true);

        var smartArt = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "an exact hierarchy3 cache with one matching node per data node and one line per parent edge is regenerable");
        smartArt.FallbackShapes.Should().HaveCount(5);
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Line)
            .Should().Be(2);
    }

    [Fact]
    public void Reader_Hierarchy3_RejectsNodeAndConnectorCacheWithExtraRole()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
            nodes: [("R", "Portfolio"), ("C1", "Product"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2")],
            includeCachedHierarchy3Connectors: true);

        using (var archive = ZipFile.Open(pptxPath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/diagrams/drawing1.xml")!;
            string drawingText;
            using (var reader = new StreamReader(entry.Open()))
                drawingText = reader.ReadToEnd();

            var drawing = XDocument.Parse(drawingText);
            drawing.Root!.Element(XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram") + "spTree")!
                .Add(new XElement(XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram") + "sp"));
            entry.Delete();
            var replacement = archive.CreateEntry("ppt/diagrams/drawing1.xml");
            using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
            drawing.Save(writer);
        }

        var smartArt = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "an extra cached role must keep PowerPoint's authoritative drawing fallback");
    }

    [Fact]
    public void Reader_ParsesTableHierarchyAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy",
            nodes:
            [
                ("R", "Portfolio"),
                ("C1", "Owners"),
                ("C2", "Milestones"),
                ("G1", "Delivery"),
                ("G2", "Launch")
            ],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("C1", "G1"), ("C2", "G2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "tableHierarchy is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "tableHierarchy is in the bounded shared hierarchy live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("Portfolio");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Owners", "Milestones" });
        sa.Data.Nodes[0].Children[0].Children.Should().ContainSingle().Which.Text.Should().Be("Delivery");
        sa.Data.Nodes[0].Children[1].Children.Should().ContainSingle().Which.Text.Should().Be("Launch");
    }

    [Fact]
    public void Reader_ParsesLabeledHierarchyAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy",
            nodes: [("R", "Initiative"), ("C1", "Owner"), ("C2", "Outcome")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "labeledHierarchy is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "labeledHierarchy is now in the bounded shared hierarchy live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("Initiative");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Owner", "Outcome" });
    }

    [Fact]
    public void Reader_ParsesGridMatrixAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix",
            nodes: [("id1", "A"), ("id2", "B"), ("id3", "C"), ("id4", "D")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Matrix,
            "Grid Matrix retains broad family metadata when no cached drawing is present");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "Grid Matrix remains live-capable when the package has no cached drawing to validate");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("A", "B", "C", "D");
    }

    [Fact]
    public void Compositor_GridMatrixSmartArt_UsesDedicatedSharedFourQuadrantPlan()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix",
            nodes: [("A", "A"), ("B", "B"), ("C", "C"), ("D", "D"), ("E", "Unused")],
            parOfConnections: []);

        var presentation = PptxPackageReader.Read(pptxPath);
        var smartArt = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        smartArt.Data!.IsLiveLayoutSupported = true;
        var ops = SlideCompositor.Compose(presentation, presentation.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        liveShapes.Should().HaveCount(4, "the WPF host consumes the shared four-quadrant plan");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("A", "B", "C", "D");
        liveShapes.Select(op => op.BoundsDip.X).Distinct().Should().HaveCount(2);
        liveShapes.Select(op => op.BoundsDip.Y).Distinct().Should().HaveCount(2);
        liveShapes.Should().OnlyContain(op => Math.Abs(op.BoundsDip.Width - op.BoundsDip.Height) < 0.01,
            "the shared Grid Matrix cells use a square centered envelope");
    }

    [Fact]
    public void RoundTrip_GridMatrix_AuthoringAndDrawingCacheRemainSchemaShaped()
    {
        var sourcePath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix",
            nodes: [("A", "A"), ("B", "B"), ("C", "C"), ("D", "D")],
            parOfConnections: []);
        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;

        SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.GridMatrix)
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            presentation.Theme!);
        cache.Applied.Should().BeTrue(cache.Message);
        cache.ShapeCount.Should().Be(4);

        var savedPath = WriteToPptx(presentation);
        using (var archive = ZipFile.OpenRead(savedPath))
        {
            var entry = archive.GetEntry("ppt/diagrams/drawing1.xml");
            entry.Should().NotBeNull();
            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            var document = XDocument.Parse(reader.ReadToEnd());
            var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
            document.Root!.Name.Should().Be(dsp + "drawing");
            document.Descendants(dsp + "sp").Should().HaveCount(4);
            document.Descendants(dsp + "cxnSp").Should().BeEmpty();
            document.Descendants(dsp + "cNvPr").Select(element => (string?)element.Attribute("name"))
                .Should().Contain("SmartArt_GridMatrix_Quadrant_TopLeft_1")
                .And.Contain("SmartArt_GridMatrix_Quadrant_BottomRight_4");
        }

        var reopened = PptxPackageReader.Read(savedPath);
        var reopenedSmartArt = reopened.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopenedSmartArt.Data!.LayoutUniqueId.Should().EndWith("/gridMatrix");
        reopenedSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        reopenedSmartArt.FallbackShapes.Select(s => s.PlainText).Should().Equal("A", "B", "C", "D");
    }

    [Fact]
    public void Reader_ParsesVerticalBlockListAsLiveLayoutAndWpfConsumesSharedPlan()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalBlockList",
            nodes: [("R", "Overview"), ("C", "Detail"), ("N", "Next")],
            parOfConnections: [("R", "C"), ("C", "N")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "verticalBlockList is admitted to the shared list live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Children.Should().ContainSingle();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .Where(op => op.Text is not null)
            .ToList();
        liveShapes.Should().HaveCount(3,
            "WPF composes the authored vertical block list from shared live shapes");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Overview", "Detail", "Next");
        liveShapes.Should().OnlyContain(op => op.BoundsDip.Width > 0 && op.BoundsDip.Height > 0);
    }

    [Fact]
    public void Reader_ParsesStackedVennAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn",
            nodes: [("id1", "Market"), ("id2", "Product"), ("id3", "Proof")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "stackedVenn stays in the relationship-family SmartArt planner");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "stackedVenn now has bounded shared stacked-ellipse geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Market", "Product", "Proof");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        liveShapes.Should().HaveCount(3, "three stacked-Venn ellipses should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("stacked-Venn live geometry emits no connectors");
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared stacked-Venn X offsets");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared stacked-Venn Y offsets");
    }

    [Fact]
    public void Reader_ParsesInterlockingRingsAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings",
            nodes: [("id1", "Plan"), ("id2", "Build"), ("id3", "Review"), ("id4", "Share")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "Interlocking Rings is admitted through the shared relationship-family layout planner");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Plan", "Build", "Review", "Share");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        liveShapes.Should().HaveCount(4);
        liveShapes.Should().OnlyContain(op => op.Text != null);
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia consume the same live ring positions");
        liveShapes.Select(op => op.BoundsDip.Width).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Reader_ParsesBasicRelationshipAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            nodes: [("id1", "A"), ("id2", "B"), ("id3", "C")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship,
            "unsupported relationship siblings still retain broad relationship-family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "relationship1 now has bounded shared overlapping-ellipse geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Reader_ImportedRelationship1_AdmitsExactNodeEllipseCache()
    {
        var presentation = PptxPackageReader.Read(
            FindRenderCompareCorpusFile("15-smartart-grouped-list.pptx"));
        var relationship = presentation.Slides[6].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        relationship.Data.Should().NotBeNull();
        relationship.Data!.LayoutUniqueId.Should().EndWith("/relationship1");
        relationship.Data.Family.Should().Be(SmartArtFamily.Relationship);
        relationship.Data.IsLiveLayoutSupported.Should().BeTrue(
            "the checked-in relationship1 cache is exactly one ordered overlapping ellipse per node");
        relationship.FallbackShapes.Should().HaveCount(3);
        relationship.FallbackShapes.Should().OnlyContain(shape =>
            shape.AutoShapeKind == DrawingShapeKind.Ellipse);

        var live = SmartArtLayoutEngine.Layout(
            relationship.Data,
            0,
            0,
            8_229_600,
            5_744_800,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Should().HaveCount(3);
        live.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        live.Select(shape => shape.PlainText).Should().Equal("Audience", "Need", "Offer");
    }

    [Fact]
    public void Reader_ImportedRelationship1_AdmitsExactTwoNodeEllipseCache()
    {
        var presentation = PptxPackageReader.Read(MakeSmartArtPptx(
            ["For", "Against"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true));
        var relationship = presentation.Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        relationship.Data.Should().NotBeNull();
        relationship.Data!.IsLiveLayoutSupported.Should().BeTrue(
            "the bounded relationship1 cache grammar also covers its source-backed two-node variant");
        relationship.FallbackShapes.Should().HaveCount(2);
        relationship.FallbackShapes.Should().OnlyContain(shape =>
            shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.Ellipse
            && shape.Effects == null);
        relationship.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("For", "Against");

        var live = SmartArtLayoutEngine.Layout(
            relationship.Data,
            0,
            0,
            8_229_600,
            5_744_800,
            presentation.Theme);
        live.Should().NotBeNull();
        live!.Should().HaveCount(2);
        live.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        live.Select(shape => shape.PlainText).Should().Equal("For", "Against");
    }

    [Fact]
    public void Reader_Relationship1_WithExtraRole_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Audience", "Need", "Offer"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true,
            groupedListUnmodeledRole: true);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "an extra relationship role is outside the exact node-only cache grammar");
    }

    [Fact]
    public void Reader_Relationship1_TwoNodeWithExtraRole_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["For", "Against"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true,
            groupedListUnmodeledRole: true);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "the two-node admission must still reject an extra cached relationship role");
        smartArt.FallbackShapes.Should().HaveCount(3);
    }

    [Fact]
    public void Reader_Relationship1_WithUnsupportedGeometry_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Audience", "Need", "Offer"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: false);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "non-ellipse relationship geometry must remain on cached fallback");
    }

    [Fact]
    public void Reader_Relationship1_WithWrongOverlapRatio_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Audience", "Need", "Offer"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true,
            relationship1HorizontalStepEmu: 1_500_000L);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "a materially different relationship overlap ratio must remain on cached fallback");
    }

    [Fact]
    public void Reader_Relationship1_WithAuthoredEffects_PreservesCachedFallback()
    {
        var pptxPath = MakeSmartArtPptx(
            ["Audience", "Need", "Offer"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true,
            includeNodeOuterShadow: true);

        var smartArt = PptxPackageReader.Read(pptxPath).Slides[0].Shapes
            .First(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "relationship1 shape effects are outside the exact shared node grammar");
    }

    [Fact]
    public void EditingSession_Relationship1TwoNodeCacheEdit_IsUndoableAndRoundTrips()
    {
        var sourcePath = MakeSmartArtPptx(
            ["For", "Against"],
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
            relationship1NodeAndEllipseCache: true);
        var presentation = PptxPackageReader.Read(sourcePath);
        var shape = presentation.Slides[0].Shapes
            .First(candidate => candidate.Kind == SlideShapeKind.SmartArt);
        var smartArt = shape.SmartArt!;
        var targetId = smartArt.Data!.Nodes[1].ModelId;
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.EditSmartArt(shape.Id, candidate =>
        {
            var edit = SmartArtEditingPlanner.Apply(
                candidate.Data,
                SmartArtNodeEditIntent.ChangeText(targetId, "Against revised"));
            if (!edit.Applied || !SmartArtEditingPlanner.RewriteDataPart(candidate).Applied)
                return false;

            return SmartArtEditingPlanner.RegenerateDrawingCache(
                candidate,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                presentation.Theme!).Applied;
        }).Should().BeTrue();

        var updated = presentation.Slides[0].Shapes
            .First(candidate => candidate.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        updated.Data!.Nodes.Select(node => node.Text).Should().Equal("For", "Against revised");
        updated.Data.IsLiveLayoutSupported.Should().BeTrue();
        updated.FallbackShapes.Should().HaveCount(2);
        updated.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("For", "Against revised");

        session.Bus.Undo();
        var undone = presentation.Slides[0].Shapes
            .First(candidate => candidate.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        undone.Data!.Nodes.Select(node => node.Text).Should().Equal("For", "Against");
        undone.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("For", "Against");

        session.Bus.Redo();
        var roundTripPath = WriteToPptx(presentation);
        var reopened = PptxPackageReader.Read(roundTripPath).Slides[0].Shapes
            .First(candidate => candidate.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;
        reopened.Data!.IsLiveLayoutSupported.Should().BeTrue();
        reopened.FallbackShapes.Should().HaveCount(2);
        reopened.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("For", "Against revised");
    }

    [Fact]
    public void Reader_ParsesOpposingIdeasAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/opposingIdeas",
            nodes: [("id1", "For"), ("id2", "Against")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "opposingIdeas now has bounded shared opposing-arrow geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("For", "Against");
    }

    [Fact]
    public void Reader_ParsesConvergingRadialAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/convergingRadial",
            nodes: [("id1", "Top"), ("id2", "Right"), ("id3", "Bottom"), ("id4", "Left")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "convergingRadial now has bounded shared compass-arrow geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Top", "Right", "Bottom", "Left");
    }

    [Fact]
    public void Reader_ParsesAlternatingProcessAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2"), ("id3", "Stage 3"), ("id4", "Stage 4")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "alternatingProcess remains in the shared process family");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "alternatingProcess now has bounded shared upper/lower-track geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2", "Stage 3", "Stage 4");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        ops.Skip(1).OfType<DrawOp.Shape>()
            .Should().HaveCount(7, "four alternating-process boxes plus three connectors should render from shared live data");
    }

    [Fact]
    public void Reader_ParsesArrowRibbonAsLiveLayoutSupported()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon",
            nodes: [("id1", "Pitch"), ("id2", "Build"), ("id3", "Launch")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "arrowRibbon is a process-family SmartArt layout");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "arrowRibbon now has bounded shared ribbon-segment geometry");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Pitch", "Build", "Launch");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        liveShapes.Should().HaveCount(5, "three arrow-ribbon segments plus two connectors should render from shared live data");
        liveShapes.Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Pitch", "Build", "Launch");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared arrow-ribbon connector DrawOps");
    }

    [Fact]
    public void Compositor_LiveProcessSmartArt_ParOfChainRendersEveryStep()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes:
            [
                ("n1", "Plan"),
                ("n2", "Design"),
                ("n3", "Build"),
                ("n4", "Test"),
                ("n5", "Deploy")
            ],
            parOfConnections:
            [
                ("n1", "n2"),
                ("n2", "n3"),
                ("n3", "n4"),
                ("n4", "n5")
            ]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process);
        sa.Data.Nodes.Should().ContainSingle("the process chain is rooted at the first step");
        sa.Data.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Design");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(9, "five process boxes plus four connectors should render from live data");
    }

    [Fact]
    public void Compositor_BasicProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three basic-process boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
    }

    [Fact]
    public void Compositor_SegmentedProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three segmented-process boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("segmentedProcess stages should consume the shared vertical stack");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.X)
            .Distinct()
            .Should().ContainSingle("segmentedProcess stages should share one centered segment column");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared connector DrawOps");
    }

    [Fact]
    public void Compositor_ChevronProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three chevron-process stages should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.All(op => op.Text is not null).Should().BeTrue(
            "the shared Chevron geometry carries the process direction");
    }

    [Fact]
    public void Compositor_ChevronProcessSmartArt_RendersAllStagesBeyondOriginalTwelveItemCutoff()
    {
        var nodes = Enumerable.Range(1, 13)
            .Select(index => ($"n{index}", $"Stage {index}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(13, "all 13 chevron stages should remain live");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, 13).Select(index => $"Stage {index}"));
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("the shared interlocking stages preserve authored order");
    }

    [Fact]
    public void Compositor_BasicChevronProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three basic-chevron-process stages should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared basic-chevron-process DrawOps");
        liveShapes.All(op => op.Text is not null).Should().BeTrue();
    }

    [Fact]
    public void Compositor_ClosedChevronProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three closed-chevron-process stages should render from shared live data");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Plan", "Build", "Ship");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared closed-chevron-process DrawOps");
        liveShapes.All(op => op.Text is not null).Should().BeTrue();
    }

    [Fact]
    public void Compositor_BendingProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three bending-process boxes plus two connectors should render from shared live data");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Plan", "Build", "Ship");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared bending-process DrawOps");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared bending-process connector DrawOps");
    }

    [Fact]
    public void Compositor_BendingProcessSmartArt_RendersAllNodesBeyondOriginalTwelveItemCutoff()
    {
        var nodes = Enumerable.Range(1, 13)
            .Select(index => ($"n{index}", $"Node {index}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(25, "13 bending-process boxes plus 12 connectors should remain live");
        liveShapes.Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal(Enumerable.Range(1, 13).Select(index => $"Node {index}"));
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(12, "the shared two-track plan emits one connector between each adjacent pair");
    }

    [Fact]
    public void Compositor_CircleProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/circleProcess",
            nodes: [("n1", "Discover"), ("n2", "Plan"), ("n3", "Build"), ("n4", "Review")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four circle-process boxes plus four loop connectors should render from shared live data");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Discover", "Plan", "Build", "Review");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared circle-process connector DrawOps");
    }

    [Fact]
    public void Compositor_FunnelProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess",
            nodes: [("n1", "Discover"), ("n2", "Qualify"), ("n3", "Convert"), ("n4", "Retain")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(7, "four funnel-process stages plus three connectors should render from shared live data");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Discover", "Qualify", "Convert", "Retain");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared top-to-bottom funnel DrawOps");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("WPF and Avalonia hosts consume shared narrowing funnel DrawOps");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume shared funnel-process connector DrawOps");
    }

    [Fact]
    public void Compositor_VerticalProcessSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalProcess",
            nodes: [("n1", "Discover"), ("n2", "Qualify"), ("n3", "Convert"), ("n4", "Retain")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(7, "four vertical-process boxes plus three connectors should render from shared live data");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Discover", "Qualify", "Convert", "Retain");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared top-to-bottom vertical-process DrawOps");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume shared vertical-process connector DrawOps");
    }

    [Fact]
    public void Compositor_VerticalBoxListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three vertical-box-list boxes should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("list-family live geometry emits no connectors");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared vertical list DrawOps");
    }

    [Fact]
    public void Compositor_StackedListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/stackedList",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three stacked-list boxes should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("list-family live geometry emits no connectors");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared stacked-list DrawOps");
    }

    [Fact]
    public void Compositor_DescendingBlockListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList",
            nodes: [("n1", "Plan"), ("n2", "Build"), ("n3", "Ship")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three descending-block-list boxes should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("list-family live geometry emits no connectors");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared descending-block-list DrawOps");
        liveShapes.Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("WPF and Avalonia hosts consume shared descending-block width geometry");

        var rightEdge = liveShapes[0].BoundsDip.X + liveShapes[0].BoundsDip.Width;
        foreach (var op in liveShapes)
        {
            (op.BoundsDip.X + op.BoundsDip.Width).Should().BeApproximately(rightEdge, 0.01,
                "WPF and Avalonia hosts consume shared right-aligned descending-block DrawOps");
        }
    }

    [Fact]
    public void Compositor_BasicPyramidSmartArt_RendersSharedLiveSegments()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid",
            nodes: [("n1", "Vision"), ("n2", "Strategy"), ("n3", "Execution"), ("n4", "Proof")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4, "four basic-pyramid segments should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("basic-pyramid live geometry emits no connectors");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Vision", "Strategy", "Execution", "Proof"]);
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared top-to-bottom pyramid DrawOps");
        liveShapes.Select(op => op.BoundsDip.Width)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared widening pyramid segment geometry");
    }

    [Fact]
    public void Compositor_InvertedPyramidSmartArt_RendersSharedLiveSegments()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid",
            nodes: [("n1", "Market"), ("n2", "Product"), ("n3", "Team"), ("n4", "Task")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;
        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4);
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Market", "Product", "Team", "Task");
        liveShapes.Select(op => op.BoundsDip.Y).Should().BeInAscendingOrder();
        liveShapes.Select(op => op.BoundsDip.Width).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Compositor_BasicVennSmartArt_RendersSharedLiveEllipses()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicVenn",
            nodes: [("n1", "Audience"), ("n2", "Need"), ("n3", "Offer")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three basic-Venn ellipses should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("basic-Venn live geometry emits no connectors");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Audience", "Need", "Offer"]);
        for (int i = 1; i < liveShapes.Count; i++)
        {
            liveShapes[i].BoundsDip.X.Should().BeGreaterThan(liveShapes[i - 1].BoundsDip.X);
            liveShapes[i].BoundsDip.X.Should().BeLessThan(
                liveShapes[i - 1].BoundsDip.X + liveShapes[i - 1].BoundsDip.Width,
                "WPF and Avalonia hosts consume shared overlapping Venn ellipse DrawOps");
        }
    }

    [Fact]
    public void Compositor_RadialVennSmartArt_RendersSharedLiveEllipses()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn",
            nodes: [("n1", "Customer"), ("n2", "Product"), ("n3", "Market"), ("n4", "Proof")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4, "four radial-Venn ellipses should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("radial-Venn live geometry emits no connectors");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Customer", "Product", "Market", "Proof"]);
        liveShapes.Select(op => op.BoundsDip.X).Distinct().Should().HaveCountGreaterThan(1,
            "WPF and Avalonia hosts consume shared radial Venn X placement");
        liveShapes.Select(op => op.BoundsDip.Y).Distinct().Should().HaveCountGreaterThan(1,
            "WPF and Avalonia hosts consume shared radial Venn Y placement");
    }

    [Fact]
    public void Compositor_TargetListSmartArt_RendersSharedLiveEllipses()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/targetList",
            nodes: [("n1", "Market"), ("n2", "Segment"), ("n3", "Account"), ("n4", "Champion")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4, "four target-list ellipses should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("target-list live geometry emits no connectors");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Market", "Segment", "Account", "Champion"]);
        liveShapes.Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("WPF and Avalonia hosts consume shared nested target ellipse DrawOps");

        var centerX = liveShapes[0].BoundsDip.X + liveShapes[0].BoundsDip.Width / 2;
        var centerY = liveShapes[0].BoundsDip.Y + liveShapes[0].BoundsDip.Height / 2;
        foreach (var op in liveShapes)
        {
            (op.BoundsDip.X + op.BoundsDip.Width / 2).Should().BeApproximately(centerX, 0.01,
                "shared target-list DrawOps should preserve a common target center");
            (op.BoundsDip.Y + op.BoundsDip.Height / 2).Should().BeApproximately(centerY, 0.01,
                "shared target-list DrawOps should preserve a common target center");
        }
    }

    [Fact]
    public void Compositor_TargetListSmartArt_RendersAllNodesBeyondOriginalFiveNodeCutoff()
    {
        var nodes = Enumerable.Range(1, 6)
            .Select(i => ($"n{i}", $"Node {i}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/targetList",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();

        liveShapes.Should().HaveCount(6,
            "the shared WPF/Avalonia compositor should emit one live ellipse per node");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(nodes.Select(node => node.Item2));
        liveShapes.Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder();
    }

    [Fact]
    public void Compositor_StackedVennSmartArt_RendersSharedLiveEllipses()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn",
            nodes: [("n1", "Market"), ("n2", "Product"), ("n3", "Proof")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Relationship);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "three stacked-Venn ellipses should render from shared live data");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("stacked-Venn live geometry emits no connectors");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Market", "Product", "Proof"]);
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared stacked-Venn X offsets");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("WPF and Avalonia hosts consume shared stacked-Venn Y offsets");

        for (int i = 1; i < liveShapes.Count; i++)
        {
            liveShapes[i].BoundsDip.X.Should().BeLessThan(liveShapes[i - 1].BoundsDip.X + liveShapes[i - 1].BoundsDip.Width,
                "shared stacked-Venn ellipses should overlap horizontally");
            liveShapes[i].BoundsDip.Y.Should().BeLessThan(liveShapes[i - 1].BoundsDip.Y + liveShapes[i - 1].BoundsDip.Height,
                "shared stacked-Venn ellipses should overlap vertically");
        }
    }

    [Fact]
    public void Compositor_BasicCycleSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicCycle",
            nodes: [("n1", "Discover"), ("n2", "Plan"), ("n3", "Build"), ("n4", "Review")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four basic-cycle boxes plus four connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Discover");
        renderedText.Should().Contain("Review");
    }

    [Fact]
    public void Compositor_RadialCycleSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialCycle",
            nodes: [("n1", "Identify"), ("n2", "Analyze"), ("n3", "Act"), ("n4", "Review")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four radial-cycle boxes plus four connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Identify");
        renderedText.Should().Contain("Review");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared radial-cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_RadialListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialList",
            nodes: [("n1", "Discover"), ("n2", "Plan"), ("n3", "Build"), ("n4", "Review")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four radial-list boxes plus four connectors should render from shared live data");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain("Discover")
            .And.Contain("Review");
        var radialBounds = liveShapes.Select(op => op.BoundsDip).ToArray();
        var radialCenterX = (radialBounds.Min(bounds => bounds.X) + radialBounds.Max(bounds => bounds.X + bounds.Width)) / 2;
        var radialCenterY = (radialBounds.Min(bounds => bounds.Y) + radialBounds.Max(bounds => bounds.Y + bounds.Height)) / 2;
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared radial-list connector DrawOps");
        liveShapes.Where(op => op.Text is null)
            .Should().OnlyContain(op =>
                new[]
                {
                    Math.Sqrt(Math.Pow(op.BoundsDip.X - radialCenterX, 2) + Math.Pow(op.BoundsDip.Y - radialCenterY, 2)),
                    Math.Sqrt(Math.Pow(op.BoundsDip.X + op.BoundsDip.Width - radialCenterX, 2) + Math.Pow(op.BoundsDip.Y - radialCenterY, 2)),
                    Math.Sqrt(Math.Pow(op.BoundsDip.X - radialCenterX, 2) + Math.Pow(op.BoundsDip.Y + op.BoundsDip.Height - radialCenterY, 2)),
                    Math.Sqrt(Math.Pow(op.BoundsDip.X + op.BoundsDip.Width - radialCenterX, 2) + Math.Pow(op.BoundsDip.Y + op.BoundsDip.Height - radialCenterY, 2))
                }.Min() < 0.25,
                "the WPF compositor must receive four spokes with a shared radial center endpoint");
    }

    [Fact]
    public void Compositor_RadialListSmartArt_RendersAllItemsBeyondOriginalEightItemCutoff()
    {
        var nodes = Enumerable.Range(1, 9)
            .Select(i => ($"n{i}", $"Item {i}"))
            .ToArray();
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/radialList",
            nodes: nodes,
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();

        var liveShapes = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();

        liveShapes.Should().HaveCount(18,
            "the shared WPF/Avalonia compositor should emit nine item boxes and nine center spokes");
        liveShapes.Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(nodes.Select(node => node.Item2));
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(9);
    }

    [Fact]
    public void Compositor_BasicListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/list1",
            nodes: [("n1", "Item 1"), ("n2", "Item 2"), ("n3", "Item 3")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(3, "Basic List should render one live box per node without connectors");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain("Item 1")
            .And.Contain("Item 3");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("Basic List has no connector DrawOps");
    }

    [Fact]
    public void Compositor_GearCycleSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/gearCycle",
            nodes: [("n1", "Initiate"), ("n2", "Coordinate"), ("n3", "Deliver"), ("n4", "Improve")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four gear-cycle boxes plus four connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Initiate");
        renderedText.Should().Contain("Improve");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared gear-cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_BlockCycleSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/blockCycle",
            nodes: [("n1", "Sense"), ("n2", "Decide"), ("n3", "Act"), ("n4", "Learn")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four block-cycle boxes plus four connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Sense");
        renderedText.Should().Contain("Learn");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared block-cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_NonDirectionalCycleSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle",
            nodes: [("n1", "Observe"), ("n2", "Align"), ("n3", "Deliver"), ("n4", "Adapt")],
            parOfConnections: []);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(8, "four non-directional cycle boxes plus four connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Observe");
        renderedText.Should().Contain("Adapt");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume shared non-directional cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_BasicHierarchySmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy",
            nodes: [("R", "Company"), ("C1", "Product"), ("G1", "Platform"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("C1", "G1")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(7, "four basic-hierarchy role boxes plus three connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Company", "Product", "Platform", "Operations"]);
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume shared BasicHierarchy connector DrawOps");
        var boxesByText = liveShapes
            .Where(op => op.Text is not null)
            .ToDictionary(
                op => op.Text!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);
        boxesByText["Company"].BoundsDip.Y.Should().BeLessThan(boxesByText["Product"].BoundsDip.Y);
        boxesByText["Product"].BoundsDip.Y.Should().BeLessThan(boxesByText["Platform"].BoundsDip.Y);
        boxesByText["Operations"].BoundsDip.Y.Should().Be(boxesByText["Product"].BoundsDip.Y);
    }

    [Fact]
    public void Compositor_HorizontalHierarchySmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy",
            nodes: [("R", "Portfolio"), ("C1", "Product"), ("C2", "Operations")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three horizontal-hierarchy boxes plus two connectors should render from shared live data");
        var boxesByText = liveShapes
            .Where(op => op.Text is not null)
            .ToDictionary(
                op => op.Text!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);

        boxesByText.Keys.Should().BeEquivalentTo("Portfolio", "Product", "Operations");
        boxesByText["Product"].BoundsDip.X.Should().BeGreaterThan(boxesByText["Portfolio"].BoundsDip.X,
            "WPF/Avalonia consume shared left-to-right horizontal hierarchy geometry");
        boxesByText["Operations"].BoundsDip.X.Should().Be(boxesByText["Product"].BoundsDip.X,
            "sibling report boxes share the same depth column");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "horizontalHierarchy uses shared connector DrawOps");
    }

    [Fact]
    public void Compositor_LabeledHierarchySmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy",
            nodes: [("R", "Initiative"), ("C1", "Owner"), ("C2", "Outcome")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three labeled-hierarchy boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Initiative");
        renderedText.Should().Contain("Owner");
        renderedText.Should().Contain("Outcome");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "labeledHierarchy uses the shared hierarchy connector DrawOps consumed by WPF and Avalonia");

        var initiative = liveShapes.Single(op => op.Text?.Paragraphs.First().Runs.First().Text == "Initiative");
        var childBoxes = liveShapes
            .Where(op => op.Text?.Paragraphs.First().Runs.First().Text is "Owner" or "Outcome")
            .ToList();
        initiative.BoundsDip.Width.Should().BeGreaterThan(0);
        childBoxes.Should().HaveCount(2);
        childBoxes.Should().OnlyContain(op => op.BoundsDip.X > initiative.BoundsDip.X,
            "the shared labeled-hierarchy plan keeps branch content to the right of its label column");
    }

    [Fact]
    public void Compositor_TableHierarchySmartArt_RendersSharedCellsWithoutCachedFallback()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy",
            nodes:
            [
                ("R", "Portfolio"),
                ("C1", "Owners"),
                ("C2", "Milestones"),
                ("G1", "Delivery"),
                ("G2", "Launch")
            ],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("C1", "G1"), ("C2", "G2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.IsLiveLayoutSupported.Should().BeTrue();
        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5,
            "tableHierarchy should produce one root header and four aligned table cells");
        liveShapes.All(op => op.Text is not null).Should().BeTrue(
            "tableHierarchy's definition has no connecting lines");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().BeEquivalentTo("Portfolio", "Owners", "Milestones", "Delivery", "Launch");
    }

    [Fact]
    public void Compositor_OrgChartSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
            nodes: [("R", "CEO"), ("C1", "Sales"), ("C2", "Engineering")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three org-chart boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("CEO");
        renderedText.Should().Contain("Sales");
        renderedText.Should().Contain("Engineering");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared orgChart connector DrawOps");
    }

    [Fact]
    public void Compositor_OrgChartAssistantSmartArt_RendersAssistantSideSlotFromReaderData()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
            nodes: [("R", "CEO"), ("A", "Executive Assistant"), ("C1", "Sales"), ("C2", "Engineering")],
            parOfConnections: [("R", "A"), ("R", "C1"), ("R", "C2")],
            assistantNodeIds: ["A"]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();
        var root = sa.Data.Nodes.Should().ContainSingle().Subject;
        root.Children.Single(child => child.Text == "Executive Assistant").IsAssistant
            .Should().BeTrue("dgm:pt type=\"asst\" must survive import into the shared SmartArt model");

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(9, "four org-chart boxes plus five routed assistant/report connector segments should render from shared live data");
        var boxesByText = liveShapes
            .Where(op => op.Text is not null)
            .ToDictionary(
                op => op.Text!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);

        boxesByText.Keys.Should().BeEquivalentTo("CEO", "Executive Assistant", "Sales", "Engineering");
        boxesByText["Executive Assistant"].BoundsDip.Y.Should().BeGreaterThan(boxesByText["CEO"].BoundsDip.Y);
        boxesByText["Sales"].BoundsDip.Y.Should().BeGreaterThan(boxesByText["Executive Assistant"].BoundsDip.Y);
        boxesByText["Executive Assistant"].BoundsDip.X.Should().BeGreaterThan(
            boxesByText["CEO"].BoundsDip.X + boxesByText["CEO"].BoundsDip.Width / 2,
            "WPF/Avalonia consume the shared assistant side-slot geometry rather than host-local SmartArt policy");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(5, "assistant routing uses three shared segments and reports use direct shared connector DrawOps");
    }

    [Fact]
    public void Compositor_VerticalBulletListSmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList",
            nodes: [("R", "Project"), ("C1", "Scope"), ("C2", "Timeline"), ("C3", "Risks")],
            parOfConnections: [("R", "C1"), ("R", "C2"), ("R", "C3")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(4, "four vertical-bullet-list bullet rows should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Project");
        renderedText.Should().Contain("Scope");
        renderedText.Should().Contain("Timeline");
        renderedText.Should().Contain("Risks");
        liveShapes.All(op => op.Text is not null)
            .Should().BeTrue("vertical bullet lists are flat editable rows without hierarchy connectors");
    }

    [Fact]
    public void Compositor_UnsupportedProcessSibling_UsesCachedFallbackShapes()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Process,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalProcess",
            IsLiveLayoutSupported = false
        };
        data.Nodes.Add(new SmartArtNode { Text = "Live A", Level = 0 });
        data.Nodes.Add(new SmartArtNode { Text = "Live B", Level = 0 });

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 90,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached sibling fallback" } }
                    }
                }
            }
        });

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 91,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
            SmartArt = smart
        });

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        shapeOps.Should().ContainSingle("unsupported process siblings should use cached drawing fallback");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached sibling fallback");
    }

    [Fact]
    public void Compositor_UnsupportedCycleSibling_UsesCachedFallbackShapes()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Cycle,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/continuousCycle",
            IsLiveLayoutSupported = false
        };
        data.Nodes.Add(new SmartArtNode { Text = "Live A", Level = 0 });
        data.Nodes.Add(new SmartArtNode { Text = "Live B", Level = 0 });
        data.Nodes.Add(new SmartArtNode { Text = "Live C", Level = 0 });

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 92,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached cycle sibling fallback" } }
                    }
                }
            }
        });

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 93,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
            SmartArt = smart
        });

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        shapeOps.Should().ContainSingle("unsupported cycle siblings should use cached drawing fallback");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached cycle sibling fallback");
    }

    [Fact]
    public void Compositor_UnsupportedHierarchySibling_UsesCachedFallbackShapes()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/unknownHierarchy",
            IsLiveLayoutSupported = false
        };
        var root = new SmartArtNode { Text = "Root", Level = 0 };
        root.Children.Add(new SmartArtNode { Text = "Child", Level = 1 });
        data.Nodes.Add(root);

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 94,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached hierarchy sibling fallback" } }
                    }
                }
            }
        });

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 95,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
            SmartArt = smart
        });

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        shapeOps.Should().ContainSingle("unsupported hierarchy siblings should use cached drawing fallback");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached hierarchy sibling fallback");
    }

    [Fact]
    public void RenderCompareCorpus_DefaultList_IsAdmittedOnlyForTheAuditedStaggeredCache()
    {
        var presentation = PptxPackageReader.Read(FindRenderCompareCorpusFile("14-smartart-live.pptx"));
        var smartShape = presentation.Slides[3].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data.Should().NotBeNull();
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
        smartArt.Data.LayoutUniqueId.Should().EndWith("/default");
        smartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.FallbackShapes.Should().HaveCount(5);
        smartArt.FallbackShapes.Select(shape => shape.OffsetXEmu)
            .Should().Equal(0, 3_754_437, 7_508_875, 1_877_218, 5_631_656);
        smartArt.FallbackShapes.Select(shape => shape.OffsetYEmu)
            .Should().Equal(194_468, 194_468, 194_468, 2_583_656, 2_583_656);
        smartArt.FallbackShapes.Select(shape => shape.ExtentCxEmu)
            .Should().AllBeEquivalentTo(3_413_125);
        smartArt.FallbackShapes.Select(shape => shape.ExtentCyEmu)
            .Should().AllBeEquivalentTo(2_047_875);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Requirement 1", "Requirement 2", "Requirement 3", "Requirement 4", string.Empty);

        var live = SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartShape.OffsetXEmu,
            smartShape.OffsetYEmu,
            smartShape.ExtentCxEmu,
            smartShape.ExtentCyEmu,
            presentation.Theme,
            presentation.Slides[3].ColorMapOverride,
            smartArt.QuickStyle,
            smartArt.Colors);

        live.Should().NotBeNull();
        live!.Should().HaveCount(5);
        for (var index = 0; index < live.Count; index++)
        {
            (live[index].OffsetXEmu - smartShape.OffsetXEmu)
                .Should().Be(smartArt.FallbackShapes[index].OffsetXEmu);
            (live[index].OffsetYEmu - smartShape.OffsetYEmu)
                .Should().Be(smartArt.FallbackShapes[index].OffsetYEmu);
            live[index].ExtentCxEmu.Should().Be(smartArt.FallbackShapes[index].ExtentCxEmu);
            live[index].ExtentCyEmu.Should().Be(smartArt.FallbackShapes[index].ExtentCyEmu);
        }

        var upper = live.Take(3).ToArray();
        var lower = live.Skip(3).ToArray();
        upper.Select(shape => shape.OffsetYEmu).Distinct().Should().ContainSingle();
        lower.Select(shape => shape.OffsetYEmu).Distinct().Should().ContainSingle();
        lower[0].OffsetXEmu.Should().BeLessThan(lower[1].OffsetXEmu);
        live[4].TextBody!.Paragraphs.Should().BeEmpty("the fifth package slot is an empty editable template slot");
    }

    [Fact]
    public void RenderCompareCorpus_DefaultList_EffectBearingCacheStaysCachedAndSurvivesSaveReopen()
    {
        var sourcePath = FindRenderCompareCorpusFile("14-smartart-live.pptx");
        var mutatedPath = Path.Combine(_tempDir, "default-list-effect.pptx");
        File.Copy(sourcePath, mutatedPath);
        AddOuterShadowToDefaultDrawing(mutatedPath);

        var loaded = PptxPackageReader.Read(mutatedPath);
        var loadedSmartArt = loaded.Slides[3].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        loadedSmartArt.Data!.IsLiveLayoutSupported.Should().BeFalse();
        SmartArtLayoutEngine.Layout(
            loadedSmartArt.Data,
            loaded.Slides[3].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).OffsetXEmu,
            loaded.Slides[3].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).OffsetYEmu,
            loaded.Slides[3].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).ExtentCxEmu,
            loaded.Slides[3].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).ExtentCyEmu,
            loaded.Theme)
            .Should().BeNull("an effect-bearing imported cache is outside the live default contract");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var reopened = PptxPackageReader.Read(new MemoryStream(saved.ToArray()));
        var reopenedSmartArt = reopened.Slides[3].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopenedSmartArt.Data!.IsLiveLayoutSupported.Should().BeFalse();
        Encoding.UTF8.GetString(reopenedSmartArt.Parts[reopenedSmartArt.DrawingPartPath!].Bytes)
            .Should().Contain("outerShdw");
        reopenedSmartArt.FallbackShapes.Should().HaveCount(5);
        reopenedSmartArt.FallbackShapes[3].PlainText.Should().Be("Requirement 4");
    }

    [Theory]
    [InlineData("slot-count")]
    [InlineData("geometry")]
    [InlineData("text")]
    public void RenderCompareCorpus_DefaultList_NearMissesRemainCached(string mutation)
    {
        var mutatedPath = Path.Combine(_tempDir, $"default-list-{mutation}.pptx");
        File.Copy(FindRenderCompareCorpusFile("14-smartart-live.pptx"), mutatedPath);
        MutateDefaultDrawing(mutatedPath, mutation);

        var presentation = PptxPackageReader.Read(mutatedPath);
        var smartShape = presentation.Slides[3].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var smartArt = smartShape.SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeFalse(mutation);
        SmartArtLayoutEngine.Layout(
            smartArt.Data,
            smartShape.OffsetXEmu,
            smartShape.OffsetYEmu,
            smartShape.ExtentCxEmu,
            smartShape.ExtentCyEmu,
            presentation.Theme)
            .Should().BeNull(mutation);
    }

    [Fact]
    public void Reader_ParsesSmartArtData_HierarchyWithChildren()
    {
        // root "R" has two children "C1", "C2" via parOf connections
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            nodes: [("R", "Root"), ("C1", "Child1"), ("C2", "Child2")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa   = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy, "uniqueId contains 'hierarchy'");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue("hierarchy1 is an admitted live layout");
        sa.Data.Nodes.Should().HaveCount(1, "one root node");

        var root = sa.Data.Nodes[0];
        root.Text.Should().Be("Root");
        root.Children.Should().HaveCount(2, "root has two parOf children");
        root.Children.Select(c => c.Text).Should().BeEquivalentTo(new[] { "Child1", "Child2" });
    }

    [Fact]
    public void Reader_ParsesSmartArtData_FamilyClassification_Cycle()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/cycle1",
            nodes: [("A", "Phase A"), ("B", "Phase B"), ("C", "Phase C")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle);
    }

    [Fact]
    public void Reader_ParsesSmartArtData_FamilyClassification_List()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/list1",
            nodes: [("A", "Bullet A"), ("B", "Bullet B")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void Reader_ParsesSmartArtQuickStyleAndColorsMetadata()
    {
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        var quickStyleXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "styleDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute("uniqueId", "urn:smartart:style:intense-effect"),
                new XElement(dgmNs + "title", new XAttribute("val", "Intense Effect")),
                new XElement(dgmNs + "catLst",
                    new XElement(dgmNs + "cat", new XAttribute("type", "3D"))),
                new XElement(dgmNs + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(aNs + "lnRef", new XAttribute("idx", "2")),
                    new XElement(aNs + "fillRef", new XAttribute("idx", "5")),
                    new XElement(aNs + "effectRef", new XAttribute("idx", "1")),
                    new XElement(aNs + "fontRef", new XAttribute("idx", "major")))));

        var colorsXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute("uniqueId", "urn:smartart:colors:colorful-accent"),
                new XElement(dgmNs + "title", new XAttribute("val", "Colorful Accent")),
                new XElement(dgmNs + "catLst",
                    new XElement(dgmNs + "cat", new XAttribute("type", "colorful"))),
                new XElement(dgmNs + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(dgmNs + "fillClrLst",
                        new XElement(aNs + "schemeClr", new XAttribute("val", "accent3")),
                        new XElement(aNs + "srgbClr", new XAttribute("val", "8844CC"))))));

        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes: [("A", "Alpha"), ("B", "Beta")],
            parOfConnections: [],
            quickStyleXml: quickStyleXml,
            colorsXml: colorsXml);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.QuickStyle.Should().NotBeNull();
        sa.QuickStyle!.UniqueId.Should().Be("urn:smartart:style:intense-effect");
        sa.QuickStyle.Title.Should().Be("Intense Effect");
        sa.QuickStyle.Category.Should().Be("3D");
        sa.QuickStyle.StyleLabels.Should().Contain("node0");
        sa.QuickStyle.StyleLabelMetadata.Should().ContainSingle(label =>
            label.Name == "node0"
            && label.LineReferenceIndex == 2
            && label.FillReferenceIndex == 5
            && label.EffectReferenceIndex == 1
            && label.FontReferenceIndex == "major");

        sa.Colors.Should().NotBeNull();
        sa.Colors!.UniqueId.Should().Be("urn:smartart:colors:colorful-accent");
        sa.Colors.Title.Should().Be("Colorful Accent");
        sa.Colors.Category.Should().Be("colorful");
        sa.Colors.ColorLabels.Should().Contain("node0");
        sa.Colors.Palette.Should().HaveCount(2);
        sa.Colors.Palette[0].SchemeColor!.RoleName.Should().Be("accent3");
        sa.Colors.Palette[1].Resolved.Should().Be(SrgbColor.FromRgb(0x8844CC));
        sa.Parts["ppt/diagrams/quickStyle1.xml"].Bytes.Should().NotBeEmpty();
        sa.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Reader_SmartArtColorMetadata_NodeFillPalette_IsNotPollutedByLineOrTextColors()
    {
        // Mirrors a real "Colorful - Accent Colors" colorsDef: the node0 styleLbl carries
        // fillClrLst=accent1..accent6, THEN linClrLst=accent1..accent6-with-shade (a
        // genuinely different resolved color, so the old flatten+dedup did NOT collapse
        // it), THEN txFillClrLst/txLinClrLst. Only the fillClrLst colors may end up in the
        // node fill palette (KB1).
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        XElement SchemeClr(string accent) => new(aNs + "schemeClr", new XAttribute("val", accent));
        XElement ShadedSchemeClr(string accent) => new(aNs + "schemeClr",
            new XAttribute("val", accent),
            new XElement(aNs + "shade", new XAttribute("val", "50000")));

        var accents = new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "accent6" };

        var colorsXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute("uniqueId", "urn:smartart:colors:colorful-accent-colors"),
                new XElement(dgmNs + "title", new XAttribute("val", "Colorful - Accent Colors")),
                new XElement(dgmNs + "catLst",
                    new XElement(dgmNs + "cat", new XAttribute("type", "colorful"))),
                new XElement(dgmNs + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(dgmNs + "fillClrLst", accents.Select(SchemeClr)),
                    new XElement(dgmNs + "linClrLst", accents.Select(ShadedSchemeClr)),
                    new XElement(dgmNs + "txFillClrLst", new XElement(aNs + "schemeClr", new XAttribute("val", "tx1"))),
                    new XElement(dgmNs + "txLinClrLst", new XElement(aNs + "schemeClr", new XAttribute("val", "tx1"))))));

        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes: [("A", "Alpha"), ("B", "Beta"), ("C", "Gamma"), ("D", "Delta"), ("E", "Epsilon"), ("F", "Zeta"), ("G", "Eta")],
            parOfConnections: [],
            colorsXml: colorsXml);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Colors.Should().NotBeNull();
        sa.Colors!.Palette.Should().HaveCount(6, "the node fill cycle is fillClrLst's 6 accents, not the 6+ line/text colors too");

        var expectedRoleNames = accents;
        sa.Colors.Palette.Select(c => c.SchemeColor!.RoleName)
            .Should().BeEquivalentTo(expectedRoleNames, options => options.WithStrictOrdering(),
                "node fill palette must be exactly fillClrLst's accent cycle in document order");

        // None of the fill-palette entries carry the linClrLst shade transform: the
        // resolved color for each accent must equal the plain (unshaded) scheme color,
        // proving the shaded line-list entries never made it into the fill palette.
        foreach (var color in sa.Colors.Palette)
            color.SchemeColor!.Shade.Should().Be(1.0, "fill palette entries must come from fillClrLst, not the shaded linClrLst");

        // node index wraps modulo the fill palette length (6): node 6 (0-based index 6,
        // the 7th node "Eta") should reuse accent1's color, same as node 0.
        sa.Colors.Palette[0].SchemeColor!.RoleName.Should().Be("accent1");
    }

    [Fact]
    public void Reader_SmartArtColorMetadata_SingleFillColor_IsUniform()
    {
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        var colorsXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute("uniqueId", "urn:smartart:colors:colorful-accent-colors"),
                new XElement(dgmNs + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(dgmNs + "fillClrLst",
                        new XElement(aNs + "schemeClr", new XAttribute("val", "accent1"))),
                    new XElement(dgmNs + "linClrLst",
                        new XElement(aNs + "schemeClr", new XAttribute("val", "accent1"),
                            new XElement(aNs + "shade", new XAttribute("val", "50000")))))));

        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes: [("A", "Alpha"), ("B", "Beta")],
            parOfConnections: [],
            colorsXml: colorsXml);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Colors.Should().NotBeNull();
        sa.Colors!.Palette.Should().HaveCount(1, "a single fillClrLst color stays uniform and is not joined by the linClrLst shade");
        sa.Colors.Palette[0].SchemeColor!.RoleName.Should().Be("accent1");
        sa.Colors.Palette[0].SchemeColor!.Shade.Should().Be(1.0);
    }

    [Fact]
    public void Reader_SmartArtColorMetadata_NoNodeFillList_FallsBackToEmptyPalette()
    {
        // A colorsDef with a styleLbl but no fillClrLst at all (e.g. only line/text lists,
        // or a non-node label like "bg") must not crash and must not surface those
        // unrelated colors as the node fill palette; the planner already falls back to
        // theme accents when Palette is empty.
        var dgmNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        var colorsXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute("uniqueId", "urn:smartart:colors:no-fill-list"),
                new XElement(dgmNs + "styleLbl",
                    new XAttribute("name", "bg"),
                    new XElement(dgmNs + "txFillClrLst",
                        new XElement(aNs + "schemeClr", new XAttribute("val", "tx1"))))));

        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            nodes: [("A", "Alpha")],
            parOfConnections: [],
            colorsXml: colorsXml);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Colors.Should().NotBeNull();
        sa.Colors!.Palette.Should().BeEmpty("no fillClrLst exists anywhere, so the node fill palette must be empty, not populated from txFillClrLst");
    }

    [Fact]
    public void Reader_ParsesSmartArtData_UnknownFamilyIsUnknown()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/freeformDiagram",
            nodes: [("A", "X")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data!.Family.Should().Be(SmartArtFamily.Unknown,
            "layout uniqueId 'freeformDiagram' doesn't match any supported family keyword so it should be Unknown");
    }

    [Fact]
    public void Reader_SmartArtData_DoesNotBreakExistingFallbackShapeParse()
    {
        // Verify that adding Data parsing doesn't corrupt the fallback-shape round-trip
        var nodeTexts = new[] { "Step A", "Step B" };
        var pptxPath  = MakeSmartArtPptx(nodeTexts); // existing helper — has dsp:drawing shapes
        var pres      = PptxPackageReader.Read(pptxPath);

        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        // FallbackShapes are still populated (round-trip path unchanged)
        sa.FallbackShapes.Should().HaveCount(nodeTexts.Length);

        // Parts bytes are still there (round-trip writes them back verbatim)
        sa.Parts.Should().ContainKey("ppt/diagrams/data1.xml");
    }

    private static string FindRenderCompareCorpusFile(string fileName) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", fileName);

    private static void AddOuterShadowToDefaultDrawing(string path) =>
        RewriteDefaultDrawing(path, document =>
            document.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "effectLst")
                .First()
                .Add(new XElement(
                    XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "outerShdw",
                    new XAttribute("blurRad", "25400"))));

    private static void MutateDefaultDrawing(string path, string mutation)
    {
        var dspNs = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var aNs = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        RewriteDefaultDrawing(path, document =>
        {
            var shapes = document.Descendants(dspNs + "sp").ToList();
            switch (mutation)
            {
                case "slot-count":
                    shapes[^1].Remove();
                    break;
                case "geometry":
                    shapes[^1].Element(dspNs + "spPr")!
                        .Element(aNs + "xfrm")!
                        .Element(aNs + "off")!
                        .SetAttributeValue("x", "5631655");
                    break;
                case "text":
                    shapes[3].Descendants(aNs + "t").Single().Value = "Requirement X";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        });
    }

    private static void RewriteDefaultDrawing(string path, Action<XDocument> mutate)
    {
        const string drawingPath = "ppt/diagrams/drawing4.xml";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var source = archive.GetEntry(drawingPath)!;
        XDocument document;
        using (var stream = source.Open())
            document = XDocument.Load(stream);

        mutate(document);
        source.Delete();
        var replacement = archive.CreateEntry(drawingPath);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RewriteSmartArtDrawing(string path, Action<XDocument> mutate)
    {
        const string drawingPath = "ppt/diagrams/drawing1.xml";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var source = archive.GetEntry(drawingPath)!;
        XDocument document;
        using (var stream = source.Open())
            document = XDocument.Load(stream);

        mutate(document);
        source.Delete();
        var replacement = archive.CreateEntry(drawingPath);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RewriteSmartArtData(string path, Action<XDocument> mutate)
    {
        const string dataPath = "ppt/diagrams/data1.xml";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var source = archive.GetEntry(dataPath)!;
        XDocument document;
        using (var stream = source.Open())
            document = XDocument.Load(stream);

        mutate(document);
        source.Delete();
        var replacement = archive.CreateEntry(dataPath);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RewriteList1Drawing(string path, Action<XDocument> mutate)
    {
        const string drawingPath = "ppt/diagrams/drawing5.xml";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var source = archive.GetEntry(drawingPath)!;
        XDocument document;
        using (var stream = source.Open())
            document = XDocument.Load(stream);

        mutate(document);
        source.Delete();
        var replacement = archive.CreateEntry(drawingPath);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RewriteList1Data(string path, Action<XDocument> mutate)
    {
        const string dataPath = "ppt/diagrams/data5.xml";
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var source = archive.GetEntry(dataPath)!;
        XDocument document;
        using (var stream = source.Open())
            document = XDocument.Load(stream);

        mutate(document);
        source.Delete();
        var replacement = archive.CreateEntry(dataPath);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
