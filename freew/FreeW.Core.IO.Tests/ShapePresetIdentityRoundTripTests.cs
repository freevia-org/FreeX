using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A Word shape's DrawingML preset identity (<c>a:prstGeom/@prst</c> plus its <c>a:avLst</c> adjust
/// values) must survive read → model → write for EVERY preset, whether or not FreeW draws a dedicated
/// outline for it. Before this, any preset beyond rect / roundRect / ellipse was saved back as a
/// freeform <c>a:custGeom</c> polygon (or, for arc-based presets, as a plain <c>rect</c>).
/// </summary>
public class ShapePresetIdentityRoundTripTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    public static IEnumerable<object[]> AllDedicatedPresets() =>
        PresetShapeGeometry.DedicatedPresets.OrderBy(name => name, StringComparer.Ordinal).Select(name => new object[] { name });

    private static TextDocument DocumentWith(Shape shape)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);
        return doc;
    }

    private static MemoryStream Write(TextDocument document)
    {
        var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument DocumentXml(Stream docx)
    {
        docx.Position = 0;
        using var zip = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    /// <summary>
    /// Simulates a Word-authored file: writes a plain rectangle and rewrites its geometry (and optionally
    /// its a:ln) in the raw XML to something FreeW's own writer would not have produced by itself.
    /// </summary>
    private static MemoryStream WordAuthored(string preset, (string Name, string Formula)[]? adjustments = null, Action<XElement>? editSpPr = null)
    {
        using var source = Write(DocumentWith(Shape.Preset(ShapeKind.Rectangle, widthPt: 120, heightPt: 80)));
        var output = new MemoryStream();
        using (var sourceZip = new ZipArchive(source, ZipArchiveMode.Read))
        using (var targetZip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in sourceZip.Entries)
            {
                using var from = entry.Open();
                using var to = targetZip.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "word/document.xml")
                {
                    from.CopyTo(to);
                    continue;
                }

                var xml = XDocument.Load(from);
                var prstGeom = xml.Descendants(A + "prstGeom").Single();
                prstGeom.Attribute("prst")!.Value = preset;
                var avLst = prstGeom.Element(A + "avLst")!;
                foreach (var (name, formula) in adjustments ?? [])
                    avLst.Add(new XElement(A + "gd", new XAttribute("name", name), new XAttribute("fmla", formula)));
                editSpPr?.Invoke(prstGeom.Parent!);
                xml.Save(to);
            }
        }

        output.Position = 0;
        return output;
    }

    private static Shape SingleShape(TextDocument document) =>
        document.Paragraphs.Single().Runs.Single(run => run.Shape is not null).Shape!;

    [Theory]
    [MemberData(nameof(AllDedicatedPresets))]
    public void DedicatedPreset_SavesBackAsTheSamePreset_NotACustomPolygon(string preset)
    {
        var read = DocxReader.Read(WordAuthored(preset));
        var shape = SingleShape(read);

        shape.PresetName.Should().Be(preset);
        shape.HasCustomGeometry.Should().BeTrue($"'{preset}' has a dedicated outline");
        shape.HasIntactPresetGeometry.Should().BeTrue();

        var xml = DocumentXml(Write(read));
        xml.Descendants(A + "custGeom").Should().BeEmpty();
        xml.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value.Should().Be(preset);
    }

    [Theory]
    [InlineData("gear6")]
    [InlineData("curvedRightArrow")]
    [InlineData("horizontalScroll")]
    [InlineData("actionButtonHome")]
    [InlineData("noSmoking")]
    [InlineData("funnel")]
    public void PresetWithoutADedicatedOutline_StillSavesBackAsItself(string preset)
    {
        var read = DocxReader.Read(WordAuthored(preset, [("adj", "val 12345")]));
        var shape = SingleShape(read);

        shape.PresetName.Should().Be(preset);
        shape.HasCustomGeometry.Should().BeFalse("FreeW draws no dedicated outline for this preset");
        shape.Kind.Should().Be(ShapeKind.Rectangle);

        var prstGeom = DocumentXml(Write(read)).Descendants(A + "prstGeom").Single();
        prstGeom.Attribute("prst")!.Value.Should().Be(preset);
        prstGeom.Element(A + "avLst")!.Elements(A + "gd").Single().Attribute("fmla")!.Value.Should().Be("val 12345");
    }

    [Fact]
    public void AdjustValues_RoundTripVerbatimAndInOrder()
    {
        var read = DocxReader.Read(WordAuthored("rightArrow", [("adj1", "val 30000"), ("adj2", "val 71234")]));
        SingleShape(read).PresetAdjustments.Should().Equal(
            new ShapeAdjustment("adj1", "val 30000"),
            new ShapeAdjustment("adj2", "val 71234"));

        var gds = DocumentXml(Write(read)).Descendants(A + "prstGeom").Single().Element(A + "avLst")!.Elements(A + "gd").ToList();
        gds.Select(gd => (gd.Attribute("name")!.Value, gd.Attribute("fmla")!.Value))
            .Should().Equal(("adj1", "val 30000"), ("adj2", "val 71234"));
    }

    [Fact]
    public void AdjustValues_ChangeTheImportedOutline()
    {
        var defaultArrow = SingleShape(DocxReader.Read(WordAuthored("rightArrow")));
        var thinArrow = SingleShape(DocxReader.Read(WordAuthored("rightArrow", [("adj1", "val 10000"), ("adj2", "val 80000")])));

        PresetShapeGeometry.Signature(thinArrow.CustomGeometry!)
            .Should().NotBe(PresetShapeGeometry.Signature(defaultArrow.CustomGeometry!));
    }

    [Fact]
    public void RoundRectWithAdjustValue_KeepsItsNamedKindAndItsAdjustValue()
    {
        var read = DocxReader.Read(WordAuthored("roundRect", [("adj", "val 40000")]));
        var shape = SingleShape(read);

        shape.Kind.Should().Be(ShapeKind.RoundedRectangle);
        shape.HasCustomGeometry.Should().BeFalse();

        var prstGeom = DocumentXml(Write(read)).Descendants(A + "prstGeom").Single();
        prstGeom.Attribute("prst")!.Value.Should().Be("roundRect");
        prstGeom.Descendants(A + "gd").Single().Attribute("fmla")!.Value.Should().Be("val 40000");
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.RoundedRectangle)]
    [InlineData(ShapeKind.Ellipse)]
    public void PlainNamedKinds_LoadExactlyAsBefore_WithNoPresetIdentity(ShapeKind kind)
    {
        var shape = SingleShape(DocxReader.Read(Write(DocumentWith(Shape.Preset(kind, 100, 60)))));

        shape.Kind.Should().Be(kind);
        shape.PresetName.Should().BeNull();
        shape.PresetAdjustments.Should().BeEmpty();
        shape.HasCustomGeometry.Should().BeFalse();
    }

    [Fact]
    public void EditedOutline_IsNoLongerThePreset_AndSavesAsCustomGeometry()
    {
        var read = DocxReader.Read(WordAuthored("star5"));
        var shape = SingleShape(read);
        var geometry = shape.CustomGeometry!;
        var first = geometry.Segments[0];
        geometry.Segments[0] = first with { Point = new CustomPoint(first.Point!.X + 5000, first.Point.Y) };

        shape.HasIntactPresetGeometry.Should().BeFalse();
        var xml = DocumentXml(Write(read));
        xml.Descendants(A + "prstGeom").Should().BeEmpty();
        xml.Descendants(A + "custGeom").Should().ContainSingle();
    }

    [Fact]
    public void LineEnds_RoundTrip_AndConnectorIsWrittenAsAConnectionShape()
    {
        var read = DocxReader.Read(WordAuthored("straightConnector1", editSpPr: spPr =>
            spPr.Add(new XElement(A + "ln", new XAttribute("w", 19050),
                new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", "4472C4"))),
                new XElement(A + "headEnd", new XAttribute("type", "oval")),
                new XElement(A + "tailEnd", new XAttribute("type", "triangle"), new XAttribute("w", "lg"), new XAttribute("len", "med"))))));
        var shape = SingleShape(read);

        shape.HeadEnd.Should().Be(new ShapeLineEnd("oval"));
        shape.TailEnd.Should().Be(new ShapeLineEnd("triangle", "lg", "med"));

        var xml = DocumentXml(Write(read));
        xml.Descendants(Wps + "cNvCnPr").Should().ContainSingle();
        var ln = xml.Descendants(A + "ln").Single();
        ln.Elements().Select(element => element.Name.LocalName).Should().Equal("solidFill", "headEnd", "tailEnd");
        ln.Element(A + "tailEnd")!.Attribute("w")!.Value.Should().Be("lg");
        ln.Element(A + "tailEnd")!.Attribute("len")!.Value.Should().Be("med");
        ln.Element(A + "headEnd")!.Attribute("type")!.Value.Should().Be("oval");
    }

    [Fact]
    public void LineEndsWithoutAnOutlineColour_StillRoundTrip()
    {
        var read = DocxReader.Read(WordAuthored("line", editSpPr: spPr =>
            spPr.Add(new XElement(A + "ln", new XElement(A + "tailEnd", new XAttribute("type", "stealth"))))));

        SingleShape(read).TailEnd.Should().Be(new ShapeLineEnd("stealth"));
        DocumentXml(Write(read)).Descendants(A + "tailEnd").Single().Attribute("type")!.Value.Should().Be("stealth");
    }

    [Theory]
    [InlineData("star5")]
    [InlineData("wedgeRoundRectCallout")]
    [InlineData("gear6")]
    [InlineData("bentConnector3")]
    public void PresetShape_IsByteStableAcrossRepeatedSaves(string preset)
    {
        var first = DocxReader.Read(WordAuthored(preset, [("adj1", "val 20000")]));
        var firstXml = DocumentXml(Write(first)).ToString(SaveOptions.DisableFormatting);
        var second = DocxReader.Read(Write(first));
        var secondXml = DocumentXml(Write(second)).ToString(SaveOptions.DisableFormatting);

        secondXml.Should().Be(firstXml);
    }

    [Theory]
    [InlineData("star5\" onload=\"x")]
    [InlineData("has space")]
    [InlineData("")]
    public void UnusablePresetToken_IsNotKeptAsIdentity(string preset)
    {
        var shape = SingleShape(DocxReader.Read(WordAuthored(preset)));

        shape.PresetName.Should().BeNull();
        shape.Kind.Should().Be(ShapeKind.Rectangle);
    }

    [Fact]
    public void ShapeCreatedFromAPreset_WritesThatPreset_AndReadsBackIntact()
    {
        var created = PresetShapeFactory.Create("flowChartDecision", 120, 80, "#DCE6F1");
        var read = SingleShape(DocxReader.Read(Write(DocumentWith(created))));

        read.PresetName.Should().Be("flowChartDecision");
        read.HasIntactPresetGeometry.Should().BeTrue();
        read.FillColorHex.Should().Be("#DCE6F1");
    }

    [Fact]
    public void TextInsideAPresetShape_SurvivesTheRoundTrip()
    {
        var created = PresetShapeFactory.Create("hexagon", 140, 90);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Inside"));
        created.TextParagraphs.Add(paragraph);

        var read = SingleShape(DocxReader.Read(Write(DocumentWith(created))));

        read.PresetName.Should().Be("hexagon");
        read.PlainText.Should().Be("Inside");
        read.HasCustomGeometry.Should().BeTrue();
    }
}
