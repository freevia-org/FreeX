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
    public void XlsxAdapter_RoundTrip_BarChartCategoryReverseOrder_IsIdempotent()
    {
        // For a horizontal Bar chart the category axis is the Left axis (YAxisReverseOrder) and the
        // value axis is the Bottom axis (XAxisReverseOrder). Round-15 routed only the READER this way;
        // round-16 mirrored it on the WRITER so file->model->file is idempotent: a reversed category
        // axis (value normal) stays on the category axis across repeated save/load and never flips
        // onto the value axis.
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            YAxisReverseOrder = true, // category axis (left, for Bar) reversed
            XAxisReverseOrder = false, // value axis (bottom, for Bar) NOT reversed
        };

        var loadedOnce = RoundTrip(chart);
        loadedOnce.YAxisReverseOrder.Should().BeTrue();
        loadedOnce.XAxisReverseOrder.Should().BeFalse();

        var loadedTwice = RoundTrip(loadedOnce);
        loadedTwice.YAxisReverseOrder.Should().BeTrue();
        loadedTwice.XAxisReverseOrder.Should().BeFalse();
    }
}
