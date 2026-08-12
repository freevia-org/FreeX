using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class TrendlineProjectionOwnershipSourceGuardTests
{
    [Fact]
    public void Wpf_renderer_consumes_the_portable_plan_and_contains_no_trendline_math()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var renderer = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.UI", "ChartRenderer.Trendlines.cs"));

        renderer.Should().Contain("TrendlineProjectionPlanner.Plan(");
        renderer.Should().NotContain("CalculateLinearWithFixedIntercept");
        renderer.Should().NotContain("ApplyTrendlineForecast");
        renderer.Should().NotContain("ExtrapolateY");
        renderer.Should().NotContain("TryCalculateRSquared");
        renderer.Should().NotContain("FormatLinearEquation");
        renderer.Should().NotContain("GetTrendlineEquationText");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.UI", "ChartTrendlineCalculator.cs"))
            .Should().BeFalse("the OxyPlot calculation facade is superseded by the portable projection plan");
    }

    [Fact]
    public void Layout_engine_consumes_the_same_plan_without_private_projection_math()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var layoutEngine = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Presentation",
            "Charts",
            "ChartLayoutEngine.cs"));

        layoutEngine.Should().Contain("TrendlineProjectionPlanner.Plan(");
        layoutEngine.Should().NotContain("ApplyTrendlineInterceptAndForecast");
        layoutEngine.Should().NotContain("CalculateLinearWithFixedIntercept");
        layoutEngine.Should().NotContain("ApplyTrendlineForecast");
        layoutEngine.Should().NotContain("ExtrapolateY");
        layoutEngine.Should().NotContain("TrendlineAnnotationFormatter.BuildAnnotationLines");
        layoutEngine.Should().NotContain("!chart.ShowLinearTrendline");
    }
}
