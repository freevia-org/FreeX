using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 71 io-chart-axis-4 bucket:
///  - R71-io-chart-axis-4-1: a date axis's own explicit &lt;c:scaling&gt;/&lt;c:min&gt;/&lt;c:max&gt;
///    (a pinned date range) was never read (ApplyCategoryAxisProperties only ever inspected
///    &lt;c:scaling&gt; for its &lt;c:orientation&gt;) or written (ToCategoryAxisXml's scaling only
///    emitted orientation), so a fixed category-axis date range was silently dropped on every
///    round-trip.
///  - R71-io-chart-axis-4-2: the secondary value axis's own &lt;c:dispUnits&gt; was never read, and
///    the writer always hardcoded the PRIMARY axis's display unit onto the secondary axis.
///  - R71-io-chart-axis-4-3: a plain (single-run) axis title's explicit &lt;a:bodyPr&gt;@rot (e.g.
///    rot="0" to force a vertical axis's title horizontal) was never read, and the writer always
///    reconstructed bodyPr purely from the vertical bool (always rot=-5400000 for a left/right axis).
///  - R71-io-chart-axis-4-4: ToEffectiveValueAxisCrossBetween ignored valueAxisOnX, so a horizontal
///    Bar chart's own value-axis crossBetween (captured into XAxisCrossBetween, not YAxisCrossBetween)
///    was discarded on save.
/// </summary>
public sealed class R71_ChartAxisDateDisplayUnitRotationCrossBetweenTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R71ChartAxisDateDisplayUnitRotationCrossBetween");
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

    // --- R71-io-chart-axis-4-1 -----------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesDateAxisExplicitMinMax()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            XAxisIsDateAxis = true,
            XAxisMinimum = 43831,
            XAxisMaximum = 44926,
        };

        var chartXml = SaveChartXml(chart);
        var dateAxis = chartXml.Descendants(ChartNs + "dateAx").Should().ContainSingle().Subject;
        var scaling = dateAxis.Element(ChartNs + "scaling");
        scaling.Should().NotBeNull();
        scaling!.Element(ChartNs + "min")!.Attribute("val")!.Value.Should().Be("43831",
            "pre-fix, ToCategoryAxisXml never emitted <c:min> for catAx/dateAx");
        scaling.Element(ChartNs + "max")!.Attribute("val")!.Value.Should().Be("44926");

        var loaded = RoundTrip(chart);
        loaded.XAxisIsDateAxis.Should().BeTrue();
        loaded.XAxisMinimum.Should().Be(43831,
            "pre-fix, ApplyCategoryAxisProperties never read <c:min>/<c:max> off a date axis's own <c:scaling>");
        loaded.XAxisMaximum.Should().Be(44926);
    }

    // Sibling no-regression: a date axis with no explicit bounds emits no <c:min>/<c:max> at all.
    [Fact]
    public void XlsxAdapter_Save_DateAxisWithNoExplicitBounds_EmitsNoMinMax()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            XAxisIsDateAxis = true,
        };

        var chartXml = SaveChartXml(chart);
        var dateAxis = chartXml.Descendants(ChartNs + "dateAx").Should().ContainSingle().Subject;
        var scaling = dateAxis.Element(ChartNs + "scaling");
        scaling.Should().NotBeNull();
        scaling!.Element(ChartNs + "min").Should().BeNull();
        scaling.Element(ChartNs + "max").Should().BeNull();
    }

    // Sibling no-regression: a plain (non-date) category axis is unaffected, and the value axis's own
    // min/max still round-trips exactly as before this fix.
    [Fact]
    public void XlsxAdapter_RoundTrip_ValueAxisMinMax_StillRoundTrips()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisMinimum = 0,
            YAxisMaximum = 100,
        };

        var chartXml = SaveChartXml(chart);
        var categoryAxis = chartXml.Descendants(ChartNs + "catAx").Should().ContainSingle().Subject;
        categoryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "min").Should().BeNull();
        categoryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "max").Should().BeNull();

        var loaded = RoundTrip(chart);
        loaded.YAxisMinimum.Should().Be(0);
        loaded.YAxisMaximum.Should().Be(100);
    }

    // --- R71-io-chart-axis-4-2 -----------------------------------------------------------------

    private static Workbook CreateColumnLineComboWorkbook(ChartModel chart)
    {
        var workbook = new Workbook("R71ChartAxisSecondaryDisplayUnit");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Growth"));
        for (uint row = 2; row <= 4; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 1_000_000));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(0.1 + (offset * 0.02)));
        }

        chart.Type = ChartType.Column;
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        chart.ShowSecondaryAxis = true;
        chart.SecondaryAxisSeriesIndexes = [1];
        chart.UseComboLineForSecondarySeries = true;
        chart.ComboLineSeriesIndexes = [1];
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static ChartModel RoundTripCombo(ChartModel chart)
    {
        var workbook = CreateColumnLineComboWorkbook(chart);
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    private static XDocument SaveComboChartXml(ChartModel chart)
    {
        var workbook = CreateColumnLineComboWorkbook(chart);
        var bytes = XlsxPackageTestHelper.SaveToBytes(workbook);
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml");
    }

    [Fact]
    public void ComboChart_SecondaryAxisOwnDisplayUnit_RoundTripsIndependentlyOfPrimary()
    {
        var chart = new ChartModel
        {
            SecondaryAxisDisplayUnit = ChartAxisDisplayUnit.Millions,
        };

        var chartXml = SaveComboChartXml(chart);
        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var primaryAxis = valueAxes[0];
        var secondaryAxis = valueAxes[1];

        primaryAxis.Element(ChartNs + "dispUnits").Should().BeNull(
            "the primary axis never had a display unit and must not gain one");
        secondaryAxis.Element(ChartNs + "dispUnits")!
            .Element(ChartNs + "builtInUnit")!.Attribute("val")!.Value.Should().Be("millions",
                "pre-fix, ToChartAxesXml always hardcoded the PRIMARY axis's display unit onto the secondary axis");

        var reloaded = RoundTripCombo(chart);
        reloaded.YAxisDisplayUnit.Should().BeNull();
        reloaded.SecondaryAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions,
            "pre-fix, ApplySecondaryAxisProperties never read <c:dispUnits> off the secondary axis at all");
    }

    // Sibling no-regression / reverse case: a primary axis WITH its own display unit must not leak
    // onto a secondary axis that genuinely has none of its own.
    [Fact]
    public void ComboChart_PrimaryAxisDisplayUnit_DoesNotLeakOntoSecondaryAxisWithNone()
    {
        var chart = new ChartModel
        {
            YAxisDisplayUnit = ChartAxisDisplayUnit.Thousands,
        };

        var chartXml = SaveComboChartXml(chart);
        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        valueAxes[0].Element(ChartNs + "dispUnits")!
            .Element(ChartNs + "builtInUnit")!.Attribute("val")!.Value.Should().Be("thousands");
        valueAxes[1].Element(ChartNs + "dispUnits").Should().BeNull(
            "the secondary axis has no display unit of its own and must not inherit the primary axis's");

        var reloaded = RoundTripCombo(chart);
        reloaded.YAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Thousands);
        reloaded.SecondaryAxisDisplayUnit.Should().BeNull();
    }

    // --- R71-io-chart-axis-4-3 -----------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_VerticalYAxisTitle_PreservesCustomHorizontalRotation()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisTitle = "Revenue",
            YAxisTitleRotation = 0,
        };

        var chartXml = SaveChartXml(chart);
        var valueAxisTitleBodyPr = chartXml.Descendants(ChartNs + "valAx").Single()
            .Element(ChartNs + "title")!
            .Descendants(DrawingNs + "bodyPr").Single();
        valueAxisTitleBodyPr.Attribute("rot")!.Value.Should().Be("0",
            "pre-fix, ToAxisTitleXml always hardcoded rot=-5400000 for a vertical axis title, ignoring a captured rot=0");
        valueAxisTitleBodyPr.Attribute("vert")!.Value.Should().Be("horz");

        var loaded = RoundTrip(chart);
        loaded.YAxisTitleRotation.Should().Be(0,
            "pre-fix, ApplyAxisTitleFormatting never inspected <a:bodyPr>@rot at all");
    }

    // Sibling no-regression: a default vertical title (no captured rotation) keeps Excel's standard
    // -5400000 default.
    [Fact]
    public void XlsxAdapter_Save_DefaultVerticalYAxisTitle_KeepsStandardRotation()
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
        valueAxisTitleBodyPr.Attribute("rot")!.Value.Should().Be("-5400000");
        valueAxisTitleBodyPr.Attribute("vert")!.Value.Should().Be("horz");
    }

    // Sibling no-regression: a horizontal (X/category) axis title is unaffected by this fix.
    [Fact]
    public void XlsxAdapter_Save_HorizontalXAxisTitle_StaysHorizontalAndUnaffected()
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

    // --- R71-io-chart-axis-4-4 -----------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_HorizontalBarChart_ValueAxisCrossBetween_Survives()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            XAxisCrossBetween = ChartAxisCrossBetween.MidCategory,
        };

        var chartXml = SaveChartXml(chart);
        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        valueAxis.Element(ChartNs + "crossBetween")!.Attribute("val")!.Value.Should().Be("midCat",
            "pre-fix, ToEffectiveValueAxisCrossBetween always read chart.YAxisCrossBetween, discarding the horizontal Bar chart's own X-routed crossBetween");

        var loaded = RoundTrip(chart);
        loaded.XAxisCrossBetween.Should().Be(ChartAxisCrossBetween.MidCategory);
    }

    // Sibling no-regression: a (vertical) Column chart's value axis keeps reading YAxisCrossBetween.
    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnChart_ValueAxisCrossBetween_StillUsesYAxisField()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisCrossBetween = ChartAxisCrossBetween.MidCategory,
        };

        var chartXml = SaveChartXml(chart);
        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        valueAxis.Element(ChartNs + "crossBetween")!.Attribute("val")!.Value.Should().Be("midCat");

        var loaded = RoundTrip(chart);
        loaded.YAxisCrossBetween.Should().Be(ChartAxisCrossBetween.MidCategory);
    }

    // Sibling no-regression: a classic stacked bar chart with no explicit crossBetween on either axis
    // still gets Excel's "between" default.
    [Fact]
    public void XlsxAdapter_Save_StackedBarChart_NoExplicitCrossBetween_StillDefaultsToBetween()
    {
        var chart = new ChartModel
        {
            Type = ChartType.StackedBar,
        };

        var chartXml = SaveChartXml(chart);
        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        valueAxis.Element(ChartNs + "crossBetween")!.Attribute("val")!.Value.Should().Be("between");
    }
}
