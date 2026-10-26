using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R81-io-drawing-chart-name: the chart writer emits the ChartModel.Name onto the chart's
/// &lt;xdr:graphicFrame&gt;&lt;xdr:nvGraphicFramePr&gt;&lt;xdr:cNvPr name="..."/&gt;, but the reader
/// called <c>ReadNonVisualName(chartElement)</c> on the &lt;c:chart&gt;/&lt;cx:chart&gt; element --
/// a self-closing r:id reference that (per the OOXML schema) has no cNvPr descendant of its own -- so
/// the name always read back as null and a chart's Name was silently dropped on every open+save
/// round-trip through FreeX. The name lives on the ANCESTOR graphicFrame (the same element the reader
/// already uses for the chart's transform/anchor and, since R80, its Alt Text title/description).
/// </summary>
public sealed class R81_ChartNameRoundTripTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    private static Workbook BuildWorkbookWithNamedChart(string? name)
    {
        var workbook = new Workbook("ChartName");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(25));
        sheet.Charts.Add(new ChartModel
        {
            Name = name,
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Title = "Sales by quarter"
        });

        return workbook;
    }

    // Failing before the fix: the reader read the chart's name from the <c:chart> element (which has no
    // cNvPr) instead of the ancestor <xdr:graphicFrame>, so chart.Name always came back null even though
    // the writer had correctly emitted name="My Chart" on the graphicFrame's <xdr:cNvPr>.
    [Fact]
    public void XlsxAdapter_ChartName_SurvivesSaveAndReload()
    {
        var workbook = BuildWorkbookWithNamedChart("My Chart");

        using var package = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, package);
        package.Position = 0;

        // Confirm the writer actually emitted name="My Chart" onto the chart's
        // <xdr:graphicFrame><xdr:nvGraphicFramePr><xdr:cNvPr> -- not just that the round-trip happens
        // to work through some other mechanism (the <c:chart> element carries no name of its own).
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingEntry = archive.Entries.Should().ContainSingle(e => e.FullName == "xl/drawings/drawing1.xml").Subject;
            using var drawingStream = drawingEntry.Open();
            var drawingXml = XDocument.Load(drawingStream);
            var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame")
                .Descendants(SpreadsheetDrawingNs + "cNvPr")
                .Should().ContainSingle().Subject;

            cNvPr.Attribute("name")!.Value.Should().Be("My Chart",
                "the chart's Name must be written to the graphicFrame's cNvPr name attribute");
        }

        package.Position = 0;
        var reloaded = adapter.Load(package);
        var chart = reloaded.GetSheet("Data")!.Charts.Should().ContainSingle().Subject;

        chart.Name.Should().Be("My Chart",
            "a chart's Name must survive an open+save round-trip through FreeX (read from the graphicFrame's cNvPr, not the <c:chart> element)");
    }

    // No-regression sibling: a chart with no explicit Name still round-trips without throwing, and the
    // writer's fallback name ("Chart 1" -- chartIndex is 1-based, see XlsxWorksheetChartWriter) is what
    // the reader now round-trips back -- confirming the read path resolves against the graphicFrame's
    // cNvPr (which always carries at least the fallback name) rather than the name-less <c:chart>
    // element (which would have read back null).
    [Fact]
    public void XlsxAdapter_ChartWithoutExplicitName_RoundTripsFallbackName()
    {
        var workbook = BuildWorkbookWithNamedChart(name: null);

        using var package = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, package);
        package.Position = 0;

        var reloaded = adapter.Load(package);
        var chart = reloaded.GetSheet("Data")!.Charts.Should().ContainSingle().Subject;

        // The writer emits DrawingName(chart.Name, "Chart {chartIndex}") -> "Chart 1" for the first
        // chart (chartIndex starts at 1) when Name is null/blank, and the reader now resolves that same
        // graphicFrame cNvPr name instead of reading null off the name-less <c:chart> element.
        chart.Name.Should().Be("Chart 1",
            "a chart with no explicit name round-trips the writer's fallback graphicFrame cNvPr name");
        chart.Title.Should().Be("Sales by quarter", "unrelated chart metadata must be unaffected");
    }
}
