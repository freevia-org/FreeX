using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class GroupContentControlRoundTripTests
{
    private const string StoreItemId = "{A1111111-B222-C333-D444-E55555555555}";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    [Fact]
    public void WordGroupControls_PreserveInlineAndBlockOwnershipMetadataAndCanonicalPackageXml()
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
            "canonical Group SDT XML must remain stable after reopening and saving again");
        AssertOffice2013Valid(secondSave);
    }

    [Fact]
    public void LegacyFreeWColorAttribute_IsRetainedAndCanonicalizedToOfficeSchema()
    {
        using var source = BuildPackage(useLegacyColorAttribute: true);
        var imported = DocxReader.Read(source);
        AssertModel(imported);

        var saved = Write(imported);
        var (blockProperties, inlineProperties) = GetProperties(ReadDocumentXml(saved));
        foreach (var properties in new[] { blockProperties, inlineProperties })
        {
            var color = properties.Element(W15 + "color")!;
            color.Attribute(W + "val").Should().NotBeNull();
            color.Attribute(W15 + "val").Should().BeNull();
        }
        AssertOffice2013Valid(saved);
    }

    private static void AssertModel(TextDocument document)
    {
        document.Blocks.Should().HaveCount(3);

        var first = document.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        var second = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        first.PlainText.Should().Be("Grouped block line one");
        second.PlainText.Should().Be("Grouped block line two");
        first.BlockContentControl.Should().NotBeNull();
        second.BlockContentControl.Should().BeSameAs(first.BlockContentControl,
            "both paragraphs belong to one body-level Group SDT");
        first.Runs.Should().OnlyContain(run => run.Control == null);
        second.Runs.Should().OnlyContain(run => run.Control == null);

        var block = first.BlockContentControl!;
        block.Kind.Should().Be(BlockContentControlKind.Group);
        block.Tag.Should().Be("BlockGroup");
        block.Alias.Should().Be("Locked block group");
        block.LockMode.Should().Be(ContentControlLockMode.ControlAndContentLocked);
        block.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "901",
            DataBinding: new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:block",
                "xmlns:ns0='urn:freew:group'"),
            PlaceholderDocPart: "DefaultPlaceholder_BlockGroup",
            ShowingPlaceholder: true,
            Temporary: true,
            Appearance: "tags",
            Color: "4472C4"));

        var inlineParagraph = document.Blocks[2].Should().BeOfType<Paragraph>().Subject;
        inlineParagraph.BlockContentControl.Should().BeNull();
        var inlineRun = inlineParagraph.Runs.Should().ContainSingle().Subject;
        inlineRun.Text.Should().Be("Grouped inline text");
        inlineRun.Control.Should().NotBeNull();

        var inline = inlineRun.Control!;
        inline.Kind.Should().Be(ContentControlKind.Group);
        inline.Tag.Should().Be("InlineGroup");
        inline.Alias.Should().Be("Locked inline group");
        inline.LockMode.Should().Be(ContentControlLockMode.ControlLocked);
        inline.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "902",
            Appearance: "boundingBox",
            Color: "C00000"));
    }

    private static void AssertSourcePackageXml(XDocument xml)
    {
        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertGroupKind(blockProperties);
        AssertGroupKind(inlineProperties);
        blockProperties.Element(W + "lock")!.Attribute(W + "val")!.Value
            .Should().Be("sdtContentLocked");
        inlineProperties.Element(W + "lock")!.Attribute(W + "val")!.Value
            .Should().Be("sdtLocked");
    }

    private static void AssertCanonicalPackageXml(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        var blockSdt = body.Elements(W + "sdt").Should().ContainSingle(
            "the body-level Group must remain a body-level SDT wrapper").Subject;
        blockSdt.Element(W + "sdtContent")!.Elements(W + "p").Should().HaveCount(2);
        body.Elements(W + "p").Should().ContainSingle(
            "the inline Group must remain inside its paragraph");

        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertGroupKind(blockProperties);
        AssertGroupKind(inlineProperties);
        AssertCommonMetadata(
            blockProperties,
            alias: "Locked block group",
            lockValue: "sdtContentLocked",
            id: "901",
            tag: "BlockGroup",
            appearance: "tags",
            color: "4472C4");
        blockProperties.Element(W + "placeholder")!.Element(W + "docPart")!
            .Attribute(W + "val")!.Value.Should().Be("DefaultPlaceholder_BlockGroup");
        blockProperties.Element(W + "showingPlcHdr").Should().NotBeNull();
        blockProperties.Element(W + "temporary").Should().NotBeNull();
        var binding = blockProperties.Element(W + "dataBinding")!;
        binding.Attribute(W + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(W + "xpath")!.Value.Should().Be("/ns0:root/ns0:block");
        binding.Attribute(W + "prefixMappings")!.Value.Should().Be("xmlns:ns0='urn:freew:group'");

        AssertCommonMetadata(
            inlineProperties,
            alias: "Locked inline group",
            lockValue: "sdtLocked",
            id: "902",
            tag: "InlineGroup",
            appearance: "boundingBox",
            color: "C00000");
    }

    private static void AssertGroupKind(XElement properties)
    {
        properties.Elements(W + "group").Should().ContainSingle();
        properties.Element(W + "group")!.IsEmpty.Should().BeTrue();
        properties.Elements(W + "text").Should().BeEmpty();
        properties.Elements(W + "richText").Should().BeEmpty();
        properties.Elements(W + "docPartObj").Should().BeEmpty();
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

    private static MemoryStream BuildPackage(bool useLegacyColorAttribute = false)
    {
        var colorValueAttribute = useLegacyColorAttribute ? "w15:val" : "w:val";
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
                        <w:alias w:val="Locked block group"/>
                        <w:lock w:val="sdtContentLocked"/>
                        <w:placeholder><w:docPart w:val="DefaultPlaceholder_BlockGroup"/></w:placeholder>
                        <w:showingPlcHdr/>
                        <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:group'" w:xpath="/ns0:root/ns0:block" w:storeItemID="{{StoreItemId}}"/>
                        <w:temporary/>
                        <w:id w:val="901"/>
                        <w15:color {{colorValueAttribute}}="4472C4"/>
                        <w15:appearance w15:val="tags"/>
                        <w:tag w:val="BlockGroup"/>
                        <w:group/>
                      </w:sdtPr>
                      <w:sdtContent>
                        <w:p><w:r><w:t>Grouped block line one</w:t></w:r></w:p>
                        <w:p><w:r><w:t>Grouped block line two</w:t></w:r></w:p>
                      </w:sdtContent>
                    </w:sdt>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:alias w:val="Locked inline group"/>
                          <w:lock w:val="sdtLocked"/>
                          <w:id w:val="902"/>
                          <w15:color {{colorValueAttribute}}="C00000"/>
                          <w15:appearance w15:val="boundingBox"/>
                          <w:tag w:val="InlineGroup"/>
                          <w:group/>
                        </w:sdtPr>
                        <w:sdtContent><w:r><w:t>Grouped inline text</w:t></w:r></w:sdtContent>
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
