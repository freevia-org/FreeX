using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxClassicChartDefaultTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Theory]
    [InlineData(ChartType.StackedColumn, "col", "b", "l", "General")]
    [InlineData(ChartType.PercentStackedColumn, "col", "b", "l", "0%")]
    [InlineData(ChartType.StackedBar, "bar", "l", "b", "General")]
    [InlineData(ChartType.PercentStackedBar, "bar", "l", "b", "0%")]
    public void XlsxAdapter_Save_WritesExcelNativeStackedBarColumnDefaults(
        ChartType chartType,
        string expectedDirection,
        string expectedCategoryAxisPosition,
        string expectedValueAxisPosition,
        string expectedValueAxisNumberFormat)
    {
        var chartXml = SaveChartXml(chartType, chart =>
        {
            chart.ShowDataLabels = true;
            chart.ShowSeriesLines = true;
        });

        var barChart = chartXml.Descendants(ChartNs + "barChart").Should().ContainSingle().Subject;
        barChart.Element(ChartNs + "barDir")!.Attribute("val")!.Value.Should().Be(expectedDirection);
        barChart.Element(ChartNs + "gapWidth")!.Attribute("val")!.Value.Should().Be("219");
        barChart.Element(ChartNs + "overlap")!.Attribute("val")!.Value.Should().Be("-27");
        AssertChildOrder(barChart, "ser", "dLbls", "gapWidth", "overlap", "serLines", "axId");

        chartXml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be(expectedCategoryAxisPosition);
        chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be(expectedValueAxisPosition);
        chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "numFmt")!.Should().Match<XElement>(element =>
                element.Attribute("formatCode")!.Value == expectedValueAxisNumberFormat &&
                element.Attribute("sourceLinked")!.Value == "1");
    }

    [Fact]
    public void XlsxAdapter_Save_PreservesExplicitPercentStackedValueAxisNumberFormat()
    {
        var chartXml = SaveChartXml(ChartType.PercentStackedColumn, chart =>
        {
            chart.YAxisNumberFormat = ChartDataLabelNumberFormat.Currency;
            chart.YAxisNumberFormatSourceLinked = false;
        });

        chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "numFmt")!.Should().Match<XElement>(element =>
                element.Attribute("formatCode")!.Value == "$#,##0.00" &&
                element.Attribute("sourceLinked")!.Value == "0");
    }

    [Theory]
    [InlineData(ChartType.ThreeDColumn, "col", "b", "l")]
    [InlineData(ChartType.ThreeDBar, "bar", "l", "b")]
    public void XlsxAdapter_Save_WritesExcelNative3DBarColumnDefaults(
        ChartType chartType,
        string expectedDirection,
        string expectedCategoryAxisPosition,
        string expectedValueAxisPosition)
    {
        var chartXml = SaveChartXml(chartType, chart => chart.ShowDataLabels = true);

        AssertDefault3DView(chartXml, "15", "20", "1");
        AssertDefault3DSurfaces(chartXml);

        var barChart = chartXml.Descendants(ChartNs + "bar3DChart").Should().ContainSingle().Subject;
        barChart.Element(ChartNs + "barDir")!.Attribute("val")!.Value.Should().Be(expectedDirection);
        barChart.Element(ChartNs + "gapWidth")!.Attribute("val")!.Value.Should().Be("219");
        barChart.Element(ChartNs + "shape")!.Attribute("val")!.Value.Should().Be("box");
        barChart.Element(ChartNs + "overlap").Should().BeNull();
        AssertChildOrder(barChart, "ser", "dLbls", "gapWidth", "shape", "axId");
        barChart.Elements(ChartNs + "axId").Should().HaveCount(3);

        chartXml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be(expectedCategoryAxisPosition);
        chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be(expectedValueAxisPosition);
        AssertDefaultSeriesAxis(chartXml);
    }

    [Theory]
    [InlineData(ChartType.ThreeDLine, "line3DChart")]
    [InlineData(ChartType.ThreeDArea, "area3DChart")]
    [InlineData(ChartType.ThreeDSurface, "surface3DChart")]
    public void XlsxAdapter_Save_WritesExcelNative3DSeriesAxisDefaults(
        ChartType chartType,
        string expectedPlotElement)
    {
        var chartXml = SaveChartXml(chartType);

        AssertDefault3DView(chartXml, "15", "20", "0");
        AssertDefault3DSurfaces(chartXml);

        var plotChart = chartXml.Descendants(ChartNs + expectedPlotElement).Should().ContainSingle().Subject;
        plotChart.Elements(ChartNs + "axId").Should().HaveCount(3);
        if (chartType == ChartType.ThreeDSurface)
            plotChart.Element(ChartNs + "wireframe")!.Attribute("val")!.Value.Should().Be("0");

        AssertDefaultSeriesAxis(chartXml);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesExcelNative3DPieViewAndSurfaces()
    {
        var chartXml = SaveChartXml(ChartType.ThreeDPie);

        AssertDefault3DView(chartXml, "30", "0", "0");
        AssertDefault3DSurfaces(chartXml);
        chartXml.Descendants(ChartNs + "catAx").Should().BeEmpty();
        chartXml.Descendants(ChartNs + "valAx").Should().BeEmpty();
    }

    private static XDocument SaveChartXml(ChartType chartType, Action<ChartModel>? configure = null)
    {
        var workbook = new Workbook("ClassicChartDefaults");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("West"));

        var rows = new[]
        {
            ("Q1", 18, 12, 9),
            ("Q2", 24, 17, 14),
            ("Q3", 21, 22, 16),
            ("Q4", 30, 25, 20)
        };
        for (var index = 0; index < rows.Length; index++)
        {
            var rowNumber = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, rowNumber, 1), new TextValue(rows[index].Item1));
            sheet.SetCell(new CellAddress(sheet.Id, rowNumber, 2), new NumberValue(rows[index].Item2));
            sheet.SetCell(new CellAddress(sheet.Id, rowNumber, 3), new NumberValue(rows[index].Item3));
            sheet.SetCell(new CellAddress(sheet.Id, rowNumber, 4), new NumberValue(rows[index].Item4));
        }

        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 4)),
            Title = chartType.ToString()
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return LoadPackageXml(archive, "xl/charts/chart1.xml");
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"the XLSX package should contain {entryName}");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static void AssertDefault3DView(
        XDocument chartXml,
        string expectedRotationX,
        string expectedRotationY,
        string expectedRightAngleAxes)
    {
        var view = chartXml.Descendants(ChartNs + "view3D").Should().ContainSingle().Subject;
        view.Element(ChartNs + "rotX")!.Attribute("val")!.Value.Should().Be(expectedRotationX);
        view.Element(ChartNs + "rotY")!.Attribute("val")!.Value.Should().Be(expectedRotationY);
        view.Element(ChartNs + "rAngAx")!.Attribute("val")!.Value.Should().Be(expectedRightAngleAxes);
    }

    private static void AssertDefault3DSurfaces(XDocument chartXml)
    {
        foreach (var name in new[] { "floor", "sideWall", "backWall" })
        {
            var surface = chartXml.Descendants(ChartNs + name).Should().ContainSingle().Subject;
            surface.Element(ChartNs + "thickness")!.Attribute("val")!.Value.Should().Be("0");
            var shapeProperties = surface.Element(ChartNs + "spPr");
            shapeProperties.Should().NotBeNull();
            shapeProperties!.Element(DrawingNs + "noFill").Should().NotBeNull();
            shapeProperties.Element(DrawingNs + "ln")!.Element(DrawingNs + "noFill").Should().NotBeNull();
            shapeProperties.Element(DrawingNs + "effectLst").Should().NotBeNull();
            shapeProperties.Element(DrawingNs + "sp3d").Should().NotBeNull();
        }
    }

    private static void AssertDefaultSeriesAxis(XDocument chartXml)
    {
        var seriesAxis = chartXml.Descendants(ChartNs + "serAx").Should().ContainSingle().Subject;
        seriesAxis.Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be("b");
        seriesAxis.Element(ChartNs + "crossAx")!.Attribute("val")!.Value.Should().Be("48672768");
    }

    private static void AssertChildOrder(XElement parent, params string[] expectedNames)
    {
        var childNames = parent.Elements().Select(element => element.Name.LocalName).ToList();
        var startIndex = 0;
        foreach (var expectedName in expectedNames)
        {
            var foundIndex = childNames.FindIndex(startIndex, name => name == expectedName);
            foundIndex.Should().BeGreaterThanOrEqualTo(0, $"{expectedName} should appear after the previous checked child");
            startIndex = foundIndex + 1;
        }
    }
}
