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
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.SmartArtTests", Guid.NewGuid().ToString("N"));

    public SmartArtTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    /// <summary>
    /// Builds a minimal but self-consistent in-memory .pptx archive with one SmartArt shape.
    /// Writes it to disk and returns the path.
    /// </summary>
    private string MakeSmartArtPptx(
        string[] nodeTexts,
        bool pictureCaptionList = false,
        bool includeNodeImage = false)
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
        foreach (var text in nodeTexts)
        {
            int idx = shapeIdx++;
            if (pictureCaptionList && includeNodeImage)
            {
                fallbackEls.Add(new XElement(dspNs + "pic",
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
                        new XElement(aNs + "off", new XAttribute("x", (idx - 1) * 914400L), new XAttribute("y", "457200")),
                        new XElement(aNs + "ext", new XAttribute("cx", "914400"), new XAttribute("cy", "457200"))),
                    new XElement(aNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(aNs + "avLst")),
                    new XElement(aNs + "solidFill",
                        new XElement(aNs + "srgbClr", new XAttribute("val", "4472C4")))),
                new XElement(dspNs + "txBody",
                    new XElement(aNs + "bodyPr"),
                    new XElement(aNs + "lstStyle"),
                    new XElement(aNs + "p",
                        new XElement(aNs + "r",
                            new XElement(aNs + "rPr", new XAttribute("lang", "en-US")),
                            new XElement(aNs + "t", text))))));
        }

        var dspDrawingXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dspNs + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", dspNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(dspNs + "spTree", fallbackEls)));

        // Build minimal diagram data XML (just a root element)
        var dataXml = pictureCaptionList
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
                                            new XElement(aNs + "t", text)))))))))
            : new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(dgmNs + "dataModel",
                    new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));

        // Minimal layout, quickStyle, colors XML
        var layoutXml  = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName),
                pictureCaptionList
                    ? new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList")
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
                new XElement(ctNs + "Override", new XAttribute("PartName", "/ppt/diagrams/colors1.xml"),    new XAttribute("ContentType", diagramColorsCT)),
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

        // Slide rels: point to layout + 4 diagram parts
        WriteEntry("ppt/slides/_rels/slide1.xml.rels", MakeRels(pkgNs,
            ("rId1",   "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml"),
            ("rIdDm1", dmRelType, "../diagrams/data1.xml"),
            ("rIdLo1", loRelType, "../diagrams/layout1.xml"),
            ("rIdQs1", qsRelType, "../diagrams/quickStyle1.xml"),
            ("rIdCs1", csRelType, "../diagrams/colors1.xml")));

        // Diagram parts
        WriteXml("ppt/diagrams/data1.xml",       dataXml);
        WriteXml("ppt/diagrams/layout1.xml",     layoutXml);
        WriteXml("ppt/diagrams/quickStyle1.xml", qsXml);
        WriteXml("ppt/diagrams/colors1.xml",     colorsXml);
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
        }
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
    }

    [Fact]
    public void Reader_SmartArt_PictureCaptionList_WithoutImage_KeepsLiveLayoutDisabled()
    {
        var pptxPath = MakeSmartArtPptx(["Caption only"], pictureCaptionList: true, includeNodeImage: false);
        var pres = PptxPackageReader.Read(pptxPath);

        var smart = pres.Slides[0].Shapes
            .First(s => s.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smart.Data.Should().NotBeNull();
        smart.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "pictureCaptionList must not claim live layout when node images cannot be resolved");
        smart.Data.Nodes.Should().ContainSingle();
        smart.Data.Nodes[0].Picture.Should().BeNull();
        smart.FallbackShapes.Should().NotBeEmpty("cached drawing remains the render fallback");
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

    // ── Compositor ───────────────────────────────────────────────────────────────

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
        XDocument? colorsXml = null)
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

        // Build data1.xml with ptLst + cxnLst
        var ptElems = nodes.Select(n =>
            new XElement(dgmNs + "pt",
                new XAttribute("modelId", n.id),
                new XAttribute("type", "node"),
                new XElement(dgmNs + "t",
                    new XElement(aNs + "p",
                        new XElement(aNs + "r",
                            new XElement(aNs + "t", n.text))))));

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

        // Minimal dsp:drawing (empty spTree)
        var dspXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dspNs + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", dspNs.NamespaceName),
                new XElement(dspNs + "spTree")));

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

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

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

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

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

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

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
            nodes: [("R", "CEO"), ("C1", "Sales"), ("C2", "Engineering")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy,
            "basicHierarchy is a hierarchy-family layout and should stay renderer-neutral");
        sa.Data.IsLiveLayoutSupported.Should().BeTrue(
            "basicHierarchy is in the bounded shared live-layout planner");
        sa.Data.Nodes.Should().ContainSingle();
        sa.Data.Nodes[0].Text.Should().Be("CEO");
        sa.Data.Nodes[0].Children.Select(n => n.Text).Should().BeEquivalentTo(new[] { "Sales", "Engineering" });
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
    public void Reader_ParsesKnownListFamilyButDisablesLiveLayoutForUnsupportedSibling()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList",
            nodes: [("id1", "Item 1"), ("id2", "Item 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.List,
            "unsupported list siblings still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeFalse(
            "list-family layouts outside the bounded allow-list should keep cached-drawing fallback");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Item 1", "Item 2");
    }

    [Fact]
    public void Reader_ParsesKnownCycleFamilyButDisablesLiveLayoutForUnsupportedSibling()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/blockCycle",
            nodes: [("id1", "Phase 1"), ("id2", "Phase 2"), ("id3", "Phase 3")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Cycle,
            "unsupported cycle siblings still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeFalse(
            "cycle-family layouts outside the bounded allow-list should keep cached-drawing fallback");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Phase 1", "Phase 2", "Phase 3");
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
    public void Reader_ParsesKnownHierarchyFamilyButDisablesLiveLayoutForUnsupportedSibling()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy",
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
    public void Reader_ParsesKnownFamilyButDisablesLiveLayoutForUnsupportedVariant()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess",
            nodes: [("id1", "Stage 1"), ("id2", "Stage 2")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Process,
            "unsupported variants still retain broad family metadata for future layout slices");
        sa.Data.IsLiveLayoutSupported.Should().BeFalse(
            "process-family layouts outside the bounded allow-list should keep cached-drawing fallback");
        sa.Data.Nodes.Select(n => n.Text).Should().Equal("Stage 1", "Stage 2");
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

        liveShapes.Should().HaveCount(5, "three chevron-process boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Plan");
        renderedText.Should().Contain("Build");
        renderedText.Should().Contain("Ship");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared chevron-process connector DrawOps");
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
    public void Compositor_BasicHierarchySmartArt_RendersSharedLiveShapes()
    {
        var pptxPath = MakeSmartArtPptxWithNodeTree(
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy",
            nodes: [("R", "CEO"), ("C1", "Sales"), ("C2", "Engineering")],
            parOfConnections: [("R", "C1"), ("R", "C2")]);

        var pres = PptxPackageReader.Read(pptxPath);
        var sa = pres.Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data.Should().NotBeNull();
        sa.Data!.Family.Should().Be(SmartArtFamily.Hierarchy);
        sa.Data.IsLiveLayoutSupported.Should().BeTrue();

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var liveShapes = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        liveShapes.Should().HaveCount(5, "three basic-hierarchy boxes plus two connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("CEO");
        renderedText.Should().Contain("Sales");
        renderedText.Should().Contain("Engineering");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared connector DrawOps");
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

        liveShapes.Should().HaveCount(7, "four vertical-bullet-list boxes plus three connectors should render from shared live data");
        var renderedText = liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Project");
        renderedText.Should().Contain("Scope");
        renderedText.Should().Contain("Timeline");
        renderedText.Should().Contain("Risks");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume shared connector DrawOps");
    }

    [Fact]
    public void Compositor_UnsupportedProcessSibling_UsesCachedFallbackShapes()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Process,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess",
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
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/textCycle",
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
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy",
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
                new XElement(dgmNs + "styleLbl", new XAttribute("name", "node0"))));

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
            layoutUniqueId: "urn:microsoft.com/office/officeart/2005/8/layout/matrix1",
            nodes: [("A", "X")],
            parOfConnections: []);

        var sa = PptxPackageReader.Read(pptxPath)
            .Slides[0].Shapes.First(s => s.Kind == SlideShapeKind.SmartArt).SmartArt!;

        sa.Data!.Family.Should().Be(SmartArtFamily.Unknown,
            "layout uniqueId 'matrix1' doesn't match any supported family keyword so it should be Unknown");
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
}
