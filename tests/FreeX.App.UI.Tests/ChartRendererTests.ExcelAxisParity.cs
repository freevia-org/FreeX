using System.Globalization;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Axes;

namespace FreeX.App.UI.Tests;

// Pins the WPF/OxyPlot renderer's automatic axis scaling to values measured in real Microsoft Excel
// (COM Axis.MinimumScale / MaximumScale / MajorUnit on a 480x300 pt chart). The portable (Avalonia)
// layout engine is pinned to the same numbers in ChartExcelAxisParityTests (Presentation.Tests).
public sealed partial class ChartRendererTests
{
    public static IEnumerable<object[]> ExcelAxisCases()
    {
        // type, y values, x values (scatter), configure id, expected value axis (min, max, major), expected x axis
        yield return Case("col-basic", ChartType.Column, [10, 20, 30, 40], null, 0, 45, 5);
        yield return Case("col-nearflat", ChartType.Column, [95, 102, 98, 110, 105], null, 85, 115, 5);
        yield return Case("col-neg-mixed", ChartType.Column, [-5, -12, 8, 3], null, -15, 10, 5);
        yield return Case("col-neg-all", ChartType.Column, [-30, -20, -45], null, -50, 0, 5);
        yield return Case("line-basic", ChartType.Line, [3, 5, 4, 8, 7], null, 0, 9, 1);
        yield return Case("line-nearflat", ChartType.Line, [100, 102, 101, 105], null, 97, 106, 1);
        yield return Case("bar-basic", ChartType.Bar, [12, 45, 33, 28], null, 0, 50, 5);
        yield return Case("area-basic", ChartType.Area, [5, 15, 10, 20], null, 0, 25, 5);
        yield return Case("scatter-basic", ChartType.Scatter, [2.1, 3.9, 6.2, 8.1, 9.8, 12.3], [1, 2, 3, 4, 5, 6], 0, 14, 2, 0, 7, 1);
        yield return Case("col-err-fixed", ChartType.Column, [10, 20, 30, 40], null, 0, 50, 5);
        yield return Case("col-err-fixed-big", ChartType.Column, [10, 20, 30, 40], null, -20, 70, 10);
        yield return Case("col-err-pct", ChartType.Column, [10, 20, 30, 40], null, 0, 60, 10);
        yield return Case("col-err-stdev", ChartType.Column, [10, 20, 30, 40], null, 0, 45, 5);
        yield return Case("scatter-err-x", ChartType.Scatter, [2.1, 3.9, 6.2, 8.1, 9.8, 12.3], [1, 2, 3, 4, 5, 6], 0, 14, 2, -1, 8, 1);
        yield return Case("line-tl-lin-fwd2", ChartType.Line, [3, 5, 4, 8, 7], null, 0, 12, 2);
        yield return Case("col-tl-poly2-fwd3", ChartType.Column, [5, 12, 20, 22, 18], null, -15, 25, 5);
        yield return Case("col-tl-exp-fwd2", ChartType.Column, [2, 4, 8, 16, 32], null, 0, 140, 20);
        yield return Case("col-tl-power", ChartType.Column, [2, 7, 15, 27, 42], null, 0, 70, 10);
        yield return Case("scatter-tl-lin-fwd5", ChartType.Scatter, [2.1, 3.9, 6.2, 8.1, 9.8, 12.3], [1, 2, 3, 4, 5, 6], 0, 25, 5, 0, 12, 2);
        yield return Case("scatter-tl-lin-bwd2", ChartType.Scatter, [2.1, 3.9, 6.2, 8.1, 9.8, 12.3], [1, 2, 3, 4, 5, 6], -4, 14, 2, -2, 7, 1);
    }

    private static object[] Case(
        string id,
        ChartType type,
        double[] y,
        double[]? x,
        double min,
        double max,
        double major,
        double? xMin = null,
        double? xMax = null,
        double? xMajor = null) =>
        [id, type, y, x!, min, max, major, xMin!, xMax!, xMajor!];

    [Theory]
    [MemberData(nameof(ExcelAxisCases))]
    public void AutomaticValueAxis_MatchesExcel(
        string id,
        ChartType type,
        double[] y,
        double[]? x,
        double min,
        double max,
        double major,
        double? xMin,
        double? xMax,
        double? xMajor)
    {
        var chart = ExcelAxisChart(id, type, y.Length);
        var cells = new List<DisplayCell> { Cell(1, 1, type == ChartType.Scatter ? "X" : "Cat"), Cell(1, 2, "S1") };
        for (var i = 0; i < y.Length; i++)
        {
            cells.Add(Cell((uint)(i + 2), 1, x is null ? "C" + (i + 1) : x[i].ToString(CultureInfo.InvariantCulture)));
            cells.Add(Cell((uint)(i + 2), 2, y[i].ToString(CultureInfo.InvariantCulture)));
        }

        var model = BuildPlotModel(chart, new ViewportModel(cells, [], []));

        var valueAxis = model.Axes.Single(axis =>
            axis.GetType() == typeof(LinearAxis) &&
            axis.Position == (type == ChartType.Bar ? AxisPosition.Bottom : AxisPosition.Left));
        Describe(valueAxis).Should().Be($"{Fmt(min)}..{Fmt(max)} /{Fmt(major)}", id + " value axis");

        if (xMin is not null)
        {
            var xAxis = model.Axes.Single(axis => axis.GetType() == typeof(LinearAxis) && axis.Position == AxisPosition.Bottom);
            Describe(xAxis).Should().Be($"{Fmt(xMin.Value)}..{Fmt(xMax!.Value)} /{Fmt(xMajor!.Value)}", id + " x axis");
        }
    }

