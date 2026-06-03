using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
    [Fact]
    public void Save_WritesNativeLikeBoxAndWhiskerStatisticsTitlesAndAxes()
    {
        var saved = SaveWorkbookWithChart(ChartType.BoxAndWhisker, endCol: 3);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var chartData = chartXml.Root!.Element(ChartExNs + "chartData")!;
        var data = chartData.Elements(ChartExNs + "data").ToList();
        data.Should().HaveCount(2);
        data[0].Elements(ChartExNs + "strDim").Should().ContainSingle()
            .Which.Element(ChartExNs + "f")!.Value.Should().Contain("$A$2:$A$4");
        data[0].Elements(ChartExNs + "numDim").Should().ContainSingle()
            .Which.Should().Match<XElement>(element =>
                element.Element(ChartExNs + "f")!.Value.Contains("$B$2:$B$4", StringComparison.Ordinal) &&
                element.Element(ChartExNs + "nf")!.Value.Contains("$B$1", StringComparison.Ordinal));
        data[1].Elements(ChartExNs + "strDim").Should().ContainSingle()
            .Which.Element(ChartExNs + "f")!.Value.Should().Contain("$A$2:$A$4");
        data[1].Elements(ChartExNs + "numDim").Should().ContainSingle()
            .Which.Should().Match<XElement>(element =>
                element.Element(ChartExNs + "f")!.Value.Contains("$C$2:$C$4", StringComparison.Ordinal) &&
                element.Element(ChartExNs + "nf")!.Value.Contains("$C$1", StringComparison.Ordinal));

        var plotArea = chartXml.Root
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!;
        var series = plotArea
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .ToList();
        series.Should().HaveCount(2);
        series.Select(element => element.Attribute("layoutId")!.Value).Should().Equal("boxWhisker", "boxWhisker");
        var uniqueIds = series.Select(element => element.Attribute("uniqueId")?.Value).ToList();
        uniqueIds.Should().NotContainNulls().And.OnlyHaveUniqueItems();
        foreach (var uniqueId in uniqueIds)
        {
            uniqueId.Should().HaveLength(38);
            uniqueId.Should().StartWith("{").And.EndWith("}");
        }

        AssertBoxAndWhiskerSeries(series[0], dataId: "0", headerReference: "$B$1", headerText: "Amount");
        AssertBoxAndWhiskerSeries(series[1], dataId: "1", headerReference: "$C$1", headerText: "Target");

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
    public void Save_WritesBoxAndWhiskerAllNumericColumnsAsValueSeriesWhenNoCategoryColumn()
    {
        var saved = SaveBoxAndWhiskerAllNumericColumnsWorkbook();

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var data = chartXml.Root!
            .Element(ChartExNs + "chartData")!
            .Elements(ChartExNs + "data")
            .ToList();

        data.Should().HaveCount(3);
        data.Should().OnlyContain(element => !element.Elements(ChartExNs + "strDim").Any());
        data[0].Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Contain("$A$2:$A$4");
        data[1].Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Contain("$B$2:$B$4");
        data[2].Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Contain("$C$2:$C$4");

        var series = chartXml.Root
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .ToList();

        series.Should().HaveCount(3);
        AssertBoxAndWhiskerSeries(series[0], dataId: "0", headerReference: "$A$1", headerText: "Alpha");
        AssertBoxAndWhiskerSeries(series[1], dataId: "1", headerReference: "$B$1", headerText: "Beta");
        AssertBoxAndWhiskerSeries(series[2], dataId: "2", headerReference: "$C$1", headerText: "Gamma");
    }

}
