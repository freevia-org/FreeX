using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class CustomXmlContentRoundTripTests
{
    [Fact]
    public void CustomXmlContent_SurvivesAsOrdinaryFormattedRuns()
    {
        using var input = BuildPackage();
        var read = DocxReader.Read(input);
        var paragraph = read.Paragraphs.Single();

        paragraph.PlainText.Should().Be("Customer name");
        paragraph.Runs.Single().Formatting.Italic.Should().BeTrue();

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(read, output);
            rewritten = output.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("Customer name");
            documentXml.Should().NotContain("customXml");
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten)).Paragraphs.Single();
        reread.PlainText.Should().Be("Customer name");
        reread.Runs.Single().Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void BodyCustomXml_PreservesVisibleBlocksAndWrapperAcrossOutsideEdit()
    {
        using var input = BuildBodyPackage();
        var document = DocxReader.Read(input);

        document.Blocks.Should().HaveCount(3);
        document.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Wrapped paragraph");
        document.Blocks[1].Should().BeOfType<Table>().Which.Rows[0].Cells[0].PlainText.Should().Be("Wrapped cell");
        document.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Outside paragraph");

        var wrapper = document.Blocks[0].BlockCustomXml;
        wrapper.Should().NotBeNull();
        ReferenceEquals(document.Blocks[1].BlockCustomXml, wrapper).Should().BeTrue();
        document.Blocks[2].BlockCustomXml.Should().BeNull();
        wrapper!.Element.Should().Be("customer");
        wrapper.Uri.Should().Be("urn:freew:test");
        wrapper.PropertiesXml.Should().Contain("status").And.Contain("active");

        ((Paragraph)document.Blocks[2]).Runs[0].Text = "Edited outside";
        var first = Write(document);
        AssertBodyWrapper(first, "Edited outside");

        var reopened = DocxReader.Read(new MemoryStream(first));
        reopened.Blocks.Should().HaveCount(3);
        reopened.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Wrapped paragraph");
        reopened.Blocks[1].Should().BeOfType<Table>().Which.Rows[0].Cells[0].PlainText.Should().Be("Wrapped cell");
        ReferenceEquals(reopened.Blocks[0].BlockCustomXml, reopened.Blocks[1].BlockCustomXml).Should().BeTrue();

        var second = Write(reopened);
        AssertBodyWrapper(second, "Edited outside");
    }

    private static void AssertBodyWrapper(byte[] package, string outsideText)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(stream).Root!.Element(w + "body")!;
        var wrapper = body.Elements().First();

        wrapper.Name.Should().Be(w + "customXml");
        wrapper.Attribute(w + "element")!.Value.Should().Be("customer");
        wrapper.Attribute(w + "uri")!.Value.Should().Be("urn:freew:test");
        wrapper.Element(w + "customXmlPr")!.Element(w + "attr")!.Attribute(w + "name")!.Value.Should().Be("status");
        wrapper.Elements().Where(element => element.Name != w + "customXmlPr")
            .Select(element => element.Name).Should().Equal(w + "p", w + "tbl");
        body.Elements(w + "p").Single().Value.Should().Be(outsideText);
    }

    private static byte[] Write(TextDocument document)
    {
        using var output = new MemoryStream();
        DocxWriter.Write(document, output);
        return output.ToArray();
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
                    <w:p><w:customXml w:element="customer" w:uri="urn:freew:test"><w:r><w:rPr><w:i/></w:rPr><w:t>Customer name</w:t></w:r></w:customXml></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildBodyPackage()
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
                    <w:customXml w:element="customer" w:uri="urn:freew:test">
                      <w:customXmlPr><w:attr w:name="status" w:val="active"/></w:customXmlPr>
                      <w:p><w:r><w:t>Wrapped paragraph</w:t></w:r></w:p>
                      <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Wrapped cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                    </w:customXml>
                    <w:p><w:r><w:t>Outside paragraph</w:t></w:r></w:p>
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
