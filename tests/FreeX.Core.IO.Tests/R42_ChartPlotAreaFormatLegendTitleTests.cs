using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-42 "chart-plotarea-format" bucket fixes:
///   - R42-io-chart-plotarea-legend-3-1: explicit chart-area/plot-area "No Fill"/"No Line" now
///     round-trips instead of reverting to the themed default.
///   - R42-io-chart-plotarea-legend-3-2: the writer now emits &lt;c:varyColors&gt; for
///     pie/3-D-pie/doughnut charts (previously only bar/bar3D ever wrote it).
///   - R42-io-chart-plotarea-legend-3-4: a chart title's manual layout/overlay are preserved even
///     when the title text itself is null/blank (auto-linked title).
/// R42-io-chart-plotarea-legend-3-3 (legend force-flipped to Bottom on loaded stacked charts) is
/// deferred -- see the round-42 fix summary; the writer (owned by this bucket) cannot distinguish
/// a freshly-created chart from one loaded with an explicit right-side legend without a
/// provenance signal that only the reader (XlsxChartLevelReader.cs, outside this bucket's owned
/// files) could supply, and the existing XlsxClassicChartDefaultTests pins the "freshly-created
/// stacked chart defaults to bottom" behavior using the exact same model shape.
/// </summary>
public sealed class R42_ChartPlotAreaFormatLegendTitleTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void XlsxAdapter_SaveLoad_PreservesExplicitChartAreaAndPlotAreaNoFillNoLine()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ChartAreaNoFill = true;
            chart.ChartAreaNoLine = true;
            chart.PlotAreaNoFill = true;
            chart.PlotAreaNoLine = true;
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var chartAreaSpPr = chartXml.Root!.Element(ChartNs + "spPr");
        chartAreaSpPr.Should().NotBeNull("an explicit chart-area No Fill/No Line choice must not be dropped on save");
        chartAreaSpPr!.Element(DrawingNs + "noFill").Should().NotBeNull();
        chartAreaSpPr.Element(DrawingNs + "ln")!.Element(DrawingNs + "noFill").Should().NotBeNull();

        var plotAreaSpPr = chartXml.Descendants(ChartNs + "plotArea").Single().Element(ChartNs + "spPr");
        plotAreaSpPr.Should().NotBeNull("an explicit plot-area No Fill/No Line choice must not be dropped on save");
        plotAreaSpPr!.Element(DrawingNs + "noFill").Should().NotBeNull();
        plotAreaSpPr.Element(DrawingNs + "ln")!.Element(DrawingNs + "noFill").Should().NotBeNull();

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ChartAreaNoFill.Should().BeTrue();
        loadedChart.ChartAreaNoLine.Should().BeTrue();
        loadedChart.PlotAreaNoFill.Should().BeTrue();
        loadedChart.PlotAreaNoLine.Should().BeTrue();
        loadedChart.ChartAreaFillColor.Should().BeNull();
        loadedChart.ChartAreaFillThemeColor.Should().BeNull();
        loadedChart.PlotAreaFillColor.Should().BeNull();
        loadedChart.PlotAreaFillThemeColor.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_OmitsChartAreaShapePropertiesAndPlotAreaNoFillWhenNothingSet()
    {
        // Sibling no-regression case: a chart with no fill/border/noFill data at all must still
        // omit the chart-area <c:spPr> entirely (the pre-existing "nothing set" behavior for a
        // border-thickness-less shape), not start emitting an empty/noFill shape-properties
        // element now that noFill/noLine are modeled. (Plot area still gets its own <c:spPr> from
        // the pre-existing default PlotAreaBorderThickness=1 -- unrelated to this fix -- so it is
        // only checked here for the absence of noFill/noLine.)
        var workbook = CreateColumnChartWorkbook(configure: null);

        var chartXml = ReadChartXml(SaveToStream(workbook));

        chartXml.Root!.Element(ChartNs + "spPr").Should().BeNull();

        var plotAreaSpPr = chartXml.Descendants(ChartNs + "plotArea").Single().Element(ChartNs + "spPr");
        if (plotAreaSpPr is not null)
        {
            plotAreaSpPr.Element(DrawingNs + "noFill").Should().BeNull();
            plotAreaSpPr.Element(DrawingNs + "ln")?.Element(DrawingNs + "noFill").Should().BeNull();
        }
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_StillRoundTripsExplicitChartAreaFillColorWhenNotNoFill()
    {
        // Sibling no-regression case: an explicit solid fill color (not "No Fill") must keep
        // round-tripping as a color, not as noFill.
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.ChartAreaFillColor = new CellColor(240, 240, 200);
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);
        var chartAreaSpPr = chartXml.Root!.Element(ChartNs + "spPr")!;
        chartAreaSpPr.Element(DrawingNs + "noFill").Should().BeNull();
        chartAreaSpPr.Element(DrawingNs + "solidFill").Should().NotBeNull();

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ChartAreaNoFill.Should().NotBe(true);
        loadedChart.ChartAreaFillColor.Should().Be(new CellColor(240, 240, 200));
    }

    [Theory]
    [InlineData(ChartType.Pie, "pieChart")]
    [InlineData(ChartType.ThreeDPie, "pie3DChart")]
    [InlineData(ChartType.Doughnut, "doughnutChart")]
    public void XlsxAdapter_Save_WritesExplicitVaryColorsOffForPieFamilyCharts(ChartType chartType, string plotElementName)
    {
        var workbook = CreatePieFamilyChartWorkbook(chartType, chart => chart.VaryColorsByPoint = false);

        var chartXml = ReadChartXml(SaveToStream(workbook));
        var plotElement = chartXml.Descendants(ChartNs + plotElementName).Should().ContainSingle().Subject;
        var varyColors = plotElement.Element(ChartNs + "varyColors");
        varyColors.Should().NotBeNull("turning off Vary Colors by Point must be preserved for pie-family charts too");
        varyColors!.Attribute("val")!.Value.Should().Be("0");

        // varyColors must precede the <c:ser> elements per CT_PieChart/CT_Pie3DChart/CT_DoughnutChart.
        plotElement.Elements().First().Name.LocalName.Should().Be("varyColors");
    }

    [Theory]
    [InlineData(ChartType.Pie, "pieChart")]
    [InlineData(ChartType.ThreeDPie, "pie3DChart")]
    [InlineData(ChartType.Doughnut, "doughnutChart")]
    public void XlsxAdapter_Save_OmitsVaryColorsForPieFamilyChartsWhenUnset(ChartType chartType, string plotElementName)
    {
        // Sibling no-regression case: a chart that never set VaryColorsByPoint keeps omitting the
        // element entirely (Excel's own default), matching pre-existing freshly-created-chart
        // behavior pinned elsewhere (e.g. XlsxClassicChartDefaultTests).
        var workbook = CreatePieFamilyChartWorkbook(chartType, configure: null);

        var chartXml = ReadChartXml(SaveToStream(workbook));
        var plotElement = chartXml.Descendants(ChartNs + plotElementName).Should().ContainSingle().Subject;
        plotElement.Element(ChartNs + "varyColors").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_PreservesManualTitleLayoutAndOverlayWhenTitleTextIsBlank()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.Title = null;
            chart.TitleOverlay = true;
            chart.TitleLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner",
                XMode = "edge",
                YMode = "edge",
                X = 0.05,
                Y = 0.02
            };
        });

        var chartXml = ReadChartXml(SaveToStream(workbook));
        var title = chartXml.Descendants(ChartNs + "title").SingleOrDefault();
        title.Should().NotBeNull("a blank/auto title with an explicit manual layout or overlay must still be written");
        title!.Element(ChartNs + "tx").Should().BeNull("no literal title text was set, so <c:tx> must stay omitted");
        title.Element(ChartNs + "layout")!.Descendants(ChartNs + "x").Single().Attribute("val")!.Value
            .Should().Be("0.05");
        title.Element(ChartNs + "overlay")!.Attribute("val")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_Save_OmitsTitleWhenTextLayoutAndOverlayAreAllAbsent()
    {
        // Sibling no-regression case: a chart with no title text, no manual layout, and no
        // overlay must keep omitting <c:title> entirely.
        var workbook = CreateColumnChartWorkbook(chart => chart.Title = null);

        var chartXml = ReadChartXml(SaveToStream(workbook));
        chartXml.Descendants(ChartNs + "title").Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_StillWritesTitleTextAlongsideManualLayout()
    {
        // Sibling no-regression case: literal title text plus a manual layout must still write
        // both the text run and the layout (not regress to text-only or layout-only).
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.TitleLayout = new ChartManualLayoutModel { X = 0.1, Y = 0.1 };
        });

        var chartXml = ReadChartXml(SaveToStream(workbook));
        var title = chartXml.Descendants(ChartNs + "title").Single();
        title.Descendants(DrawingNs + "t").Single().Value.Should().Be("Sales");
        title.Element(ChartNs + "layout").Should().NotBeNull();
    }

    private static Workbook CreateColumnChartWorkbook(Action<ChartModel>? configure)
    {
        var workbook = new Workbook("ChartPlotAreaFormatLegendTitle");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static Workbook CreatePieFamilyChartWorkbook(ChartType chartType, Action<ChartModel>? configure)
    {
        var workbook = new Workbook("PieFamilyVaryColors");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Segment"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));

        var chart = new ChartModel
        {
            Type = chartType,
            Title = "Share",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static MemoryStream SaveToStream(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument ReadChartXml(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        return XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/charts/chart1.xml",
            "the XLSX package should contain xl/charts/chart1.xml");
    }
}
