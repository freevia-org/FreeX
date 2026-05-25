using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxChartExWriterTests
{
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ClassicChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    [Theory]
    [InlineData(ChartType.Treemap, "treemap")]
    [InlineData(ChartType.Sunburst, "sunburst")]
    [InlineData(ChartType.Histogram, "clusteredColumn")]
    [InlineData(ChartType.Pareto, "clusteredColumn", true)]
    [InlineData(ChartType.BoxAndWhisker, "boxWhisker")]
    [InlineData(ChartType.Waterfall, "waterfall")]
    [InlineData(ChartType.Funnel, "funnel")]
    public void Save_WritesSchemaShapedChartExPartForRenderableModernCharts(
        ChartType chartType,
        string expectedLayoutId,
        bool expectParetoLine = false)
    {
        var saved = SaveWorkbookWithChart(chartType);

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            chartXml.Root!.Name.Should().Be(ChartExNs + "chartSpace");
            chartXml.Root.Elements().Select(element => element.Name).Take(2)
                .Should().Equal(ChartExNs + "chartData", ChartExNs + "chart");

            var classicChartSeriesDataElements = chartXml.Descendants()
                .Where(element =>
                    element.Name.LocalName is "ser" or "cat" or "val" &&
                    element.Name.NamespaceName == "http://schemas.openxmlformats.org/drawingml/2006/chart")
                .ToList();
            classicChartSeriesDataElements.Should().BeEmpty();

            var chartData = chartXml.Root.Element(ChartExNs + "chartData");
            chartData.Should().NotBeNull();
            var data = chartData!.Elements(ChartExNs + "data").Should().ContainSingle().Subject;
            data.Attribute("id")!.Value.Should().Be("data0");
            data.Elements(ChartExNs + "strDim").Should().ContainSingle()
                .Which.Should().Match<XElement>(element =>
                    element.Attribute("type")!.Value == "cat" &&
                    element.Element(ChartExNs + "f")!.Value.Contains("$A$2:$A$4", StringComparison.Ordinal));
            data.Elements(ChartExNs + "numDim").Should().ContainSingle()
                .Which.Should().Match<XElement>(element =>
                    element.Attribute("type")!.Value == "val" &&
                    element.Element(ChartExNs + "f")!.Value.Contains("$B$2:$B$4", StringComparison.Ordinal) &&
                    element.Element(ChartExNs + "nf")!.Value.Contains("$B$1", StringComparison.Ordinal));

            var plotAreaRegion = chartXml.Root
                .Element(ChartExNs + "chart")!
                .Element(ChartExNs + "plotArea")!
                .Element(ChartExNs + "plotAreaRegion");
            plotAreaRegion.Should().NotBeNull();

            var regionSeries = plotAreaRegion!.Elements(ChartExNs + "series").ToList();
            regionSeries.Should().HaveCount(expectParetoLine ? 2 : 1);
            var series = regionSeries[0];
            series.Attribute("layoutId")!.Value.Should().Be(expectedLayoutId);
            series.Element(ChartExNs + "dataId")!.Attribute("val")!.Value.Should().Be("data0");

            if (expectParetoLine)
            {
                var paretoLine = regionSeries[1];
                paretoLine.Attribute("layoutId")!.Value.Should().Be("paretoLine");
                paretoLine.Element(ChartExNs + "dataId")!.Attribute("val")!.Value.Should().Be("data0");
            }

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            var chartContentTypeOverrides = contentTypesXml.Root!
                .Elements(ContentTypesNs + "Override")
                .Where(element =>
                    string.Equals(element.Attribute("PartName")?.Value, "/xl/charts/chart1.xml", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("ContentType")?.Value, ChartExContentType, StringComparison.Ordinal))
                .ToList();
            chartContentTypeOverrides.Should().ContainSingle();

            var drawingRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!);
            var chartExRelationships = drawingRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    element.Attribute("Type")?.Value == ChartExRelationshipType &&
                    element.Attribute("Target")?.Value == "../charts/chart1.xml")
                .ToList();
            chartExRelationships.Should().ContainSingle();

            var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
            var graphicData = drawingXml.Descendants(DrawingNs + "graphicData").Should().ContainSingle().Subject;
            graphicData.Attribute("uri")!.Value.Should().Be(ChartExDrawingUri);
            graphicData.Elements(ChartExNs + "chart").Should().ContainSingle()
                .Which.Attribute(RelNs + "id")!.Value.Should().Be("rIdFreexcelChart1");
            graphicData.Elements(ClassicChartNs + "chart").Should().BeEmpty();
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
