using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class PlainTextContentControlMultiLineRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void MultiLine_PreservesTriStateAndCanonicalizesTokensAcrossTwoSaves()
    {
        var sourceBytes = CreateSource();
        var sourceProperties = GetPropertiesByTag(sourceBytes);
        AssertSourceToken(sourceProperties, "TrueNumeric", "1");
        AssertSourceToken(sourceProperties, "TrueWord", "true");
        AssertSourceToken(sourceProperties, "FalseNumeric", "0");
        AssertSourceToken(sourceProperties, "FalseWord", "false");
        AssertSourceToken(sourceProperties, "Absent", null);
        sourceProperties["RichText"].Element(W + "richText").Should().NotBeNull();
        sourceProperties["RichText"].Element(W + "text").Should().BeNull();

        var document = ReadDocx(sourceBytes);
        AssertModel(document, "TrueNumeric", ContentControlKind.PlainText, true);
        AssertModel(document, "TrueWord", ContentControlKind.PlainText, true);
        AssertModel(document, "FalseNumeric", ContentControlKind.PlainText, false);
        AssertModel(document, "FalseWord", ContentControlKind.PlainText, false);
        AssertModel(document, "Absent", ContentControlKind.PlainText, null);
        AssertModel(document, "RichText", ContentControlKind.RichText, null);

        var firstBytes = WriteDocx(document);
        var firstProperties = GetPropertiesByTag(firstBytes);
        AssertCanonicalToken(firstProperties, "TrueNumeric", "1");
        AssertCanonicalToken(firstProperties, "TrueWord", "1");
        AssertCanonicalToken(firstProperties, "FalseNumeric", "0");
        AssertCanonicalToken(firstProperties, "FalseWord", "0");
        AssertCanonicalToken(firstProperties, "Absent", null);
        firstProperties["RichText"].Element(W + "richText").Should().NotBeNull();
        firstProperties["RichText"].Element(W + "text").Should().BeNull();
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        AssertModel(reopened, "TrueNumeric", ContentControlKind.PlainText, true);
        AssertModel(reopened, "TrueWord", ContentControlKind.PlainText, true);
        AssertModel(reopened, "FalseNumeric", ContentControlKind.PlainText, false);
        AssertModel(reopened, "FalseWord", ContentControlKind.PlainText, false);
        AssertModel(reopened, "Absent", ContentControlKind.PlainText, null);
        AssertModel(reopened, "RichText", ContentControlKind.RichText, null);

        var secondBytes = WriteDocx(reopened);
        var secondProperties = GetPropertiesByTag(secondBytes);
        secondProperties.Keys.Should().BeEquivalentTo(firstProperties.Keys);
        foreach (var tag in firstProperties.Keys)
        {
            secondProperties[tag].ToString(SaveOptions.DisableFormatting)
                .Should().Be(firstProperties[tag].ToString(SaveOptions.DisableFormatting));
        }
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    private static void AssertSourceToken(
        IReadOnlyDictionary<string, XElement> properties,
        string tag,
        string? token)
    {
        var text = properties[tag].Element(W + "text");
        text.Should().NotBeNull();
        text!.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
            .Should().HaveCount(token is null ? 0 : 1);
        text.Attribute(W + "multiLine")?.Value.Should().Be(token);
    }

    private static void AssertCanonicalToken(
        IReadOnlyDictionary<string, XElement> properties,
        string tag,
        string? token)
    {
        var text = properties[tag].Element(W + "text");
        text.Should().NotBeNull();
        var attributes = text!.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        if (token is null)
            attributes.Should().BeEmpty();
        else
        {
            attributes.Should().ContainSingle();
            attributes[0].Name.Should().Be(W + "multiLine");
            attributes[0].Value.Should().Be(token);
        }
        text.Elements().Should().BeEmpty();
    }

    private static void AssertModel(
        TextDocument document,
        string tag,
        ContentControlKind kind,
        bool? multiLine)
    {
        var control = document.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Control)
            .Single(control => control?.Tag == tag)!;
        control.Kind.Should().Be(kind);
        control.PlainTextMultiLine.Should().Be(multiLine);
    }

    private static byte[] CreateSource()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="TrueNumeric"/><w:text w:multiLine="1"/></w:sdtPr><w:sdtContent><w:r><w:t>True numeric</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="TrueWord"/><w:text w:multiLine="true"/></w:sdtPr><w:sdtContent><w:r><w:t>True word</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="FalseNumeric"/><w:text w:multiLine="0"/></w:sdtPr><w:sdtContent><w:r><w:t>False numeric</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="FalseWord"/><w:text w:multiLine="false"/></w:sdtPr><w:sdtContent><w:r><w:t>False word</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="Absent"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>Absent</w:t></w:r></w:sdtContent></w:sdt></w:p>
                    <w:p><w:sdt><w:sdtPr><w:tag w:val="RichText"/><w:richText/></w:sdtPr><w:sdtContent><w:r><w:t>Rich text</w:t></w:r></w:sdtContent></w:sdt></w:p>
                  </w:body>
                </w:document>
                """);
        }

        return stream.ToArray();
    }

    private static byte[] WriteDocx(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDocx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static Dictionary<string, XElement> GetPropertiesByTag(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry)
            .Descendants(W + "sdtPr")
            .ToDictionary(
                properties => properties.Element(W + "tag")!.Attribute(W + "val")!.Value,
                properties => properties);
    }

    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