    [Fact]
    public void LineChart_ForwardForecast_AppendsCategorySlotsLikeExcel()
    {
        // Excel (480x300 pt, 5 categories): forward 2 -> 7 category slots, forward 1.5 -> 6, backward 0.5 -> 5.
        foreach (var (forward, backward, slots) in new (double?, double?, int)[] { (2, null, 7), (1.5, null, 6), (null, 0.5, 5), (1, 0.5, 6) })
        {
            var chart = ExcelAxisChart("line-tl-lin-fwd2", ChartType.Line, 5);
            chart.TrendlineForward = forward;
            chart.TrendlineBackward = backward;
            var cells = new List<DisplayCell> { Cell(1, 1, "Cat"), Cell(1, 2, "S1") };
            var values = new[] { 3, 5, 4, 8, 7 };
            for (var i = 0; i < values.Length; i++)
            {
                cells.Add(Cell((uint)(i + 2), 1, "C" + (i + 1)));
                cells.Add(Cell((uint)(i + 2), 2, values[i].ToString(CultureInfo.InvariantCulture)));
            }

            var model = BuildPlotModel(chart, new ViewportModel(cells, [], []));

            var category = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
            (category.Maximum - category.Minimum).Should().BeApproximately(slots, 1e-9, $"forward {forward} backward {backward}");
            category.Minimum.Should().BeApproximately(-0.5, 1e-9, "line points sit between tick marks");
        }
    }

    [Fact]
    public void LineChart_PointsSitBetweenTickMarks_UnlessAuthoredMidCat()
    {
        var cells = new List<DisplayCell> { Cell(1, 1, "Cat"), Cell(1, 2, "S1") };
        for (var i = 0; i < 3; i++)
        {
            cells.Add(Cell((uint)(i + 2), 1, "C" + (i + 1)));
            cells.Add(Cell((uint)(i + 2), 2, (i + 1).ToString(CultureInfo.InvariantCulture)));
        }

        var between = BuildPlotModel(ExcelAxisChart("line", ChartType.Line, 3), new ViewportModel(cells, [], []));
        var betweenAxis = between.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        (betweenAxis.Minimum, betweenAxis.Maximum).Should().Be((-0.5, 2.5));

        var onTicksChart = ExcelAxisChart("line", ChartType.Line, 3);
        onTicksChart.YAxisCrossBetween = ChartAxisCrossBetween.MidCategory;
        var onTicks = BuildPlotModel(onTicksChart, new ViewportModel(cells, [], []));
        var onTicksAxis = onTicks.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        (onTicksAxis.Minimum, onTicksAxis.Maximum).Should().Be((0.0, 2.0));
    }

    private static ChartModel ExcelAxisChart(string id, ChartType type, int points)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = type,
            FirstRowIsHeader = true,
            FirstColIsCategories = type != ChartType.Scatter,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, (uint)(points + 1), 2)),
        };

        if (id.Contains("-err-", StringComparison.Ordinal))
        {
            chart.ShowErrorBars = true;
            chart.ErrorBarKind = id switch
            {
                var name when name.EndsWith("pct", StringComparison.Ordinal) => ChartErrorBarKind.Percentage,
                var name when name.EndsWith("stdev", StringComparison.Ordinal) => ChartErrorBarKind.StdDev,
                _ => ChartErrorBarKind.FixedValue,
            };
            chart.ErrorBarValue = id switch
            {
                "col-err-fixed" => 5,
                "col-err-fixed-big" => 20,
                "col-err-pct" => 25,
                "col-err-stdev" => 1,
                "scatter-err-x" => 1.5,
                _ => 5,
            };
            if (id == "scatter-err-x")
                chart.ErrorBarAxisDirection = ChartErrorBarAxisDirection.X;
        }

        if (id.Contains("-tl-", StringComparison.Ordinal))
        {
            chart.ShowLinearTrendline = true;
            chart.TrendlineType = id switch
            {
                "col-tl-poly2-fwd3" => ChartTrendlineType.Polynomial,
                "col-tl-exp-fwd2" => ChartTrendlineType.Exponential,
                "col-tl-power" => ChartTrendlineType.Power,
                _ => ChartTrendlineType.Linear,
            };
            chart.TrendlineForward = id switch
            {
                "line-tl-lin-fwd2" => 2,
                "col-tl-poly2-fwd3" => 3,
                "col-tl-exp-fwd2" => 2,
                "col-tl-power" => 1,
                "scatter-tl-lin-fwd5" => 5,
                _ => null,
            };
            chart.TrendlineBackward = id == "scatter-tl-lin-bwd2" ? 2 : null;
        }

        return chart;
    }

    private static string Describe(Axis axis) =>
        $"{Fmt(axis.Minimum)}..{Fmt(axis.Maximum)} /{Fmt(axis.MajorStep)}";

    private static string Fmt(double value) =>
        Math.Round(value, 9).ToString("0.#########", CultureInfo.InvariantCulture);
}
