using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r569: an .ods cell carries its number in <c>office:value</c>, and
/// <see cref="OdsFileAdapter"/> turns it into a <see cref="NumberValue"/> directly. That is a
/// THIRD door into a cell value that bypasses the formula evaluator, after the external-link cache
/// r562 fixed and the typed/paste doors r563 censused -- and r562's correction to r552 said an
/// invariant asserted about the model has to be defended at EVERY writer, not the one being read.
/// This is that rule applied to the writer nobody had checked.
///
/// <para><c>office:value="1e400"</c> parses successfully to Infinity, and the same is true of the
/// text-content fallback the reader uses when the attribute is absent, so both branches are
/// covered. Returning blank is what the reader already does for a value it cannot read.</para>
/// </summary>
public sealed class R569_OdsCellValuesAreFiniteTests
{
    private static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    /// A hand-written ODS package, so the hostile shape is one a third-party writer could produce.
    /// FreeX's own Save would never emit it.
    private static Stream BuildOds(XElement firstCell)
    {
        var content = new XElement(
            OfficeNs + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OfficeNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", TableNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", TextNs.NamespaceName),
            new XElement(
                OfficeNs + "body",
                new XElement(
                    OfficeNs + "spreadsheet",
                    new XElement(
                        TableNs + "table",
                        new XAttribute(TableNs + "name", "Sheet1"),
                        new XElement(
                            TableNs + "table-row",
                            firstCell,
                            new XElement(
                                TableNs + "table-cell",
                                new XAttribute(OfficeNs + "value-type", "float"),
                                new XAttribute(OfficeNs + "value", "7"),
                                new XElement(TextNs + "p", "7")))))));

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            new XDocument(content).Save(entryStream);
        }

        stream.Position = 0;
        return stream;
    }

    private static XElement ValueCell(string value) =>
        new(
            TableNs + "table-cell",
            new XAttribute(OfficeNs + "value-type", "float"),
            new XAttribute(OfficeNs + "value", value),
            new XElement(TextNs + "p", value));

    /// The reader's second branch: no office:value attribute, so it falls back to the cell's text.
    private static XElement TextOnlyCell(string text) =>
        new(
            TableNs + "table-cell",
            new XAttribute(OfficeNs + "value-type", "float"),
            new XElement(TextNs + "p", text));

    private static ScalarValue LoadFirstCell(XElement cell)
    {
        using var package = BuildOds(cell);
        var workbook = new OdsFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);
        return sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value ?? BlankValue.Instance;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_cell_value_that_is_not_finite_does_not_enter_the_model(string hostile)
    {
        var value = LoadFirstCell(ValueCell(hostile));

        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cell became " + value);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void The_text_fallback_branch_is_guarded_too(string hostile)
    {
        // Two parses feed the same NumberValue construction; guarding one would leave the other.
        var value = LoadFirstCell(TextOnlyCell(hostile));

        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("the cell became " + value);
    }

    [Fact]
    public void An_unusable_cell_does_not_discard_its_neighbour()
    {
        // Granularity: the rest of the row must still load, so a fix that abandoned the row would
        // fail here rather than pass quietly.
        using var package = BuildOds(ValueCell("1e400"));
        var sheet = new OdsFileAdapter().Load(package).GetSheetAt(0);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value
            .Should().BeOfType<NumberValue>().Which.Value.Should().Be(7);
    }

    [Fact]
    public void Ordinary_cell_values_are_still_read()
    {
        // Non-vacuity, and it proves the fixture actually reaches the reader -- the check r566
        // learned to make after a fixture that loaded nothing made every hostile case "pass".
        LoadFirstCell(ValueCell("2.5")).Should().BeOfType<NumberValue>().Which.Value.Should().Be(2.5);
        LoadFirstCell(TextOnlyCell("-17")).Should().BeOfType<NumberValue>().Which.Value.Should().Be(-17);
    }
}
