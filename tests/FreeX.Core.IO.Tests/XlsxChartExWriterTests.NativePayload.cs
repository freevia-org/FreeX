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
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledLegend(ChartType chartType)
    {
        var source = SaveWorkbookWithChart(chartType, configureChart: chart =>
        {
            chart.ShowLegend = true;
            chart.LegendPosition = ChartLegendPosition.Right;
        });
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        var chart = workbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowLegend = true;
        chart.LegendPosition = ChartLegendPosition.Bottom;
        chart.LegendOverlay = true;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadPackageXml(savedArchive.GetEntry("xl/charts/chart1.xml")!);
        var savedText = savedChartXml.ToString(SaveOptions.DisableFormatting);
        savedText.Should().Contain("original-source-chart");
        var legend = savedChartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "legend");
        legend.Should().NotBeNull();
        // chartEx legend position/overlay are attributes on <cx:legend>, not child elements.
        legend!.Attribute("pos")!.Value.Should().Be("b");
        legend.Attribute("overlay")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledTitle()
    {
        var source = SaveWorkbookWithChart(ChartType.Treemap);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        workbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject.Title = "Edited ChartEx Title";

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadPackageXml(savedArchive.GetEntry("xl/charts/chart1.xml")!);
        savedChartXml.ToString(SaveOptions.DisableFormatting)
            .Should().Contain("original-source-chart")
            .And.Contain("Edited ChartEx Title");
    }

    [Fact]
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledDataRange()
    {
        var source = SaveWorkbookWithChart(ChartType.Treemap);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("D"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadPackageXml(savedArchive.GetEntry("xl/charts/chart1.xml")!);
        var savedText = savedChartXml.ToString(SaveOptions.DisableFormatting);
        savedText.Should().Contain("original-source-chart");
        savedText.Should().Contain("$A$2:$A$5");
        savedText.Should().Contain("$B$2:$B$5");
        savedText.Should().NotContain("$B$2:$B$4");
    }

}
