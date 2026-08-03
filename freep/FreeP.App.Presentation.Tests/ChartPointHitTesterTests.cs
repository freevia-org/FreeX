using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartPointHitTesterTests
{
    [Fact]
    public void TryHitTest_ResolvesColumnPointFromPlannedRectangle()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            Categories = { "Q1", "Q2" },
        };
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = SlideTransformCore.DipToEmu(320),
            ExtentCyEmu = SlideTransformCore.DipToEmu(220),
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 320, 220));
        var first = scene.Rectangles[0].Bounds;

        ChartPointHitTester.TryHitTest(
            slide,
            presentation,
            first.X + first.Width / 2.0,
            first.Y + first.Height / 2.0,
            out var hit).Should().BeTrue();
        hit.Should().Be(new ChartPointHit(7, 0, 0));
    }

    [Fact]
    public void TryHitTest_UnrotatesChartBeforeResolvingLineMarker()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            Categories = { "Q1", "Q2", "Q3" },
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 30, 20 });
        chart.Series.Add(series);

        const double left = 40;
        const double top = 30;
        const double width = 320;
        const double height = 220;
        const double rotation = 27;
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 12,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = SlideTransformCore.DipToEmu(left),
            OffsetYEmu = SlideTransformCore.DipToEmu(top),
            ExtentCxEmu = SlideTransformCore.DipToEmu(width),
            ExtentCyEmu = SlideTransformCore.DipToEmu(height),
            RotationDeg = rotation,
            Chart = chart,
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, width, height));
        var marker = scene.LineSeries[0].Markers[1].Center;
        var centerX = left + width / 2.0;
        var centerY = top + height / 2.0;
        var world = SlideTransformCore.RotatePoint(
            left + marker.X,
            top + marker.Y,
            centerX,
            centerY,
            rotation);

        ChartPointHitTester.TryHitTest(slide, presentation, world.X, world.Y, out var hit)
            .Should().BeTrue();
        hit.Should().Be(new ChartPointHit(12, 0, 1));
    }
}
