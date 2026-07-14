using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline SmartArt / DrawingML diagrams (roadmap item Y1): a
/// <see cref="Run.SmartArt"/> must survive write→read with its kind and node texts/structure, materialise
/// the FOUR diagram PARTS (data / layout / quickStyle / colors) each with a content-type Override and a
/// document relationship, and reference all four from an inline w:drawing's dgm:relIds.
/// </summary>
public class SmartArtRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";

    private const string DataContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
    private const string LayoutContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
    private const string StyleContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
    private const string ColorsContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
    private const string DrawingContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml";
    private const string DrawingRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument SingleDiagramDocument(SmartArt smartArt)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void ListDiagram_KindAndNodeTexts_SurviveRoundTrip()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta", "Gamma"]);

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var run = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null);
        var diagram = run.SmartArt!;
        diagram.Kind.Should().Be(SmartArtKind.List);
        diagram.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
        diagram.Nodes.Should().OnlyContain(n => n.Children.Count == 0);
    }

    [Fact]
    public void ProcessDiagram_KindSurvivesRoundTrip()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Ship"]);

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!.Kind
            .Should().Be(SmartArtKind.Process);
    }

    [Fact]
    public void Hierarchy_NodeTreeStructureSurvivesRoundTrip()
    {
        // CEO -> { VP Eng -> { Lead }, VP Sales }
        var ceo = new SmartArtNode("CEO");
        var vpEng = ceo.AddChild("VP Eng");
        vpEng.AddChild("Lead");
        ceo.AddChild("VP Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(ceo);

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Kind.Should().Be(SmartArtKind.Hierarchy);

        var root = diagram.Nodes.Should().ContainSingle().Subject;
        root.Text.Should().Be("CEO");
        root.Children.Select(c => c.Text).Should().Equal("VP Eng", "VP Sales");

        var eng = root.Children[0];
        eng.Children.Should().ContainSingle().Which.Text.Should().Be("Lead");
        root.Children[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void Diagram_FourPartsContentTypesAndRelationships_ArePresentInZip()
    {
        var docx = WriteBytes(SingleDiagramDocument(SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"])));

        // The four diagram parts exist in the package.
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/diagrams/data1.xml").Should().NotBeNull("the data part carries the node text");
            zip.GetEntry("word/diagrams/layout1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/quickStyle1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/colors1.xml").Should().NotBeNull();
        }

        // [Content_Types].xml declares an Override for each of the four parts.
        var types = EntryXml(docx, "[Content_Types].xml");
        var overrides = types.Root!.Elements(Ct + "Override").ToList();
        overrides.Should().Contain(o => o.Attribute("PartName")!.Value == "/word/diagrams/data1.xml" && o.Attribute("ContentType")!.Value == DataContentType);
        overrides.Should().Contain(o => o.Attribute("PartName")!.Value == "/word/diagrams/layout1.xml" && o.Attribute("ContentType")!.Value == LayoutContentType);
        overrides.Should().Contain(o => o.Attribute("PartName")!.Value == "/word/diagrams/quickStyle1.xml" && o.Attribute("ContentType")!.Value == StyleContentType);
        overrides.Should().Contain(o => o.Attribute("PartName")!.Value == "/word/diagrams/colors1.xml" && o.Attribute("ContentType")!.Value == ColorsContentType);

        // document.xml.rels carries the four diagram relationships pointing at the parts.
        var rels = EntryXml(docx, "word/_rels/document.xml.rels");
        var relList = rels.Root!.Elements(Rel + "Relationship").ToList();
        var dataRel = relList.Single(r => r.Attribute("Type")!.Value.EndsWith("/diagramData", System.StringComparison.Ordinal));
        var layoutRel = relList.Single(r => r.Attribute("Type")!.Value.EndsWith("/diagramLayout", System.StringComparison.Ordinal));
        var styleRel = relList.Single(r => r.Attribute("Type")!.Value.EndsWith("/diagramQuickStyle", System.StringComparison.Ordinal));
        var colorsRel = relList.Single(r => r.Attribute("Type")!.Value.EndsWith("/diagramColors", System.StringComparison.Ordinal));
        dataRel.Attribute("Target")!.Value.Should().Be("diagrams/data1.xml");
        layoutRel.Attribute("Target")!.Value.Should().Be("diagrams/layout1.xml");
        styleRel.Attribute("Target")!.Value.Should().Be("diagrams/quickStyle1.xml");
        colorsRel.Attribute("Target")!.Value.Should().Be("diagrams/colors1.xml");

        // document.xml references all four relationships from the inline dgm:relIds.
        var documentXml = EntryXml(docx, "word/document.xml");
        var relIds = documentXml.Descendants(Dgm + "relIds").Single();
        relIds.Attribute(R + "dm")!.Value.Should().Be(dataRel.Attribute("Id")!.Value);
        relIds.Attribute(R + "lo")!.Value.Should().Be(layoutRel.Attribute("Id")!.Value);
        relIds.Attribute(R + "qs")!.Value.Should().Be(styleRel.Attribute("Id")!.Value);
        relIds.Attribute(R + "cs")!.Value.Should().Be(colorsRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void DataPart_CarriesNodeTextsInDataModel()
    {
        var docx = WriteBytes(SingleDiagramDocument(SmartArt.Create(SmartArtKind.List, ["Red", "Green", "Blue"])));
        var dataXml = EntryXml(docx, "word/diagrams/data1.xml");

        dataXml.Root!.Name.Should().Be(Dgm + "dataModel");
        var texts = dataXml.Descendants(A + "t").Select(t => t.Value).ToList();
        texts.Should().Contain(["Red", "Green", "Blue"]);
    }

    [Fact]
    public void Diagram_RoundTripsInsideTableCell()
    {
        // SmartArt is an inline run mark, so it must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Cell A", "Cell B"]);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromSmartArt(smartArt));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        var diagram = cellParagraph.Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Nodes.Select(n => n.Text).Should().Equal("Cell A", "Cell B");
    }

    [Fact]
    public void Diagram_RenderedDrawingPart_ContentTypeAndDataRel_ArePresent()
    {
        // F2: a fifth part (word/diagrams/drawing1.xml — dsp:drawing) carries pre-laid-out shapes, referenced
        // from the data part via a diagramDrawing relationship + a dgm:dataModelExt inside the data model.
        var docx = WriteBytes(SingleDiagramDocument(SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"])));

        // The drawing part exists in the package.
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
            zip.GetEntry("word/diagrams/drawing1.xml").Should().NotBeNull("the rendered-geometry part must be emitted");

        // [Content_Types].xml declares the drawing Override.
        var overrides = EntryXml(docx, "[Content_Types].xml").Root!.Elements(Ct + "Override").ToList();
        overrides.Should().Contain(o =>
            o.Attribute("PartName")!.Value == "/word/diagrams/drawing1.xml" &&
            o.Attribute("ContentType")!.Value == DrawingContentType);

        // The data part's own .rels carries a diagramDrawing relationship to drawing1.xml.
        var dataRels = EntryXml(docx, "word/diagrams/_rels/data1.xml.rels");
        var drawingRel = dataRels.Root!.Elements(Rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value == DrawingRelType);
        drawingRel.Attribute("Target")!.Value.Should().Be("drawing1.xml");

        // The data part remains schema-valid and no longer carries the old dgm:dataModelExt marker.
        var dataXml = EntryXml(docx, "word/diagrams/data1.xml");
        dataXml.Descendants(Dgm + "dataModelExt").Should().BeEmpty();
    }

    [Fact]
    public void RenderedDrawing_HasOneShapePerNode_WithTextAndNonZeroExtent()
    {
        var docx = WriteBytes(SingleDiagramDocument(SmartArt.Create(SmartArtKind.List, ["Red", "Green", "Blue"])));
        var drawing = EntryXml(docx, "word/diagrams/drawing1.xml");

        drawing.Root!.Name.Should().Be(Dsp + "drawing");
        var shapes = drawing.Descendants(Dsp + "sp").ToList();
        shapes.Should().HaveCount(3, "one dsp:sp per node");

        // Each shape carries its node text and a non-zero a:xfrm extent.
        shapes.SelectMany(sp => sp.Descendants(A + "t").Select(t => t.Value))
            .Should().Contain(["Red", "Green", "Blue"]);
        foreach (var sp in shapes)
        {
            var ext = sp.Descendants(A + "ext").Single();
            long.Parse(ext.Attribute("cx")!.Value).Should().BeGreaterThan(0);
            long.Parse(ext.Attribute("cy")!.Value).Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Hierarchy_RenderedDrawing_HasShapePerNodeIncludingNestedChildren()
    {
        var ceo = new SmartArtNode("CEO");
        var vpEng = ceo.AddChild("VP Eng");
        vpEng.AddChild("Lead");
        ceo.AddChild("VP Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(ceo);

        var docx = WriteBytes(SingleDiagramDocument(smartArt));
        var drawing = EntryXml(docx, "word/diagrams/drawing1.xml");

        // 4 nodes total (CEO, VP Eng, Lead, VP Sales) → 4 shapes.
        drawing.Descendants(Dsp + "sp").Should().HaveCount(4);
        drawing.Descendants(A + "t").Select(t => t.Value)
            .Should().Contain(["CEO", "VP Eng", "Lead", "VP Sales"]);
    }

    [Fact]
    public void TwoDiagrams_GetDistinctPartsAndRelationships()
    {
        var doc = new TextDocument();
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromSmartArt(SmartArt.Create(SmartArtKind.List, ["First diagram"])));
        var p2 = new Paragraph();
        p2.Runs.Add(Run.FromSmartArt(SmartArt.Create(SmartArtKind.Process, ["Second diagram"])));
        doc.Blocks.Add(p1);
        doc.Blocks.Add(p2);

        var docx = WriteBytes(doc);
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/diagrams/data1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/data2.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/layout2.xml").Should().NotBeNull();
        }

        var read = DocxReader.Read(new MemoryStream(docx));
        var diagrams = read.Paragraphs.SelectMany(p => p.Runs).Where(r => r.SmartArt is not null).Select(r => r.SmartArt!).ToList();
        diagrams.Should().HaveCount(2);
        diagrams[0].Nodes.Single().Text.Should().Be("First diagram");
        diagrams[0].Kind.Should().Be(SmartArtKind.List);
        diagrams[1].Nodes.Single().Text.Should().Be("Second diagram");
        diagrams[1].Kind.Should().Be(SmartArtKind.Process);
    }

    // ── Mutation round-trips: each mutation must survive write→read unchanged ──────────────────

    [Fact]
    public void MutatedDiagram_AddedNode_SurvivesRoundTrip()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["One", "Two"]);
        smartArt.Nodes.Add(new SmartArtNode("Three")); // Add Shape mutation

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Nodes.Select(n => n.Text).Should().Equal("One", "Two", "Three");
    }

    [Fact]
    public void MutatedDiagram_RemovedNode_SurvivesRoundTrip()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Alpha", "Beta", "Gamma"]);
        smartArt.Nodes.RemoveAt(smartArt.Nodes.Count - 1); // Remove Shape mutation

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void MutatedDiagram_PromotedNode_SurvivesRoundTrip()
    {
        // CEO → { VP Eng, VP Sales }  →  promote VP Sales  →  CEO → { VP Eng }, VP Sales
        var ceo = new SmartArtNode("CEO");
        ceo.AddChild("VP Eng");
        ceo.AddChild("VP Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(ceo);

        var last = ceo.Children[^1];
        ceo.Children.RemoveAt(ceo.Children.Count - 1);
        smartArt.Nodes.Insert(1, last);

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Nodes.Select(n => n.Text).Should().Equal("CEO", "VP Sales");
        diagram.Nodes[0].Children.Select(c => c.Text).Should().Equal("VP Eng");
    }

    [Fact]
    public void MutatedDiagram_KindChangedByReplace_SurvivesRoundTrip()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["A", "B"]);
        var replacement = SmartArt.Create(SmartArtKind.List, ["X", "Y", "Z"]);
        smartArt.Kind = replacement.Kind;
        smartArt.Nodes.Clear();
        foreach (var node in replacement.Nodes)
            smartArt.Nodes.Add(node);

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.Kind.Should().Be(SmartArtKind.List);
        diagram.Nodes.Select(n => n.Text).Should().Equal("X", "Y", "Z");
    }

    // ── Gallery id round-trip tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void LayoutId_RoundTrips_ViaLayoutPart()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["A", "B"]);
        smartArt.LayoutId = "cycle1";

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.LayoutId.Should().Be("cycle1");
    }

    [Fact]
    public void ColorSchemeId_RoundTrips_ViaColorsPart()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["X", "Y"]);
        smartArt.ColorSchemeId = "colorful2";

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.ColorSchemeId.Should().Be("colorful2");
    }

    [Fact]
    public void StyleId_RoundTrips_ViaQuickStylePart()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["P", "Q"]);
        smartArt.StyleId = "intense1";

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.StyleId.Should().Be("intense1");
    }

    [Fact]
    public void AllThreeGalleryIds_RoundTripTogether()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Root"]);
        smartArt.LayoutId = "hierarchy1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "subtle2";

        var read = RoundTrip(SingleDiagramDocument(smartArt));

        var diagram = read.Paragraphs.Single().Runs.Single(r => r.SmartArt is not null).SmartArt!;
        diagram.LayoutId.Should().Be("hierarchy1");
        diagram.ColorSchemeId.Should().Be("accent1");
        diagram.StyleId.Should().Be("subtle2");
    }

    [Fact]
    public void GalleryIds_PresentInDiagramParts_AsUniqueIdSuffixAndFreewExtension()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["A"]);
        smartArt.LayoutId = "radial1";
        smartArt.ColorSchemeId = "mono1";
        smartArt.StyleId = "flat1";

        var bytes = WriteBytes(SingleDiagramDocument(smartArt));

        // Gallery ids are persisted in schema-valid uniqueId suffixes.
        var layoutXml = EntryXml(bytes, "word/diagrams/layout1.xml");
        var layoutRoot = layoutXml.Root!;
        layoutRoot.Attribute("uniqueId")!.Value.Should().EndWith("radial1");

        var colorsXml = EntryXml(bytes, "word/diagrams/colors1.xml");
        colorsXml.Root!.Attribute("uniqueId")!.Value.Should().EndWith("mono1");

        var qsXml = EntryXml(bytes, "word/diagrams/quickStyle1.xml");
        qsXml.Root!.Attribute("uniqueId")!.Value.Should().EndWith("flat1");
    }

    [Fact]
    public void NullGalleryIds_DoNotWriteFreewExtensionAttributes()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["A"]);
        // LayoutId / ColorSchemeId / StyleId left null (default).

        var bytes = WriteBytes(SingleDiagramDocument(smartArt));

        var layoutXml = EntryXml(bytes, "word/diagrams/layout1.xml");
        layoutXml.Root!.Attribute("freewLayoutId").Should().BeNull();

        var colorsXml = EntryXml(bytes, "word/diagrams/colors1.xml");
        colorsXml.Root!.Attribute("freewColorId").Should().BeNull();

        var qsXml = EntryXml(bytes, "word/diagrams/quickStyle1.xml");
        qsXml.Root!.Attribute("freewStyleId").Should().BeNull();
    }
}
