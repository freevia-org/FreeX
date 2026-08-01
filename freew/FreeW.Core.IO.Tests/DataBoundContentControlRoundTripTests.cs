using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class DataBoundContentControlRoundTripTests
{
    private const string StoreItemId = "{11111111-2222-3333-4444-555555555555}";
    private static readonly byte[] ItemBytes = Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:freew:test\"><name>Original value</name></root>");
    private static readonly byte[] ItemPropsBytes = Encoding.UTF8.GetBytes(
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><ds:datastoreItem ds:itemID=\"{StoreItemId}\" xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:freew:test\"/></ds:schemaRefs></ds:datastoreItem>");

    [Fact]
    public void BoundControl_RetainsCustomXmlGraphAndBindingAcrossEditedDisplayText()
    {
        using var input = BuildPackage();
        var document = DocxReader.Read(input);
        var run = document.Paragraphs.Single().Runs.Single();
        run.Control!.WordMetadata!.DataBinding!.StoreItemId.Should().Be(StoreItemId);
        run.Text = "Edited display value";

        var first = Write(document);
        EntryBytes(first, "customXml/item1.xml").Should().Equal(ItemBytes);
        EntryBytes(first, "customXml/itemProps1.xml").Should().Equal(ItemPropsBytes);
        EntryText(first, "word/_rels/document.xml.rels").Should().Contain("../customXml/item1.xml");
        AssertBoundDocument(first, "Edited display value");

        var reopened = DocxReader.Read(new MemoryStream(first));
        reopened.Paragraphs.Single().Runs.Single().Control!.WordMetadata!.DataBinding
            .Should().Be(new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:name",
                "xmlns:ns0='urn:freew:test'"));

        var second = Write(reopened);
        EntryBytes(second, "customXml/item1.xml").Should().Equal(ItemBytes);
        EntryBytes(second, "customXml/itemProps1.xml").Should().Equal(ItemPropsBytes);
        AssertBoundDocument(second, "Edited display value");
    }

    private static void AssertBoundDocument(byte[] package, string expectedText)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var documentStream = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(documentStream);
        var binding = xml.Descendants(w + "dataBinding").Should().ContainSingle().Subject;
        binding.Attribute(w + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(w + "xpath")!.Value.Should().Be("/ns0:root/ns0:name");
        xml.Descendants(w + "t").Single().Value.Should().Be(expectedText);
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static byte[] EntryBytes(byte[] package, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(path)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string EntryText(byte[] package, string path) =>
        Encoding.UTF8.GetString(EntryBytes(package, path));

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:sdt><w:sdtPr>
                    <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:test'" w:xpath="/ns0:root/ns0:name" w:storeItemID="{{StoreItemId}}"/>
                    <w:id w:val="17"/><w:tag w:val="BoundName"/><w:text/>
                  </w:sdtPr><w:sdtContent><w:r><w:t>Original display value</w:t></w:r></w:sdtContent></w:sdt></w:p><w:sectPr/></w:body>
                </w:document>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCustomXml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>
                </Relationships>
                """);
            Add(zip, "customXml/item1.xml", ItemBytes);
            Add(zip, "customXml/itemProps1.xml", ItemPropsBytes);
            Add(zip, "customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/>
                </Relationships>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string text) =>
        Add(zip, path, Encoding.UTF8.GetBytes(text));

    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
