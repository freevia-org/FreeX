using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R92-meta-4: <see cref="ChartSeriesFormat.FillAlpha"/> (a series fill's authored transparency,
/// parsed from the chart XML's &lt;a:alpha&gt; and faithfully round-tripped on save by r91) was
/// never consumed by the desktop chart renderer -- every fill-color conversion built an opaque
/// <see cref="OxyColor"/> via <c>OxyColor.FromRgb</c>, dropping the authored alpha entirely. These
/// tests assert the renderer actually draws with the reduced alpha (ink is drawn transparent, not
/// just that the state is readable) across every series-format fill call site in
/// ChartRenderer.SeriesFormatting.cs plus the pie-chart series-level fill in ChartRenderer.cs.
/// </summary>
public sealed class R92_ChartSeriesFillAlphaTests
{
    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ViewportModel SimpleViewport() => new(
        [
            Cell(1, 1, "Quarter"),
            Cell(1, 2, "Revenue"),
            Cell(2, 1, "Q1"),
            Cell(2, 2, "10"),
            Cell(3, 1, "Q2"),
            Cell(3, 2, "20")
        ],
        [],
        []);

    [Fact]
    public void ColumnRenderer_AppliesSeriesFillAlphaToRectangleBarFillColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(112, 173, 71), FillAlpha: 0.5)
            ]
        };

        var model = BuildPlotModel(chart, SimpleViewport());

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.FillColor.A.Should().Be((byte)128, "50% authored alpha should render as a semi-transparent fill, not fully opaque");
        series.FillColor.R.Should().Be(112);
        series.FillColor.G.Should().Be(173);
        series.FillColor.B.Should().Be(71);
    }

    [Fact]
    public void ColumnRenderer_NoAlpha_RemainsFullyOpaque()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(112, 173, 71))
            ]
        };

        var model = BuildPlotModel(chart, SimpleViewport());

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.FillColor.A.Should().Be((byte)255, "no authored <a:alpha> should leave the fill fully opaque (no regression)");
    }

    [Fact]
    public void BarRenderer_AppliesSeriesFillAlphaToBarFillColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(200, 100, 50), FillAlpha: 0.25)
            ]
        };

        var model = BuildPlotModel(chart, SimpleViewport());

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.FillColor.A.Should().Be((byte)64);
    }

    [Fact]
    public void AreaRenderer_AppliesSeriesFillAlphaToAreaFill()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Area,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(91, 155, 213), FillAlpha: 0.5)
            ]
        };

        var model = BuildPlotModel(chart, SimpleViewport());

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<AreaSeries>().Subject;
        series.Fill.A.Should().Be((byte)128);
    }

    [Fact]
    public void PieRenderer_AppliesSeriesLevelFillAlphaToSliceFill()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(0x92, 0xD0, 0x50), FillAlpha: 0.4)
            ]
        };

        var model = BuildPlotModel(chart, SimpleViewport());

        var pie = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pie.Slices.Should().NotBeEmpty();
        foreach (var slice in pie.Slices)
            slice.Fill.A.Should().Be((byte)102, "0.4 authored alpha (0.4*255 rounded) should apply to every slice falling back to the series-level fill");
    }
}
