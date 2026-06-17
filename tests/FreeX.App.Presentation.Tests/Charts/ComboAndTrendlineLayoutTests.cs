using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ComboAndTrendlineLayoutTests
{
    // ---- Secondary value axis -------------------------------------------------------------

    [Fact]
    public void No_secondary_axis_by_default()
    {
        var chart = Chart(ChartType.Column);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 1, 2)]));

        layout.SecondaryValueAxis.Should().BeNull();
        layout.Series.Should().OnlyContain(s => !s.UsesSecondaryAxis);
    }

    [Fact]
    public void Secondary_axis_is_built_and_assigned_series_are_flagged()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 2)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Side.Should().Be(AxisSide.Right);

        layout.Series[0].UsesSecondaryAxis.Should().BeFalse();
        layout.Series[1].UsesSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Secondary_axis_with_no_explicit_list_sends_all_but_first_series_secondary()
    {
        var chart = Chart(ChartType.Line, c => c.ShowSecondaryAxis = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 1, 2), Series(2, "S3", 3, 4)]));

        layout.Series[0].UsesSecondaryAxis.Should().BeFalse();
        layout.Series[1].UsesSecondaryAxis.Should().BeTrue();
        layout.Series[2].UsesSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Secondary_axis_scale_is_driven_by_the_secondary_series_range()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
        });
        // Primary range ~ [0, 1000]; secondary range ~ [0, 5]. A point of value 5 on the secondary
        // axis must land near the top of the plot, not near the bottom (which the primary scale gives).
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 1000, 800), Series(1, "Secondary", 5, 4)],
            new PlotRect(0, 0, 200, 100)));

        var secondaryColumn = layout.Series[1].Bars[0];
        // The bar's top (smaller Y) should be well above the plot mid-line because 5 ≈ axis max.
        secondaryColumn.Rect.Top.Should().BeLessThan(40);
    }

    [Fact]
    public void Secondary_axis_ignored_when_only_one_series()
    {
        var chart = Chart(ChartType.Column, c => c.ShowSecondaryAxis = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 10, 20)]));
        layout.SecondaryValueAxis.Should().BeNull();
    }

    // ---- Trendline overlay ----------------------------------------------------------------

    [Fact]
    public void No_trendline_overlay_by_default()
    {
        var chart = Chart(ChartType.Line);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]));
        layout.Series[0].Trendline.Should().BeNull();
    }

    [Fact]
    public void Linear_trendline_overlay_attaches_to_first_series_in_pixel_space()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 2, 3, 4)]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Fit.Should().Be(TrendlineFitKind.Linear);
        trend.Points.Should().HaveCount(2);

        // For a rising series the trendline rises on screen (later x is higher => smaller Y).
        trend.Points[1].X.Should().BeGreaterThan(trend.Points[0].X);
        trend.Points[1].Y.Should().BeLessThan(trend.Points[0].Y);
    }

    [Fact]
    public void Trendline_endpoints_align_with_the_category_scale()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var plot = new PlotRect(0, 0, 300, 100);
        var request = Request(chart, ["A", "B", "C"], [Series(0, "S1", 2, 4, 6)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var trend = layout.Series[0].Trendline!;
        var seriesPoints = layout.Series[0].Points;
        // The trendline starts at the first category x and ends at the last category x.
        trend.Points[0].X.Should().BeApproximately(seriesPoints[0].Position.X, 1e-6);
        trend.Points[^1].X.Should().BeApproximately(seriesPoints[^1].Position.X, 1e-6);
    }

    [Fact]
    public void Moving_average_overlay_records_its_fit_kind()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.MovingAverage;
            c.TrendlinePeriod = 2;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 3, 2, 6)]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Fit.Should().Be(TrendlineFitKind.MovingAverage);
        trend.Points.Should().HaveCount(3);
    }

    [Fact]
    public void Scatter_trendline_uses_explicit_x_values()
    {
        var chart = Chart(ChartType.Scatter, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var series = ScatterSeries(0, "S1", [10, 20, 30], 5, 10, 15);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Points.Should().HaveCount(2);
    }

    [Fact]
    public void Trendline_not_attached_for_unsupported_chart_type()
    {
        var chart = Chart(ChartType.Pie, c => c.ShowLinearTrendline = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 10, 20)]));
        layout.Series.Should().OnlyContain(s => s.Trendline == null);
    }
}
