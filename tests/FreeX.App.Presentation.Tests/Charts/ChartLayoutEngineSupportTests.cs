using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartLayoutEngineSupportTests
{
    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.ThreeDColumn)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.ThreeDBar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Radar)]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    public void Supported_types_lay_out_without_throwing(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue();
        var request = Request(Chart(type), ["A", "B"], [Series(0, "S1", 10, 20)]);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Funnel)]
    public void Deferred_types_are_not_supported_and_throw(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeFalse();
        var request = Request(Chart(type), ["A"], [Series(0, "S1", 10)]);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Empty_series_does_not_throw()
    {
        var request = Request(Chart(ChartType.Column), [], []);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().NotThrow();
    }
}
