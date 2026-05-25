using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxChartExWriterTests
{
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    [Theory]
    [InlineData(ChartType.Treemap, "treemapChart")]
    [InlineData(ChartType.Sunburst, "sunburstChart")]
    [InlineData(ChartType.Histogram, "histogramChart")]
    [InlineData(ChartType.Pareto, "histogramChart")]
    [InlineData(ChartType.BoxAndWhisker, "boxWhiskerChart")]
    [InlineData(ChartType.Waterfall, "waterfallChart")]
    [InlineData(ChartType.Funnel, "funnelChart")]
    public void Save_WritesChartExPartForRenderableModernCharts(ChartType chartType, string expectedFamilyElement)
    {
        var saved = SaveWorkbookWithChart(chartType);

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!.Name.Should().Be(ChartExNs + "chartSpace");
            chartXml.Descendants(ChartExNs + expectedFamilyElement).Should().ContainSingle();
            chartXml.Descendants().Any(element => element.Name.LocalName == "ser").Should().BeTrue();
            chartXml.Descendants().Any(element => element.Name.LocalName == "f" && element.Value.Contains("$B$1", StringComparison.Ordinal)).Should().BeTrue();
            chartXml.Descendants().Any(element => element.Name.LocalName == "f" && element.Value.Contains("$A$2:$A$4", StringComparison.Ordinal)).Should().BeTrue();
            chartXml.Descendants().Any(element => element.Name.LocalName == "f" && element.Value.Contains("$B$2:$B$4", StringComparison.Ordinal)).Should().BeTrue();

            if (chartType == ChartType.Pareto)
                chartXml.Descendants(ChartExNs + "paretoLine").Should().ContainSingle();
            else
                chartXml.Descendants(ChartExNs + "paretoLine").Should().BeEmpty();

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            var chartContentTypeOverrides = contentTypesXml.Root!
                .Elements(ContentTypesNs + "Override")
                .Where(element =>
                    string.Equals(element.Attribute("PartName")?.Value, "/xl/charts/chart1.xml", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("ContentType")?.Value, ChartExContentType, StringComparison.Ordinal))
                .ToList();
            chartContentTypeOverrides.Should().ContainSingle();

            var drawingRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!);
            drawingRelsXml.ToString().Should().Contain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart");
            drawingRelsXml.ToString().Should().Contain("../charts/chart1.xml");
        }

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloadedChart.Type.Should().Be(chartType);
        reloadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loaded.GetSheetAt(0).Id, 1, 1),
            new CellAddress(loaded.GetSheetAt(0).Id, 4, 2)));
        reloadedChart.FirstRowIsHeader.Should().BeTrue();
        reloadedChart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void Save_DoesNotWriteMapChartUntilRenderable()
    {
        var saved = SaveWorkbookWithChart(ChartType.Map);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.Entries.Select(entry => entry.FullName).Should().NotContain(name => name.StartsWith("xl/charts/", StringComparison.Ordinal));

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        var chartContentTypeOverrides = contentTypesXml.Root!
            .Elements(ContentTypesNs + "Override")
            .Where(element =>
                element.Attribute("PartName")?.Value.StartsWith("/xl/charts/", StringComparison.Ordinal) == true ||
                element.Attribute("ContentType")?.Value == ChartExContentType)
            .ToList();
        chartContentTypeOverrides.Should().BeEmpty();
    }

    private static MemoryStream SaveWorkbookWithChart(ChartType chartType)
    {
        var workbook = new Workbook("ChartExWriterTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = chartType.ToString()
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
