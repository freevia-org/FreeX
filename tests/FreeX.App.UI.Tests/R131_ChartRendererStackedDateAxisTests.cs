using System;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R131-REMEDIATION (WPF-family gap): the r131 date-category-axis fix (<see cref="ChartModel.XAxisIsDateAxis"/>
/// plotting proportionally instead of at the plain 0,1,2… index) was wired only into the main
/// non-stacked Column/Area/Line loop in ChartRenderer.cs. ChartRenderer.Stacked.cs builds its own
/// category axes independently (BuildStackedColumnModel / BuildStackedAreaModel) and never consulted
/// XAxisIsDateAxis, so an unevenly dated STACKED column or area chart still plotted its segments at
/// the plain evenly-spaced index. These tests pin the fix directly on the stacked builders.
/// </summary>
public sealed class R131_ChartRendererStackedDateAxisTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1);
    private static readonly DateTime Day2 = new(2026, 1, 2);
    private static readonly DateTime Day10 = new(2026, 1, 10);

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]).Should().BeOfType<PlotModel>().Subject;
    }

    private static ViewportModel BuildDateViewport(SheetId sheetId) => new(
        [
            Cell(1, 1, "Date"),
            Cell(1, 2, "S1"),
            Cell(2, 1, "2026-01-01"),
            Cell(2, 2, "10"),
            Cell(3, 1, "2026-01-02"),
            Cell(3, 2, "20"),
            Cell(4, 1, "2026-01-10"),
            Cell(4, 2, "30")
        ],
        [],
        []);

    [Fact]
    public void StackedColumnRenderer_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            XAxisIsDateAxis = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, BuildDateViewport(sheetId));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle(s => s is RectangleBarSeries).Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(3);

        var expectedDay1 = DateTimeAxis.ToDouble(Day1);
        var expectedDay2 = DateTimeAxis.ToDouble(Day2);
        var expectedDay10 = DateTimeAxis.ToDouble(Day10);

        ((series.Items[0].X0 + series.Items[0].X1) / 2).Should().BeApproximately(expectedDay1, 0.01);
        ((series.Items[1].X0 + series.Items[1].X1) / 2).Should().BeApproximately(expectedDay2, 0.01);
        ((series.Items[2].X0 + series.Items[2].X1) / 2).Should().BeApproximately(expectedDay10, 0.01);

        // The 1-day gap (bar 0 -> bar 1) must be far smaller than the 8-day gap (bar 1 -> bar 2) --
        // an evenly spaced index axis would make both gaps equal (1 category-unit each).
        var firstGap = expectedDay2 - expectedDay1;
        var secondGap = expectedDay10 - expectedDay2;
        secondGap.Should().BeApproximately(8 * firstGap, 0.01);
    }

    // Sibling of StackedColumnRenderer_DateCategoryAxis_PlotsProportionalToActualDates: a chart that
    // never opted into a date axis (XAxisIsDateAxis stays false) must keep the plain evenly spaced
    // index axis -- proving the fix cannot widen past its own XAxisIsDateAxis guard.
    [Fact]
    public void StackedColumnRenderer_PlainTextCategoryAxis_StaysEvenlySpacedIndexAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "S1"),
                Cell(2, 1, "North"),
                Cell(2, 2, "10"),
                Cell(3, 1, "South"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().NotBeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle(s => s is RectangleBarSeries).Which.Should().BeOfType<RectangleBarSeries>().Subject;
        ((series.Items[0].X0 + series.Items[0].X1) / 2).Should().BeApproximately(0, 0.01);
        ((series.Items[1].X0 + series.Items[1].X1) / 2).Should().BeApproximately(1, 0.01);
    }

    [Fact]
    public void StackedAreaRenderer_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedArea,
            XAxisIsDateAxis = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, BuildDateViewport(sheetId));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle(s => s is AreaSeries).Which.Should().BeOfType<AreaSeries>().Subject;
        series.Points.Should().HaveCount(3);

        var expectedDay1 = DateTimeAxis.ToDouble(Day1);
        var expectedDay2 = DateTimeAxis.ToDouble(Day2);
        var expectedDay10 = DateTimeAxis.ToDouble(Day10);

        var firstGap = series.Points[1].X - series.Points[0].X;
        var secondGap = series.Points[2].X - series.Points[1].X;
        series.Points[0].X.Should().BeApproximately(expectedDay1, 0.01);
        series.Points[1].X.Should().BeApproximately(expectedDay2, 0.01);
        series.Points[2].X.Should().BeApproximately(expectedDay10, 0.01);
        secondGap.Should().BeApproximately(8 * firstGap, 0.01);
    }

    // Sibling guard test for the area path: a plain (non-date) stacked area chart must keep its
    // original zero-based indexed category axis exactly as before this fix.
    [Fact]
    public void StackedAreaRenderer_PlainTextCategoryAxis_StaysEvenlySpacedIndexAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedArea,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "S1"),
                Cell(2, 1, "North"),
                Cell(2, 2, "10"),
                Cell(3, 1, "South"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().NotBeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle(s => s is AreaSeries).Which.Should().BeOfType<AreaSeries>().Subject;
        series.Points[0].X.Should().BeApproximately(0, 0.01);
        series.Points[1].X.Should().BeApproximately(1, 0.01);
    }
}
