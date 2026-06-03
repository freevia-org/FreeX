using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
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
        ChartTypeSupport.IsChartExFamily(chartType).Should().BeTrue();

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
            data.Attribute("id")!.Value.Should().Be("0");
            data.Elements(ChartExNs + "strDim").Should().ContainSingle()
                .Which.Should().Match<XElement>(element =>
                    element.Attribute("type")!.Value == "cat" &&
                    element.Element(ChartExNs + "f")!.Value.Contains("$A$2:$A$4", StringComparison.Ordinal));
            var expectedNumDimType = chartType is ChartType.Treemap or ChartType.Sunburst ? "size" : "val";
            data.Elements(ChartExNs + "numDim").Should().ContainSingle()
                .Which.Should().Match<XElement>(element =>
                    element.Attribute("type")!.Value == expectedNumDimType &&
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
            series.Element(ChartExNs + "dataId")!.Attribute("val")!.Value.Should().Be("0");

            if (expectParetoLine)
            {
                var columnLayoutPr = series.Elements(ChartExNs + "layoutPr").Should().ContainSingle().Subject;
                columnLayoutPr.Elements(ChartExNs + "aggregation").Should().ContainSingle();
                series.Elements(ChartExNs + "axisId").Should().BeEmpty();

                var paretoLine = regionSeries[1];
                paretoLine.Attribute("layoutId")!.Value.Should().Be("paretoLine");
                paretoLine.Attribute("ownerIdx")!.Value.Should().Be("0");
                paretoLine.Elements(ChartExNs + "dataId").Should().BeEmpty();
                paretoLine.Elements(ChartExNs + "axisId").Should().BeEmpty();
            }

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            var chartContentTypeOverrides = contentTypesXml.Root!
                .Elements(ContentTypesNs + "Override")
                .Where(element =>
                    string.Equals(element.Attribute("PartName")?.Value, "/xl/charts/chart1.xml", StringComparison.Ordinal) &&
                    string.Equals(element.Attribute("ContentType")?.Value, ChartExContentType, StringComparison.Ordinal))
                .ToList();
            chartContentTypeOverrides.Should().ContainSingle();

            contentTypesXml.Root!
                .Elements(ContentTypesNs + "Override")
                .Where(element =>
                    element.Attribute("PartName")?.Value == "/xl/charts/style1.xml" &&
                    element.Attribute("ContentType")?.Value == ChartExStyleContentType)
                .Should().ContainSingle();
            contentTypesXml.Root!
                .Elements(ContentTypesNs + "Override")
                .Where(element =>
                    element.Attribute("PartName")?.Value == "/xl/charts/colors1.xml" &&
                    element.Attribute("ContentType")?.Value == ChartExColorStyleContentType)
                .Should().ContainSingle();

            var drawingRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!);
            var chartExRelationships = drawingRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    element.Attribute("Type")?.Value == ChartExRelationshipType &&
                    element.Attribute("Target")?.Value == "../charts/chart1.xml")
                .ToList();
            chartExRelationships.Should().ContainSingle();

            var chartRelsXml = LoadPackageXml(archive.GetEntry("xl/charts/_rels/chart1.xml.rels")!);
            chartRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    element.Attribute("Type")?.Value == ChartExStyleRelationshipType &&
                    element.Attribute("Target")?.Value == "style1.xml")
                .Should().ContainSingle();
            chartRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    element.Attribute("Type")?.Value == ChartExColorStyleRelationshipType &&
                    element.Attribute("Target")?.Value == "colors1.xml")
                .Should().ContainSingle();

            var styleXml = LoadPackageXml(archive.GetEntry("xl/charts/style1.xml")!);
            styleXml.Root!.Name.Should().Be(ChartStyleNs + "chartStyle");
            AssertExcelNativeChartExStyle(styleXml);

            var colorsXml = LoadPackageXml(archive.GetEntry("xl/charts/colors1.xml")!);
            colorsXml.Root!.Name.Should().Be(ChartStyleNs + "colorStyle");
            colorsXml.Root.Attribute("meth")!.Value.Should().Be("cycle");
            colorsXml.Root.Attribute("id")!.Value.Should().Be("10");
            colorsXml.Root.Elements(DrawingNs + "schemeClr").Select(element => element.Attribute("val")!.Value)
                .Should().Equal("accent1", "accent2", "accent3", "accent4", "accent5", "accent6");
            AssertExcelNativeChartExColorStyle(colorsXml);

            var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
            drawingXml.Root!.Elements(SpreadsheetDrawingNs + "twoCellAnchor").Should().ContainSingle();
            drawingXml.Root.Elements(SpreadsheetDrawingNs + "absoluteAnchor").Should().BeEmpty();
            var alternateContent = drawingXml.Descendants(MarkupCompatNs + "AlternateContent").Should().ContainSingle().Subject;
            alternateContent.Element(MarkupCompatNs + "Choice").Should().NotBeNull();
            var choice = alternateContent.Element(MarkupCompatNs + "Choice")!;
            choice.Attribute("Requires")!.Value.Should().Be("cx1");
            choice.Attribute(XNamespace.Xmlns + "cx1")!.Value.Should().Be(ChartExCompatNs.NamespaceName);
            alternateContent.Element(MarkupCompatNs + "Fallback").Should().NotBeNull();
            var fallback = alternateContent.Element(MarkupCompatNs + "Fallback")!;
            fallback.Descendants(SpreadsheetDrawingNs + "sp").Should().ContainSingle();
            fallback.Descendants(DrawingNs + "t").Select(element => element.Value)
                .Should().Contain(text => text.Contains("This chart isn't available", StringComparison.Ordinal));

            var graphicData = drawingXml.Descendants(DrawingNs + "graphicData").Should().ContainSingle().Subject;
            graphicData.Attribute("uri")!.Value.Should().Be(ChartExDrawingUri);
            graphicData.Elements(ChartExNs + "chart").Should().ContainSingle()
                .Which.Attribute(RelNs + "id")!.Value.Should().Be("rIdFreeXChart1");
            graphicData.Elements(ClassicChartNs + "chart").Should().BeEmpty();
        }

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.TextBoxes.Should().BeEmpty();
        loadedSheet.DrawingShapes.Should().BeEmpty();
        var reloadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        reloadedChart.Type.Should().Be(chartType);
        reloadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
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

}
