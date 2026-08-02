using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class ListContentControlLastValueRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData("dropDownList", ContentControlKind.DropDownList, "G")]
    [InlineData("comboBox", ContentControlKind.ComboBox, "")]
    public void LastValue_PersistsThroughXmlReopenAndSecondSave(
        string elementName,
        ContentControlKind expectedKind,
        string expectedLastValue)
    {
        var sourceBytes = CreateSource(elementName, $" w:lastValue=\"{expectedLastValue}\"");
        GetListElement(sourceBytes, elementName).Attribute(W + "lastValue")
            .Should().NotBeNull().And.Subject.Value.Should().Be(expectedLastValue);

        var document = ReadDocx(sourceBytes);
        var control = document.Paragraphs.Single().Runs.Single().Control!;
        control.Kind.Should().Be(expectedKind);
        control.ListLastValue.Should().Be(expectedLastValue);

        var firstBytes = WriteDocx(document);
        var firstList = GetListElement(firstBytes, elementName);
        AssertCanonicalListElement(firstList, elementName, expectedLastValue);
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        reopened.Paragraphs.Single().Runs.Single().Control!.ListLastValue
            .Should().Be(expectedLastValue);

        var secondBytes = WriteDocx(reopened);
        GetListElement(secondBytes, elementName).ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstList.ToString(SaveOptions.DisableFormatting));
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    [Theory]
    [InlineData("dropDownList")]
    [InlineData("comboBox")]
    public void AbsentLastValue_RemainsAbsentAcrossReopenAndSecondSave(string elementName)
    {
        var sourceBytes = CreateSource(elementName, string.Empty);
        GetListElement(sourceBytes, elementName).Attribute(W + "lastValue").Should().BeNull();

        var document = ReadDocx(sourceBytes);
        document.Paragraphs.Single().Runs.Single().Control!.ListLastValue.Should().BeNull();

        var firstBytes = WriteDocx(document);
        var firstList = GetListElement(firstBytes, elementName);
        AssertCanonicalListElement(firstList, elementName, null);
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        reopened.Paragraphs.Single().Runs.Single().Control!.ListLastValue.Should().BeNull();

        var secondBytes = WriteDocx(reopened);
        GetListElement(secondBytes, elementName).ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstList.ToString(SaveOptions.DisableFormatting));
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    private static byte[] CreateSource(string elementName, string lastValueAttribute)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:{{elementName}}{{lastValueAttribute}}>
                            <w:listItem w:displayText="Red" w:value="R"/>
                            <w:listItem w:displayText="Green" w:value="G"/>
                          </w:{{elementName}}>
                        </w:sdtPr>
                        <w:sdtContent><w:r><w:t>Green</w:t></w:r></w:sdtContent>
                      </w:sdt>
                    </w:p>
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

    private static XElement GetListElement(byte[] bytes, string elementName)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + elementName).Single();
    }

    private static void AssertCanonicalListElement(
        XElement list,
        string elementName,
        string? lastValue)
    {
        list.Name.Should().Be(W + elementName);
        var attributes = list.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        if (lastValue is null)
            attributes.Should().BeEmpty();
        else
        {
            attributes.Should().ContainSingle();
            attributes[0].Name.Should().Be(W + "lastValue");
            attributes[0].Value.Should().Be(lastValue);
        }

        var items = list.Elements().ToArray();
        items.Select(item => item.Name).Should().OnlyContain(name => name == W + "listItem");
        items.Select(item => (
                DisplayText: item.Attribute(W + "displayText")?.Value,
                Value: item.Attribute(W + "value")?.Value))
            .Should().Equal(("Red", "R"), ("Green", "G"));
        items.SelectMany(item => item.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            .Should().HaveCount(4);
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
