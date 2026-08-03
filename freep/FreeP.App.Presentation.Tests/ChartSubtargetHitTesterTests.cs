using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartSubtargetHitTesterTests
{
    [Fact]
    public void TryHitTest_ResolvesChartTitleBeforeChartArea()
    {
        var chart = new ChartShape
        {
            Title = "Revenue",
            ChartType = ChartType.ColumnClustered,
            Categories = { "Q1", "Q2" },
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);
        var (presentation, slide, shape) = AddChart(chart, 320, 220);
        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 320, 220));
        var title = scene.Title!.Value;

        ChartSubtargetHitTester.TryHitTest(
            slide,
            presentation,
            40 + title.Bounds.X + title.Bounds.Width / 2,
            30 + title.Bounds.Y + title.Bounds.Height / 2,
            out var hit).Should().BeTrue();
        hit.Kind.Should().Be(ChartSubtargetKind.Title);
    }

    [Fact]
    public void TryHitTest_PreservesAxisTitleTarget()
    {
        var chart = new ChartShape
        {
            CategoryAxis = { Title = "Quarter" },
            ChartType = ChartType.ColumnClustered,
            Categories = { "Q1", "Q2" },
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);
        var (presentation, slide, _) = AddChart(chart, 320, 220);
        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 320, 220));
        var title = scene.AxisTitles.Single(axisTitle => axisTitle.AxisKind == ChartAxisKind.Category);

        ChartSubtargetHitTester.TryHitTest(
            slide,
            presentation,
            40 + title.Label.Bounds.X + title.Label.Bounds.Width / 2,
            30 + title.Label.Bounds.Y + title.Label.Bounds.Height / 2,
            out var hit).Should().BeTrue();
        hit.Kind.Should().Be(ChartSubtargetKind.AxisTitle);
        hit.AxisKind.Should().Be(ChartAxisKind.Category);
    }

    [Fact]
    public void TryHitTest_ResolvesPlotAreaWhenNoSeriesIsUnderPointer()
    {
        var chart = new ChartShape { ChartType = ChartType.LineMarkers, Categories = { "Q1", "Q2" } };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);
        var (presentation, slide, shape) = AddChart(chart, 320, 220);
        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 320, 220));
        var point = new ChartPlanPoint(scene.Frame.Plot.X + 4, scene.Frame.Plot.Y + scene.Frame.Plot.Height / 2);

        ChartSubtargetHitTester.TryHitTest(
            slide,
            presentation,
            point.X + 40,
            point.Y + 30,
            out var hit).Should().BeTrue();
        hit.Kind.Should().Be(ChartSubtargetKind.PlotArea);
    }

    private static (Presentation Presentation, Slide Slide, SlideShape Shape) AddChart(ChartShape chart, double width, double height)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 21,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = SlideTransformCore.DipToEmu(40),
            OffsetYEmu = SlideTransformCore.DipToEmu(30),
            ExtentCxEmu = SlideTransformCore.DipToEmu(width),
            ExtentCyEmu = SlideTransformCore.DipToEmu(height),
            Chart = chart,
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return (presentation, slide, shape);
    }
}
