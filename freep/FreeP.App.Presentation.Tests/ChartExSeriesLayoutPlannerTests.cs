using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartExSeriesLayoutPlannerTests
{
    [Fact]
    public void BuildOptions_UsesOnlyPreservedChartExSeriesLayouts()
    {
        var chart = MakeChart();

        ChartExSeriesLayoutPlanner.CanEdit(chart).Should().BeTrue();
        ChartExSeriesLayoutPlanner.BuildOptions(chart).Select(option => option.Label)
            .Should().Equal("Sales: Histogram", "Budget: Pareto");
        ChartExSeriesLayoutPlanner.BuildLayoutChoices(chart)
            .Should().Equal("histogram", "pareto");
    }

    [Fact]
    public void BuildCommitPlan_RejectsLayoutOutsideThePayloadAllowlist()
    {
        var chart = MakeChart();

        var act = () => ChartExSeriesLayoutPlanner.BuildCommitPlan(chart, 0, "boxWhisker");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildCommitPlan_NormalizesAnExistingLayout()
    {
        var chart = MakeChart();

        ChartExSeriesLayoutPlanner.BuildCommitPlan(chart, 1, " HISTOGRAM ")
            .Should().Be(new ChartExSeriesLayoutCommitPlan(1, "HISTOGRAM"));
    }

    private static ChartShape MakeChart()
    {
        var chart = new ChartShape
        {
            IsChartEx = true,
            PreservedChartExXml = "<cx:chartSpace />",
        };
        chart.Series.Add(new ChartSeries { Name = "Sales", ChartExLayoutId = "histogram" });
        chart.Series.Add(new ChartSeries { Name = "Budget", ChartExLayoutId = "pareto" });
        return chart;
    }
}
