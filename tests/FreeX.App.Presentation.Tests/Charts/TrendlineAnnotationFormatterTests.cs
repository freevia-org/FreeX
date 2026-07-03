using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Unit tests for <see cref="TrendlineAnnotationFormatter"/> — the portable equation / R-squared
/// annotation text builder (F18). Mirrors the source (WPF) renderer's
/// <c>ChartRenderer.Trendlines.cs</c> equation formatting.
/// </summary>
public sealed class TrendlineAnnotationFormatterTests
{
    private static IReadOnlyList<TrendPoint> Points(params (double X, double Y)[] points) =>
        points.Select(p => new TrendPoint(p.X, p.Y)).ToArray();

    [Fact]
    public void Neither_flag_set_returns_no_lines()
    {
        var chart = new ChartModel { ShowTrendlineEquation = false, ShowTrendlineRSquared = false };
        var source = Points((0, 1), (1, 3), (2, 5));
        var trend = Points((0, 1), (2, 5));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().BeEmpty();
    }

    [Fact]
    public void ShowTrendlineEquation_linear_fit_formats_as_y_equals_mx_plus_b()
    {
        var chart = new ChartModel
        {
            ShowTrendlineEquation = true,
            TrendlineType = ChartTrendlineType.Linear,
        };
        // y = 2x + 1 exactly.
        var source = Points((0, 1), (1, 3), (2, 5), (3, 7));
        var trend = Points((0, 1), (3, 7));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().ContainSingle();
        lines[0].Should().Be("y = 2x + 1");
    }

    [Fact]
    public void ShowTrendlineRSquared_perfect_fit_reports_R_squared_of_one()
    {
        var chart = new ChartModel
        {
            ShowTrendlineRSquared = true,
            TrendlineType = ChartTrendlineType.Linear,
        };
        var source = Points((0, 1), (1, 3), (2, 5), (3, 7));
        var trend = Points((0, 1), (3, 7));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().ContainSingle();
        lines[0].Should().Be("R² = 1.0000");
    }

    [Fact]
    public void Both_flags_set_returns_equation_then_rsquared_in_order()
    {
        var chart = new ChartModel
        {
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineType = ChartTrendlineType.Linear,
        };
        var source = Points((0, 1), (1, 3), (2, 5), (3, 7));
        var trend = Points((0, 1), (3, 7));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("y = ");
        lines[1].Should().StartWith("R² = ");
    }

    [Fact]
    public void MovingAverage_equation_reports_the_period()
    {
        var chart = new ChartModel
        {
            ShowTrendlineEquation = true,
            TrendlineType = ChartTrendlineType.MovingAverage,
            TrendlinePeriod = 3,
        };
        var source = Points((0, 1), (1, 2), (2, 3), (3, 4));
        var trend = Points((2, 2), (3, 3));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().ContainSingle();
        lines[0].Should().Be("Moving average (3)");
    }

    [Fact]
    public void Polynomial_equation_reports_the_order()
    {
        var chart = new ChartModel
        {
            ShowTrendlineEquation = true,
            TrendlineType = ChartTrendlineType.Polynomial,
            TrendlineOrder = 3,
        };
        var source = Points((0, 1), (1, 2), (2, 3), (3, 4));
        var trend = Points((0, 1), (1, 2), (2, 3), (3, 4));

        var lines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, source, trend);

        lines.Should().ContainSingle();
        lines[0].Should().Be("Polynomial (order 3)");
    }
}
