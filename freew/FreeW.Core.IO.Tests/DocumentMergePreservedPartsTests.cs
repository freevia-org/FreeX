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
    public void Merge_WritesRemappedBookmarksAndInternalReferences()
    {
        var source = new TextDocument();
        var sourceParagraph = new Paragraph("Source target") { BookmarkName = "Shared" };
        sourceParagraph.Runs.Add(new Run("jump") { HyperlinkAnchor = "Shared" });
        sourceParagraph.Runs.Add(Run.CrossReferenceFieldRun(
            new CrossReferenceField(CrossRefFieldKind.Ref, "Shared", CrossRefInsertAs.Text, Hyperlink: true),
            "Source target"));
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        target.Blocks.Add(new Paragraph("Target") { BookmarkName = "Shared" });

        DocumentMerge.Merge(target, target.Blocks.Count, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var document = ReadXml(zip, "word/document.xml");
        document.Descendants(Wordprocessing + "bookmarkStart")
            .Select(bookmark => (string?)bookmark.Attribute(Wordprocessing + "name"))
            .Should().Contain(["Shared", "Shared_FreeW1"]);
        document.Descendants(Wordprocessing + "hyperlink")
            .Single().Attribute(Wordprocessing + "anchor")!.Value.Should().Be("Shared_FreeW1");
        document.Descendants(Wordprocessing + "fldSimple")
            .Single().Attribute(Wordprocessing + "instr")!.Value.Should().Contain("REF Shared_FreeW1");

        var reread = DocxReader.Read(new MemoryStream(bytes));
        reread.Blocks.OfType<Paragraph>().Last().BookmarkName.Should().Be("Shared_FreeW1");
        reread.Blocks.OfType<Paragraph>().Last().Runs.Single(run => run.Text == "jump").HyperlinkAnchor.Should().Be("Shared_FreeW1");
        reread.Blocks.OfType<Paragraph>().Last().Runs.Single(run => run.CrossReference is not null).CrossReference!.Target.Should().Be("Shared_FreeW1");
    }

    [Fact]
    public void Merge_WritesRemappedSourceStyleWithoutReplacingTargetDefinition()
    {
        var source = new TextDocument();
        source.Styles["SharedStyle"] = new DocumentStyle
        {
            Id = "SharedStyle", Name = "Source style", Run = new RunFormatting { Bold = true, ColorHex = "#AA0000" }
        };
        source.Blocks.Add(new Paragraph("Source") { StyleId = "SharedStyle" });

        var target = new TextDocument();
        target.Styles["SharedStyle"] = new DocumentStyle
        {
            Id = "SharedStyle", Name = "Target style", Run = new RunFormatting { Italic = true, ColorHex = "#0000AA" }
        };
        target.Blocks.Add(new Paragraph("Target") { StyleId = "SharedStyle" });

        DocumentMerge.Merge(target, target.Blocks.Count, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var document = ReadXml(zip, "word/document.xml");
        document.Descendants(Wordprocessing + "pStyle").Last().Attribute(Wordprocessing + "val")!.Value.Should().Be("SharedStyle_FreeW1");
        var styles = ReadXml(zip, "word/styles.xml").Root!.Elements(Wordprocessing + "style").ToList();
        styles.Single(style => (string?)style.Attribute(Wordprocessing + "styleId") == "SharedStyle")
            .Element(Wordprocessing + "name")!.Attribute(Wordprocessing + "val")!.Value.Should().Be("Target style");
        styles.Single(style => (string?)style.Attribute(Wordprocessing + "styleId") == "SharedStyle_FreeW1")
            .Element(Wordprocessing + "name")!.Attribute(Wordprocessing + "val")!.Value.Should().Be("Source style");

        var reread = DocxReader.Read(new MemoryStream(bytes));
        reread.Blocks.OfType<Paragraph>().Last().StyleId.Should().Be("SharedStyle_FreeW1");
        reread.Styles["SharedStyle_FreeW1"].Run.ColorHex.Should().Be("#AA0000");
        reread.Styles["SharedStyle"].Run.ColorHex.Should().Be("#0000AA");
    }

    [Fact]
    public void Merge_WritesTransferredPreservedNumberingForParagraphsAndStyles()
    {
        var source = new TextDocument();
        source.Preserved.OriginalNumbering = RawNumbering(12, 12, "source");
        source.Styles["RawList"] = new DocumentStyle
        {
            Id = "RawList", Name = "Source raw list", PreservedNumbering = new PreservedNumbering(12, 2)
        };
        var sourceParagraph = new Paragraph("Source") { StyleId = "RawList", PreservedNumbering = new PreservedNumbering(12, 1) };
        sourceParagraph.Runs.Add(Run.FootnoteReference(1));
        source.Blocks.Add(sourceParagraph);
        var sourceFootnote = new Footnote(1, "Source note");
        sourceFootnote.Content[0].PreservedNumbering = new PreservedNumbering(12, 0);
        source.Footnotes[1] = sourceFootnote;

        var target = new TextDocument();
        target.Preserved.OriginalNumbering = RawNumbering(12, 12, "target");
        target.Styles["RawList"] = new DocumentStyle { Id = "RawList", Name = "Target raw list" };
        target.Blocks.Add(new Paragraph("Target") { PreservedNumbering = new PreservedNumbering(12, 0) });

        DocumentMerge.Merge(target, target.Blocks.Count, source);
        var bytes = WriteBytes(target);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var numbering = ReadXml(zip, "word/numbering.xml");
        numbering.Descendants(Wordprocessing + "num")
            .Select(number => (string?)number.Attribute(Wordprocessing + "numId"))
            .Should().Equal("4", "5");
        var document = ReadXml(zip, "word/document.xml");
        document.Descendants(Wordprocessing + "numId").Last().Attribute(Wordprocessing + "val")!.Value.Should().Be("5");
        ReadXml(zip, "word/footnotes.xml").Descendants(Wordprocessing + "numId").Single()
            .Attribute(Wordprocessing + "val")!.Value.Should().Be("5");
        var styles = ReadXml(zip, "word/styles.xml").Root!.Elements(Wordprocessing + "style");
        styles.Single(style => (string?)style.Attribute(Wordprocessing + "styleId") == "RawList_FreeW1")
            .Descendants(Wordprocessing + "numId").Single().Attribute(Wordprocessing + "val")!.Value.Should().Be("5");

        var reread = DocxReader.Read(new MemoryStream(bytes));
        reread.Blocks.OfType<Paragraph>().Should().OnlyContain(paragraph => paragraph.Formatting.ListKind == ListKind.Number);
        reread.Styles["RawList_FreeW1"].PreservedNumbering.Should().Be(new PreservedNumbering(5, 2));
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

    private static XElement RawNumbering(int abstractId, int numberId, string label) =>
        new(Wordprocessing + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", Wordprocessing.NamespaceName),
            new XElement(Wordprocessing + "abstractNum",
                new XAttribute(Wordprocessing + "abstractNumId", abstractId),
                new XElement(Wordprocessing + "multiLevelType", new XAttribute(Wordprocessing + "val", label))),
            new XElement(Wordprocessing + "num",
                new XAttribute(Wordprocessing + "numId", numberId),
                new XElement(Wordprocessing + "abstractNumId", new XAttribute(Wordprocessing + "val", abstractId))));
}
