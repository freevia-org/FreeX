using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class StrictOoxmlAutomaticReadTests
{
    private const string StrictW = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string StrictR = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string PackageR = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string TransitionalR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly byte[] Attachment = [0, 1, 2, 3, 254, 255];
    private static readonly byte[] CustomXml = Encoding.UTF8.GetBytes(
        $"<record xmlns=\"urn:example:record\" Type=\"{StrictR}/hyperlink\"><value>custom data</value></record>");

    [Theory]
    [InlineData("reader")]
    [InlineData("docx")]
    [InlineData("docm")]
    [InlineData("dotx")]
    [InlineData("dotm")]
    [InlineData("strict")]
    public void NormalOpenAutomaticallyReadsStrictDocumentsAndTheirRelatedStories(string route)
    {
        using var stream = new MemoryStream(Package());
        var document = route switch
        {
            "docx" => DocxFileAdapter.Docx().Load(stream),
            "docm" => DocxFileAdapter.Docm().Load(stream),
            "dotx" => DocxFileAdapter.Dotx().Load(stream),
            "dotm" => DocxFileAdapter.Dotm().Load(stream),
            "strict" => DocxFileAdapter.Strict().Load(stream),
            _ => DocxReader.Read(stream),
        };

        document.Paragraphs.First().Runs.Should().Contain(run => run.Text == "Strict body" && run.Formatting.Bold);
        document.Paragraphs.First().Runs.Should().Contain(run =>
            run.Text == "link" && run.HyperlinkUrl == "https://example.test/document");
        document.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("Table cell");
        document.FinalSectionHeadersFooters.Header!.PlainText.Should().Be("Header");
        document.Footnotes[1].PlainText.Should().Be("Footnote");
        document.Page.BackgroundColorHex.Should().Be("#EFEFEF");
        stream.CanRead.Should().BeTrue("normalization must leave the caller's stream open");
    }

    [Fact]
    public void DirectReaderSupportsStrictPackagesFromNonseekableStreams()
    {
        using var stream = new NonseekableStream(Package());

        var document = DocxReader.Read(stream);

        document.Paragraphs.First().PlainText.Should().Contain("Strict body");
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void NormalizationAndSaveKeepCustomXmlBinaryPartsAndTheirRelationships()
    {
        using var source = new MemoryStream(Package());
        var document = DocxReader.Read(source);
        document.Preserved.Parts.Single(part => part.PartName == "/customXml/item1.xml").Bytes.Should().Equal(CustomXml);
        document.Preserved.Parts.Single(part => part.PartName == "/attachments/data.bin").Bytes.Should().Equal(Attachment);
        document.Preserved.OriginalCustomProperties!.Name.NamespaceName.Should()
            .Be("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");

        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        Bytes(archive, "customXml/item1.xml").Should().Equal(CustomXml);
        Bytes(archive, "attachments/data.bin").Should().Equal(Attachment);
        Xml(archive, "word/_rels/document.xml.rels").Descendants(XName.Get("Relationship", PackageR))
            .Should().Contain(relationship => relationship.Attribute("Type")!.Value == $"{TransitionalR}/customXml");
        Xml(archive, "_rels/.rels").Descendants(XName.Get("Relationship", PackageR))
            .Should().Contain(relationship => relationship.Attribute("Type")!.Value == "urn:example:relationships/data");
    }

    [Fact]
    public void APartThatCannotBeParsedDuringNormalizationKeepsAllItsOriginalBytes()
    {
        var malformed = Encoding.UTF8.GetBytes("<record xmlns=\"urn:example:record\"><value>unfinished");
        using var source = new MemoryStream(Package(malformed));

        using var normalized = StrictOoxmlTransform.RewriteStrictToTransitional(source);
        using var archive = new ZipArchive(normalized, ZipArchiveMode.Read);

        Bytes(archive, "customXml/item1.xml").Should().Equal(malformed);
    }

    [Fact]
    public void StrictSaveKeepsOpcNamespacesAndUsesCanonicalStrictMetadataNamespacesAndRelationships()
    {
        using var source = new MemoryStream(Package());
        var document = DocxFileAdapter.Strict().Load(source);
        using var saved = new MemoryStream();
        DocxFileAdapter.Strict().Save(document, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);

        Xml(archive, "[Content_Types].xml").Root!.Name.NamespaceName.Should().Be(ContentTypes);
        var relationships = Xml(archive, "_rels/.rels");
        relationships.Root!.Name.NamespaceName.Should().Be(PackageR);
        var types = relationships.Descendants(XName.Get("Relationship", PackageR))
            .Select(relationship => relationship.Attribute("Type")!.Value);
        types.Should().Contain($"{StrictR}/extendedProperties");
        types.Should().Contain($"{StrictR}/customProperties");
        types.Should().Contain("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
        Xml(archive, "docProps/app.xml").Root!.Name.NamespaceName.Should()
            .Be("http://purl.oclc.org/ooxml/officeDocument/extendedProperties");
        Xml(archive, "docProps/custom.xml").Root!.Name.NamespaceName.Should()
            .Be("http://purl.oclc.org/ooxml/officeDocument/customProperties");
    }

    private static byte[] Package(byte[]? customXml = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string xml) => Write(name, Encoding.UTF8.GetBytes(xml));
            void Write(string name, byte[] bytes)
            {
                using var target = archive.CreateEntry(name).Open();
                target.Write(bytes);
            }

            Add("[Content_Types].xml", $"""
                <Types xmlns="{ContentTypes}">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="bin" ContentType="application/octet-stream"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
                  <Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>
                </Types>
                """);
            Add("_rels/.rels", $"""
                <Relationships xmlns="{PackageR}">
                  <Relationship Id="doc" Type="{StrictR}/officeDocument" Target="word/document.xml"/>
                  <Relationship Id="app" Type="{StrictR}/extendedProperties" Target="docProps/app.xml"/>
                  <Relationship Id="custom" Type="{StrictR}/customProperties" Target="docProps/custom.xml"/>
                  <Relationship Id="data" Type="urn:example:relationships/data" Target="attachments/data.bin"/>
                </Relationships>
                """);
            Add("word/document.xml", $"""
                <w:document xmlns:w="{StrictW}" xmlns:r="{StrictR}">
                  <w:background w:color="EFEFEF"/>
                  <w:body>
                    <w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Strict body</w:t></w:r>
                      <w:hyperlink r:id="link"><w:r><w:t>link</w:t></w:r></w:hyperlink>
                      <w:r><w:footnoteReference w:id="1"/></w:r></w:p>
                    <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                    <w:sectPr><w:headerReference w:type="default" r:id="header"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
            Add("word/_rels/document.xml.rels", $"""
                <Relationships xmlns="{PackageR}">
                  <Relationship Id="header" Type="{StrictR}/header" Target="header1.xml"/>
                  <Relationship Id="notes" Type="{StrictR}/footnotes" Target="footnotes.xml"/>
                  <Relationship Id="link" Type="{StrictR}/hyperlink" Target="https://example.test/document" TargetMode="External"/>
                  <Relationship Id="custom" Type="{StrictR}/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """);
            Add("word/header1.xml", $"<w:hdr xmlns:w=\"{StrictW}\"><w:p><w:r><w:t>Header</w:t></w:r></w:p></w:hdr>");
            Add("word/footnotes.xml", $"<w:footnotes xmlns:w=\"{StrictW}\"><w:footnote w:id=\"1\"><w:p><w:r><w:t>Footnote</w:t></w:r></w:p></w:footnote></w:footnotes>");
            Add("docProps/app.xml", "<Properties xmlns=\"http://purl.oclc.org/ooxml/officeDocument/extendedProperties\"><Application>Fixture</Application></Properties>");
            Add("docProps/custom.xml", """
                <Properties xmlns="http://purl.oclc.org/ooxml/officeDocument/customProperties" xmlns:vt="http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes">
                  <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Fixture"><vt:lpwstr>Preserved</vt:lpwstr></property>
                </Properties>
                """);
            Write("customXml/item1.xml", customXml ?? CustomXml);
            Write("attachments/data.bin", Attachment);
        }
        return stream.ToArray();
    }

    private static byte[] Bytes(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XDocument Xml(ZipArchive archive, string path) =>
        XDocument.Load(new MemoryStream(Bytes(archive, path)));

    private sealed class NonseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
