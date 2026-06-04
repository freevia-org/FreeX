using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
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

    private static void AssertExcelNativeChartExStyle(XDocument styleXml)
    {
        styleXml.Root!.Attribute("id")!.Value.Should().Be("201");
        styleXml.Root.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "axisTitle",
            "categoryAxis",
            "chartArea",
            "dataLabel",
            "dataLabelCallout",
            "dataPoint",
            "dataPoint3D",
            "dataPointLine",
            "dataPointMarker",
            "dataPointMarkerLayout",
            "dataPointWireframe",
            "dataTable",
            "downBar",
            "dropLine",
            "errorBar",
            "floor",
            "gridlineMajor",
            "gridlineMinor",
            "hiLoLine",
            "leaderLine",
            "legend",
            "plotArea",
            "plotArea3D",
            "seriesAxis",
            "seriesLine",
            "title",
            "trendline",
            "trendlineLabel",
            "upBar",
            "valueAxis",
            "wall");

        var dataPoint = styleXml.Root.Elements(ChartStyleNs + "dataPoint").Should().ContainSingle().Subject;
        dataPoint.Element(ChartStyleNs + "fillRef")!.Attribute("idx")!.Value.Should().Be("1");
        dataPoint.Element(ChartStyleNs + "fillRef")!.Element(ChartStyleNs + "styleClr")!
            .Attribute("val")!.Value.Should().Be("auto");
        dataPoint.Elements(ChartStyleNs + "spPr").Should().BeEmpty();

        var chartArea = styleXml.Root.Elements(ChartStyleNs + "chartArea").Should().ContainSingle().Subject;
        chartArea.Element(ChartStyleNs + "spPr")!
            .Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "schemeClr")!
            .Attribute("val")!.Value.Should().Be("bg1");
        chartArea.Attribute("mods")!.Value.Should().Be("allowNoFillOverride allowNoLineOverride");

        var categoryAxisLine = styleXml.Root.Elements(ChartStyleNs + "categoryAxis").Should().ContainSingle().Subject
            .Element(ChartStyleNs + "spPr")!
            .Element(DrawingNs + "ln")!;
        categoryAxisLine.Attribute("cap")!.Value.Should().Be("flat");
        categoryAxisLine.Attribute("cmpd")!.Value.Should().Be("sng");
        categoryAxisLine.Attribute("algn")!.Value.Should().Be("ctr");

        var gridlineMajorLine = styleXml.Root.Elements(ChartStyleNs + "gridlineMajor").Should().ContainSingle().Subject
            .Element(ChartStyleNs + "spPr")!
            .Element(DrawingNs + "ln")!;
        gridlineMajorLine.Attribute("cap")!.Value.Should().Be("flat");
        gridlineMajorLine.Attribute("cmpd")!.Value.Should().Be("sng");
        gridlineMajorLine.Attribute("algn")!.Value.Should().Be("ctr");

        var titleDefaultRunProperties = styleXml.Root.Elements(ChartStyleNs + "title").Should().ContainSingle().Subject
            .Element(ChartStyleNs + "defRPr")!;
        titleDefaultRunProperties.Attribute("sz")!.Value.Should().Be("1400");
        titleDefaultRunProperties.Attribute("kern")!.Value.Should().Be("1200");
        titleDefaultRunProperties.Attribute("b")!.Value.Should().Be("0");

        var dataPointMarkerLayout = styleXml.Root.Elements(ChartStyleNs + "dataPointMarkerLayout")
            .Should()
            .ContainSingle()
            .Subject;
        dataPointMarkerLayout.Attribute("symbol")!.Value.Should().Be("circle");
        dataPointMarkerLayout.Attribute("size")!.Value.Should().Be("5");

        styleXml.Root.Elements(ChartStyleNs + "plotArea").Should().ContainSingle().Subject
            .Attribute("mods")!.Value.Should().Be("allowNoFillOverride allowNoLineOverride");
        styleXml.Root.Elements(ChartStyleNs + "plotArea3D").Should().ContainSingle().Subject
            .Attribute("mods")!.Value.Should().Be("allowNoFillOverride allowNoLineOverride");
    }

    private static void AssertExcelNativeChartExColorStyle(XDocument colorsXml)
    {
        colorsXml.Root!.Elements(ChartStyleNs + "variation")
            .Select(variation =>
            {
                var lumMod = variation.Element(DrawingNs + "lumMod")?.Attribute("val")?.Value ?? string.Empty;
                var lumOff = variation.Element(DrawingNs + "lumOff")?.Attribute("val")?.Value ?? string.Empty;
                return $"{lumMod}:{lumOff}";
            })
            .Should()
            .Equal(
                ":",
                "60000:",
                "80000:20000",
                "80000:",
                "60000:40000",
                "50000:",
                "70000:30000",
                "70000:",
                "50000:50000");
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
        => XlsxPackageTestFixtures.LoadPackageXml(entry);

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument xml)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        xml.Save(stream);
    }
}
