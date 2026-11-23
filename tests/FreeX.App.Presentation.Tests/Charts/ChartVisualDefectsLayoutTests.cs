using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Visual-parity fixes found by rendering both hosts offscreen next to Excel: trendline legend text,
/// value axis spanning error bars, whiskers centred on clustered bars, and forecast clipping.
/// </summary>
public sealed class ChartVisualDefectsLayoutTests
{
    private static readonly PlotRect Inset = new(50, 40, 300, 200);

    [Theory]
    [InlineData(ChartTrendlineType.Linear, "Linear (Sales)")]
    [InlineData(ChartTrendlineType.Exponential, "Expon. (Sales)")]
    [InlineData(ChartTrendlineType.Logarithmic, "Log. (Sales)")]
    [InlineData(ChartTrendlineType.Polynomial, "Poly. (Sales)")]
    [InlineData(ChartTrendlineType.Power, "Power (Sales)")]
    public void TrendlineLegendName_matchesExcel(ChartTrendlineType type, string expected)
    {
        var chart = Chart(ChartType.Line, c => c.TrendlineType = type);

        TrendlineAnnotationFormatter.GetLegendName(chart, "Sales").Should().Be(expected);
    }

    [Fact]
    public void TrendlineLegendName_movingAverageUsesPeriod_andCustomNameWins()
    {
        var moving = Chart(ChartType.Line, c =>
        {
            c.TrendlineType = ChartTrendlineType.MovingAverage;
            c.TrendlinePeriod = 3;
        });
        TrendlineAnnotationFormatter.GetLegendName(moving, "Sales").Should().Be("3 per. Mov. Avg. (Sales)");

        var named = Chart(ChartType.Line, c => c.TrendlineName = "My fit");
        TrendlineAnnotationFormatter.GetLegendName(named, "Sales").Should().Be("My fit");
    }

    [Fact]
    public void Legend_listsEachSeriesTrendlineAfterTheSeries_withExcelNames()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.ShowLinearTrendline = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(
            chart,
            ["Q1", "Q2", "Q3"],
            [Series(0, "North", 1, 2, 3), Series(1, "South", 3, 2, 1)],
            Inset));

        layout.Legend.Entries.Select(e => e.Label).Should()
            .Equal("North", "South", "Linear (North)", "Linear (South)");
        layout.Legend.Entries.Where(e => e.IsTrendline).Should().HaveCount(2);
        layout.Series[0].Trendline!.Label.Should().Be("Linear (North)");
    }

    [Fact]
    public void ValueAxis_spansErrorBarEnds()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.ErrorBarKind = ChartErrorBarKind.FixedValue;
            c.ErrorBarValue = 7;
        });
        var layout = ChartLayoutEngine.Layout(Request(
            chart,
            ["Q1", "Q2"],
            [Series(0, "North", 10, 30)],
            Inset));

        var whisker = layout.Series[0].ErrorBars!.Whiskers[1];
        whisker.PlusEnd.Y.Should().BeGreaterThanOrEqualTo(Inset.Top - 0.001, "the 30 + 7 whisker must not be clipped by the plot top");
        layout.ValueAxis!.Scale.Maximum.Should().BeGreaterThanOrEqualTo(37);
    }

    [Fact]
    public void ErrorBars_onClusteredColumns_sitOnTheBarCentre()
    {
        var chart = Chart(ChartType.Column, c => c.ShowErrorBars = true);
        var layout = ChartLayoutEngine.Layout(Request(
            chart,
            ["Q1", "Q2"],
            [Series(0, "North", 10, 14), Series(1, "South", 30, 26)],
            Inset));

        foreach (var series in layout.Series)
        {
            foreach (var whisker in series.ErrorBars!.Whiskers)
            {
                var bar = series.Bars.Single(b => b.PointIndex == whisker.PointIndex);
                whisker.Center.X.Should().BeApproximately(bar.Rect.Center.X, 0.001);
            }
        }
    }

    [Fact]
    public void Trendline_withForwardForecast_stopsAtThePlotEdge()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.TrendlineForward = 3;
        });
        var layout = ChartLayoutEngine.Layout(Request(
            chart,
            ["Q1", "Q2", "Q3", "Q4"],
            [Series(0, "North", 10, 20, 30, 45)],
            Inset));

        var points = layout.Series[0].Trendline!.Points;
        points.Count.Should().BeGreaterThanOrEqualTo(2);
        points.Should().OnlyContain(p => p.X <= Inset.Right + 0.6 && p.X >= Inset.Left - 0.6
            && p.Y <= Inset.Bottom + 0.6 && p.Y >= Inset.Top - 0.6);
        points[^1].X.Should().BeApproximately(Inset.Right, 0.6, "the forecast runs to, not past, the plot edge");
    }
}
