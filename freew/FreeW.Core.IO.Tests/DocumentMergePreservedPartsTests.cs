using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;
using FreeW.Core.IO;
using FluentAssertions;

namespace FreeW.Core.IO.Tests;

public class DocumentMergePreservedPartsTests
{
    private static readonly XNamespace Relationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Wordprocessing =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void Merge_WritesTransferredNoteAndCommentReferencesWithRemappedIds()
    {
        var source = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(Run.EndnoteReference(1));
        paragraph.Runs.Add(Run.CommentReference(0));
        source.Blocks.Add(paragraph);
        source.Footnotes[1] = new Footnote(1, "Source footnote");
        source.Endnotes[1] = new Endnote(1, "Source endnote");
        source.Comments[0] = new Comment(0, "Source comment");

        var target = new TextDocument();
        target.Footnotes[1] = new Footnote(1, "Target footnote");
        target.Endnotes[1] = new Endnote(1, "Target endnote");
        target.Comments[0] = new Comment(0, "Target comment");

        DocumentMerge.Merge(target, 0, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var document = ReadXml(zip, "word/document.xml");
        document.Descendants(Wordprocessing + "footnoteReference")
            .Single().Attribute(Wordprocessing + "id")!.Value.Should().Be("2");
        document.Descendants(Wordprocessing + "endnoteReference")
            .Single().Attribute(Wordprocessing + "id")!.Value.Should().Be("2");
        document.Descendants(Wordprocessing + "commentReference")
            .Single().Attribute(Wordprocessing + "id")!.Value.Should().Be("1");
        ReadXml(zip, "word/footnotes.xml").Descendants(Wordprocessing + "footnote")
            .Should().Contain(note => (string?)note.Attribute(Wordprocessing + "id") == "2" && note.Value.Contains("Source footnote"));
        ReadXml(zip, "word/endnotes.xml").Descendants(Wordprocessing + "endnote")
            .Should().Contain(note => (string?)note.Attribute(Wordprocessing + "id") == "2" && note.Value.Contains("Source endnote"));
        ReadXml(zip, "word/comments.xml").Descendants(Wordprocessing + "comment")
            .Should().Contain(comment => (string?)comment.Attribute(Wordprocessing + "id") == "1" && comment.Value.Contains("Source comment"));
    }

    [Fact]
    public void Merge_PreservesRenamedAltChunkPackageGraph_WhenWritten()
    {
        const string altChunkRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk";
        const string altChunkContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        var source = new TextDocument();
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/afchunk.docx",
            [1],
            altChunkContentType,
            altChunkRelationship));
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/_rels/afchunk.docx.rels",
            Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"media/altchunk.png\" /></Relationships>")));
        source.Preserved.Parts.Add(new PreservedPart("/word/media/altchunk.png", [2]));
        source.Preserved.ContentTypeDefaults["png"] = "image/png";
        source.Blocks.Add(new AltChunkBlock("/word/afchunk.docx"));

        var target = new TextDocument();
        target.Preserved.Parts.Add(new PreservedPart("/word/afchunk.docx", [9], altChunkContentType, altChunkRelationship));
        target.Preserved.Parts.Add(new PreservedPart("/word/_rels/afchunk.docx.rels", [8]));
        target.Preserved.Parts.Add(new PreservedPart("/word/media/altchunk.png", [7]));

        DocumentMerge.Merge(target, 0, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("word/afchunk-freew-import1.docx").Should().NotBeNull();
        zip.GetEntry("word/_rels/afchunk-freew-import1.docx.rels").Should().NotBeNull();
        zip.GetEntry("word/media/altchunk-freew-import1.png").Should().NotBeNull();
        var documentRelationships = ReadXml(zip, "word/_rels/document.xml.rels");
        documentRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "afchunk-freew-import1.docx");
        var chunkRelationships = ReadXml(zip, "word/_rels/afchunk-freew-import1.docx.rels");
        chunkRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "media/altchunk-freew-import1.png");
    }

    [Fact]
    public void Merge_PreservesRenamedDrawingPackageGraph_WhenWritten()
    {
        const string chartRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        const string chartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
        var source = new TextDocument();
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/chart1.xml",
            Encoding.UTF8.GetBytes("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"),
            chartContentType,
            chartRelationship));
        source.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/_rels/chart1.xml.rels",
            Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"../media/image1.png\" /></Relationships>")));
        source.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [2]));
        source.Preserved.ContentTypeDefaults["png"] = "image/png";
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(Run.FromPreservedDrawing(new PreservedDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId7\" /></w:drawing>",
            [new PreservedDrawingReference("rId7", "/word/charts/chart1.xml", chartRelationship)])));
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/chart1.xml", [9], chartContentType, chartRelationship));
        target.Preserved.Parts.Add(new PreservedPart("/word/charts/_rels/chart1.xml.rels", [8]));
        target.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [7]));

        DocumentMerge.Merge(target, 0, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("word/charts/chart1-freew-import1.xml").Should().NotBeNull();
        zip.GetEntry("word/charts/_rels/chart1-freew-import1.xml.rels").Should().NotBeNull();
        zip.GetEntry("word/media/image1-freew-import1.png").Should().NotBeNull();

        var documentRelationships = ReadXml(zip, "word/_rels/document.xml.rels");
        documentRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "charts/chart1-freew-import1.xml");
        var chartRelationships = ReadXml(zip, "word/charts/_rels/chart1-freew-import1.xml.rels");
        chartRelationships.Root!.Elements(Relationships + "Relationship")
            .Should().Contain(relationship => relationship.Attribute("Target")!.Value == "../media/image1-freew-import1.png");
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        using var stream = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(stream);
    }
}
