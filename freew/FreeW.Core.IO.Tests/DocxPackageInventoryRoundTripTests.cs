using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

public class DocxPackageInventoryRoundTripTests
{
    private const string StylesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    private const string StylesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

    private static readonly string[] GlossaryEntries =
    [
        "word/glossary/document.xml",
        "word/glossary/_rels/document.xml.rels",
        "word/glossary/styles.xml",
        "word/glossary/media/image1.png"
    ];

    [Fact]
    public void GlossaryPackageParts_ContentTypesAndRelationships_SurviveReadWriteRoundTrip()
    {
        var sourceBytes = AuthorPackageWithGlossary();
        var source = DocxPackageInventory.Read(sourceBytes);
        var read = ReadDoc(sourceBytes);

        read.Preserved.Parts.Select(part => part.PartName).Should().Contain([
            "/word/glossary/document.xml",
            "/word/glossary/_rels/document.xml.rels",
            "/word/glossary/styles.xml",
            "/word/glossary/media/image1.png"
        ]);

        var rewritten = DocxPackageInventory.Read(WriteBytes(read));

        rewritten.ShouldPreserveVerbatim(source, GlossaryEntries);
        rewritten.ShouldDeclareOverride("/word/glossary/document.xml", Ooxml.GlossaryDocumentContentType);
        rewritten.ShouldDeclareOverride("/word/glossary/styles.xml", StylesContentType);
        rewritten.ShouldDeclareDefault("png", "image/png");
        rewritten.ShouldContainRelationship(
            "word/_rels/document.xml.rels",
            Ooxml.GlossaryDocumentRelType,
            "glossary/document.xml");
        rewritten.ShouldContainRelationship(
            "word/glossary/_rels/document.xml.rels",
            StylesRelType,
            "styles.xml");
        rewritten.ShouldContainRelationship(
            "word/glossary/_rels/document.xml.rels",
            Ooxml.ImageRelType,
            "media/image1.png");
    }

    [Fact]
    public void GlossaryPackageParts_SurviveASecondReadWriteRoundTrip()
    {
        var onceBytes = WriteBytes(ReadDoc(AuthorPackageWithGlossary()));
        var twice = DocxPackageInventory.Read(WriteBytes(ReadDoc(onceBytes)));

        twice.ShouldPreserveVerbatim(DocxPackageInventory.Read(onceBytes), GlossaryEntries);
        twice.ShouldDeclareOverride("/word/glossary/document.xml", Ooxml.GlossaryDocumentContentType);
        twice.ShouldContainRelationship(
            "word/_rels/document.xml.rels",
            Ooxml.GlossaryDocumentRelType,
            "glossary/document.xml");
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] AuthorPackageWithGlossary()
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
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="{StylesContentType}"/>
                  <Override PartName="/word/glossary/document.xml" ContentType="{Ooxml.GlossaryDocumentContentType}"/>
                  <Override PartName="/word/glossary/styles.xml" ContentType="{StylesContentType}"/>
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
                  <Relationship Id="rIdStyles" Type="{StylesRelType}" Target="styles.xml"/>
                  <Relationship Id="rIdGlossary" Type="{Ooxml.GlossaryDocumentRelType}" Target="glossary/document.xml"/>
                </Relationships>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            Add("word/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>
                """);

            Add("word/glossary/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:glossaryDocument xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:docParts>
                    <w:docPart>
                      <w:docPartPr>
                        <w:name w:val="FreeW AutoText"/>
                        <w:style w:val="GlossaryQuote"/>
                        <w:category>
                          <w:name w:val="General"/>
                          <w:gallery w:val="autoTxt"/>
                        </w:category>
                      </w:docPartPr>
                      <w:docPartBody>
                        <w:p><w:r><w:t>Preserved glossary text</w:t></w:r></w:p>
                      </w:docPartBody>
                    </w:docPart>
                  </w:docParts>
                </w:glossaryDocument>
                """);

            Add("word/glossary/_rels/document.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdStyles" Type="{StylesRelType}" Target="styles.xml"/>
                  <Relationship Id="rIdImage" Type="{Ooxml.ImageRelType}" Target="media/image1.png"/>
                </Relationships>
                """);

            Add("word/glossary/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="GlossaryQuote">
                    <w:name w:val="Glossary Quote"/>
                    <w:qFormat/>
                  </w:style>
                </w:styles>
                """);

            AddBytes(
                "word/glossary/media/image1.png",
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMB/6XZDxsAAAAASUVORK5CYII="));
        }

        return stream.ToArray();
    }
}
