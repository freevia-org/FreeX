using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class TrendlineProjectionPlannerTests
{
    [Fact]
    public void Plan_applies_a_fixed_linear_intercept()
    {
        var chart = Chart(ChartTrendlineType.Linear);
        chart.TrendlineIntercept = 5;

        var plan = TrendlineProjectionPlanner.Plan(chart, Points((1, 9), (2, 13), (3, 17)));

        plan.Should().NotBeNull();
        plan!.Points.Should().HaveCount(2);
        var first = plan.Points[0];
        var last = plan.Points[^1];
        var slope = (last.Y - first.Y) / (last.X - first.X);
        (first.Y - (slope * first.X)).Should().BeApproximately(5, 1e-9);
    }

    [Theory]
    [InlineData(ChartTrendlineType.Linear)]
    [InlineData(ChartTrendlineType.Exponential)]
    [InlineData(ChartTrendlineType.Logarithmic)]
    [InlineData(ChartTrendlineType.Power)]
    public void Plan_extrapolates_forward_and_backward_with_the_fit_shape(ChartTrendlineType type)
    {
        var chart = Chart(type);
        chart.TrendlineForward = 2;
        chart.TrendlineBackward = 0.5;
        double Fit(double x) => type switch
        {
            ChartTrendlineType.Exponential => 2 * Math.Exp(0.5 * x),
            ChartTrendlineType.Logarithmic => 3 + (2 * Math.Log(x)),
            ChartTrendlineType.Power => 1.5 * Math.Pow(x, 2),
            _ => 4 * x + 5,
        };
        var source = Points((1, Fit(1)), (2, Fit(2)), (3, Fit(3)), (4, Fit(4)));

        var plan = TrendlineProjectionPlanner.Plan(chart, source);

        plan.Should().NotBeNull();
        plan!.Points[0].X.Should().BeApproximately(0.5, 1e-9);
        plan.Points[0].Y.Should().BeApproximately(Fit(0.5), 1e-6);
        plan.Points[^1].X.Should().BeApproximately(6, 1e-9);
        plan.Points[^1].Y.Should().BeApproximately(Fit(6), 1e-5);
    }

    [Fact]
    public void Moving_average_ignores_forecast_options()
    {
        var chart = Chart(ChartTrendlineType.MovingAverage);
        chart.TrendlinePeriod = 2;
        chart.TrendlineForward = 2;
        chart.TrendlineBackward = 1;

        var plan = TrendlineProjectionPlanner.Plan(chart, Points((0, 2), (1, 4), (2, 8), (3, 16)));

        plan.Should().NotBeNull();
        plan!.Points.Select(point => point.X).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Bar_projection_swaps_points_and_annotation_anchor()
    {
        var chart = Chart(ChartTrendlineType.Linear, ChartType.Bar);
        chart.ShowTrendlineEquation = true;
        var source = Points((0, 10), (1, 30), (2, 30));

        var plan = TrendlineProjectionPlanner.Plan(chart, source, swapAxes: true);

        plan.Should().NotBeNull();
        plan!.Points.Select(point => point.Y).Should().Equal(0, 2);
        plan.AnnotationAnchor.Should().Be(new TrendPoint(10, 2));
        plan.AnnotationLines.Should().ContainSingle().Which.Should().Be("y = 10x + 13.333");
    }

    [Fact]
    public void Plan_preserves_equation_and_r_squared_order()
    {
        var chart = Chart(ChartTrendlineType.Linear);
        chart.ShowTrendlineEquation = true;
        chart.ShowTrendlineRSquared = true;

        var plan = TrendlineProjectionPlanner.Plan(chart, Points((0, 1), (1, 3), (2, 5)));

        plan.Should().NotBeNull();
        plan!.AnnotationLines.Should().HaveCount(2);
        plan.AnnotationLines[0].Should().Be("y = 2x + 1");
        plan.AnnotationLines[1].Should().Be("R\u00B2 = 1.0000");
        plan.AnnotationAnchor.Should().Be(new TrendPoint(0, 5));
    }

    [Fact]
    public void Plan_returns_null_when_trendlines_are_disabled_or_unsupported()
    {
        var chart = Chart(ChartTrendlineType.Linear);
        chart.ShowLinearTrendline = false;
        TrendlineProjectionPlanner.Plan(chart, Points((0, 1), (1, 2))).Should().BeNull();

        chart.ShowLinearTrendline = true;
        chart.Type = ChartType.Pie;
        TrendlineProjectionPlanner.Plan(chart, Points((0, 1), (1, 2))).Should().BeNull();
    }

    private static ChartModel Chart(ChartTrendlineType type, ChartType chartType = ChartType.Line) =>
        new()
        {
            Type = chartType,
            ShowLinearTrendline = true,
            TrendlineType = type,
            TrendlinePeriod = 2,
            TrendlineOrder = 2,
        };

    private static IReadOnlyList<TrendPoint> Points(params (double X, double Y)[] points) =>
        points.Select(point => new TrendPoint(point.X, point.Y)).ToArray();
}
