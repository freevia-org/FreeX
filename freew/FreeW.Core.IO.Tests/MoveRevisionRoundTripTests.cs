using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class MoveRevisionRoundTripTests
{
    [Fact]
    public void MovedContent_PreservesMoveIdentityAndRenderingCategories()
    {
        using var input = BuildPackage();
        var read = DocxReader.Read(input).Paragraphs.Single();

        read.PlainText.Should().Be("Keep old new");
        read.Runs.Single(run => run.Text == "old ").Revision.Should().Be(RevisionKind.Deleted);
        read.Runs.Single(run => run.Text == "new").Revision.Should().Be(RevisionKind.Inserted);
        read.Runs.Single(run => run.Text == "old ").MoveRevisionId.Should().Be(1);
        read.Runs.Single(run => run.Text == "new").MoveRevisionId.Should().Be(1);
        read.Runs.Where(run => run.Revision != RevisionKind.None)
            .Should().OnlyContain(run => run.RevisionAuthor == "A" && run.RevisionDateXml == "2026-07-23T12:00:00Z");

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(new TextDocument { Blocks = { read } }, output);
            rewritten = output.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("w:moveFrom").And.Contain("w:moveTo");
            documentXml.Should().Contain("w:id=\"1\"");
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten)).Paragraphs.Single();
        reread.PlainText.Should().Be("Keep old new");
        reread.Runs.Single(run => run.Text == "old ").Revision.Should().Be(RevisionKind.Deleted);
        reread.Runs.Single(run => run.Text == "new").Revision.Should().Be(RevisionKind.Inserted);
        reread.Runs.Single(run => run.Text == "old ").MoveRevisionId.Should().Be(1);
        reread.Runs.Single(run => run.Text == "new").MoveRevisionId.Should().Be(1);
    }

    [Fact]
    public void NewRevisionIds_SkipPreservedMoveIds()
    {
        using var input = BuildPackage();
        var paragraph = DocxReader.Read(input).Paragraphs.Single();
        paragraph.Runs.Add(new Run("later") { Revision = RevisionKind.Inserted });

        using var output = new MemoryStream();
        DocxWriter.Write(new TextDocument { Blocks = { paragraph } }, output);
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var document = XDocument.Load(entry);

        document.Descendants(Ooxml.W + "moveFrom").Single().Attribute(Ooxml.W + "id")!.Value.Should().Be("1");
        document.Descendants(Ooxml.W + "moveTo").Single().Attribute(Ooxml.W + "id")!.Value.Should().Be("1");
        document.Descendants(Ooxml.W + "ins").Single().Attribute(Ooxml.W + "id")!.Value.Should().Be("2");
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Keep </w:t></w:r><w:moveFrom w:id="1" w:author="A" w:date="2026-07-23T12:00:00Z"><w:r><w:delText>old </w:delText></w:r></w:moveFrom><w:moveTo w:id="1" w:author="A" w:date="2026-07-23T12:00:00Z"><w:r><w:t>new</w:t></w:r></w:moveTo></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
