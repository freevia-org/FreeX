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

    /// <summary>Moves a writer-authored OLE object and its two relationships from document.xml into header1.xml.</summary>
    private static byte[] AuthorHeaderEmbeddedObjectPackage()
    {
        var sourceBytes = WriteBytes(ExcelObjectDocument());
        using var sourceStream = new MemoryStream(sourceBytes);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);

        static XDocument ReadEntry(ZipArchive zip, string path)
        {
            using var stream = zip.GetEntry(path)!.Open();
            return XDocument.Load(stream);
        }

        var sourceDocument = ReadEntry(source, "word/document.xml");
        var obj = new XElement(sourceDocument.Descendants(W + "object").Single());
        var sourceRelationships = ReadEntry(source, "word/_rels/document.xml.rels");
        var allRelationships = sourceRelationships.Root!.Elements(Rel + "Relationship").ToList();
        var objectRelationships = allRelationships
            .Where(relationship => relationship.Attribute("Type")!.Value.EndsWith("/oleObject", StringComparison.Ordinal)
                                || relationship.Attribute("Type")!.Value.EndsWith("/image", StringComparison.Ordinal))
            .Select(relationship => new XElement(relationship))
            .ToList();
        objectRelationships.Should().HaveCount(2);

        var documentRelationships = new XDocument(new XElement(Rel + "Relationships",
            allRelationships
                .Where(relationship => !objectRelationships.Any(candidate => candidate.Attribute("Id")!.Value == relationship.Attribute("Id")!.Value))
                .Select(relationship => new XElement(relationship)),
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdHeader1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"),
                new XAttribute("Target", "header1.xml"))));
        var headerRelationships = new XDocument(new XElement(Rel + "Relationships", objectRelationships));
        var document = new XDocument(new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(W + "body",
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "Body text"))),
                new XElement(W + "sectPr", new XElement(W + "headerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", "rIdHeader1"))))));
        var header = new XDocument(new XElement(W + "hdr",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(W + "p", new XElement(W + "r", obj))));
        var contentTypes = ReadEntry(source, "[Content_Types].xml");
        contentTypes.Root!.Add(new XElement(Ct + "Override",
            new XAttribute("PartName", "/word/header1.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml")));

        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddXml(string path, XDocument xml)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                xml.Save(stream);
            }

            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName is "[Content_Types].xml" or "word/document.xml" or "word/_rels/document.xml.rels")
                    continue;
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var target = entry.Open();
                input.CopyTo(target);
            }

            AddXml("[Content_Types].xml", contentTypes);
            AddXml("word/document.xml", document);
            AddXml("word/_rels/document.xml.rels", documentRelationships);
            AddXml("word/header1.xml", header);
            AddXml("word/_rels/header1.xml.rels", headerRelationships);
        }
        return output.ToArray();
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
    public void HeaderEmbeddedObject_PreservesPartLocalPayloadAndIconRelationships()
    {
        var source = AuthorHeaderEmbeddedObjectPackage();
        var read = DocxReader.Read(new MemoryStream(source));
        var runs = read.FinalSectionHeadersFooters.Header!.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToList();
        runs.Should().ContainSingle(run => run.PreservedDrawing != null);
        runs.Should().NotContain(run => run.EmbeddedObject != null);

        var rewritten = WriteBytes(read);
        using var sourceZip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var rewrittenZip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read);
        foreach (var part in new[] { "word/embeddings/oleObject1.bin", "word/media/image1.png" })
        {
            using var sourcePart = sourceZip.GetEntry(part)!.Open();
            using var rewrittenPart = rewrittenZip.GetEntry(part)!.Open();
            using var sourceBytes = new MemoryStream();
            using var rewrittenBytes = new MemoryStream();
            sourcePart.CopyTo(sourceBytes);
            rewrittenPart.CopyTo(rewrittenBytes);
            rewrittenBytes.ToArray().Should().Equal(sourceBytes.ToArray());
        }

        var headerRels = EntryXml(rewritten, "word/_rels/header1.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var oleRelationship = headerRels.Single(relationship => relationship.Attribute("Type")!.Value.EndsWith("/oleObject", StringComparison.Ordinal));
        var imageRelationship = headerRels.Single(relationship => relationship.Attribute("Type")!.Value.EndsWith("/image", StringComparison.Ordinal));
        EntryXml(rewritten, "word/header1.xml").Descendants(O + "OLEObject").Single()
            .Attribute(R + "id")!.Value.Should().Be(oleRelationship.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/header1.xml").Descendants(V + "imagedata").Single()
            .Attribute(R + "id")!.Value.Should().Be(imageRelationship.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value.EndsWith("/oleObject", StringComparison.Ordinal)
                                           || relationship.Attribute("Type")!.Value.EndsWith("/image", StringComparison.Ordinal));

        var secondRead = DocxReader.Read(new MemoryStream(rewritten));
        secondRead.FinalSectionHeadersFooters.Header!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.PreservedDrawing != null);
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
    public void LinkedObject_ExternalTargetAndIcon_SurviveTextEditAndTwoWrites()
    {
        const string target = "file:///C:/Data/Quarterly%20Results.xlsx";
        var linked = EmbeddedObject.CreateLinked(
            target,
            "Excel.Sheet.12",
            new InlineImage(IconPng, 84, 42));
        var document = new TextDocument();
        var objectParagraph = new Paragraph();
        objectParagraph.Runs.Add(Run.FromEmbeddedObject(linked));
        document.Blocks.Add(objectParagraph);
        document.Blocks.Add(new Paragraph("Original body text"));

        var imported = DocxReader.Read(new MemoryStream(WriteBytes(document)));
        var importedParagraphs = imported.Paragraphs.ToList();
        var importedObject = importedParagraphs[0].Runs.Single().EmbeddedObject!;
        importedObject.IsLinked.Should().BeTrue();
        importedObject.LinkedTarget.Should().Be(target);
        importedObject.ProgId.Should().Be("Excel.Sheet.12");
        importedObject.Payload.Should().BeEmpty();
        importedObject.Icon!.PngBytes.Should().Equal(IconPng);
        importedObject.WidthPt.Should().Be(84);
        importedObject.HeightPt.Should().Be(42);

        importedParagraphs[1].Runs[0].Text = "Edited body text";
        var firstWrite = WriteBytes(imported);
        AssertLinkedPackage(firstWrite, target);

        var reopened = DocxReader.Read(new MemoryStream(firstWrite));
        var reopenedParagraphs = reopened.Paragraphs.ToList();
        reopenedParagraphs[1].Runs[0].Text.Should().Be("Edited body text");
        var reopenedObject = reopenedParagraphs[0].Runs.Single().EmbeddedObject!;
        reopenedObject.IsLinked.Should().BeTrue();
        reopenedObject.LinkedTarget.Should().Be(target);
        reopenedObject.Icon!.PngBytes.Should().Equal(IconPng);

        var secondWrite = WriteBytes(reopened);
        AssertLinkedPackage(secondWrite, target);
        DocxReader.Read(new MemoryStream(secondWrite)).Paragraphs.First().Runs.Single()
            .EmbeddedObject!.LinkedTarget.Should().Be(target);
    }

    private static void AssertLinkedPackage(byte[] docx, string target)
    {
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.StartsWith("word/embeddings/oleObject", StringComparison.Ordinal));
            zip.GetEntry("word/media/image1.png").Should().NotBeNull();
        }

        EntryXml(docx, "[Content_Types].xml").Root!.Elements(Ct + "Default")
            .Should().NotContain(element => element.Attribute("Extension") != null
                && element.Attribute("Extension")!.Value == "bin");

        var relationships = EntryXml(docx, "word/_rels/document.xml.rels").Root!
            .Elements(Rel + "Relationship").ToList();
        var oleRelationship = relationships.Single(element =>
            element.Attribute("Type")!.Value.EndsWith("/oleObject", StringComparison.Ordinal));
        oleRelationship.Attribute("Target")!.Value.Should().Be(target);
        oleRelationship.Attribute("TargetMode")!.Value.Should().Be("External");

        var imageRelationship = relationships.Single(element =>
            element.Attribute("Type")!.Value.EndsWith("/image", StringComparison.Ordinal));
        var documentXml = EntryXml(docx, "word/document.xml");
        var ole = documentXml.Descendants(O + "OLEObject").Single();
        ole.Attribute("Type")!.Value.Should().Be("Link");
        ole.Attribute("ProgID")!.Value.Should().Be("Excel.Sheet.12");
        ole.Attribute(R + "id")!.Value.Should().Be(oleRelationship.Attribute("Id")!.Value);
        documentXml.Descendants(V + "imagedata").Single().Attribute(R + "id")!.Value
            .Should().Be(imageRelationship.Attribute("Id")!.Value);
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
