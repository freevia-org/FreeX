using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>Round-trip coverage for the run character scale (w:w) and the table row grid skips (w:gridBefore/gridAfter).</summary>
public class CharacterScaleAndGridSkipRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument doc, out XDocument xml)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
            xml = XDocument.Load(entry);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument SingleRun(RunFormatting formatting)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("wide", formatting));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Theory]
    [InlineData(150)]
    [InlineData(66)]
    [InlineData(100)]
    public void CharacterScale_RoundTripsAndIsWrittenAsWw(int percent)
    {
        var read = RoundTrip(SingleRun(new RunFormatting { CharacterScalePercent = percent }), out var xml);

        read.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.CharacterScalePercent.Should().Be(percent);
        xml.Descendants(W + "rPr").First().Element(W + "w")!.Attribute(W + "val")!.Value
            .Should().Be(percent.ToString());
    }

    [Fact]
    public void CharacterScale_DefaultWritesNoWwElement()
    {
        var read = RoundTrip(SingleRun(new RunFormatting()), out var xml);

        read.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.CharacterScalePercent.Should().Be(0);
        xml.Descendants(W + "w").Where(e => e.Parent?.Name == W + "rPr").Should().BeEmpty();
    }

    [Fact]
    public void CharacterScale_IsWrittenBetweenSpacingAndKern()
    {
        RoundTrip(SingleRun(new RunFormatting { CharacterSpacingPt = 1, CharacterScalePercent = 120, KerningMinSizePt = 8 }), out var xml);

        var names = xml.Descendants(W + "rPr").First().Elements().Select(e => e.Name.LocalName).ToList();
        names.IndexOf("w").Should().BeGreaterThan(names.IndexOf("spacing"));
        names.IndexOf("w").Should().BeLessThan(names.IndexOf("kern"));
    }

    [Fact]
    public void GridBeforeAndAfter_RoundTrip_AndShiftTheCellProjection()
    {
        var table = new Table();
        table.ColumnWidthsPt.AddRange([40, 100, 60, 60]);
        var row = new TableRow { GridBefore = 1, GridAfter = 1 };
        row.Cells.Add(new TableCell());
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc, out var xml);

        var readRow = read.Blocks.OfType<Table>().Single().Rows.Single();
        readRow.GridBefore.Should().Be(1);
        readRow.GridAfter.Should().Be(1);
        TableGridProjection.ProjectRow(readRow).Select(p => p.StartColumn).Should().Equal(1, 2);
        TableGridProjection.TableWidth(read.Blocks.OfType<Table>().Single()).Should().Be(3);
        var trPr = xml.Descendants(W + "trPr").Single();
        trPr.Elements().Select(e => e.Name.LocalName).Should().Equal("gridBefore", "wBefore", "gridAfter", "wAfter");
        trPr.Element(W + "wBefore")!.Attribute(W + "w")!.Value.Should().Be("800");
        trPr.Element(W + "wAfter")!.Attribute(W + "w")!.Value.Should().Be("1200");
    }

    [Fact]
    public void Row_WithoutGridSkips_WritesNoGridElements()
    {
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc, out var xml);

        xml.Descendants(W + "gridBefore").Should().BeEmpty();
        xml.Descendants(W + "gridAfter").Should().BeEmpty();
        read.Blocks.OfType<Table>().Single().Rows.Single().GridBefore.Should().Be(0);
    }
}
