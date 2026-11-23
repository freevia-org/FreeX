using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Pins the portable chart axis scaling to values measured in real Microsoft Excel (COM
/// <c>Axis.MinimumScale/MaximumScale/MajorUnit</c>, see <see cref="ExcelAxisGroundTruth"/>): the
/// automatic value-axis algorithm, the axis extension for error bars and trendline forecasts, and the
/// category-slot extension a Forward forecast adds.
/// </summary>
public sealed class ChartExcelAxisParityTests
{
    // Roughly Excel's 480x300 pt chart plot area (410 x 233 pt) in DIP.
    private static readonly PlotRect LargePlot = new(0, 0, 547, 311);

    public static IEnumerable<object[]> AutoScaleRows() =>
        ExcelAxisGroundTruth.AutoScale.Select((row, index) => new object[] { index });

    [Theory]
    [MemberData(nameof(AutoScaleRows))]
    public void Automatic_value_axis_matches_excel(int row)
    {
        var (values, min, max, major) = ExcelAxisGroundTruth.AutoScale[row];

        var range = ExcelAxisAutoScale.Compute(values.Min(), values.Max());

        Fmt(range.Minimum).Should().Be(Fmt(min), Describe(values));
        Fmt(range.Maximum).Should().Be(Fmt(max), Describe(values));
        Fmt(range.MajorUnit).Should().Be(Fmt(major), Describe(values));
    }

    public static IEnumerable<object[]> BatteryRows() =>
        ExcelAxisGroundTruth.Cases.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(BatteryRows))]
    public void Layout_axis_scale_matches_excel(string id)
    {
        var excel = ExcelAxisGroundTruth.Cases.Single(c => c.Id == id);
        var layout = ChartLayoutEngine.Layout(Build(excel));

        var (value, x) = AxisScales(layout, excel.Type);
        Triple(value).Should().Be($"{Fmt(excel.ValMin)}..{Fmt(excel.ValMax)} /{Fmt(excel.ValMajor)}", id + " value axis");
        if (excel.XMin is not null)
            Triple(x!).Should().Be($"{Fmt(excel.XMin.Value)}..{Fmt(excel.XMax!.Value)} /{Fmt(excel.XMajor!.Value)}", id + " x axis");
        if (excel.Val2Min is not null)
        {
            layout.SecondaryValueAxis.Should().NotBeNull(id);
            Triple(layout.SecondaryValueAxis!.Scale).Should().Be(
                $"{Fmt(excel.Val2Min.Value)}..{Fmt(excel.Val2Max!.Value)} /{Fmt(excel.Val2Major!.Value)}", id + " secondary axis");
        }
    }

    // Forward forecast category slots measured in Excel (480x300 chart, 5 categories): the plot is divided
    // into n + ceil(forward - 0.5) slots for columns/lines/bars (points sit between tick marks), a
    // half-period backcast adds none, and an area chart (points on the tick marks) gets n - 1 + ceil(forward) slots.
    [Theory]
    [InlineData(ChartType.Line, 2.0, null, 7)]
    [InlineData(ChartType.Line, 1.5, null, 6)]
    [InlineData(ChartType.Line, null, 0.5, 5)]
    [InlineData(ChartType.Line, 1.0, 0.5, 6)]
    [InlineData(ChartType.Column, 2.0, null, 7)]
    [InlineData(ChartType.Column, 1.5, null, 6)]
    [InlineData(ChartType.Column, null, 0.5, 5)]
    [InlineData(ChartType.Bar, 2.0, null, 7)]
    [InlineData(ChartType.Bar, null, 0.5, 5)]
    public void Forecast_adds_category_slots_between_ticks_like_excel(ChartType type, double? forward, double? backward, int expectedSlots)
    {
        var chart = Chart(type, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineForward = forward;
            c.TrendlineBackward = backward;
        });
        var layout = ChartLayoutEngine.Layout(ChartLayoutTestData.Request(
            chart, ["A", "B", "C", "D", "E"], [ChartLayoutTestData.Series(0, "S1", 3, 5, 4, 8, 7)], LargePlot));

        var scale = layout.CategoryAxis!.Scale;
        (scale.Maximum - scale.Minimum).Should().BeApproximately(expectedSlots, 1e-9);
        scale.Minimum.Should().BeApproximately(-0.5, 1e-9);
    }

    [Fact]
    public void Area_forecast_extends_the_tick_mark_axis_by_whole_periods_like_excel()
    {
        var chart = Chart(ChartType.Area, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineForward = 2;
        });
        var layout = ChartLayoutEngine.Layout(ChartLayoutTestData.Request(
            chart, ["A", "B", "C", "D"], [ChartLayoutTestData.Series(0, "S1", 5, 15, 10, 20)], LargePlot));

        var scale = layout.CategoryAxis!.Scale;
        scale.Minimum.Should().BeApproximately(0, 1e-9);
        scale.Maximum.Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void Line_points_sit_between_tick_marks_like_excel()
    {
        // Excel line chart, 5 categories, 401.9 pt wide plot: first marker centre is half a slot in.
        var layout = ChartLayoutEngine.Layout(ChartLayoutTestData.Request(
            Chart(ChartType.Line), ["A", "B", "C", "D", "E"], [ChartLayoutTestData.Series(0, "S1", 3, 5, 4, 8, 7)], LargePlot));

        var points = layout.Series[0].Points;
        var slot = LargePlot.Width / 5;
        points[0].Position.X.Should().BeApproximately(slot / 2, 1e-6);
        points[4].Position.X.Should().BeApproximately(LargePlot.Width - (slot / 2), 1e-6);
    }

    [Fact]
    public void Standard_deviation_error_bars_hang_off_the_series_mean_like_excel()
    {
        // Excel draws every point's whisker as mean +- k*sd (12.1..37.9 for 10,20,30,40 with k=1).
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.ErrorBarKind = ChartErrorBarKind.StdDev;
            c.ErrorBarValue = 1;
        });
        var layout = ChartLayoutEngine.Layout(ChartLayoutTestData.Request(
            chart, ["A", "B", "C", "D"], [ChartLayoutTestData.Series(0, "S1", 10, 20, 30, 40)], LargePlot));

        var scale = layout.ValueAxis!.Scale;
        var whiskers = layout.Series[0].ErrorBars!.Whiskers;
        var sd = Math.Sqrt(((15 * 15) + (5 * 5) + (5 * 5) + (15 * 15)) / 3.0);
        foreach (var whisker in whiskers)
        {
            scale.InverseTransform(whisker.PlusEnd.Y).Should().BeApproximately(25 + sd, 1e-6);
            scale.InverseTransform(whisker.MinusEnd.Y).Should().BeApproximately(25 - sd, 1e-6);
        }

        // 37.9 stays inside the automatic 0..45 axis exactly like Excel (no extension for the whisker).
        Triple(scale).Should().Be("0..45 /5");
    }

    [Fact]
    public void Small_plots_get_fewer_axis_intervals_like_excel()
    {
        // Excel (inside plot height pt -> intervals): 43 -> 2, 63 -> 4, 83 -> 5, 103 -> 7, 143+ -> 10.
        ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(63 / 0.75).Should().Be(4);
        ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(83 / 0.75).Should().Be(5);
        ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(103 / 0.75).Should().Be(7);
        ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(233 / 0.75).Should().Be(10);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(76 / 0.75).Should().Be(2);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(146 / 0.75).Should().Be(4);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(226 / 0.75).Should().Be(6);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(342 / 0.75).Should().Be(9);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(358 / 0.75).Should().Be(9);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(372 / 0.75).Should().Be(10);
        ExcelAxisAutoScale.MaxIntervalsForHorizontalExtent(442 / 0.75).Should().Be(10);

        // 0..10.5 on a 113 pt tall plot: Excel picks major 2 (6 intervals), not the 10-interval major 1.
        var small = ExcelAxisAutoScale.Compute(0, 10, ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(113 / 0.75));
        small.MajorUnit.Should().Be(2);
        small.Maximum.Should().Be(12);
        // ... and on a 53 pt tall plot major 5 (3 intervals).
        var tiny = ExcelAxisAutoScale.Compute(0, 10, ExcelAxisAutoScale.MaxIntervalsForVerticalExtent(53 / 0.75));
        tiny.MajorUnit.Should().Be(5);
        tiny.Maximum.Should().Be(15);
    }

    private static (AxisScale Value, AxisScale? X) AxisScales(ChartLayout layout, ChartType type)
    {
        if (type == ChartType.Scatter)
            return (layout.ValueAxis!.Scale, layout.CategoryAxis!.Scale);
        return (layout.ValueAxis!.Scale, null);
    }

    private static string Triple(AxisScale scale) =>
        $"{Fmt(scale.Minimum)}..{Fmt(scale.Maximum)} /{Fmt(scale.MajorStep)}";

    private static string Fmt(double value) => Math.Round(value, 9).ToString("0.#########", CultureInfo.InvariantCulture);

    private static string Describe(IEnumerable<double> values) =>
        "data " + string.Join(",", values.Select(Fmt));

    private static ChartModel Chart(ChartType type, Action<ChartModel>? configure = null) =>
        ChartLayoutTestData.Chart(type, configure);

    private static ChartLayoutRequest Build(ExcelAxisCase excel)
    {
        var chart = ChartLayoutTestData.Chart(excel.Type);
        var secondary = new List<int>();
        for (var i = 0; i < excel.Series.Length; i++)
        {
            var series = excel.Series[i];
            if (series.Secondary)
                secondary.Add(i);
            if (series.Err is { } err)
            {
                chart.ShowErrorBars = true;
                chart.ErrorBarKind = err.Kind;
                chart.ErrorBarValue = err.Amount;
                chart.ErrorBarAxisDirection = err.AlongX ? ChartErrorBarAxisDirection.X : ChartErrorBarAxisDirection.Y;
                chart.ErrorBarDirection = err.Include == 2 ? ChartErrorBarDirection.Plus : ChartErrorBarDirection.Both;
                if (err.Custom is { } custom)
                {
                    var cache = CacheXml(custom);
                    chart.ErrorBarPlusRangeCacheXml = cache;
                    chart.ErrorBarMinusRangeCacheXml = cache;
                }
            }

            if (series.Trend is { } trend)
            {
                chart.ShowLinearTrendline = true;
                chart.TrendlineType = trend.Type;
                chart.TrendlineOrder = trend.Order;
                chart.TrendlinePeriod = trend.Period;
                chart.TrendlineForward = trend.Forward;
                chart.TrendlineBackward = trend.Backward;
            }
        }

        if (secondary.Count > 0)
        {
            chart.ShowSecondaryAxis = true;
            chart.SecondaryAxisSeriesIndexes = secondary;
        }

        var count = excel.Series[0].Y.Length;
        var categories = Enumerable.Range(1, count).Select(i => "C" + i).ToList();
        var data = excel.Series
            .Select((s, i) => new ChartSeriesData
            {
                SeriesIndex = i,
                Name = "S" + (i + 1),
                Values = s.Y.Select(v => (double?)v).ToArray(),
                XValues = s.X,
            })
            .ToArray();
        // Excel's own plot-area size (points) in DIP, so the axis interval budget matches the measured chart.
        var plot = new PlotRect(0, 0, excel.PlotWidthPt * 4 / 3, excel.PlotHeightPt * 4 / 3);
        return ChartLayoutTestData.Request(chart, categories, data, plot);
    }

    private static string CacheXml(double[] values)
    {
        var xml = new StringBuilder("<numCache>");
        for (var i = 0; i < values.Length; i++)
            xml.Append(CultureInfo.InvariantCulture, $"<pt idx=\"{i}\"><v>{values[i]}</v></pt>");
        return xml.Append("</numCache>").ToString();
    }
}
