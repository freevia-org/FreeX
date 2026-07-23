using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class AltChunkRoundTripTests
{
    private static readonly string[] PreservedEntries =
    [
        "word/afchunk.html",
        "word/_rels/afchunk.html.rels",
        "word/media/altchunk.png"
    ];

    [Fact]
    public void AltChunk_PayloadRelationshipGraphAndBodyPlacement_SurviveRoundTrip()
    {
        var sourceBytes = AuthorPackageWithAltChunk();
        var source = DocxPackageInventory.Read(sourceBytes);

        var document = ReadDocument(sourceBytes);

        document.Blocks.Should().HaveCount(3);
        document.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Before");
        document.Blocks[1].Should().BeOfType<AltChunkBlock>().Which.PreservedPartName.Should().Be("/word/afchunk.html");
        document.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("After");
        document.Preserved.Parts.Select(part => part.PartName).Should().Contain(PreservedEntries.Select(path => "/" + path));

        var rewrittenBytes = WriteDocument(document);
        var rewritten = DocxPackageInventory.Read(rewrittenBytes);

        rewritten.ShouldPreserveVerbatim(source, PreservedEntries);
        rewritten.ShouldDeclareDefault("html", "text/html");
        rewritten.ShouldDeclareDefault("png", "image/png");
        rewritten.ShouldContainRelationship(
            "word/_rels/document.xml.rels",
            Ooxml.AltChunkRelType,
            "afchunk.html");
        rewritten.ShouldContainRelationship(
            "word/_rels/afchunk.html.rels",
            Ooxml.ImageRelType,
            "media/altchunk.png");

        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Elements().Take(3).Select(element => element.Name).Should().Equal(
            Ooxml.W + "p",
            Ooxml.W + "altChunk",
            Ooxml.W + "p");
        body.Element(Ooxml.W + "altChunk")!.Attribute(Ooxml.R + "id")!.Value.Should().StartWith("rIdPreserved");

        var reopened = ReadDocument(rewrittenBytes);
        reopened.Blocks[1].Should().BeOfType<AltChunkBlock>().Which.PreservedPartName.Should().Be("/word/afchunk.html");
    }

    private static TextDocument ReadDocument(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static byte[] WriteDocument(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static byte[] AuthorPackageWithAltChunk()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }

            void AddBytes(string path, byte[] content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="html" ContentType="text/html"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add("word/_rels/document.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdAltChunk" Type="{Ooxml.AltChunkRelType}" Target="afchunk.html"/>
                </Relationships>
                """);
            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Before</w:t></w:r></w:p>
                    <w:altChunk r:id="rIdAltChunk"/>
                    <w:p><w:r><w:t>After</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            Add("word/afchunk.html", "<html><body><p>Preserved altChunk HTML</p><img src=\"media/altchunk.png\"/></body></html>");
            Add("word/_rels/afchunk.html.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage" Type="{Ooxml.ImageRelType}" Target="media/altchunk.png"/>
                </Relationships>
                """);
            AddBytes(
                "word/media/altchunk.png",
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMB/6XZDxsAAAAASUVORK5CYII="));
        }

        return stream.ToArray();
    }
}
