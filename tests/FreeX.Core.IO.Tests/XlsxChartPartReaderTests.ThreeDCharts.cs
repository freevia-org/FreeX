using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_Reads3DColumnChartAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("bar3DChart"));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.ThreeDColumn);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_Reads3DBarChartAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("bar3DChart", """<c:barDir val="bar"/>"""));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.ThreeDBar);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_Reads3DPieChartAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("pie3DChart"));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.ThreeDPie);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_Reads3DAreaChartAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("area3DChart"));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.ThreeDArea);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_Reads3DLineChartAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("line3DChart"));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.ThreeDLine);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }
}
