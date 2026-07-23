using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-app-accessibility-a11y-5-1: ChartModel had no Alt Text (title/description) field at all, so a
/// chart's Alt Text (set in real Excel via right-click &gt; View Alt Text, which writes
/// &lt;xdr:cNvPr descr="..." title="..."/&gt; on the chart's graphicFrame) was silently and
/// permanently dropped on every FreeX open+save round-trip -- the reader never captured it and the
/// writer never re-emitted it.
/// </summary>
public sealed class R80_ChartAltTextRoundTripTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    private static Workbook BuildWorkbookWithChart(string? altTextTitle, string? altTextDescription)
    {
        var workbook = new Workbook("ChartAltText");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(25));
        sheet.Charts.Add(new ChartModel
        {
            Name = "Chart 1",
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Title = "Sales by quarter",
            AltTextTitle = altTextTitle,
            AltTextDescription = altTextDescription
        });

        return workbook;
    }

    // Failing before the fix: ChartModel had no AltTextTitle/AltTextDescription fields at all, and
    // even after adding them, the writer never emitted a descr/title attribute on the chart's
    // <xdr:cNvPr> and the reader never read one back -- so a chart's real Alt Text set in Excel was
    // silently discarded on open+save round-trip through FreeX.
    [Fact]
    public void XlsxAdapter_ChartAltText_SurvivesSaveAndReload()
    {
        var workbook = BuildWorkbookWithChart(
            altTextTitle: "Quarterly sales chart",
            altTextDescription: "Bar chart showing sales figures for each quarter of the year.");

        using var package = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, package);
        package.Position = 0;

        // Confirm the writer actually emitted the descr/title attributes onto the chart's
        // <xdr:graphicFrame><xdr:nvGraphicFramePr><xdr:cNvPr> -- not just that the round-trip
        // happens to work through some other mechanism.
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingEntry = archive.Entries.Should().ContainSingle(e => e.FullName == "xl/drawings/drawing1.xml").Subject;
            using var drawingStream = drawingEntry.Open();
            var drawingXml = XDocument.Load(drawingStream);
            var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame")
                .Descendants(SpreadsheetDrawingNs + "cNvPr")
                .Should().ContainSingle().Subject;

            cNvPr.Attribute("title")?.Value.Should().Be("Quarterly sales chart",
                "the chart's Alt Text title must be written to the graphicFrame's cNvPr title attribute");
            cNvPr.Attribute("descr")?.Value.Should().Be(
                "Bar chart showing sales figures for each quarter of the year.",
                "the chart's Alt Text description must be written to the graphicFrame's cNvPr descr attribute");
        }

        package.Position = 0;
        var reloaded = adapter.Load(package);
        var chart = reloaded.GetSheet("Data")!.Charts.Should().ContainSingle().Subject;

        chart.AltTextTitle.Should().Be("Quarterly sales chart",
            "a chart's Alt Text title must survive an open+save round-trip through FreeX");
        chart.AltTextDescription.Should().Be(
            "Bar chart showing sales figures for each quarter of the year.",
            "a chart's Alt Text description must survive an open+save round-trip through FreeX");
    }

    // No-regression sibling: a chart that never had Alt Text set must keep round-tripping with null
    // Alt Text (and no stray descr/title attribute on the cNvPr), and unrelated chart metadata (Name,
    // Title) must be unaffected by the new fields.
    [Fact]
    public void XlsxAdapter_ChartWithoutAltText_RoundTripsWithNullAltTextAndUnaffectedMetadata()
    {
        var workbook = BuildWorkbookWithChart(altTextTitle: null, altTextDescription: null);

        using var package = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, package);
        package.Position = 0;

        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingEntry = archive.Entries.Should().ContainSingle(e => e.FullName == "xl/drawings/drawing1.xml").Subject;
            using var drawingStream = drawingEntry.Open();
            var drawingXml = XDocument.Load(drawingStream);
            var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame")
                .Descendants(SpreadsheetDrawingNs + "cNvPr")
                .Should().ContainSingle().Subject;

            cNvPr.Attribute("title").Should().BeNull("no Alt Text title was set on the chart");
            cNvPr.Attribute("descr").Should().BeNull("no Alt Text description was set on the chart");
        }

        package.Position = 0;
        var reloaded = adapter.Load(package);
        var chart = reloaded.GetSheet("Data")!.Charts.Should().ContainSingle().Subject;

        chart.AltTextTitle.Should().BeNull();
        chart.AltTextDescription.Should().BeNull();
        // chart.Name now round-trips too (fixed in R81-io-drawing-chart-name: the reader was reading the
        // name from the name-less <c:chart> element instead of the ancestor <xdr:graphicFrame>). Assert
        // both Name and Title survive so the Alt Text fields are shown not to disturb either.
        chart.Name.Should().Be("Chart 1", "the chart Name must round-trip and be unaffected by the Alt Text fields");
        chart.Title.Should().Be("Sales by quarter", "adding Alt Text fields must not disturb the existing chart Title round-trip");
    }
}
