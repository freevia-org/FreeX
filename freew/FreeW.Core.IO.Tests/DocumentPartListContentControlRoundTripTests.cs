using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class DocumentPartListContentControlRoundTripTests
{
    private const string StoreItemId = "{D1111111-E222-F333-A444-B55555555555}";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    [Fact]
    public void WordDocumentPartLists_PreserveInlineAndMultiBlockOwnershipMetadataAndCanonicalPackageXml()
    {
        using var source = BuildPackage();
        var sourceBytes = source.ToArray();
        AssertSourcePackageXml(ReadDocumentXml(sourceBytes));
        AssertOffice2013Valid(sourceBytes);

        var imported = DocxReader.Read(new MemoryStream(sourceBytes));
        AssertModel(imported);

        var saved = Write(imported);
        var savedXml = ReadDocumentXml(saved);
        AssertCanonicalPackageXml(savedXml);
        AssertOffice2013Valid(saved);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        AssertModel(reopened);

        var secondSave = Write(reopened);
        var secondXml = ReadDocumentXml(secondSave);
        AssertCanonicalPackageXml(secondXml);
        XNode.DeepEquals(savedXml, secondXml).Should().BeTrue(
            "canonical document-part list SDT XML must remain stable after reopening and saving again");
        AssertOffice2013Valid(secondSave);
    }

    private static void AssertModel(TextDocument document)
    {
        document.Blocks.Should().HaveCount(3);

        var first = document.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        var second = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        first.PlainText.Should().Be("Block document part line one");
        second.PlainText.Should().Be("Block document part line two");
        first.BlockContentControl.Should().NotBeNull();
        second.BlockContentControl.Should().BeSameAs(first.BlockContentControl,
            "both paragraphs belong to one body-level document-part list SDT");
        first.Runs.Should().OnlyContain(run => run.Control == null);
        second.Runs.Should().OnlyContain(run => run.Control == null);

        var block = first.BlockContentControl!;
        block.Kind.Should().Be(BlockContentControlKind.DocumentPart);
        block.Kind.Should().NotBe(BlockContentControlKind.BuildingBlockGallery);
        block.Tag.Should().Be("BlockDocumentPartList");
        block.Alias.Should().Be("Locked block document part list");
        block.DocPartGallery.Should().Be("AutoText");
        block.DocPartCategory.Should().Be("General");
        block.DocPartUnique.Should().BeTrue();
        block.LockMode.Should().Be(ContentControlLockMode.ControlAndContentLocked);
        block.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "1201",
            DataBinding: new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:documentPart",
                "xmlns:ns0='urn:freew:document-part'"),
            PlaceholderDocPart: "DefaultPlaceholder_BlockDocumentPartList",
            ShowingPlaceholder: true,
            Temporary: true,
            Appearance: "tags",
            Color: "4472C4"));

        var inlineParagraph = document.Blocks[2].Should().BeOfType<Paragraph>().Subject;
        inlineParagraph.BlockContentControl.Should().BeNull();
        var inlineRun = inlineParagraph.Runs.Should().ContainSingle().Subject;
        inlineRun.Text.Should().Be("Choose an equation");
        inlineRun.Control.Should().NotBeNull();

        var inline = inlineRun.Control!;
        inline.Kind.Should().Be(ContentControlKind.DocumentPart);
        inline.Kind.Should().NotBe(ContentControlKind.BuildingBlockGallery);
        inline.Tag.Should().Be("InlineDocumentPartList");
        inline.Alias.Should().Be("Locked inline document part list");
        inline.DocPartGallery.Should().Be("Equations");
        inline.DocPartCategory.Should().Be("Built-In");
        inline.DocPartUnique.Should().BeFalse("w:docPartUnique w:val=\"0\" is explicitly off");
        inline.LockMode.Should().Be(ContentControlLockMode.ControlLocked);
        inline.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "1202",
            Appearance: "boundingBox",
            Color: "C00000"));
    }

    private static void AssertSourcePackageXml(XDocument xml)
    {
        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertDocumentPartList(blockProperties, "AutoText", "General", expectedUnique: true);
        blockProperties.Element(W + "docPartList")!.Element(W + "docPartUnique")!
            .Attribute(W + "val")!.Value.Should().Be("1");
        AssertDocumentPartList(inlineProperties, "Equations", "Built-In", expectedUnique: true);
        inlineProperties.Element(W + "docPartList")!.Element(W + "docPartUnique")!
            .Attribute(W + "val")!.Value.Should().Be("0");
    }

    private static void AssertCanonicalPackageXml(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        var blockSdt = body.Elements(W + "sdt").Should().ContainSingle(
            "the body-level document-part list must remain one body-level SDT wrapper").Subject;
        blockSdt.Element(W + "sdtContent")!.Elements(W + "p").Should().HaveCount(2);
        body.Elements(W + "p").Should().ContainSingle(
            "the inline document-part list must remain inside its paragraph");

        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertDocumentPartList(blockProperties, "AutoText", "General", expectedUnique: true);
        blockProperties.Element(W + "docPartList")!.Element(W + "docPartUnique")!
            .Attributes().Should().BeEmpty("true is serialized in canonical empty-element form");
        AssertCommonMetadata(
            blockProperties,
            alias: "Locked block document part list",
            lockValue: "sdtContentLocked",
            id: "1201",
            tag: "BlockDocumentPartList",
            appearance: "tags",
            color: "4472C4");
        blockProperties.Element(W + "placeholder")!.Element(W + "docPart")!
            .Attribute(W + "val")!.Value.Should().Be("DefaultPlaceholder_BlockDocumentPartList");
        blockProperties.Element(W + "showingPlcHdr").Should().NotBeNull();
        blockProperties.Element(W + "temporary").Should().NotBeNull();
        var binding = blockProperties.Element(W + "dataBinding")!;
        binding.Attribute(W + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(W + "xpath")!.Value.Should().Be("/ns0:root/ns0:documentPart");
        binding.Attribute(W + "prefixMappings")!.Value.Should().Be("xmlns:ns0='urn:freew:document-part'");

        AssertDocumentPartList(inlineProperties, "Equations", "Built-In", expectedUnique: false);
        inlineProperties.Element(W + "docPartList")!.Element(W + "docPartUnique").Should().BeNull(
            "explicit false is canonicalized by omitting the optional on/off property");
        AssertCommonMetadata(
            inlineProperties,
            alias: "Locked inline document part list",
            lockValue: "sdtLocked",
            id: "1202",
            tag: "InlineDocumentPartList",
            appearance: "boundingBox",
            color: "C00000");
    }

    private static void AssertDocumentPartList(
        XElement properties,
        string expectedGallery,
        string expectedCategory,
        bool expectedUnique)
    {
        var docPart = properties.Elements(W + "docPartList").Should().ContainSingle().Subject;
        docPart.Element(W + "docPartGallery")!.Attribute(W + "val")!.Value
            .Should().Be(expectedGallery);
        docPart.Element(W + "docPartCategory")!.Attribute(W + "val")!.Value
            .Should().Be(expectedCategory);
        (docPart.Element(W + "docPartUnique") is not null).Should().Be(expectedUnique);
        properties.Elements(W + "docPartObj").Should().BeEmpty(
            "document-part lists must remain distinct from building-block gallery objects");
        properties.Elements(W + "text").Should().BeEmpty();
        properties.Elements(W + "richText").Should().BeEmpty();
    }

    private static void AssertCommonMetadata(
        XElement properties,
        string alias,
        string lockValue,
        string id,
        string tag,
        string appearance,
        string color)
    {
        properties.Element(W + "alias")!.Attribute(W + "val")!.Value.Should().Be(alias);
        properties.Element(W + "lock")!.Attribute(W + "val")!.Value.Should().Be(lockValue);
        properties.Element(W + "id")!.Attribute(W + "val")!.Value.Should().Be(id);
        properties.Element(W + "tag")!.Attribute(W + "val")!.Value.Should().Be(tag);
        properties.Element(W15 + "appearance")!.Attribute(W15 + "val")!.Value.Should().Be(appearance);
        properties.Element(W15 + "color")!.Attribute(W + "val")!.Value.Should().Be(color);
    }

    private static (XElement Block, XElement Inline) GetProperties(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        var block = body.Elements(W + "sdt").Should().ContainSingle().Subject;
        var inline = body.Elements(W + "p").Should().ContainSingle().Subject
            .Elements(W + "sdt").Should().ContainSingle().Subject;
        return (block.Element(W + "sdtPr")!, inline.Element(W + "sdtPr")!);
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
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="{{W}}" xmlns:w15="{{W15}}"
                            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                            mc:Ignorable="w15">
                  <w:body>
                    <w:sdt>
                      <w:sdtPr>
                        <w:alias w:val="Locked block document part list"/>
                        <w:lock w:val="sdtContentLocked"/>
                        <w:placeholder><w:docPart w:val="DefaultPlaceholder_BlockDocumentPartList"/></w:placeholder>
                        <w:showingPlcHdr/>
                        <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:document-part'" w:xpath="/ns0:root/ns0:documentPart" w:storeItemID="{{StoreItemId}}"/>
                        <w:temporary/>
                        <w:id w:val="1201"/>
                        <w15:color w:val="4472C4"/>
                        <w15:appearance w15:val="tags"/>
                        <w:tag w:val="BlockDocumentPartList"/>
                        <w:docPartList>
                          <w:docPartGallery w:val="AutoText"/>
                          <w:docPartCategory w:val="General"/>
                          <w:docPartUnique w:val="1"/>
                        </w:docPartList>
                      </w:sdtPr>
                      <w:sdtContent>
                        <w:p><w:r><w:t>Block document part line one</w:t></w:r></w:p>
                        <w:p><w:r><w:t>Block document part line two</w:t></w:r></w:p>
                      </w:sdtContent>
                    </w:sdt>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:alias w:val="Locked inline document part list"/>
                          <w:lock w:val="sdtLocked"/>
                          <w:id w:val="1202"/>
                          <w15:color w:val="C00000"/>
                          <w15:appearance w15:val="boundingBox"/>
                          <w:tag w:val="InlineDocumentPartList"/>
                          <w:docPartList>
                            <w:docPartGallery w:val="Equations"/>
                            <w:docPartCategory w:val="Built-In"/>
                            <w:docPartUnique w:val="0"/>
                          </w:docPartList>
                        </w:sdtPr>
                        <w:sdtContent><w:r><w:t>Choose an equation</w:t></w:r></w:sdtContent>
                      </w:sdt>
                    </w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument ReadDocumentXml(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static void AssertOffice2013Valid(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var document = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(document)
            .Select(error => $"{error.Id}: {error.Description}; node={error.Node?.OuterXml}")
            .ToList();
        errors.Should().BeEmpty();
    }

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
