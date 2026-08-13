using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class WaterfallTotalTests
{
    [Fact]
    public void Planner_DrawsMarkedPointFromZeroWithoutConsumingItsValue()
    {
        var bars = WaterfallBarPlanner.Compute(
            [100, -30, 20, 90],
            [0, 2],
            WaterfallNullTotalsPolicy.NoTotals);

        bars.Select(bar => bar.Kind).Should().Equal(
            WaterfallBarKind.Total,
            WaterfallBarKind.Decrease,
            WaterfallBarKind.Total,
            WaterfallBarKind.Increase);
        bars[0].Bottom.Should().Be(0);
        bars[0].Top.Should().Be(0);
        bars[1].Bottom.Should().Be(-30);
        bars[1].Top.Should().Be(0);
        bars[2].Top.Should().Be(0);
        bars[3].Bottom.Should().Be(-30);
        bars[3].Top.Should().Be(60);
    }

    [Fact]
    public void BuildWaterfallPrimitives_UsesTotalAnchorAndKeepsLaterIncrementCumulative()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Waterfall,
            Categories = { "Start", "Reduction", "Total", "Growth" },
            WaterfallTotalPointIndices = [0, 2],
        };
        var series = new ChartSeries { Name = "Value" };
        series.Values.AddRange(new double?[] { 100, -30, 0, 20 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        var bars = scene.WaterfallConnectorLines;

        ChartRenderPlanner.BuildWaterfallPrimitives(chart, scene.Frame.Plot)
            .Should().HaveCount(4);
        bars.Should().HaveCount(3);
        bars.Should().OnlyContain(line => line.Start.Y == line.End.Y);
        bars[1].Start.Y.Should().BeGreaterThan(bars[0].Start.Y);
        bars[2].Start.Y.Should().BeApproximately(bars[1].Start.Y, 0.001);
    }

    [Fact]
    public void SetWaterfallTotalPointCommand_IsUndoableAndPreservesNullVsEmpty()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape
        {
            ChartType = ChartType.Waterfall,
            Categories = { "A", "B", "C" },
        };
        chart.Series.Add(new ChartSeries { Name = "Value" });
        chart.Series[0].Values.AddRange(new double?[] { 10, -2, 5 });
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Chart, Chart = chart });
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetWaterfallTotalPointCommand(0, 7, 1, setAsTotal: true));
        chart.WaterfallTotalPointIndices.Should().Equal(1);
        bus.Undo();
        chart.WaterfallTotalPointIndices.Should().BeNull();

        chart.WaterfallTotalPointIndices = [];
        bus.Execute(new SetWaterfallTotalPointCommand(0, 7, 1, setAsTotal: true));
        bus.Undo();
        chart.WaterfallTotalPointIndices.Should().BeEmpty();
    }
}
