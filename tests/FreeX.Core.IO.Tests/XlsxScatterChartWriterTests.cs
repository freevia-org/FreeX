using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxScatterChartWriterTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void Save_DefaultScatterSuppressesConnectorLines()
    {
        using var saved = Save(CreateScatterWorkbook());
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);

        var scatterChart = LoadScatterChart(archive);
        scatterChart.Element(ChartNs + "scatterStyle")!
            .Attribute("val")!
            .Value.Should().Be("lineMarker");

        var series = scatterChart.Elements(ChartNs + "ser").ToList();
        series.Should().HaveCount(2);
        foreach (var item in series)
        {
            var line = item.Element(ChartNs + "spPr")!.Element(DrawingNs + "ln")!;
            line.Element(DrawingNs + "noFill").Should().NotBeNull();
            line.Element(DrawingNs + "solidFill").Should().BeNull();
        }
    }

    [Fact]
    public void Save_ScatterKeepsConnectorLinesWhenSeriesRequestsLineOrSmoothing()
    {
        using var saved = Save(CreateScatterWorkbook(
        [
            new ChartSeriesFormat(0, StrokeColor: new CellColor(20, 110, 180)),
            new ChartSeriesFormat(1, Smooth: true)
        ]));
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);

        var series = LoadScatterChart(archive).Elements(ChartNs + "ser").ToList();
        series.Should().HaveCount(2);

        var explicitLine = series[0].Element(ChartNs + "spPr")!.Element(DrawingNs + "ln")!;
        explicitLine.Element(DrawingNs + "noFill").Should().BeNull();
        explicitLine.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "srgbClr")!
            .Attribute("val")!
            .Value.Should().Be("146EB4");

        series[1].Element(ChartNs + "spPr").Should().BeNull();
        series[1].Element(ChartNs + "smooth")!
            .Attribute("val")!
            .Value.Should().Be("1");
    }

    private static Workbook CreateScatterWorkbook(IReadOnlyList<ChartSeriesFormat>? seriesFormats = null)
    {
        var workbook = new Workbook("ScatterWriter");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Beta"));

        for (uint row = 2; row <= 5; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(offset));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 2));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(offset * 3));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            Title = "Scatter",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 3)),
            SeriesFormats = seriesFormats?.ToList() ?? []
        });
        return workbook;
    }

    private static MemoryStream Save(Workbook workbook)
    {
        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static XElement LoadScatterChart(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/charts/chart1.xml");
        entry.Should().NotBeNull();

        using var stream = entry!.Open();
        var chartXml = XDocument.Load(stream);
        return chartXml.Descendants(ChartNs + "scatterChart").Should().ContainSingle().Subject;
    }
}
