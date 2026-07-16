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

        // document.xml references the required data/layout relationships from the inline dgm:relIds.
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
    public void Diagram_RenderedDrawingPart_UsesNativeRelationshipAndGalleryParts()
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

        // The data model points Word at the pre-laid-out drawing through the required Office extension.
        var dataXml = EntryXml(docx, "word/diagrams/data1.xml");
        var dataModelExt = dataXml.Descendants(Dsp + "dataModelExt").Should().ContainSingle().Subject;
        dataModelExt.Attribute("minVer")!.Value.Should().Be(Dgm.NamespaceName);

        var documentRels = EntryXml(docx, "word/_rels/document.xml.rels");
        documentRels.Root!.Elements(Rel + "Relationship")
            .Should().ContainSingle(r =>
                r.Attribute("Id")!.Value == dataModelExt.Attribute("relId")!.Value &&
                r.Attribute("Type")!.Value == DrawingRelType &&
                r.Attribute("Target")!.Value == "diagrams/drawing1.xml");

        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
            zip.GetEntry("word/diagrams/_rels/data1.xml.rels").Should().BeNull("native Word keeps the drawing relationship at document scope");

        var styleLabels = EntryXml(docx, "word/diagrams/quickStyle1.xml")
            .Descendants(Dgm + "styleLbl").Select(e => e.Attribute("name")!.Value).ToList();
        styleLabels.Should().Contain("node0");
        styleLabels.Should().Contain("fgAcc1", "the Word presentation scaffold needs the gallery text labels");
        var colorLabels = EntryXml(docx, "word/diagrams/colors1.xml")
            .Descendants(Dgm + "styleLbl").Select(e => e.Attribute("name")!.Value).ToList();
        colorLabels.Should().Contain("node0");
        colorLabels.Should().Contain("fgAcc1", "the Word presentation scaffold needs the gallery text labels");
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
            sp.Element(Dsp + "spPr")!.Elements(A + "solidFill")
                .Should().ContainSingle("Word needs an explicit fill in the cached drawing");
        }
    }

    [Fact]
    public void Process_RenderedDrawing_UsesNonZeroGroupFrameAndUniqueShapeIds()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Idea", "Prototype", "Review", "Launch"]);
        smartArt.HeightPt = 180;
        var docx = WriteBytes(SingleDiagramDocument(smartArt));
        var drawing = EntryXml(docx, "word/diagrams/drawing1.xml");
        var data = EntryXml(docx, "word/diagrams/data1.xml");
        var layout = EntryXml(docx, "word/diagrams/layout1.xml");
        var groupXfrm = drawing.Descendants(Dsp + "grpSpPr").Descendants(A + "xfrm").Single();
        var frameExt = groupXfrm.Element(A + "ext")!;
        var childExt = groupXfrm.Element(A + "chExt")!;
        frameExt.Attribute("cx")!.Value.Should().Be(childExt.Attribute("cx")!.Value);
        frameExt.Attribute("cy")!.Value.Should().Be(childExt.Attribute("cy")!.Value);
        long.Parse(frameExt.Attribute("cx")!.Value).Should().BeGreaterThan(0);
        long.Parse(frameExt.Attribute("cy")!.Value).Should().BeGreaterThan(0);

        var shapes = drawing.Descendants(Dsp + "sp").ToList();
        shapes.Should().HaveCount(7);
        shapes.Count(sp => sp.Descendants(A + "prstGeom").Any(geom => geom.Attribute("prst")?.Value == "rightArrow"))
            .Should().Be(3, "Word's Basic Process drawing has one right-arrow connector between each pair of nodes");
        var shapeIds = shapes.Select(sp => sp.Element(Dsp + "nvSpPr")!.Element(Dsp + "cNvPr")!.Attribute("id")!.Value);
        shapeIds.Should().OnlyHaveUniqueItems();
        shapeIds.Should().NotContain("0");

        var presentationPoints = data.Root!.Element(Dgm + "ptLst")!.Elements(Dgm + "pt")
            .Where(pt => pt.Attribute("type")?.Value == "pres").ToList();
        presentationPoints.Should().NotBeEmpty("Word needs a presentation scaffold for a flat process gallery");
        data.Root.Element(Dgm + "ptLst")!.Elements(Dgm + "pt")
            .Select(pt => pt.Attribute("modelId")!.Value)
            .Should().OnlyHaveUniqueItems("Word requires semantic, transition, and presentation point ids to share one namespace");
        data.Root.Element(Dgm + "cxnLst")!.Elements(Dgm + "cxn")
            .Count(cxn => cxn.Attribute("type")?.Value == "presParOf")
            .Should().BeGreaterThan(0, "Word needs presentation-parent links to place flat process nodes");
        data.Root.Element(Dgm + "ptLst")!.Elements(Dgm + "pt")
            .Count(pt => pt.Attribute("type")?.Value == "parTrans")
            .Should().Be(4, "Word's Process data model materializes one parent transition per node");
        data.Root.Element(Dgm + "ptLst")!.Elements(Dgm + "pt")
            .Count(pt => pt.Attribute("type")?.Value == "sibTrans")
            .Should().Be(4, "Word's Process data model materializes one sibling transition per node");
        data.Root.Element(Dgm + "ptLst")!.Elements(Dgm + "pt")
            .Select(pt => pt.Element(Dgm + "prSet")?.Attribute("presName")?.Value)
            .Should().Contain(["Name0", "node", "sibTrans", "connectorText"]);
        layout.Descendants(Dgm + "alg").Select(alg => alg.Attribute("type")!.Value)
            .Should().Contain("lin", "Word's Basic Process uses a linear geometry");
        layout.Descendants(Dgm + "layoutNode")
            .Single(node => node.Attribute("name")?.Value == "sibTrans")
            .Element(Dgm + "shape")!.Attribute("type")!.Value
            .Should().Be("conn", "Word's Basic Process uses connector geometry for sibling transitions");
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
        drawing.Descendants(Dsp + "sp").Should().HaveCount(11);
        drawing.Descendants(Dsp + "sp").Select(sp => sp.Attribute("modelId")!.Value)
            .Should().Contain(
                "{4F4B0000-0000-4000-8000-000000009002}",
                "{4F4B0000-0000-4000-8000-000000009007}",
                "{4F4B0000-0000-4000-8000-000000009012}",
                "{4F4B0000-0000-4000-8000-000000009017}");
        drawing.Descendants(A + "t").Select(t => t.Value)
            .Should().Contain(["CEO", "VP Eng", "Lead", "VP Sales"]);
    }

    [Fact]
    public void WordHierarchy_DefaultDrawingUsesNativeOfficeAccentInsteadOfThemeBlack()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Root", "Child"]);

        var drawing = EntryXml(WriteBytes(SingleDiagramDocument(smartArt)), "word/diagrams/drawing1.xml");
        var cachedShapeColors = drawing.Descendants(Dsp + "sp")
            .SelectMany(shape => shape.Element(Dsp + "spPr")!.Descendants(A + "srgbClr"))
            .Select(color => color.Attribute("val")!.Value)
            .ToList();

        cachedShapeColors.Should().Contain("156082");
        drawing.Descendants(A + "schemeClr")
            .Should().NotContain(color => color.Attribute("val")!.Value == "accent1");
    }

    [Fact]
    public void WordHierarchy_ExplicitColorSchemeUsesPaletteRgbInColorsPart()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Root"]);
        smartArt.ColorSchemeId = "accent1";

        var colors = EntryXml(WriteBytes(SingleDiagramDocument(smartArt)), "word/diagrams/colors1.xml");
        var nodeColors = colors.Descendants(Dgm + "styleLbl")
            .Single(label => label.Attribute("name")!.Value == "node0");

        nodeColors.Element(Dgm + "fillClrLst")!.Element(A + "srgbClr")!.Attribute("val")!.Value.Should().Be("1F3864");
        nodeColors.Element(Dgm + "linClrLst")!.Element(A + "srgbClr")!.Attribute("val")!.Value.Should().Be("1F3864");
    }

    [Fact]
    public void Pyramid_RenderedDrawing_CarriesNodeTextInEachBand()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.LayoutId = "pyramid1";

        var drawing = EntryXml(WriteBytes(SingleDiagramDocument(smartArt)), "word/diagrams/drawing1.xml");
        var shapes = drawing.Descendants(Dsp + "sp").ToList();

        shapes.Should().HaveCount(4);
        shapes.Select(sp => sp.Element(Dsp + "spPr")!.Element(A + "prstGeom")!.Attribute("prst")!.Value)
            .Should().OnlyContain(kind => kind == "trapezoid");
        shapes.SelectMany(sp => sp.Descendants(A + "t").Select(t => t.Value))
            .Should().Contain(["Top", "Middle", "Lower", "Base"]);
    }

    [Fact]
    public void PyramidData_UsesWordTextPlaceholdersAndPresentationStyles()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.LayoutId = "pyramid1";

        var data = EntryXml(WriteBytes(SingleDiagramDocument(smartArt)), "word/diagrams/data1.xml");
        var semanticNodes = data.Descendants(Dgm + "pt")
            .Where(pt => pt.Element(Dgm + "t")?.Descendants(A + "t").Any() == true)
            .ToList();
        semanticNodes.Should().HaveCount(4);
        semanticNodes.Select(pt => pt.Element(Dgm + "prSet")!.Attribute("phldrT")!.Value)
            .Should().Equal("[Text]", "[Text]", "[Text]", "[Text]");

        var levels = data.Descendants(Dgm + "pt")
            .Where(pt => pt.Element(Dgm + "prSet")?.Attribute("presName")?.Value == "level")
            .ToList();
        levels.Should().HaveCount(4);
        levels.Select(pt => pt.Element(Dgm + "prSet")!.Attribute("presStyleIdx")!.Value)
            .Should().Equal("0", "1", "2", "3");
        levels.Select(pt => pt.Element(Dgm + "prSet")!.Attribute("presStyleCnt")!.Value)
            .Should().OnlyContain(value => value == "4");
    }

    [Fact]
    public void WordStockRenderedDrawing_UsesNativeShapeScaffoldAndPresentationIds()
    {
        var hierarchy = SmartArt.Create(SmartArtKind.Hierarchy, ["Root", "Child"]);
        var pyramid = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Base"]);
        pyramid.LayoutId = "pyramid1";

        var document = new TextDocument();
        var hierarchyParagraph = new Paragraph();
        hierarchyParagraph.Runs.Add(Run.FromSmartArt(hierarchy));
        document.Blocks.Add(hierarchyParagraph);
        var pyramidParagraph = new Paragraph();
        pyramidParagraph.Runs.Add(Run.FromSmartArt(pyramid));
        document.Blocks.Add(pyramidParagraph);

        var bytes = WriteBytes(document);
        var hierarchyData = EntryXml(bytes, "word/diagrams/data1.xml");
        var hierarchyDrawing = EntryXml(bytes, "word/diagrams/drawing1.xml");
        var pyramidData = EntryXml(bytes, "word/diagrams/data2.xml");
        var pyramidDrawing = EntryXml(bytes, "word/diagrams/drawing2.xml");

        foreach (var shape in hierarchyDrawing.Descendants(Dsp + "sp")
                     .Concat(pyramidDrawing.Descendants(Dsp + "sp")))
        {
            shape.Element(Dsp + "style").Should().NotBeNull();
            shape.Element(Dsp + "txBody").Should().NotBeNull();
            shape.Element(Dsp + "txXfrm").Should().NotBeNull();
            shape.Element(Dsp + "spPr")!.Element(A + "effectLst").Should().NotBeNull();
        }

        var pyramidShapes = pyramidDrawing.Descendants(Dsp + "sp").ToList();
        pyramidShapes.Should().HaveCount(3);
        pyramidShapes.Select(shape => shape.Attribute("modelId")!.Value)
            .Should().Equal(
                pyramidData.Descendants(Dgm + "prSet")
                    .Where(prSet => prSet.Attribute("presName")?.Value == "level")
                    .Select(prSet => prSet.Parent!.Attribute("modelId")!.Value));
        pyramidShapes.SelectMany(shape => shape.Descendants(A + "prstGeom"))
            .Should().OnlyContain(geometry => geometry.Attribute("prst")!.Value == "trapezoid"
                && geometry.Element(A + "avLst")!.Element(A + "gd")!.Attribute("fmla")!.Value == "val 68182");

        hierarchyDrawing.Descendants(Dsp + "sp").Count()
            .Should().BeGreaterThan(hierarchyData.Descendants(Dgm + "pt")
                .Count(pt => pt.Element(Dgm + "prSet")?.Attribute("presName")?.Value == "background"));
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
        qsXml.Root!.Attribute("uniqueId")!.Value.Should().EndWith("simple1");
    }

    [Fact]
    public void WordFlatSmartArt_UsesWordPresentationScaffoldAndGalleryContracts()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["A", "B", "C"]);

        var bytes = WriteBytes(SingleDiagramDocument(smartArt));
        var data = EntryXml(bytes, "word/diagrams/data1.xml");
        var ptList = data.Root!.Element(Dgm + "ptLst")!;
        var docPoint = ptList.Elements(Dgm + "pt").Single(pt => pt.Attribute("type")?.Value == "doc");
        docPoint.Element(Dgm + "prSet").Should().NotBeNull("Word needs the stock gallery ids on the document point");
        docPoint.Element(Dgm + "spPr").Should().NotBeNull();
        ptList.Elements(Dgm + "pt").Count(pt => pt.Attribute("type")?.Value == "pres")
            .Should().BeGreaterThan(3, "Word needs presentation points for the cached node scaffold");
        ptList.Elements(Dgm + "pt").Where(pt => pt.Attribute("type") == null)
            .Should().OnlyContain(pt => pt.Element(Dgm + "prSet") != null && pt.Element(Dgm + "spPr") != null);

        var connections = data.Root.Element(Dgm + "cxnLst")!.Elements(Dgm + "cxn").ToList();
        connections.Count(cxn => cxn.Attribute("type") == null).Should().Be(3);
        connections.Count(cxn => cxn.Attribute("type")?.Value == "presOf").Should().BeGreaterThan(0);
        connections.Count(cxn => cxn.Attribute("type")?.Value == "presParOf").Should().BeGreaterThan(0);

        var layout = EntryXml(bytes, "word/diagrams/layout1.xml");
        layout.Descendants(Dgm + "alg").Select(alg => alg.Attribute("type")!.Value)
            .Should().Contain("hierRoot");

        var quickStyle = EntryXml(bytes, "word/diagrams/quickStyle1.xml");
        quickStyle.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/quickstyle/simple1");
        quickStyle.Descendants(Dgm + "styleLbl").Select(label => label.Attribute("name")!.Value)
            .Should().Contain(["node0", "fgAcc1"]);

        var colors = EntryXml(bytes, "word/diagrams/colors1.xml");
        colors.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/colors/accent0_1");
        var nodeColors = colors.Descendants(Dgm + "styleLbl").Single(label => label.Attribute("name")!.Value == "node0");
        nodeColors.Element(Dgm + "fillClrLst")!.Element(A + "srgbClr")!.Attribute("val")!.Value.Should().Be("156082");
        nodeColors.Element(Dgm + "linClrLst")!.Element(A + "srgbClr")!.Attribute("val")!.Value.Should().Be("156082");
        nodeColors.Element(Dgm + "txFillClrLst")!.Elements().Should().BeEmpty();
    }

    [Fact]
    public void WordHierarchy_UsesCanonicalGalleryAndPresentationMetadata()
    {
        var smartArt = new SmartArt
        {
            Kind = SmartArtKind.Hierarchy,
            LayoutId = "orgchart1",
            ColorSchemeId = "accent1",
            StyleId = "intense1"
        };
        var root = new SmartArtNode("Plan");
        smartArt.Nodes.Add(root);
        var child = root.AddChild("Build");
        child.AddChild("Verify");

        var bytes = WriteBytes(SingleDiagramDocument(smartArt));
        var data = EntryXml(bytes, "word/diagrams/data1.xml");
        var ptList = data.Root!.Element(Dgm + "ptLst")!;
        var docPoint = ptList.Elements(Dgm + "pt").Single(pt => pt.Attribute("type")?.Value == "doc");
        var docPrSet = docPoint.Element(Dgm + "prSet")!;

        docPrSet.Attribute("qsTypeId")!.Value.Should().EndWith("/quickstyle/simple1");
        docPrSet.Attribute("phldr")!.Value.Should().Be("0");
        ptList.Elements(Dgm + "pt").Count(pt => pt.Attribute("type")?.Value == "pres").Should().Be(18);

        XElement pres(string name) => ptList.Elements(Dgm + "pt")
            .Single(pt => pt.Element(Dgm + "prSet")?.Attribute("presName")?.Value == name)
            .Element(Dgm + "prSet")!;

        pres("hierChild1").Element(Dgm + "presLayoutVars")!
            .Element(Dgm + "chPref")!.Attribute("val")!.Value.Should().Be("1");
        pres("background").Attributes().Should().Contain(attribute => attribute.Name.LocalName == "presStyleLbl" && attribute.Value == "node0");
        pres("background").Attribute("presStyleCnt")!.Value.Should().Be("1");
        pres("text").Attribute("presStyleLbl")!.Value.Should().Be("fgAcc0");
        pres("text").Element(Dgm + "presLayoutVars")!.Element(Dgm + "chPref")!
            .Attribute("val")!.Value.Should().Be("3");
        pres("background2").Attribute("presStyleLbl")!.Value.Should().Be("node2");
        pres("background2").Attribute("presStyleCnt")!.Value.Should().Be("2");
        pres("Name10").Attribute("presStyleLbl")!.Value.Should().Be("parChTrans1D2");
        pres("Name10").Attribute("presStyleIdx")!.Value.Should().Be("0");
        pres("Name10").Attribute("presStyleCnt")!.Value.Should().Be("2");

        var quickStyle = EntryXml(bytes, "word/diagrams/quickStyle1.xml");
        quickStyle.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/quickstyle/simple1");
        quickStyle.Root.Attribute("freewStyleId")!.Value.Should().Be("intense1");
        var colors = EntryXml(bytes, "word/diagrams/colors1.xml");
        colors.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/colors/accent1_2");
        colors.Descendants(Dgm + "styleLbl").Single(label => label.Attribute("name")!.Value == "fgAcc0")
            .Element(Dgm + "fillClrLst")!.Element(A + "schemeClr")!.Attribute("val")!.Value.Should().Be("lt1");
        colors.Descendants(Dgm + "styleLbl").Single(label => label.Attribute("name")!.Value == "fgAcc0")
            .Element(Dgm + "fillClrLst")!.Element(A + "schemeClr")!.Element(A + "alpha")!
            .Attribute("val")!.Value.Should().Be("90000");
        colors.Descendants(Dgm + "styleLbl").Single(label => label.Attribute("name")!.Value == "fgAcc0")
            .Element(Dgm + "txFillClrLst")!.Element(A + "schemeClr")!.Attribute("val")!.Value.Should().Be("dk1");
    }

    [Fact]
    public void WordColorGalleryIds_UseNativeSuffixAndPreserveFreeWId()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Root"]);
        smartArt.ColorSchemeId = "accent1";

        var bytes = WriteBytes(SingleDiagramDocument(smartArt));
        var data = EntryXml(bytes, "word/diagrams/data1.xml");
        var docPrSet = data.Root!.Element(Dgm + "ptLst")!.Element(Dgm + "pt")!
            .Element(Dgm + "prSet")!;
        docPrSet.Attribute("csTypeId")!.Value.Should().EndWith("/colors/accent1_2");
        docPrSet.Attribute("csCatId")!.Value.Should().Be("accent1");

        var colors = EntryXml(bytes, "word/diagrams/colors1.xml");
        colors.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/accent1_2");
        colors.Root.Attribute("freewColorId")!.Value.Should().Be("accent1");
    }

    [Fact]
    public void WordStockHierarchyAndPyramidLayouts_UseCanonicalIdsAndAlgorithms()
    {
        var hierarchy = new SmartArt { Kind = SmartArtKind.Hierarchy, LayoutId = "orgchart1" };
        var root = new SmartArtNode("Plan");
        hierarchy.Nodes.Add(root);
        root.AddChild("Build");
        root.Children[0].AddChild("Verify");

        var pyramid = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        pyramid.LayoutId = "pyramid1";

        var doc = new TextDocument();
        var first = new Paragraph();
        first.Runs.Add(Run.FromSmartArt(hierarchy));
        doc.Blocks.Add(first);
        var second = new Paragraph();
        second.Runs.Add(Run.FromSmartArt(pyramid));
        doc.Blocks.Add(second);

        var bytes = WriteBytes(doc);
        var hierarchyData = EntryXml(bytes, "word/diagrams/data1.xml");
        var hierarchyLayout = EntryXml(bytes, "word/diagrams/layout1.xml");
        var pyramidData = EntryXml(bytes, "word/diagrams/data2.xml");
        var pyramidLayout = EntryXml(bytes, "word/diagrams/layout2.xml");

        hierarchyData.Root!.Element(Dgm + "ptLst")!.Element(Dgm + "pt")!
            .Element(Dgm + "prSet")!.Attribute("loTypeId")!.Value
            .Should().EndWith("/layout/orgChart1");
        hierarchyData.Root.Element(Dgm + "ptLst")!.Element(Dgm + "pt")!
            .Element(Dgm + "prSet")!.Attribute("loCatId")!.Value.Should().Be("hierarchy");
        hierarchyLayout.Root!.Element(Dgm + "layoutNode")!
            .Descendants(Dgm + "alg").Select(alg => alg.Attribute("type")!.Value)
            .Should().Contain("hierRoot");
        hierarchyLayout.Descendants(Dgm + "alg").Select(alg => alg.Attribute("type")!.Value)
            .Should().Contain("hierChild");
        hierarchyLayout.Descendants(Dgm + "layoutNode").Select(node => node.Attribute("name")!.Value)
            .Should().Contain(["hierRoot1", "composite", "background", "text", "hierChild2", "Name10"]);
        hierarchyData.Descendants(Dgm + "prSet").Select(prSet => prSet.Attribute("presName")?.Value)
            .Should().Contain(["hierRoot1", "composite", "background", "text", "hierChild2"]);

        pyramidData.Root!.Element(Dgm + "ptLst")!.Element(Dgm + "pt")!
            .Element(Dgm + "prSet")!.Attribute("loCatId")!.Value.Should().Be("pyramid");
        pyramidLayout.Root!.Element(Dgm + "layoutNode")!
            .Descendants(Dgm + "alg").Select(alg => alg.Attribute("type")!.Value)
            .Should().Contain("pyra");
        pyramidLayout.Descendants(Dgm + "shape")
            .Select(shape => shape.Attribute("type")?.Value)
            .Should().Contain("trapezoid");
        pyramidLayout.Descendants(Dgm + "layoutNode").Select(node => node.Attribute("name")!.Value)
            .Should().Contain(["Name0", "Name8", "level", "levelTx"]);
        pyramidData.Descendants(Dgm + "prSet").Select(prSet => prSet.Attribute("presName")?.Value)
            .Should().Contain(["Name0", "Name8", "level", "levelTx"]);
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
