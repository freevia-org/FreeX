using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
    [Fact]
    public void Save_TreatsSingleColumnHistogramRangeAsValues()
    {
        var workbook = new Workbook("SingleColumnHistogram");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Histogram,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Title = "Histogram"
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var data = chartXml.Descendants(ChartExNs + "data").Should().ContainSingle().Subject;
        data.Elements(ChartExNs + "strDim").Should().BeEmpty();
        data.Elements(ChartExNs + "numDim").Should().ContainSingle()
            .Which.Element(ChartExNs + "f")!.Value.Should().Contain("$A$2:$A$4");
        chartXml.Descendants(ChartExNs + "series").Should().ContainSingle();
    }


    [Fact]
    public void Save_WritesHistogramDefaultBinningButOmitsCustomBinningValuesForExcelOpenability()
    {
        var saved = SaveWorkbookWithChart(ChartType.Histogram, configureChart: chart =>
            chart.HistogramBinning = new HistogramBinningModel(
                HistogramBinningMode.BinWidth, BinWidth: 5, OverflowThreshold: 25));

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var binning = chartXml.Descendants(ChartExNs + "binning").Should().ContainSingle().Subject;
        binning.Attribute("intervalClosed")!.Value.Should().Be("r");
        chartXml.Descendants(ChartExNs + "binCount").Should().BeEmpty();
        chartXml.Descendants(ChartExNs + "binSize").Should().BeEmpty();
    }

    [Fact]
    public void Save_WritesParetoAggregationOwnerLineAndPercentageAxes()
    {
        var saved = SaveWorkbookWithChart(ChartType.Pareto);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var plotArea = chartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!;
        var regionSeries = plotArea
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .ToList();

        var columnSeries = regionSeries[0];
        columnSeries.Attribute("layoutId")!.Value.Should().Be("clusteredColumn");
        columnSeries.Elements(ChartExNs + "dataId").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("0");
        columnSeries.Elements(ChartExNs + "layoutPr").Should().ContainSingle()
            .Which.Elements(ChartExNs + "aggregation").Should().ContainSingle();
        columnSeries.Elements(ChartExNs + "axisId").Should().BeEmpty();

        var paretoLine = regionSeries[1];
        paretoLine.Attribute("layoutId")!.Value.Should().Be("paretoLine");
        paretoLine.Attribute("ownerIdx")!.Value.Should().Be("0");
        paretoLine.Elements(ChartExNs + "dataId").Should().BeEmpty();
        paretoLine.Elements(ChartExNs + "axisId").Should().BeEmpty();

        var axes = plotArea.Elements(ChartExNs + "axis").ToList();
        axes.Select(axis => axis.Attribute("id")!.Value).Should().Equal("0", "1", "2");
        axes[0].Elements(ChartExNs + "catScaling").Should().ContainSingle()
            .Which.Attribute("gapWidth")!.Value.Should().Be("2.19000006");
        axes[0].Elements(ChartExNs + "tickLabels").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "valScaling").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "majorGridlines").Should().ContainSingle();
        axes[1].Elements(ChartExNs + "tickLabels").Should().ContainSingle();
        axes[2].Elements(ChartExNs + "valScaling").Should().ContainSingle()
            .Which.Should().Match<XElement>(element =>
                element.Attribute("min")!.Value == "0" &&
                element.Attribute("max")!.Value == "1");
        axes[2].Elements(ChartExNs + "units").Should().ContainSingle()
            .Which.Attribute("unit")!.Value.Should().Be("percentage");
        axes[2].Elements(ChartExNs + "tickLabels").Should().ContainSingle();
    }


    [Fact]
    public void SaveLoad_HistogramBinningIsNotPersistedThroughChartExForExcelOpenability()
    {
        var saved = SaveWorkbookWithChart(ChartType.Histogram, configureChart: chart =>
            chart.HistogramBinning = new HistogramBinningModel(
                HistogramBinningMode.BinCount, BinCount: 5, OverflowThreshold: 25, UnderflowThreshold: 12));

        var loaded = new XlsxFileAdapter().Load(saved);
        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.HistogramBinning.Should().BeNull();
    }

}
