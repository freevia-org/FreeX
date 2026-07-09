using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 15 chart-axis-scaling findings:
///  - R15-chart-axis-scaling-1: bar-family value-axis min/max/majorUnit/minorUnit/logBase must be
///    written from the X* fields (where the reader/sanitizer keep them), not the Y* fields.
///  - R15-chart-axis-scaling-2: a date category axis's numeric majorUnit/minorUnit must be read back,
///    not just its baseTimeUnit/majorTimeUnit/minorTimeUnit.
///  - R15-chart-axis-scaling-3: the category axis's reverse-order flag must be routed to the field the
///    renderer reads for the category axis's physical position, and must not be clobbered by the
///    subsequent value-axis read pass.
/// </summary>
public sealed class R15_chart_Tests
{
    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R15ChartAxisScaling");
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

    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesHorizontalBarChartValueAxisBounds()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            XAxisMinimum = 0,
            XAxisMaximum = 1,
            XAxisMajorUnit = 0.2,
        };

        var loaded = RoundTrip(chart);

        // Pre-fix, the writer always emitted these from Y*, so a Bar chart's value-axis bounds
        // (which live in X*) were never serialized and came back null.
        loaded.XAxisMinimum.Should().Be(0);
        loaded.XAxisMaximum.Should().Be(1);
        loaded.XAxisMajorUnit.Should().Be(0.2);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesDateAxisNumericMajorUnit()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            XAxisIsDateAxis = true,
            XAxisMajorUnit = 2,
        };

        var loaded = RoundTrip(chart);

        loaded.XAxisIsDateAxis.Should().BeTrue();
        // Pre-fix, the dateAx branch only read baseTimeUnit/majorTimeUnit/minorTimeUnit, so the
        // numeric majorUnit the writer emits (ToAxisUnitXml("majorUnit", chart.XAxisMajorUnit, ...))
        // was silently dropped on read.
        loaded.XAxisMajorUnit.Should().Be(2);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_BarChartCategoryReverseOrder_RoutesToCorrectAxisAndIsNotClobbered()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            XAxisReverseOrder = true, // writer always serializes catAx orientation from XAxisReverseOrder
            YAxisReverseOrder = false, // the value axis (bottom, for a Bar chart) is NOT reversed
        };

        var loaded = RoundTrip(chart);

        // The renderer applies YAxisReverseOrder to the Left/category axis and XAxisReverseOrder to
        // the Bottom/value axis. For a Bar chart the category axis IS the Left axis, so a reversed
        // catAx must come back as YAxisReverseOrder=true.
        loaded.YAxisReverseOrder.Should().BeTrue(
            "the category axis is rendered on the left for a Bar chart, and the renderer reads that axis's reverse flag from YAxisReverseOrder");
        // And it must not have been clobbered back to false, nor bled into XAxisReverseOrder, by the
        // subsequent value-axis read pass (which legitimately owns XAxisReverseOrder for Bar charts).
        loaded.XAxisReverseOrder.Should().BeFalse(
            "the value axis (bottom) was not reversed, and must not inherit the category axis's reverse flag");
    }
}
