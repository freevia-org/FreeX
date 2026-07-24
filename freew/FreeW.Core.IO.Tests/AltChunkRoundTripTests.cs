using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class AltChunkRoundTripTests
{
    [Fact]
    public void HtmlAltChunk_MaterializesEditableBlocksAndChunkLocalImages()
    {
        var sourceBytes = AuthorPackageWithAltChunk();
        var document = ReadDocument(sourceBytes);

        document.Blocks.Should().HaveCount(3);
        document.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Before");
        var imported = document.Blocks[1].Should().BeOfType<Paragraph>().Which;
        imported.PlainText.Should().Be("Materialized altChunk HTML");
        imported.Runs.Where(run => run.Image is not null).Should().ContainSingle();
        imported.Runs.Single(run => run.Image is not null).Image!.Bytes.Should()
            .Equal(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMB/6XZDxsAAAAASUVORK5CYII="));
        document.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("After");
        document.Preserved.Parts.Should().NotContain(part => part.PartName == "/word/afchunk.html");

        var rewrittenBytes = WriteDocument(document);
        var rewritten = DocxPackageInventory.Read(rewrittenBytes);

        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().BeNull();
        zip.GetEntry("word/afchunk.html").Should().BeNull();
        rewritten.ShouldDeclareDefault("png", "image/png");

        var reopened = ReadDocument(rewrittenBytes);
        reopened.Blocks[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Materialized altChunk HTML");
        reopened.Blocks[1].Should().BeOfType<Paragraph>().Which.Runs
            .Where(run => run.Image is not null).Should().ContainSingle();
    }

    [Fact]
    public void MhtmlAltChunk_MaterializesEditableBlocksAndEmbeddedImages()
    {
        var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMB/6XZDxsAAAAASUVORK5CYII=");
        var mhtml = $$"""
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="freew-boundary"; type="text/html"

            --freew-boundary
            Content-Type: text/html; charset=utf-8

            <!doctype html><html><body><p>Materialized MHTML altChunk<img src="cid:altchunk-image" alt="MHTML embedded image"/></p></body></html>
            --freew-boundary
            Content-Type: image/png
            Content-ID: <altchunk-image>
            Content-Transfer-Encoding: base64

            {{Convert.ToBase64String(imageBytes)}}
            --freew-boundary--
            """;
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.mht",
            chunkContentType: "message/rfc822",
            chunkPayload: mhtml.ReplaceLineEndings("\r\n"),
            includeChunkLocalImage: false);

        var document = ReadDocument(sourceBytes);

        document.Blocks.Should().HaveCount(3);
        var imported = document.Blocks[1].Should().BeOfType<Paragraph>().Which;
        imported.PlainText.Should().Be("Materialized MHTML altChunk");
        var importedImage = imported.Runs.Single(run => run.Image is not null).Image!;
        importedImage.Bytes.Should().Equal(imageBytes);
        importedImage.AltText.Should().Be("MHTML embedded image");
        document.Preserved.Parts.Should().NotContain(part => part.PartName == "/word/afchunk.mht");

        var rewrittenBytes = WriteDocument(document);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        zip.GetEntry("word/afchunk.mht").Should().BeNull();
        zip.GetEntry("word/_rels/afchunk.mht.rels").Should().BeNull();
        zip.GetEntry("word/media/altchunk.png").Should().BeNull();
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().BeNull();

        var reopened = ReadDocument(rewrittenBytes);
        reopened.Blocks[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Materialized MHTML altChunk");
        reopened.Blocks[1].Should().BeOfType<Paragraph>().Which.Runs
            .Where(run => run.Image is not null).Should().ContainSingle();
    }

    [Fact]
    public void NonHtmlAltChunk_RetainsItsPayloadAndBodyMarker()
    {
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.rtf",
            chunkContentType: "application/rtf",
            chunkPayload: @"{\rtf1\ansi Preserved RTF}");
        var source = DocxPackageInventory.Read(sourceBytes);
        var document = ReadDocument(sourceBytes);

        document.Blocks[1].Should().BeOfType<AltChunkBlock>().Which.PreservedPartName.Should().Be("/word/afchunk.rtf");

        var rewrittenBytes = WriteDocument(document);
        var rewritten = DocxPackageInventory.Read(rewrittenBytes);

        rewritten.ShouldPreserveVerbatim(source, ["word/afchunk.rtf", "word/_rels/afchunk.rtf.rels", "word/media/altchunk.png"]);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().NotBeNull();
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

    private static byte[] AuthorPackageWithAltChunk(
        string chunkPartName = "afchunk.html",
        string chunkContentType = "text/html",
        string? chunkPayload = null,
        bool includeChunkLocalImage = true)
    {
        chunkPayload ??= "<html><body><p>Materialized altChunk HTML<img src=\"media/altchunk.png\"/></p></body></html>";
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
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="{Path.GetExtension(chunkPartName).TrimStart('.')}" ContentType="{chunkContentType}"/>
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
                  <Relationship Id="rIdAltChunk" Type="{Ooxml.AltChunkRelType}" Target="{chunkPartName}"/>
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
            Add("word/" + chunkPartName, chunkPayload);
            if (includeChunkLocalImage)
            {
                Add("word/_rels/" + chunkPartName + ".rels",
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
        }

        return stream.ToArray();
    }
}
