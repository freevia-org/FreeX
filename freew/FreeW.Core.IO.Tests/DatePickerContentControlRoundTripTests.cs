using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class DatePickerContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void DateMetadata_PersistsThroughXmlReopenAndSecondSave()
    {
        var expectedMetadata = new ContentControlDateMetadata(
            FullDate: "2026-06-19T00:00:00Z",
            Calendar: "gregorian",
            LanguageId: "en-US",
            StoreMappedDataAs: "dateTime");
        var document = ReadSource(
            """
            <w:p>
              <w:sdt>
                <w:sdtPr>
                  <w:tag w:val="Signed"/>
                  <w:date w:fullDate="2026-06-19T00:00:00Z">
                    <w:dateFormat w:val="yyyy-MM-dd"/>
                    <w:lid w:val="en-US"/>
                    <w:storeMappedDataAs w:val="dateTime"/>
                    <w:calendar w:val="gregorian"/>
                  </w:date>
                </w:sdtPr>
                <w:sdtContent><w:r><w:t>2026-06-19</w:t></w:r></w:sdtContent>
              </w:sdt>
            </w:p>
            """);

        var run = document.Paragraphs.Single().Runs.Single();
        run.Control!.DateMetadata.Should().Be(expectedMetadata);
        run.Text = "2026-06-20";

        var firstBytes = WriteDocx(document);
        var firstDate = GetDateElement(firstBytes);
        AssertDateMetadata(firstDate, expectedMetadata);
        firstDate.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "dateFormat", "lid", "storeMappedDataAs", "calendar");
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        var reopenedRun = reopened.Paragraphs.Single().Runs.Single();
        reopenedRun.Text.Should().Be("2026-06-20");
        reopenedRun.Control!.DateFormat.Should().Be("yyyy-MM-dd");
        reopenedRun.Control.DateMetadata.Should().Be(expectedMetadata);

        var secondBytes = WriteDocx(reopened);
        GetDateElement(secondBytes).ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstDate.ToString(SaveOptions.DisableFormatting));
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    [Fact]
    public void AbsentMetadata_UsesCanonicalFormatAndRemainsOmitted()
    {
        var document = ReadSource(
            """
            <w:p>
              <w:sdt>
                <w:sdtPr><w:date/></w:sdtPr>
                <w:sdtContent><w:r><w:t>6/19/2026</w:t></w:r></w:sdtContent>
              </w:sdt>
            </w:p>
            """);

        var control = document.Paragraphs.Single().Runs.Single().Control!;
        control.DateFormat.Should().Be(ContentControl.DefaultDateFormat);
        control.DateMetadata.Should().BeNull();

        var firstBytes = WriteDocx(document);
        var firstDate = GetDateElement(firstBytes);
        firstDate.Attributes().Should().BeEmpty();
        firstDate.Elements().Should().ContainSingle()
            .Which.Name.Should().Be(W + "dateFormat");
        firstDate.Element(W + "dateFormat")!.Attribute(W + "val")!.Value
            .Should().Be(ContentControl.DefaultDateFormat);
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        reopened.Paragraphs.Single().Runs.Single().Control!.DateMetadata.Should().BeNull();
        var secondDate = GetDateElement(WriteDocx(reopened));
        secondDate.ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstDate.ToString(SaveOptions.DisableFormatting));
    }

    private static void AssertDateMetadata(XElement date, ContentControlDateMetadata expected)
    {
        date.Attribute(W + "fullDate")!.Value.Should().Be(expected.FullDate);
        date.Element(W + "calendar")!.Attribute(W + "val")!.Value.Should().Be(expected.Calendar);
        date.Element(W + "lid")!.Attribute(W + "val")!.Value.Should().Be(expected.LanguageId);
        date.Element(W + "storeMappedDataAs")!.Attribute(W + "val")!.Value
            .Should().Be(expected.StoreMappedDataAs);
    }

    private static TextDocument ReadSource(string bodyXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{{bodyXml}}</w:body>
                </w:document>
                """);
        }

        stream.Position = 0;
        return DocxReader.Read(stream);
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

    private static XElement GetDateElement(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + "date").Single();
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
