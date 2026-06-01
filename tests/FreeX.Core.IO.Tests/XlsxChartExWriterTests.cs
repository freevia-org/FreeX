using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxChartExWriterTests
{
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartExStyleContentType = "application/vnd.ms-office.chartstyle+xml";
    private const string ChartExColorStyleContentType = "application/vnd.ms-office.chartcolorstyle+xml";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ClassicChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace ChartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace ChartExCompatNs = "http://schemas.microsoft.com/office/drawing/2015/9/8/chartex";

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
            styleXml.Root.Attribute("id")!.Value.Should().Be("410");
            styleXml.Root.Elements(ChartStyleNs + "legend").Should().ContainSingle();
            styleXml.Root.Elements(ChartStyleNs + "valueAxis").Should().ContainSingle();

            var colorsXml = LoadPackageXml(archive.GetEntry("xl/charts/colors1.xml")!);
            colorsXml.Root!.Name.Should().Be(ChartStyleNs + "colorStyle");
            colorsXml.Root.Attribute("meth")!.Value.Should().Be("cycle");
            colorsXml.Root.Elements(DrawingNs + "schemeClr").Select(element => element.Attribute("val")!.Value)
                .Should().Equal("accent1", "accent2", "accent3", "accent4", "accent5", "accent6");

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

    [Fact]
    public void Save_WritesWaterfallSubtotalsLayoutPr()
    {
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
            chart.WaterfallTotalPointIndices = [0, 2]);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        var subtotals = chartXml.Descendants(ChartExNs + "subtotals").Should().ContainSingle().Subject;
        subtotals.Elements(ChartExNs + "idx").Select(idx => idx.Attribute("val")!.Value)
            .Should().Equal("0", "2");
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

    [Fact]
    public void SaveLoad_WaterfallTotalPointIndicesRoundTripThroughChartEx()
    {
        var saved = SaveWorkbookWithChart(ChartType.Waterfall, configureChart: chart =>
            chart.WaterfallTotalPointIndices = [0, 2]);

        var loaded = new XlsxFileAdapter().Load(saved);
        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.WaterfallTotalPointIndices.Should().Equal(0, 2);
    }

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

    private static MemoryStream SaveWorkbookWithChart(ChartType chartType, int endCol = 2, Action<ChartModel>? configureChart = null)
    {
        var workbook = new Workbook("ChartExWriterTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        if (endCol >= 3)
            sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Target"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        if (endCol >= 3)
            sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        if (endCol >= 3)
            sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(22));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        if (endCol >= 3)
            sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(32));
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, (uint)endCol)),
            Title = chartType.ToString()
        };
        configureChart?.Invoke(chart);
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static MemoryStream SaveBoxAndWhiskerAllNumericColumnsWorkbook()
    {
        var workbook = new Workbook("BoxAndWhiskerAllNumeric");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Gamma"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(22));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(32));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(34));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.BoxAndWhisker,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            Title = "BoxAndWhisker"
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static void AssertBoxAndWhiskerSeries(
        XElement series,
        string dataId,
        string headerReference,
        string headerText)
    {
        series.Element(ChartExNs + "dataId")!.Attribute("val")!.Value.Should().Be(dataId);
        var txData = series.Element(ChartExNs + "tx")!.Element(ChartExNs + "txData")!;
        txData.Element(ChartExNs + "f")!.Value.Should().Contain(headerReference);
        txData.Element(ChartExNs + "v")!.Value.Should().Be(headerText);
        series.Element(ChartExNs + "layoutPr")!
            .Element(ChartExNs + "statistics")!
            .Attribute("quartileMethod")!
            .Value
            .Should()
            .Be("exclusive");
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument xml)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        xml.Save(stream);
    }
}
