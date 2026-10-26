using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r492: a number style's decimal-places count is chosen by the FILE, so it has to be bounded.
///
/// <para>OdsStyleTable guarded it with `d > 0` - a lower bound only, the integer form of the shape
/// r486 swept for doubles - and then built <c>new string('0', d)</c>. A document declaring
/// <c>number:decimal-places="2000000000"</c> therefore asked for a two-billion-character string, four
/// gigabytes of UTF-16, thrown as an OutOfMemoryException while merely OPENING the file. No malice is
/// required for the milder version: any corrupt or generator-buggy value produces a nonsense format.</para>
///
/// <para>The bound is 30, the maximum number of decimal places Excel's number formats allow, and the
/// same figure FormatCellsNumberFormatPlanner already clamps the Format Cells dialog to. Aligning
/// with the sibling rather than inventing a limit.</para>
/// </summary>
public sealed class R492_OdsDecimalPlacesAreBoundedTests
{
    private static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace NumberNs = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";

    private static Stream BuildOdsWithDecimalPlaces(string decimalPlaces)
    {
        var content = new XElement(OfficeNs + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OfficeNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", TableNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", TextNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", StyleNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", NumberNs.NamespaceName),
            new XElement(OfficeNs + "automatic-styles",
                new XElement(NumberNs + "number-style",
                    new XAttribute(StyleNs + "name", "N100"),
                    new XElement(NumberNs + "number",
                        new XAttribute(NumberNs + "decimal-places", decimalPlaces))),
                new XElement(StyleNs + "style",
                    new XAttribute(StyleNs + "name", "ce1"),
                    new XAttribute(StyleNs + "family", "table-cell"),
                    new XAttribute(StyleNs + "data-style-name", "N100"))),
            new XElement(OfficeNs + "body",
                new XElement(OfficeNs + "spreadsheet",
                    new XElement(TableNs + "table",
                        new XAttribute(TableNs + "name", "Sheet1"),
                        new XElement(TableNs + "table-row",
                            new XElement(TableNs + "table-cell",
                                new XAttribute(TableNs + "style-name", "ce1"),
                                new XAttribute(OfficeNs + "value-type", "float"),
                                new XAttribute(OfficeNs + "value", "1.5"),
                                new XElement(TextNs + "p", "1.5")))))));

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entryStream = archive.CreateEntry("content.xml").Open();
            new XDocument(content).Save(entryStream);
        }

        stream.Position = 0;
        return stream;
    }

    [Theory]
    [InlineData("2000000000")]   // ~4 GB of UTF-16 before the bound
    [InlineData("100000")]
    [InlineData("2147483647")]   // int.MaxValue
    public void AnAbsurdDecimalPlacesCountDoesNotAllocateFromTheFile(string decimalPlaces)
    {
        using var package = BuildOdsWithDecimalPlaces(decimalPlaces);

        var stopwatch = Stopwatch.StartNew();
        var workbook = new OdsFileAdapter().Load(package);
        stopwatch.Stop();

        workbook.Sheets.Should().ContainSingle("the document is otherwise perfectly ordinary");

        // The bound is what keeps this fast; without it the load either dies or spends seconds
        // building a string no format could use.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "the decimal-places count must be bounded before it sizes an allocation");
    }

    [Fact]
    public void AnOrdinaryDecimalPlacesCountIsStillHonoured()
    {
        // Narrowness: the bound must not disturb a normal file.
        using var package = BuildOdsWithDecimalPlaces("4");

        var workbook = new OdsFileAdapter().Load(package);

        var cell = workbook.Sheets.Single().GetCell(1, 1);
        var format = cell is null ? null : workbook.GetStyle(cell.StyleId).NumberFormat;
        format.Should().Be("0.0000", "four decimal places is an ordinary, supported request");
    }

    [Fact]
    public void TheBoundMatchesExcelsMaximumRatherThanTruncatingToNothing()
    {
        // A file asking for more than Excel supports keeps as much as the format allows, rather
        // than silently losing every decimal.
        using var package = BuildOdsWithDecimalPlaces("500");

        var workbook = new OdsFileAdapter().Load(package);

        var cell = workbook.Sheets.Single().GetCell(1, 1);
        var format = cell is null ? null : workbook.GetStyle(cell.StyleId).NumberFormat;
        format.Should().Be("0." + new string('0', 30), "Excel's number formats allow at most 30 places");
    }
}
