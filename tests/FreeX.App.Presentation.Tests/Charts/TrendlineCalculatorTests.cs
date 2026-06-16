using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class TrendlineCalculatorTests
{
    private static IReadOnlyList<TrendPoint> Points(params (double X, double Y)[] points) =>
        points.Select(p => new TrendPoint(p.X, p.Y)).ToArray();

    [Fact]
    public void Linear_fit_recovers_slope_and_intercept_at_endpoints()
    {
        // y = 2x + 1 exactly.
        var source = Points((0, 1), (1, 3), (2, 5), (3, 7));
        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Linear, source, 2, 2);

        trend.Should().HaveCount(2);
        trend[0].X.Should().Be(0);
        trend[0].Y.Should().BeApproximately(1, 1e-9);
        trend[1].X.Should().Be(3);
        trend[1].Y.Should().BeApproximately(7, 1e-9);
    }

    [Fact]
    public void Linear_fit_least_squares_through_noisy_points()
    {
        var source = Points((0, 0), (1, 1), (2, 2), (3, 3), (4, 100));
        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Linear, source, 2, 2);

        var slope = (trend[1].Y - trend[0].Y) / (trend[1].X - trend[0].X);
        // Least-squares slope for these points is positive and well above the clean unit slope.
        slope.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Linear_fit_with_fewer_than_two_points_returns_empty()
    {
        TrendlineCalculator.Calculate(ChartTrendlineType.Linear, Points((1, 1)), 2, 2)
            .Should().BeEmpty();
    }

    [Fact]
    public void Exponential_fit_recovers_curve_samples()
    {
        // y = 3 * e^(0.5 x).
        const double a = 3.0;
        const double b = 0.5;
        var source = Points(
            (0, a * Math.Exp(b * 0)),
            (1, a * Math.Exp(b * 1)),
            (2, a * Math.Exp(b * 2)),
            (3, a * Math.Exp(b * 3)));

        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Exponential, source, 2, 2);

        trend.Count.Should().BeGreaterThanOrEqualTo(16);
        trend[0].X.Should().BeApproximately(0, 1e-9);
        trend[^1].X.Should().BeApproximately(3, 1e-9);
        // Every sample lands on the true curve.
        foreach (var point in trend)
            point.Y.Should().BeApproximately(a * Math.Exp(b * point.X), 1e-6);
    }

    [Fact]
    public void Exponential_fit_skips_nonpositive_y_and_returns_empty_when_too_few_usable()
    {
        var source = Points((0, -1), (1, 5));
        TrendlineCalculator.Calculate(ChartTrendlineType.Exponential, source, 2, 2)
            .Should().BeEmpty();
    }

    [Fact]
    public void Logarithmic_fit_recovers_curve_samples()
    {
        // y = 2 + 4 ln(x).
        var source = Points(
            (1, 2 + 4 * Math.Log(1)),
            (2, 2 + 4 * Math.Log(2)),
            (3, 2 + 4 * Math.Log(3)),
            (4, 2 + 4 * Math.Log(4)));

        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Logarithmic, source, 2, 2);

        foreach (var point in trend)
            point.Y.Should().BeApproximately(2 + 4 * Math.Log(point.X), 1e-6);
    }

    [Fact]
    public void Power_fit_recovers_curve_samples()
    {
        // y = 2.5 * x^1.7.
        const double a = 2.5;
        const double b = 1.7;
        var source = Points(
            (1, a * Math.Pow(1, b)),
            (2, a * Math.Pow(2, b)),
            (3, a * Math.Pow(3, b)),
            (4, a * Math.Pow(4, b)));

        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Power, source, 2, 2);

        foreach (var point in trend)
            point.Y.Should().BeApproximately(a * Math.Pow(point.X, b), 1e-5);
    }

    [Fact]
    public void Moving_average_averages_each_window()
    {
        var source = Points((0, 2), (1, 4), (2, 6), (3, 8));
        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.MovingAverage, source, 2, 2);

        // Window size 2: averages of consecutive pairs, anchored at the window's last x.
        trend.Should().HaveCount(3);
        trend[0].Should().Be(new TrendPoint(1, 3));
        trend[1].Should().Be(new TrendPoint(2, 5));
        trend[2].Should().Be(new TrendPoint(3, 7));
    }

    [Fact]
    public void Moving_average_with_period_three()
    {
        var source = Points((0, 3), (1, 6), (2, 9), (3, 12), (4, 15));
        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.MovingAverage, source, 3, 2);

        trend.Should().HaveCount(3);
        trend[0].Should().Be(new TrendPoint(2, 6));   // (3+6+9)/3
        trend[1].Should().Be(new TrendPoint(3, 9));   // (6+9+12)/3
        trend[2].Should().Be(new TrendPoint(4, 12));  // (9+12+15)/3
    }

    [Fact]
    public void Moving_average_returns_empty_when_window_exceeds_points()
    {
        TrendlineCalculator.Calculate(ChartTrendlineType.MovingAverage, Points((0, 1), (1, 2)), 5, 2)
            .Should().BeEmpty();
    }

    [Fact]
    public void Polynomial_fit_recovers_quadratic_samples()
    {
        // y = x^2 - 2x + 1.
        var source = Points(
            (0, 1), (1, 0), (2, 1), (3, 4), (4, 9), (5, 16));

        var trend = TrendlineCalculator.Calculate(ChartTrendlineType.Polynomial, source, 2, 2);

        trend.Count.Should().BeGreaterThanOrEqualTo(16);
        foreach (var point in trend)
        {
            var expected = point.X * point.X - 2 * point.X + 1;
            point.Y.Should().BeApproximately(expected, 1e-5);
        }
    }

    [Fact]
    public void Polynomial_returns_empty_when_points_do_not_exceed_degree()
    {
        // Degree clamps to 2; need more than 2 points.
        TrendlineCalculator.Calculate(ChartTrendlineType.Polynomial, Points((0, 1), (1, 2)), 2, 2)
            .Should().BeEmpty();
    }
}
