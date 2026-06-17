using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline embedded OLE objects (roadmap item Y2): a
/// <see cref="Run.EmbeddedObject"/> must survive write→read with its payload bytes, ProgID and presentation
/// icon, materialise a real binary embeddings PART with a content-type Default and a document relationship,
/// and reference both the payload and the icon from a classic w:object / VML v:shape / o:OLEObject run.
/// </summary>
public class EmbeddedObjectRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OleContentType = "application/vnd.openxmlformats-officedocument.oleObject";

    // A minimal 1x1 PNG (the bytes content does not matter; only that they survive the media round-trip).
    private static readonly byte[] IconPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("FreeW embedded OLE payload bytes");

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

    private static TextDocument ExcelObjectDocument()
    {
        var icon = new InlineImage(IconPng, 96, 96);
        var embedded = EmbeddedObject.Create(Payload, "Excel.Sheet.12", icon);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEmbeddedObject(embedded));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void EmbeddedObject_PayloadProgIdAndIcon_SurviveRoundTrip()
    {
        var read = RoundTrip(ExcelObjectDocument());

        var run = read.Paragraphs.Single().Runs.Single(r => r.EmbeddedObject is not null);
        var embedded = run.EmbeddedObject!;

        embedded.ProgId.Should().Be("Excel.Sheet.12");
        embedded.Payload.Should().Equal(Payload);
        embedded.Icon.Should().NotBeNull();
        embedded.Icon!.PngBytes.Should().Equal(IconPng);
        embedded.WidthPt.Should().Be(96);
        embedded.HeightPt.Should().Be(96);
    }

    [Fact]
    public void EmbeddedObject_BinPartContentTypeAndRelationship_ArePresentInZip()
    {
        var docx = WriteBytes(ExcelObjectDocument());

        // The embedded payload part itself exists in the package.
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/embeddings/oleObject1.bin").Should().NotBeNull("the OLE payload must be a real OPC part");
        }

        // [Content_Types].xml declares a Default for the bin extension with the OLE content type.
        var types = EntryXml(docx, "[Content_Types].xml");
        types.Root!.Elements(Ct + "Default")
            .Should().ContainSingle(d =>
                d.Attribute("Extension")!.Value == "bin"
                && d.Attribute("ContentType")!.Value == OleContentType);

        // document.xml.rels carries an oleObject relationship pointing at the bin part.
        var rels = EntryXml(docx, "word/_rels/document.xml.rels");
        var oleRel = rels.Root!.Elements(Rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value.EndsWith("/oleObject", StringComparison.Ordinal));
        oleRel.Attribute("Target")!.Value.Should().Be("embeddings/oleObject1.bin");

        // document.xml references that relationship from an o:OLEObject inside a w:object.
        var documentXml = EntryXml(docx, "word/document.xml");
        var ole = documentXml.Descendants(O + "OLEObject").Single();
        ole.Attribute("Type")!.Value.Should().Be("Embed");
        ole.Attribute("ProgID")!.Value.Should().Be("Excel.Sheet.12");
        ole.Attribute(R + "id")!.Value.Should().Be(oleRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void EmbeddedObject_VmlShapeReferencesIconMediaPart()
    {
        var docx = WriteBytes(ExcelObjectDocument());

        // The icon is emitted as an ordinary media part (image1.png) with an image relationship.
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/media/image1.png").Should().NotBeNull("the OLE presentation icon must be a media part");
        }

        var rels = EntryXml(docx, "word/_rels/document.xml.rels");
        var imageRel = rels.Root!.Elements(Rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value.EndsWith("/image", StringComparison.Ordinal));
        imageRel.Attribute("Target")!.Value.Should().Be("media/image1.png");

        // The VML v:shape's v:imagedata references the icon relationship.
        var documentXml = EntryXml(docx, "word/document.xml");
        var imagedata = documentXml.Descendants(V + "imagedata").Single();
        imagedata.Attribute(R + "id")!.Value.Should().Be(imageRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void EmbeddedObject_WithoutIcon_StillRoundTripsPayloadAndProgId()
    {
        var embedded = new EmbeddedObject(Payload, "Word.Document.12");
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromEmbeddedObject(embedded));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.EmbeddedObject is not null).EmbeddedObject!;
        roundTripped.ProgId.Should().Be("Word.Document.12");
        roundTripped.Payload.Should().Equal(Payload);
        roundTripped.Icon.Should().BeNull();
    }

    [Fact]
    public void EmbeddedObject_RoundTripsInsideTableCell()
    {
        // Embedded objects are an inline run mark, so they must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        var icon = new InlineImage(IconPng, 72, 72);
        var embedded = EmbeddedObject.Create(Payload, "Excel.Sheet.12", icon);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromEmbeddedObject(embedded));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        var roundTripped = cellParagraph.Runs.Single(r => r.EmbeddedObject is not null).EmbeddedObject!;
        roundTripped.ProgId.Should().Be("Excel.Sheet.12");
        roundTripped.Payload.Should().Equal(Payload);
        roundTripped.Icon!.PngBytes.Should().Equal(IconPng);
    }

    [Fact]
    public void TwoEmbeddedObjects_GetDistinctBinPartsAndRelationships()
    {
        var doc = new TextDocument();
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromEmbeddedObject(new EmbeddedObject(Encoding.UTF8.GetBytes("first"), "Excel.Sheet.12")));
        var p2 = new Paragraph();
        p2.Runs.Add(Run.FromEmbeddedObject(new EmbeddedObject(Encoding.UTF8.GetBytes("second"), "PowerPoint.Show.12")));
        doc.Blocks.Add(p1);
        doc.Blocks.Add(p2);

        var docx = WriteBytes(doc);
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/embeddings/oleObject1.bin").Should().NotBeNull();
            zip.GetEntry("word/embeddings/oleObject2.bin").Should().NotBeNull();
        }

        var read = DocxReader.Read(new MemoryStream(docx));
        var objects = read.Paragraphs.SelectMany(p => p.Runs).Where(r => r.EmbeddedObject is not null).Select(r => r.EmbeddedObject!).ToList();
        objects.Should().HaveCount(2);
        objects[0].ProgId.Should().Be("Excel.Sheet.12");
        Encoding.UTF8.GetString(objects[0].Payload).Should().Be("first");
        objects[1].ProgId.Should().Be("PowerPoint.Show.12");
        Encoding.UTF8.GetString(objects[1].Payload).Should().Be("second");
    }

    [Fact]
    public void EmbeddedObjectWithIcon_CoexistsWithABodyImage()
    {
        // The icon media part shares the image numbering with body images; ensure neither clobbers the other.
        var doc = new TextDocument();
        var imageParagraph = new Paragraph();
        var bodyImageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        imageParagraph.Runs.Add(Run.FromImage(new InlineImage(bodyImageBytes, 48, 48)));
        doc.Blocks.Add(imageParagraph);

        var oleParagraph = new Paragraph();
        oleParagraph.Runs.Add(Run.FromEmbeddedObject(
            EmbeddedObject.Create(Payload, "Excel.Sheet.12", new InlineImage(IconPng, 96, 96))));
        doc.Blocks.Add(oleParagraph);

        var read = RoundTrip(doc);

        var paragraphs = read.Paragraphs.ToList();
        var bodyImage = paragraphs[0].Runs.Single(r => r.Image is not null).Image!;
        bodyImage.PngBytes.Should().Equal(bodyImageBytes);

        var embedded = paragraphs[1].Runs.Single(r => r.EmbeddedObject is not null).EmbeddedObject!;
        embedded.Icon!.PngBytes.Should().Equal(IconPng);
        embedded.Payload.Should().Equal(Payload);
    }
}
