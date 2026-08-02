using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class CheckBoxContentControlStateMetadataRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    [Fact]
    public void StateMetadata_PersistsThroughSourceXmlModelReopenAndSecondSave()
    {
        var expected = new ContentControlCheckBoxMetadata(
            CheckedState: new ContentControlCheckBoxStateMetadata("2612", "Segoe UI Symbol"),
            UncheckedState: new ContentControlCheckBoxStateMetadata("2610", "MS Gothic"));
        var sourceBytes = CreateSource(
            """
            <w14:checked w14:val="1"/>
            <w14:checkedState w14:val="2612" w14:font="Segoe UI Symbol"/>
            <w14:uncheckedState w14:val="2610" w14:font="MS Gothic"/>
            """,
            ContentControl.CheckedGlyph);

        var sourceCheckbox = GetCheckBoxElement(sourceBytes);
        AssertStateMetadata(sourceCheckbox, expected);

        var document = ReadDocx(sourceBytes);
        var run = document.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be(ContentControl.CheckedGlyph);
        run.Control!.Checked.Should().BeTrue();
        run.Control.CheckBoxMetadata.Should().Be(expected);

        var firstBytes = WriteDocx(document);
        var firstCheckbox = GetCheckBoxElement(firstBytes);
        firstCheckbox.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "checked", "checkedState", "uncheckedState");
        AssertStateMetadata(firstCheckbox, expected);
        firstCheckbox.Element(W14 + "checked")!.Attribute(W14 + "val")!.Value.Should().Be("1");
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        var reopenedRun = reopened.Paragraphs.Single().Runs.Single();
        reopenedRun.Text.Should().Be(ContentControl.CheckedGlyph);
        reopenedRun.Control!.Checked.Should().BeTrue();
        reopenedRun.Control.CheckBoxMetadata.Should().Be(expected);

        var secondBytes = WriteDocx(reopened);
        GetCheckBoxElement(secondBytes).ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstCheckbox.ToString(SaveOptions.DisableFormatting));
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    [Fact]
    public void AbsentStateMetadata_RemainsAbsentWithoutChangingUncheckedSemantics()
    {
        var sourceBytes = CreateSource(
            """
            <w14:checked w14:val="0"/>
            """,
            ContentControl.UncheckedGlyph);
        var sourceCheckbox = GetCheckBoxElement(sourceBytes);
        sourceCheckbox.Element(W14 + "checkedState").Should().BeNull();
        sourceCheckbox.Element(W14 + "uncheckedState").Should().BeNull();

        var document = ReadDocx(sourceBytes);
        var run = document.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be(ContentControl.UncheckedGlyph);
        run.Control!.Checked.Should().BeFalse();
        run.Control.CheckBoxMetadata.Should().BeNull();

        var firstBytes = WriteDocx(document);
        var firstCheckbox = GetCheckBoxElement(firstBytes);
        firstCheckbox.Elements().Should().ContainSingle()
            .Which.Name.Should().Be(W14 + "checked");
        firstCheckbox.Element(W14 + "checked")!.Attribute(W14 + "val")!.Value.Should().Be("0");
        SchemaErrors(firstBytes).Should().BeEmpty();

        var reopened = ReadDocx(firstBytes);
        var reopenedControl = reopened.Paragraphs.Single().Runs.Single().Control!;
        reopenedControl.Checked.Should().BeFalse();
        reopenedControl.CheckBoxMetadata.Should().BeNull();

        var secondBytes = WriteDocx(reopened);
        GetCheckBoxElement(secondBytes).ToString(SaveOptions.DisableFormatting)
            .Should().Be(firstCheckbox.ToString(SaveOptions.DisableFormatting));
        SchemaErrors(secondBytes).Should().BeEmpty();
    }

    private static void AssertStateMetadata(
        XElement checkbox,
        ContentControlCheckBoxMetadata expected)
    {
        AssertState(checkbox.Element(W14 + "checkedState"), expected.CheckedState!);
        AssertState(checkbox.Element(W14 + "uncheckedState"), expected.UncheckedState!);
    }

    private static void AssertState(XElement? state, ContentControlCheckBoxStateMetadata expected)
    {
        state.Should().NotBeNull();
        state!.Attribute(W14 + "val")!.Value.Should().Be(expected.GlyphCodePoint);
        state.Attribute(W14 + "font")!.Value.Should().Be(expected.Font);
        state.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Should().HaveCount(2);
    }

    private static byte[] CreateSource(string checkboxChildren, string glyph)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                $$"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
                  <w:body>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr>
                          <w:tag w:val="Approval"/>
                          <w14:checkbox>{{checkboxChildren}}</w14:checkbox>
                        </w:sdtPr>
                        <w:sdtContent><w:r><w:t>{{glyph}}</w:t></w:r></w:sdtContent>
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

    private static XElement GetCheckBoxElement(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W14 + "checkbox").Single();
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
