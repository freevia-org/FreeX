using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// WPF renderer regressions for the chart-type differences measured against real Excel (COM): column + area
/// combos, the scatter-lines legend swatch, filled radar polygons and the stock chart axis / legend.
/// </summary>
public sealed partial class ChartRendererTests
{
    [Fact]
    public void ColumnAreaCombo_DrawsTheAreaSeriesBehindTheColumnsAndKeepsItOutOfTheClusterSlots()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3)),
            ComboAreaSeriesIndexes = [1]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Period"), Cell(1, 2, "North"), Cell(1, 3, "South"),
                Cell(2, 1, "Q1"), Cell(2, 2, "12"), Cell(2, 3, "8"),
                Cell(3, 1, "Q2"), Cell(3, 2, "19"), Cell(3, 3, "15"),
                Cell(4, 1, "Q3"), Cell(4, 2, "14"), Cell(4, 3, "21")
            ],
            [],
            []));

        // z-order: the area is added first (behind), the columns after it.
        var area = model.Series[0].Should().BeOfType<AreaSeries>().Subject;
        area.Title.Should().Be("South");
        area.Points.Select(p => p.Y).Should().Equal(8, 15, 21);
        var columns = model.Series[1].Should().BeOfType<RectangleBarSeries>().Subject;
        columns.Title.Should().Be("North");
        // The only remaining column series owns the whole category slot.
        // Excel default gap width 219 %: one column is 1 / 3.19 of the category slot wide.
        (columns.Items[0].X1 - columns.Items[0].X0).Should().BeApproximately(1 / 3.19, 1e-3);
    }

    [Fact]
    public void ScatterWithLines_LegendSwatchCarriesTheConnectingLine()
    {
        var sheetId = SheetId.New();
        var blue = new CellColor(0x15, 0x60, 0x82);
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, StrokeColor: blue, StrokeThickness: 1.5)]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"), Cell(1, 2, "North"),
                Cell(2, 1, "1"), Cell(2, 2, "12"),
                Cell(3, 1, "2.5"), Cell(3, 2, "19"),
                Cell(4, 1, "4"), Cell(4, 2, "14")
            ],
            [],
            []));

        var scatter = model.Series.OfType<ScatterSeries>().Should().ContainSingle().Subject;
        var legendLine = scatter.GetType().GetProperty("LegendLineColor")!.GetValue(scatter);
        legendLine.Should().Be((OxyColor?)OxyColor.FromRgb(blue.R, blue.G, blue.B),
            "Excel's legend entry for a scatter-with-lines series is a line through the marker");
    }

    [Fact]
    public void FilledRadar_PaintsTheEnclosedPolygon()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Radar,
            RadarFilled = true,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Metric"), Cell(1, 2, "North"),
                Cell(2, 1, "Q1"), Cell(2, 2, "12"),
                Cell(3, 1, "Q2"), Cell(3, 2, "19"),
                Cell(4, 1, "Q3"), Cell(4, 2, "14"),
                Cell(5, 1, "Q4"), Cell(5, 2, "22")
            ],
            [],
            []));

        var series = model.Series.OfType<LineSeries>().Should().ContainSingle().Subject;
        series.GetType().Name.Should().Be("FilledRadarSeries", "OxyPlot's polar plot has no area series of its own");
        series.MarkerType.Should().Be(MarkerType.None);
    }

    [Theory]
    [InlineData(18, 29, true)]
    [InlineData(100, 110, false)]
    public void Stock_ValueAxisFollowsExcelsAutomaticZeroBaselineRule(double low, double high, bool expectZero)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Day"), Cell(1, 2, "High"), Cell(1, 3, "Low"), Cell(1, 4, "Close"),
                Cell(2, 1, "Q1"), Cell(2, 2, high.ToString("0")), Cell(2, 3, low.ToString("0")), Cell(2, 4, ((low + high) / 2).ToString("0")),
                Cell(3, 1, "Q2"), Cell(3, 2, (high - 1).ToString("0")), Cell(3, 3, (low + 1).ToString("0")), Cell(3, 4, ((low + high) / 2).ToString("0"))
            ],
            [],
            []));

        var value = model.Axes.OfType<LinearAxis>().Single(axis => axis.Position == AxisPosition.Left);
        if (expectZero)
            value.Minimum.Should().Be(0);
        else
            value.Minimum.Should().BeGreaterThan(0, "Excel does not force a zero baseline when the data spread is narrow");
    }

    [Fact]
    public void Histogram_UsesExcelBinsWithEdgeToEdgeColumns()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Histogram,
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 21, 1))
        };
        double[] values = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 7, 8, 9, 9, 10, 11, 12, 12, 15, 18];
        var cells = new List<DisplayCell> { Cell(1, 1, "Value") };
        for (var i = 0; i < values.Length; i++)
            cells.Add(Cell((uint)(i + 2), 1, values[i].ToString("0")));

        var model = BuildPlotModel(chart, new ViewportModel(cells, [], []));

        var bars = model.Series.OfType<RectangleBarSeries>().Should().ContainSingle().Subject;
        bars.Items.Select(item => item.Y1).Should().Equal(11, 7, 2);
        bars.Items[0].X1.Should().Be(bars.Items[1].X0, "Excel draws histogram columns with a zero gap");
        model.Axes.OfType<CategoryAxis>().Single().Labels.Should().Equal("[2, 7.6]", "(7.6, 13.2]", "(13.2, 18.8]");
    }
}
