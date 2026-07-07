using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
    [Fact]
    public void Save_WritesWaterfallDefaultConnectorVisibilityAndAxes()
    {
        // O38: the chartEx writer now emits connectorLines per ChartModel.ShowSeriesLines (matching
        // the WPF renderer's own `chart.ShowSeriesLines ? CreateSeriesLineConnectorSeries(...) : null`
        // gate) instead of always hardcoding "1", so this "connector lines on" case must ask for it
        // explicitly rather than relying on a bare ChartModel's default.
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
            chart.ShowSeriesLines = true);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadChartXml(archive);
        var plotArea = chartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!;
        var series = plotArea
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .Should()
            .ContainSingle()
            .Subject;

        var layoutPr = series.Elements(ChartExNs + "layoutPr").Should().ContainSingle().Subject;
        layoutPr.Elements(ChartExNs + "visibility").Should().ContainSingle()
            .Which.Attribute("connectorLines")!.Value.Should().Be("1");
        layoutPr.Elements(ChartExNs + "subtotals").Should().BeEmpty();

        var axes = plotArea.Elements(ChartExNs + "axis").ToList();
        axes.Select(axis => axis.Attribute("id")!.Value).Should().Equal("0", "1");
        axes[0].Elements(ChartExNs + "catScaling").Should().ContainSingle()
            .Which.Attribute("gapWidth")!.Value.Should().Be("2.19000006");
        axes[0].Elements(ChartExNs + "tickLabels").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "valScaling").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "majorGridlines").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "tickLabels").Should().ContainSingle();
    }

    [Fact]
    public void Save_WritesWaterfallConnectorVisibilityOffWhenShowSeriesLinesIsFalse()
    {
        // O38: a Waterfall chart with connector lines explicitly turned off must round-trip that
        // choice into the chartEx XML instead of always claiming connectorLines="1".
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
            chart.ShowSeriesLines = false);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadChartXml(archive);
        var series = chartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .Should()
            .ContainSingle()
            .Subject;

        var layoutPr = series.Elements(ChartExNs + "layoutPr").Should().ContainSingle().Subject;
        layoutPr.Elements(ChartExNs + "visibility").Should().ContainSingle()
            .Which.Attribute("connectorLines")!.Value.Should().Be("0");
    }

    [Fact]
    public void Save_WritesWaterfallSubtotalsLayoutPr()
    {
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
        {
            chart.WaterfallTotalPointIndices = [0, 2];
            chart.ShowSeriesLines = true;
        });

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadChartXml(archive);
        var layoutPr = chartXml.Descendants(ChartExNs + "layoutPr").Should().ContainSingle().Subject;
        layoutPr.Elements().Select(element => element.Name.LocalName).Should().Equal("visibility", "subtotals");
        layoutPr.Element(ChartExNs + "visibility")!
            .Attribute("connectorLines")!
            .Value
            .Should()
            .Be("1");
        var subtotals = layoutPr.Elements(ChartExNs + "subtotals").Should().ContainSingle().Subject;
        subtotals.Elements(ChartExNs + "idx").Select(idx => idx.Attribute("val")!.Value)
            .Should().Equal("0", "2");
    }


    [Fact]
    public void SaveLoad_WaterfallTotalPointIndicesRoundTripThroughChartEx()
    {
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
            chart.WaterfallTotalPointIndices = [0, 2]);

        var loaded = new XlsxFileAdapter().Load(saved);
        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.WaterfallTotalPointIndices.Should().Equal(0, 2);
    }

}
