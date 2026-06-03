using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    public void SaveLoad_ChartExTitleRoundTripsForRenderableModernCharts(ChartType chartType)
    {
        var saved = SaveWorkbookWithChart(chartType);
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Title.Should().Be(chartType.ToString());

        var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resaved);
        resaved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resaved);

        reloaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject.Title.Should().Be(chartType.ToString());
    }

    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    public void SaveLoad_ChartExLegendRoundTripsForRenderableModernCharts(ChartType chartType)
    {
        var saved = SaveWorkbookWithChart(chartType, configureChart: chart =>
        {
            chart.ShowLegend = true;
            chart.LegendPosition = ChartLegendPosition.Bottom;
            chart.LegendOverlay = true;
        });

        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ShowLegend.Should().BeTrue();
        loadedChart.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        loadedChart.LegendOverlay.Should().BeTrue();
    }


    [Fact]
    public void SaveLoad_MultiSeriesChartExUnionsAllPrimarySeriesDataRanges()
    {
        var saved = SaveWorkbookWithChart(ChartType.Treemap, endCol: 3);

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!
                .Element(ChartExNs + "chartData")!
                .Elements(ChartExNs + "data")
                .Should()
                .HaveCount(2);
            chartXml.Root
                .Element(ChartExNs + "chart")!
                .Element(ChartExNs + "plotArea")!
                .Element(ChartExNs + "plotAreaRegion")!
                .Elements(ChartExNs + "series")
                .Where(element => !string.Equals(element.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase))
                .Should()
                .HaveCount(2);
        }

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loaded.GetSheetAt(0).Id, 1, 1),
            new CellAddress(loaded.GetSheetAt(0).Id, 4, 3)));
        reloadedChart.FirstRowIsHeader.Should().BeTrue();
        reloadedChart.FirstColIsCategories.Should().BeTrue();
    }
}
