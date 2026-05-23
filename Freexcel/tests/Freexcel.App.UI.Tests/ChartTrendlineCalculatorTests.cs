using FluentAssertions;
using Freexcel.Core.Model;
using OxyPlot;

namespace Freexcel.App.UI.Tests;

public sealed class ChartTrendlineCalculatorTests
{
    [Fact]
    public void Calculate_Linear_ReturnsRegressionEndpoints()
    {
        var trend = ChartTrendlineCalculator.Calculate(
            ChartTrendlineType.Linear,
            [new DataPoint(0, 1), new DataPoint(1, 3), new DataPoint(2, 5)],
            period: 2,
            order: 2);

        trend.Should().Equal(new DataPoint(0, 1), new DataPoint(2, 5));
    }

    [Fact]
    public void Calculate_MovingAverage_UsesRequestedWindow()
    {
        var trend = ChartTrendlineCalculator.Calculate(
            ChartTrendlineType.MovingAverage,
            [new DataPoint(0, 2), new DataPoint(1, 4), new DataPoint(2, 10), new DataPoint(3, 12)],
            period: 3,
            order: 2);

        trend.Should().Equal(new DataPoint(2, 16.0 / 3.0), new DataPoint(3, 26.0 / 3.0));
    }

    [Fact]
    public void TryCalculateRSquared_ReturnsOneForPerfectFit()
    {
        var source = new[] { new DataPoint(0, 1), new DataPoint(1, 3), new DataPoint(2, 5) };
        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Linear, source, period: 2, order: 2);

        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var rSquared).Should().BeTrue();
        rSquared.Should().BeApproximately(1.0, 0.000001);
    }
}
