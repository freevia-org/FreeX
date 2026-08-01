using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class BuildingBlockGalleryContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void WordBuildingBlockGalleries_PreserveInlineAndBlockOwnershipAndCanonicalPackageXml()
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
            "the canonical document XML must remain stable after reopening and saving again");
        AssertOffice2013Valid(secondSave);
    }

    private static void AssertModel(TextDocument document)
    {
        document.Blocks.Should().HaveCount(2);

        var blockParagraph = document.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        blockParagraph.PlainText.Should().Be("Choose a cover page");
        blockParagraph.Runs.Should().OnlyContain(run => run.Control == null);
        blockParagraph.BlockContentControl.Should().NotBeNull();
        var block = blockParagraph.BlockContentControl!;
        block.Kind.Should().Be(BlockContentControlKind.BuildingBlockGallery);
        block.Tag.Should().Be("CoverPageGallery");
        block.Alias.Should().Be("Cover page gallery");
        block.DocPartGallery.Should().Be("Cover Pages");
        block.DocPartCategory.Should().Be("Built-In");
        block.DocPartUnique.Should().BeTrue();
        block.LockMode.Should().Be(ContentControlLockMode.ControlLocked);
        block.WordMetadata.Should().Be(new ContentControlWordMetadata(Id: "801"));

        var inlineParagraph = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        inlineParagraph.BlockContentControl.Should().BeNull();
        var inlineRun = inlineParagraph.Runs.Should().ContainSingle().Subject;
        inlineRun.Text.Should().Be("Choose a quick part");
        inlineRun.Control.Should().NotBeNull();
        var inline = inlineRun.Control!;
        inline.Kind.Should().Be(ContentControlKind.BuildingBlockGallery);
        inline.Tag.Should().Be("QuickPartGallery");
        inline.Alias.Should().Be("Quick part gallery");
        inline.DocPartGallery.Should().Be("Quick Parts");
        inline.DocPartCategory.Should().BeNull();
        inline.DocPartUnique.Should().BeFalse("w:docPartUnique w:val=\"0\" is explicitly off");
        inline.WordMetadata.Should().Be(new ContentControlWordMetadata(Id: "802"));
    }

    private static void AssertSourcePackageXml(XDocument xml)
    {
        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertDocPart(blockProperties, "Cover Pages", "Built-In", expectedUnique: true);
        blockProperties.Element(W + "docPartObj")!.Element(W + "docPartUnique")!
            .Attribute(W + "val")!.Value.Should().Be("1");

        AssertDocPart(inlineProperties, "Quick Parts", expectedCategory: null, expectedUnique: true);
        inlineProperties.Element(W + "docPartObj")!.Element(W + "docPartUnique")!
            .Attribute(W + "val")!.Value.Should().Be("0");
    }

    private static void AssertCanonicalPackageXml(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        body.Elements(W + "sdt").Should().ContainSingle(
            "the body-level gallery must remain a body-level SDT wrapper");
        body.Elements(W + "p").Should().ContainSingle(
            "the inline gallery must remain inside its paragraph");

        var (blockProperties, inlineProperties) = GetProperties(xml);
        AssertDocPart(blockProperties, "Cover Pages", "Built-In", expectedUnique: true);
        blockProperties.Element(W + "docPartObj")!.Element(W + "docPartUnique")!
            .Attributes().Should().BeEmpty("true is serialized in canonical empty-element form");

        AssertDocPart(inlineProperties, "Quick Parts", expectedCategory: null, expectedUnique: false);
        inlineProperties.Element(W + "docPartObj")!.Element(W + "docPartUnique").Should().BeNull(
            "explicit false is canonicalized by omitting the optional on/off property");
        inlineProperties.Elements(W + "text").Should().BeEmpty();
        inlineProperties.Elements(W + "richText").Should().BeEmpty();
    }

    private static (XElement Block, XElement Inline) GetProperties(XDocument xml)
    {
        var body = xml.Root!.Element(W + "body")!;
        var block = body.Elements(W + "sdt").Should().ContainSingle().Subject;
        var inline = body.Elements(W + "p").Should().ContainSingle().Subject
            .Elements(W + "sdt").Should().ContainSingle().Subject;
        return (block.Element(W + "sdtPr")!, inline.Element(W + "sdtPr")!);
    }

    private static void AssertDocPart(
        XElement properties,
        string expectedGallery,
        string? expectedCategory,
        bool expectedUnique)
    {
        var docPart = properties.Elements(W + "docPartObj").Should().ContainSingle().Subject;
        properties.Elements(W + "docPartList").Should().BeEmpty(
            "building-block gallery objects must remain distinct from document-part lists");
        docPart.Element(W + "docPartGallery")!.Attribute(W + "val")!.Value
            .Should().Be(expectedGallery);
        docPart.Element(W + "docPartCategory")?.Attribute(W + "val")?.Value
            .Should().Be(expectedCategory);
        (docPart.Element(W + "docPartUnique") is not null).Should().Be(expectedUnique);
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
                <w:document xmlns:w="{{W}}">
                  <w:body>
                    <w:sdt>
                      <w:sdtPr>
                        <w:alias w:val="Cover page gallery"/>
                        <w:lock w:val="sdtLocked"/>
                        <w:id w:val="801"/>
                        <w:tag w:val="CoverPageGallery"/>
                        <w:docPartObj>
                          <w:docPartGallery w:val="Cover Pages"/>
                          <w:docPartCategory w:val="Built-In"/>
                          <w:docPartUnique w:val="1"/>
                        </w:docPartObj>
                      </w:sdtPr>
                      <w:sdtContent>
                        <w:p><w:r><w:t>Choose a cover page</w:t></w:r></w:p>
                      </w:sdtContent>
                    </w:sdt>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:alias w:val="Quick part gallery"/>
                          <w:id w:val="802"/>
                          <w:tag w:val="QuickPartGallery"/>
                          <w:docPartObj>
                            <w:docPartGallery w:val="Quick Parts"/>
                            <w:docPartUnique w:val="0"/>
                          </w:docPartObj>
                        </w:sdtPr>
                        <w:sdtContent><w:r><w:t>Choose a quick part</w:t></w:r></w:sdtContent>
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
