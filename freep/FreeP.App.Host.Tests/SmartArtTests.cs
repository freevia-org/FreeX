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
    private string MakeSmartArtPptx(string[] nodeTexts)
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
        const string diagramQsCT      = "application/vnd.openxmlformats-officedocument.drawingml.diagramQuickStyle+xml";
        const string diagramColorsCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
        const string diagramDrawingCT = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

        const string dmRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
        const string loRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
        const string qsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
        const string csRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
        const string dgmDrawRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

        // Build dsp:drawing XML with fallback shapes
        int shapeIdx = 1;
        var fallbackSpEls = nodeTexts.Select(text =>
        {
            int idx = shapeIdx++;
            return new XElement(dspNs + "sp",
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
                            new XElement(aNs + "t", text)))));
        }).ToArray();

        var dspDrawingXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dspNs + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", dspNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XElement(dspNs + "spTree", fallbackSpEls)));

        // Build minimal diagram data XML (just a root element)
        var dataXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(dgmNs + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));

        // Minimal layout, quickStyle, colors XML
        var layoutXml  = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(dgmNs + "layoutDef",  new XAttribute(XNamespace.Xmlns + "dgm", dgmNs.NamespaceName)));
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

        // data1.xml rels: points to drawing1.xml
        WriteEntry("ppt/diagrams/_rels/data1.xml.rels", MakeRels(pkgNs,
            ("rIdDraw1", dgmDrawRelType, "drawing1.xml")));

        return path;
    }

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
}
