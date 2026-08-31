using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartTrendlineRenderTests
{
    [Fact]
    public void ScenePlan_RendersLinearTrendlineAcrossCategoryPlot()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(["Q1", "Q2", "Q3", "Q4"]);
        var series = new ChartSeries { Name = "Revenue", Trendline = new ChartTrendline() };
        series.Values.AddRange([1, 2, 3, 4]);
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 300));

        scene.Trendlines.Should().ContainSingle();
        var trendline = scene.Trendlines[0];
        trendline.Type.Should().Be(ChartTrendlineType.Linear);
        trendline.Segments.Should().NotBeEmpty();
        trendline.Segments.First().Start.X.Should().BeApproximately(scene.Frame.Plot.X, 0.001);
        trendline.Segments.Last().End.X.Should().BeApproximately(scene.Frame.Plot.Right, 0.001);
        trendline.Segments.First().Start.Y.Should().BeGreaterThan(trendline.Segments.Last().End.Y);
    }

    [Theory]
    [InlineData(ChartTrendlineType.Exponential)]
    [InlineData(ChartTrendlineType.Logarithmic)]
    [InlineData(ChartTrendlineType.Power)]
    [InlineData(ChartTrendlineType.Polynomial)]
    [InlineData(ChartTrendlineType.MovingAverage)]
    public void ScenePlan_ResolvesEachTrendlineFamily(ChartTrendlineType type)
    {
        var chart = new ChartShape { ChartType = ChartType.Scatter };
        var series = new ChartSeries
        {
            Trendline = new ChartTrendline
            {
                Type = type,
                PolynomialOrder = type == ChartTrendlineType.Polynomial ? 2 : null,
                MovingAveragePeriod = type == ChartTrendlineType.MovingAverage ? 3 : null,
                DisplayEquation = true,
                DisplayRSquared = true
            }
        };
        series.XValues.AddRange([1, 2, 3, 4, 5]);
        series.Values.AddRange([2, 3, 5, 8, 13]);
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 640, 360));

        scene.Trendlines.Should().ContainSingle();
        var trendline = scene.Trendlines[0];
        trendline.Type.Should().Be(type);
        trendline.Segments.Should().NotBeEmpty();
        trendline.DisplayEquation.Should().BeTrue();
        trendline.DisplayRSquared.Should().BeTrue();
        if (type == ChartTrendlineType.MovingAverage)
        {
            trendline.Labels.Should().BeEmpty();
        }
        else
        {
            trendline.Labels.Should().HaveCount(2);
            trendline.Labels[0].Text.Should().StartWith("y = ");
            trendline.Labels[1].Text.Should().StartWith("R^2 = ");
        }
    }

    [Fact]
    public void ScenePlan_DoesNotCreateTrendlineForPieChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Pie };
        var series = new ChartSeries { Trendline = new ChartTrendline() };
        series.Values.AddRange([1, 2, 3]);
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.Trendlines.Should().BeEmpty();
    }

    [Fact]
    public void MovingAverage_PeriodThree_PreservesPartialWarmUpWindowsAndSampleOrder()
    {
        (double X, double Y)[] samples = [(4, 2), (7, 4), (11, 8), (18, 16)];

        var values = ChartRenderPlanner.BuildMovingAverageTrendlineValues(samples, authoredPeriod: 3);

        values.Select(value => value.X).Should().Equal(4, 7, 11, 18);
        values[0].Y.Should().BeApproximately(2, 1e-12);
        values[1].Y.Should().BeApproximately(3, 1e-12);
        values[2].Y.Should().BeApproximately(14.0 / 3.0, 1e-12);
        values[3].Y.Should().BeApproximately(28.0 / 3.0, 1e-12);
    }

    [Fact]
    public void MovingAverage_PeriodOverSampleCount_ClampsToSampleCount()
    {
        (double X, double Y)[] samples = [(1, 2), (2, 4), (3, 8), (4, 16)];

        var values = ChartRenderPlanner.BuildMovingAverageTrendlineValues(samples, authoredPeriod: 100);

        values.Select(value => value.Y).Should().Equal(2, 3, 14.0 / 3.0, 7.5);
    }

    [Fact]
    public void MovingAverage_NullPeriod_UsesDefaultPeriodTwo()
    {
        (double X, double Y)[] samples = [(1, 2), (2, 4), (3, 8), (4, 16)];

        var values = ChartRenderPlanner.BuildMovingAverageTrendlineValues(samples, authoredPeriod: null);

        values.Select(value => value.Y).Should().Equal(2, 3, 6, 12);
    }

    [Fact]
    public void MovingAverage_SourceGuard_KeepsLinearRollingWindow()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Presentation", "ChartRenderPlanner.cs");
        int start = source.IndexOf(
            "internal static IReadOnlyList<(double X, double Y)> BuildMovingAverageTrendlineValues(",
            StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        int end = source.IndexOf("private static bool TryBuildTrendlineFit(", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var method = source[start..end];
        source.Should().Contain("return BuildMovingAverageTrendlineValues(samples, trendline.MovingAveragePeriod);");
        method.Should().Contain("Math.Clamp(authoredPeriod ?? 2, 2, samples.Count)")
            .And.Contain("new (double X, double Y)[samples.Count]")
            .And.Contain("rollingSum += samples[index].Y")
            .And.Contain("rollingSum -= samples[index - period].Y")
            .And.NotContain(".Skip(")
            .And.NotContain(".Take(")
            .And.NotContain(".Average(");
    }
}
