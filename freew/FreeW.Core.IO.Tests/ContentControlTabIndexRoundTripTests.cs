using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class ContentControlTabIndexRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void TabIndex_PersistsExactTokensForBlockAndInlineOwnersThroughSecondSave()
    {
        using var source = BuildPackage(blockTabIndex: "0007", inlineTabIndex: "-0002");
        var sourceBytes = source.ToArray();
        AssertTabIndexes(ReadDocumentXml(sourceBytes), "0007", "-0002");
        AssertOnlyLegacyTabIndexSchemaDiagnostic(sourceBytes);

        var imported = Read(sourceBytes);
        AssertModel(imported, "0007", "-0002");

        var firstSave = Write(imported);
        var firstXml = ReadDocumentXml(firstSave);
        AssertTabIndexes(firstXml, "0007", "-0002");
        AssertOnlyLegacyTabIndexSchemaDiagnostic(firstSave);

        var reopened = Read(firstSave);
        AssertModel(reopened, "0007", "-0002");

        var secondSave = Write(reopened);
        var secondXml = ReadDocumentXml(secondSave);
        AssertTabIndexes(secondXml, "0007", "-0002");
        XNode.DeepEquals(firstXml, secondXml).Should().BeTrue(
            "canonical block and inline tabIndex XML must remain stable after reopening and saving again");
        AssertOnlyLegacyTabIndexSchemaDiagnostic(secondSave);
    }

    [Fact]
    public void AbsentTabIndex_RemainsAbsentForBlockAndInlineOwnersThroughSecondSave()
    {
        using var source = BuildPackage(blockTabIndex: null, inlineTabIndex: null);
        var sourceBytes = source.ToArray();
        AssertTabIndexes(ReadDocumentXml(sourceBytes), null, null);
        SchemaErrors(sourceBytes).Should().BeEmpty();

        var imported = Read(sourceBytes);
        AssertModel(imported, null, null);

        var firstSave = Write(imported);
        var firstXml = ReadDocumentXml(firstSave);
        AssertTabIndexes(firstXml, null, null);
        SchemaErrors(firstSave).Should().BeEmpty();

        var reopened = Read(firstSave);
        AssertModel(reopened, null, null);

        var secondSave = Write(reopened);
        var secondXml = ReadDocumentXml(secondSave);
        AssertTabIndexes(secondXml, null, null);
        XNode.DeepEquals(firstXml, secondXml).Should().BeTrue(
            "an absent block and inline tabIndex must not be invented on the second save");
        SchemaErrors(secondSave).Should().BeEmpty();
    }

    private static void AssertModel(
        TextDocument document,
        string? expectedBlockTabIndex,
        string? expectedInlineTabIndex)
    {
        document.Blocks.Should().HaveCount(2);

        var blockParagraph = document.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        blockParagraph.BlockContentControl.Should().NotBeNull();
        blockParagraph.BlockContentControl!.Kind.Should().Be(BlockContentControlKind.RichText);
        blockParagraph.BlockContentControl.WordMetadata?.TabIndex.Should().Be(expectedBlockTabIndex);

        var inlineParagraph = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        inlineParagraph.BlockContentControl.Should().BeNull();
        var inlineControl = inlineParagraph.Runs.Should().ContainSingle().Subject.Control;
        inlineControl.Should().NotBeNull();
        inlineControl!.Kind.Should().Be(ContentControlKind.RichText);
        inlineControl.WordMetadata?.TabIndex.Should().Be(expectedInlineTabIndex);
    }

    private static void AssertTabIndexes(
        XDocument xml,
        string? expectedBlockTabIndex,
        string? expectedInlineTabIndex)
    {
        var body = xml.Root!.Element(W + "body")!;
        var blockProperties = body.Elements(W + "sdt").Should().ContainSingle().Subject
            .Element(W + "sdtPr")!;
        var inlineProperties = body.Elements(W + "p").Should().ContainSingle().Subject
            .Elements(W + "sdt").Should().ContainSingle().Subject
            .Element(W + "sdtPr")!;

        AssertTabIndex(blockProperties, expectedBlockTabIndex);
        AssertTabIndex(inlineProperties, expectedInlineTabIndex);
        blockProperties.Elements(W + "richText").Should().ContainSingle();
        inlineProperties.Elements(W + "richText").Should().ContainSingle();
    }

    private static void AssertTabIndex(XElement properties, string? expected)
    {
        var tabIndexes = properties.Elements(W + "tabIndex").ToArray();
        if (expected is null)
        {
            tabIndexes.Should().BeEmpty();
            return;
        }

        var tabIndex = tabIndexes.Should().ContainSingle().Subject;
        tabIndex.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
            .Should().ContainSingle().Which.Name.Should().Be(W + "val");
        tabIndex.Attribute(W + "val")!.Value.Should().Be(expected);
    }

    private static MemoryStream BuildPackage(string? blockTabIndex, string? inlineTabIndex)
    {
        static string Element(string? value) => value is null
            ? string.Empty
            : $"<w:tabIndex w:val=\"{value}\"/>";

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
                      <w:sdtPr>{{Element(blockTabIndex)}}<w:richText/></w:sdtPr>
                      <w:sdtContent><w:p><w:r><w:t>Block control</w:t></w:r></w:p></w:sdtContent>
                    </w:sdt>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>{{Element(inlineTabIndex)}}<w:richText/></w:sdtPr>
                        <w:sdtContent><w:r><w:t>Inline control</w:t></w:r></w:sdtContent>
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

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
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

    private static List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static void AssertOnlyLegacyTabIndexSchemaDiagnostic(byte[] package)
    {
        var errors = SchemaErrors(package);
        errors.Should().HaveCount(2,
            "Open XML SDK 3.1.1 omits legacy w:tabIndex from both sdtPr owner particles");
        errors.Should().OnlyContain(error =>
            error.Contains("invalid child element", StringComparison.Ordinal)
            && error.Contains("wordprocessingml/2006/main:tabIndex", StringComparison.Ordinal)
            && error.EndsWith("/w:sdtPr[1]", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("/w:body[1]/w:sdt[1]/", StringComparison.Ordinal));
        errors.Should().Contain(error => error.Contains("/w:body[1]/w:p[1]/w:sdt[1]/", StringComparison.Ordinal));
    }

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
