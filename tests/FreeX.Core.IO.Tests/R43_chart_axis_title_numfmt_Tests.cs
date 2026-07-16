using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 43 chart-axis title/numFmt findings:
///  - R43-io-chart-axis-title-numfmt-3-1: the category/date axis's OWN &lt;c:numFmt&gt; (distinct
///    from the value axis's) was never written by ToCategoryAxisXml, so a custom category/date axis
///    number format (e.g. a date axis's "mmm-yy") reverted to Excel's default on every save.
///  - R43-io-chart-axis-title-numfmt-3-2: a plain (non-rich) axis title on a vertical (left/right)
///    axis always got a bare &lt;a:bodyPr/&gt; on write, flattening Excel's standard vertical
///    "Primary Vertical" axis-title orientation (rot="-5400000" vert="horz") to horizontal.
///  - R43-io-chart-axis-title-numfmt-3-3: AxisTitleFontSize/AxisTitleTextColor/AxisTitleTextThemeColor
///    were single chart-level fields shared by every axis title, so a chart with differently
///    formatted X/Y axis titles could not have that distinction represented (and, pre-existing, a
///    second read of the Y axis title formatting always clobbered the first/X read). New per-axis
///    override fields (XAxisTitleFontSize/... and YAxisTitleFontSize/...) let the writer emit
///    distinct formatting per axis when populated, falling back to the shared fields otherwise so
///    existing chart construction is unaffected.
/// </summary>
public sealed class R43_chart_axis_title_numfmt_Tests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R43ChartAxisTitleNumFmt");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static ChartModel RoundTrip(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    private static XDocument SaveChartXml(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        var bytes = XlsxPackageTestHelper.SaveToBytes(workbook);
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml");
    }

    // --- R43-io-chart-axis-title-numfmt-3-1 -------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesDateAxisCustomNumberFormat()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            XAxisIsDateAxis = true,
            XAxisNumberFormatCode = "mmm-yy",
            XAxisNumberFormatSourceLinked = false,
        };

        var chartXml = SaveChartXml(chart);
        var dateAxis = chartXml.Descendants(ChartNs + "dateAx").Should().ContainSingle().Subject;
        var numFmt = dateAxis.Element(ChartNs + "numFmt");
        numFmt.Should().NotBeNull("pre-fix, ToCategoryAxisXml never emitted a <c:numFmt> for catAx/dateAx");
        numFmt!.Attribute("formatCode")!.Value.Should().Be("mmm-yy");
        numFmt.Attribute("sourceLinked")!.Value.Should().Be("0");

        var loaded = RoundTrip(chart);
        loaded.XAxisIsDateAxis.Should().BeTrue();
        loaded.XAxisNumberFormatCode.Should().Be("mmm-yy");
        loaded.XAxisNumberFormatSourceLinked.Should().BeFalse();
    }

    // Sibling case: a plain (non-date) category axis with no explicit number format still gets a
    // valid, schema-conformant <c:numFmt> (Excel's "General"/sourceLinked default), matching the
    // always-emit pattern already used for the value axis -- not a regression to null/omitted.
    [Fact]
    public void XlsxAdapter_Save_CategoryAxisWithNoCustomFormat_EmitsGeneralSourceLinkedNumFmt()
    {
        var chart = new ChartModel { Type = ChartType.Column };

        var chartXml = SaveChartXml(chart);
        var categoryAxis = chartXml.Descendants(ChartNs + "catAx").Should().ContainSingle().Subject;
        var numFmt = categoryAxis.Element(ChartNs + "numFmt");
        numFmt.Should().NotBeNull();
        numFmt!.Attribute("formatCode")!.Value.Should().Be("General");
        numFmt.Attribute("sourceLinked")!.Value.Should().Be("1");
    }

    // --- R43-io-chart-axis-title-numfmt-3-2 -------------------------------------------------

    [Fact]
    public void XlsxAdapter_Save_YAxisTitle_UsesExcelDefaultVerticalRotation()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisTitle = "Revenue",
        };

        var chartXml = SaveChartXml(chart);
        var valueAxisTitleBodyPr = chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "title")!
            .Descendants(DrawingNs + "bodyPr").Single();

        valueAxisTitleBodyPr.Attribute("rot")!.Value.Should().Be("-5400000",
            "pre-fix, ToAxisTitleXml always wrote a bare <a:bodyPr/>, flattening Excel's default vertical Y-axis title to horizontal");
        valueAxisTitleBodyPr.Attribute("vert")!.Value.Should().Be("horz");
    }

    // Sibling case: the X (category) axis title sits on a horizontal axis, so it must NOT get the
    // vertical rotation -- only left/right axes default to rotated titles.
    [Fact]
    public void XlsxAdapter_Save_XAxisTitle_StaysHorizontal()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
        };

        var chartXml = SaveChartXml(chart);
        var categoryAxisTitleBodyPr = chartXml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "title")!
            .Descendants(DrawingNs + "bodyPr").Single();

        categoryAxisTitleBodyPr.Attribute("rot").Should().BeNull();
        categoryAxisTitleBodyPr.Attribute("vert").Should().BeNull();
    }

    // A horizontal Bar chart flips the physical axes: the category axis is on the left (vertical)
    // and the value axis is on the bottom (horizontal), so the rotation must follow the physical
    // position, not the category/value role.
    [Fact]
    public void XlsxAdapter_Save_HorizontalBarChart_CategoryAxisTitleOnLeft_IsRotated()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            XAxisTitle = "Region",
        };

        var chartXml = SaveChartXml(chart);
        var categoryAxis = chartXml.Descendants(ChartNs + "catAx").Single();
        categoryAxis.Element(ChartNs + "axPos")!.Attribute("val")!.Value.Should().Be("l");
        var categoryAxisTitleBodyPr = categoryAxis.Element(ChartNs + "title")!
            .Descendants(DrawingNs + "bodyPr").Single();

        categoryAxisTitleBodyPr.Attribute("rot")!.Value.Should().Be("-5400000");
        categoryAxisTitleBodyPr.Attribute("vert")!.Value.Should().Be("horz");
    }

    // --- R43-io-chart-axis-title-numfmt-3-3 -------------------------------------------------

    [Fact]
    public void XlsxAdapter_Save_DistinctXAndYAxisTitleOverrides_AreNotClobbered()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            // Simulates the shared field ending up holding whichever axis was read/set last (the
            // pre-existing clobbering behavior), while each axis also carries its own override.
            AxisTitleFontSize = 10,
            AxisTitleTextColor = new CellColor(0, 0, 255),
            XAxisTitleFontSize = 14,
            XAxisTitleTextColor = new CellColor(255, 0, 0),
            YAxisTitleFontSize = 10,
            YAxisTitleTextColor = new CellColor(0, 0, 255),
        };

        var chartXml = SaveChartXml(chart);

        var categoryTitleRunProperties = chartXml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "rPr").Single();
        categoryTitleRunProperties.Attribute("sz")!.Value.Should().Be("1400",
            "pre-fix, the X axis title always inherited whatever the shared AxisTitleFontSize field last held (the Y axis's value)");
        categoryTitleRunProperties.Descendants(DrawingNs + "srgbClr").Single()
            .Attribute("val")!.Value.Should().Be("FF0000");

        var valueTitleRunProperties = chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "rPr").Single();
        valueTitleRunProperties.Attribute("sz")!.Value.Should().Be("1000");
        valueTitleRunProperties.Descendants(DrawingNs + "srgbClr").Single()
            .Attribute("val")!.Value.Should().Be("0000FF");
    }

    // Sibling case: charts that never populate the new per-axis overrides keep the exact prior
    // behavior of both axis titles sharing the single AxisTitleFontSize/TextColor fields.
    [Fact]
    public void XlsxAdapter_Save_NoPerAxisOverrides_BothAxisTitlesUseSharedFormatting()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            AxisTitleFontSize = 18,
            AxisTitleTextColor = new CellColor(10, 20, 30),
        };

        var chartXml = SaveChartXml(chart);

        var categorySize = chartXml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "rPr").Single().Attribute("sz")!.Value;
        var valueSize = chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "rPr").Single().Attribute("sz")!.Value;

        categorySize.Should().Be("1800");
        valueSize.Should().Be("1800");
    }
}
