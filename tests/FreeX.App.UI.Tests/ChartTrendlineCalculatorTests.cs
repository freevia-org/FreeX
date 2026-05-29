using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;

namespace FreeX.App.UI.Tests;

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
    public void Calculate_MovingAverage_UsesRollingWindowWithoutPerPointLinq()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartTrendlineCalculator.cs"));
        var movingAverage = source[
            source.IndexOf("private static IReadOnlyList<DataPoint> CalculateMovingAverageTrendline", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<DataPoint> CalculatePolynomialTrendline", StringComparison.Ordinal)];

        movingAverage.Should().Contain("var runningTotal = 0.0;");
        movingAverage.Should().Contain("runningTotal -= points[i - windowSize].Y;");
        movingAverage.Should().NotContain(".Skip(");
        movingAverage.Should().NotContain(".Take(");
        movingAverage.Should().NotContain(".Average(");
    }

    [Fact]
    public void Calculate_RegressionTrendlines_AggregatePointsInSinglePasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartTrendlineCalculator.cs"));
        var regressionBlock = source[
            source.IndexOf("private static IReadOnlyList<DataPoint> CalculateLinearTrendline", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<DataPoint> CalculateMovingAverageTrendline", StringComparison.Ordinal)];

        regressionBlock.Should().Contain("for (var i = 0; i < points.Count; i++)");
        regressionBlock.Should().NotContain(".Where(");
        regressionBlock.Should().NotContain(".ToList(");
        regressionBlock.Should().NotContain(".Sum(");
        regressionBlock.Should().NotContain("points.Min(");
        regressionBlock.Should().NotContain("points.Max(");
    }

    [Fact]
    public void TryCalculateRSquared_ReturnsOneForPerfectFit()
    {
        var source = new[] { new DataPoint(0, 1), new DataPoint(1, 3), new DataPoint(2, 5) };
        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Linear, source, period: 2, order: 2);

        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var rSquared).Should().BeTrue();
        rSquared.Should().BeApproximately(1.0, 0.000001);
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
