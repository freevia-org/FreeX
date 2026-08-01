using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class CitationContentControlRoundTripTests
{
    private const string StoreItemId = "{C1111111-D222-E333-F444-A55555555555}";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    [Fact]
    public void WordCitationControls_PreserveInlineAndBlockOwnershipMetadataAndCanonicalPackageXml()
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
            "canonical Citation SDT XML must remain stable after reopening and saving again");
        AssertOffice2013Valid(secondSave);
    }

    [Fact]
    public void ProgrammaticCitationField_UsesCanonicalCitationSdtAndReopensWithExplicitKind()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" CITATION Ada1843 \\l 1033 ", "(Lovelace, 1843)") }
        });

        var saved = Write(document);
        var body = ReadDocumentXml(saved).Root!.Element(W + "body")!;
        var properties = body.Elements(W + "p").Single()
            .Elements(W + "sdt").Single().Element(W + "sdtPr")!;
        AssertCitationKind(properties);
        properties.Elements(W + "id").Should().ContainSingle();
        properties.Element(W + "id")!.Attribute(W + "val").Should().NotBeNull();
        properties.Attribute(W + "id").Should().BeNull(
            "w:id is a canonical sdtPr child, never an attribute on sdtPr");
        AssertOffice2013Valid(saved);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        var run = reopened.Paragraphs.Single().Runs.Single();
        run.ComplexField!.Instruction.Should().Be(" CITATION Ada1843 \\l 1033 ");
        run.Control!.Kind.Should().Be(ContentControlKind.Citation);
        run.Control.WordMetadata!.Id.Should().NotBeNullOrEmpty();
    }

    private static void AssertModel(TextDocument document)
    {
        document.Blocks.Should().HaveCount(2);

        var blockParagraph = document.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        blockParagraph.PlainText.Should().Be("Block citation content");
        blockParagraph.Runs.Should().OnlyContain(run => run.Control == null);
        blockParagraph.BlockContentControl.Should().NotBeNull();
        var block = blockParagraph.BlockContentControl!;
        block.Kind.Should().Be(BlockContentControlKind.Citation);
        block.Tag.Should().Be("BlockCitation");
        block.Alias.Should().Be("Locked block citation");
        block.LockMode.Should().Be(ContentControlLockMode.ControlAndContentLocked);
        block.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "1101",
            DataBinding: new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:citation",
                "xmlns:ns0='urn:freew:citation'"),
            PlaceholderDocPart: "DefaultPlaceholder_BlockCitation",
            ShowingPlaceholder: true,
            Temporary: true,
            Appearance: "tags",
            Color: "4472C4"));

        var inlineParagraph = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        inlineParagraph.BlockContentControl.Should().BeNull();
        var inlineRun = inlineParagraph.Runs.Should().ContainSingle().Subject;
        inlineRun.Text.Should().Be("(Lovelace, 1843)");
        inlineRun.ComplexField.Should().NotBeNull();
        inlineRun.ComplexField!.Instruction.Should().Be(" CITATION Ada1843 \\l 1033 ");

        inlineRun.Control.Should().NotBeNull();
        var inline = inlineRun.Control!;
        inline.Kind.Should().Be(ContentControlKind.Citation);
        inline.Tag.Should().Be("InlineCitation");
        inline.Alias.Should().Be("Locked inline citation");
        inline.LockMode.Should().Be(ContentControlLockMode.ControlLocked);
        inline.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "1102",
            Appearance: "boundingBox",
            Color: "C00000"));
    }

    private static void AssertSourcePackageXml(XDocument xml)
    {
        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertCitationKind(blockProperties);
        AssertCitationKind(inlineProperties);
        blockProperties.Element(W + "lock")!.Attribute(W + "val")!.Value
            .Should().Be("sdtContentLocked");
        inlineProperties.Element(W + "lock")!.Attribute(W + "val")!.Value
            .Should().Be("sdtLocked");
    }

    private static void AssertCanonicalPackageXml(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        var blockSdt = body.Elements(W + "sdt").Should().ContainSingle(
            "the body-level Citation must remain a body-level SDT wrapper").Subject;
        blockSdt.Element(W + "sdtContent")!.Elements(W + "p").Should().ContainSingle();
        body.Elements(W + "p").Should().ContainSingle(
            "the inline Citation must remain inside its paragraph");

        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertCitationKind(blockProperties);
        AssertCitationKind(inlineProperties);
        AssertCommonMetadata(
            blockProperties,
            alias: "Locked block citation",
            lockValue: "sdtContentLocked",
            id: "1101",
            tag: "BlockCitation",
            appearance: "tags",
            color: "4472C4");
        blockProperties.Element(W + "placeholder")!.Element(W + "docPart")!
            .Attribute(W + "val")!.Value.Should().Be("DefaultPlaceholder_BlockCitation");
        blockProperties.Element(W + "showingPlcHdr").Should().NotBeNull();
        blockProperties.Element(W + "temporary").Should().NotBeNull();
        var binding = blockProperties.Element(W + "dataBinding")!;
        binding.Attribute(W + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(W + "xpath")!.Value.Should().Be("/ns0:root/ns0:citation");
        binding.Attribute(W + "prefixMappings")!.Value.Should().Be("xmlns:ns0='urn:freew:citation'");

        AssertCommonMetadata(
            inlineProperties,
            alias: "Locked inline citation",
            lockValue: "sdtLocked",
            id: "1102",
            tag: "InlineCitation",
            appearance: "boundingBox",
            color: "C00000");

        var inlineSdt = body.Elements(W + "p").Single().Elements(W + "sdt").Single();
        var fieldElements = inlineSdt.Element(W + "sdtContent")!.Elements(W + "r").ToList();
        fieldElements.Should().HaveCount(5, "the Citation SDT must retain the complex field sequence");
        fieldElements.SelectMany(run => run.Elements(W + "fldChar"))
            .Select(element => element.Attribute(W + "fldCharType")!.Value)
            .Should().Equal("begin", "separate", "end");
        fieldElements.SelectMany(run => run.Elements(W + "instrText")).Single().Value
            .Should().Be(" CITATION Ada1843 \\l 1033 ");
    }

    private static void AssertCitationKind(XElement properties)
    {
        properties.Elements(W + "citation").Should().ContainSingle();
        properties.Element(W + "citation")!.IsEmpty.Should().BeTrue();
        properties.Elements(W + "text").Should().BeEmpty();
        properties.Elements(W + "richText").Should().BeEmpty();
        properties.Elements(W + "group").Should().BeEmpty();
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
                        <w:alias w:val="Locked block citation"/>
                        <w:lock w:val="sdtContentLocked"/>
                        <w:placeholder><w:docPart w:val="DefaultPlaceholder_BlockCitation"/></w:placeholder>
                        <w:showingPlcHdr/>
                        <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:citation'" w:xpath="/ns0:root/ns0:citation" w:storeItemID="{{StoreItemId}}"/>
                        <w:temporary/>
                        <w:id w:val="1101"/>
                        <w15:color w:val="4472C4"/>
                        <w15:appearance w15:val="tags"/>
                        <w:tag w:val="BlockCitation"/>
                        <w:citation/>
                      </w:sdtPr>
                      <w:sdtContent>
                        <w:p><w:r><w:t>Block citation content</w:t></w:r></w:p>
                      </w:sdtContent>
                    </w:sdt>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:alias w:val="Locked inline citation"/>
                          <w:lock w:val="sdtLocked"/>
                          <w:id w:val="1102"/>
                          <w15:color w:val="C00000"/>
                          <w15:appearance w15:val="boundingBox"/>
                          <w:tag w:val="InlineCitation"/>
                          <w:citation/>
                        </w:sdtPr>
                        <w:sdtContent>
                          <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                          <w:r><w:instrText xml:space="preserve"> CITATION Ada1843 \l 1033 </w:instrText></w:r>
                          <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                          <w:r><w:t>(Lovelace, 1843)</w:t></w:r>
                          <w:r><w:fldChar w:fldCharType="end"/></w:r>
                        </w:sdtContent>
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
