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

    [Theory]
    [InlineData("application/rtf")]
    [InlineData("text/rtf")]
    public void RtfAltChunk_MaterializesEditableBlocks(string contentType)
    {
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.rtf",
            chunkContentType: contentType,
            chunkPayload: @"{\rtf1\ansi Materialized \b RTF\b0}",
            includeChunkLocalImage: false);
        var document = ReadDocument(sourceBytes);

        document.Blocks.Should().HaveCount(3);
        document.Blocks[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Materialized RTF");
        document.Preserved.Parts.Should().NotContain(part => part.PartName == "/word/afchunk.rtf");

        var rewrittenBytes = WriteDocument(document);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().BeNull();
        zip.GetEntry("word/afchunk.rtf").Should().BeNull();

        var reopened = ReadDocument(rewrittenBytes);
        reopened.Blocks[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Materialized RTF");
    }

    [Fact]
    public void NestedWordPackageAltChunk_MaterializesEditableBlocksAndCarriesConflictingStyles()
    {
        var nested = new TextDocument();
        nested.Styles["NestedBase"] = new DocumentStyle
        {
            Id = "NestedBase",
            Name = "Nested base",
            Run = new RunFormatting { Italic = true, NoProof = true, Hidden = true, WebHidden = true }
        };
        nested.Styles["ImportedHeading"] = new DocumentStyle
        {
            Id = "ImportedHeading",
            Name = "Imported Heading",
            BasedOnStyleId = "NestedBase",
            Run = new RunFormatting { Bold = true, FontSizePt = 18, ColorHex = "#336699" }
        };
        nested.Blocks.Add(new Paragraph("Nested heading") { StyleId = "ImportedHeading" });
        nested.Blocks.Add(new Paragraph("Nested body"));

        var nestedBytes = WriteDocument(nested);
        ReadDocument(nestedBytes).Blocks.Should().HaveCount(2);
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.docx",
            chunkContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            includeChunkLocalImage: false,
            chunkBytes: nestedBytes,
            documentStyles: """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docDefaults><w:pPrDefault><w:pPr><w:spacing w:after="160"/></w:pPr></w:pPrDefault></w:docDefaults>
                  <w:style w:type="paragraph" w:styleId="ImportedHeading"><w:name w:val="Outer heading"/><w:rPr><w:i/></w:rPr></w:style>
                </w:styles>
                """);

        var document = ReadDocument(sourceBytes);

        document.Blocks[1].Should().NotBeOfType<AltChunkBlock>();
        document.Blocks.Should().HaveCount(4);
        var importedHeading = document.Blocks[1].Should().BeOfType<Paragraph>().Which;
        importedHeading.PlainText.Should().Be("Nested heading");
        importedHeading.StyleId.Should().NotBe("ImportedHeading");
        document.Styles["ImportedHeading"].Run.Italic.Should().BeTrue();
        document.Styles[importedHeading.StyleId!].Run.Bold.Should().BeTrue();
        document.Styles[importedHeading.StyleId!].Run.Italic.Should().BeTrue();
        document.Styles[importedHeading.StyleId!].Run.NoProof.Should().BeTrue();
        document.Styles[importedHeading.StyleId!].Run.Hidden.Should().BeTrue();
        document.Styles[importedHeading.StyleId!].Run.WebHidden.Should().BeTrue();
        importedHeading.Runs.Single().Formatting.Hidden.Should().BeFalse(
            "a direct false toggle does not cancel the inherited hidden style in FreeW's bool model");
        document.Styles[importedHeading.StyleId!].Run.FontSizePt.Should().Be(18);
        document.Styles[importedHeading.StyleId!].Run.ColorHex.Should().Be("#336699");
        document.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Nested body");
        document.Preserved.Parts.Should().NotContain(part => part.PartName == "/word/afchunk.docx");

        var rewrittenBytes = WriteDocument(document);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        zip.GetEntry("word/afchunk.docx").Should().BeNull();
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().BeNull();

        var reopened = ReadDocument(rewrittenBytes);
        var reopenedHeading = reopened.Blocks[1].Should().BeOfType<Paragraph>().Which;
        reopenedHeading.PlainText.Should().Be("Nested heading");
        reopened.Styles[reopenedHeading.StyleId!].Run.Bold.Should().BeTrue();
        reopened.Styles[reopenedHeading.StyleId!].Run.NoProof.Should().BeTrue();
        reopened.Styles[reopenedHeading.StyleId!].Run.Hidden.Should().BeTrue();
        reopened.Styles[reopenedHeading.StyleId!].Run.WebHidden.Should().BeTrue();
    }

    [Fact]
    public void NestedWordPackageAltChunk_WithInvalidPayload_RetainsItsPayloadAndBodyMarker()
    {
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.docx",
            chunkContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            chunkPayload: "not a nested Word package");
        var source = DocxPackageInventory.Read(sourceBytes);
        var document = ReadDocument(sourceBytes);

        document.Blocks[1].Should().BeOfType<AltChunkBlock>().Which.PreservedPartName.Should().Be("/word/afchunk.docx");

        var rewrittenBytes = WriteDocument(document);
        var rewritten = DocxPackageInventory.Read(rewrittenBytes);

        rewritten.ShouldPreserveVerbatim(source, ["word/afchunk.docx", "word/_rels/afchunk.docx.rels", "word/media/altchunk.png"]);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry).Root!.Element(Ooxml.W + "body")!;
        body.Element(Ooxml.W + "altChunk").Should().NotBeNull();
    }

    [Fact]
    public void NestedWordPackageAltChunk_WithDifferentDocumentDefaults_MaterializesWithSourceDefaults()
    {
        var nested = new TextDocument
        {
            DefaultRun = new RunFormatting
            {
                FontFamily = "Times New Roman",
                FontSizePt = 14,
                NoProof = true,
                Hidden = true,
                WebHidden = true
            },
            DefaultParagraph = new ParagraphFormatting { SpaceAfterPt = 12, SpaceAfterIsSet = true }
        };
        nested.Blocks.Add(new Paragraph("Nested body"));
        var sourceBytes = AuthorPackageWithAltChunk(
            chunkPartName: "afchunk.docx",
            chunkContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            includeChunkLocalImage: false,
            chunkBytes: WriteDocument(nested));

        var document = ReadDocument(sourceBytes);

        var imported = document.Blocks[1].Should().BeOfType<Paragraph>().Which;
        imported.PlainText.Should().Be("Nested body");
        imported.StyleId.Should().NotBeNull();
        var importedDefaults = document.Styles[imported.StyleId!];
        importedDefaults.Run.FontFamily.Should().Be("Times New Roman");
        importedDefaults.Run.FontSizePt.Should().Be(14);
        importedDefaults.Run.NoProof.Should().BeTrue();
        importedDefaults.Run.Hidden.Should().BeTrue();
        importedDefaults.Run.WebHidden.Should().BeTrue();
        importedDefaults.Paragraph.SpaceAfterPt.Should().Be(12);

        var rewrittenBytes = WriteDocument(document);
        using var zip = new ZipArchive(new MemoryStream(rewrittenBytes), ZipArchiveMode.Read);
        zip.GetEntry("word/afchunk.docx").Should().BeNull();
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
        bool includeChunkLocalImage = true,
        string? documentStyles = null,
        byte[]? chunkBytes = null)
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

            var stylesOverride = documentStyles is null
                ? string.Empty
                : "\n  <Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>";
            Add("[Content_Types].xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="{Path.GetExtension(chunkPartName).TrimStart('.')}" ContentType="{chunkContentType}"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  {stylesOverride}
                </Types>
                """);
            var stylesRelationship = documentStyles is null
                ? string.Empty
                : "\n  <Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
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
                  {stylesRelationship}
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
            if (documentStyles is not null)
                Add("word/styles.xml", documentStyles);
            if (chunkBytes is not null)
                AddBytes("word/" + chunkPartName, chunkBytes);
            else
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
