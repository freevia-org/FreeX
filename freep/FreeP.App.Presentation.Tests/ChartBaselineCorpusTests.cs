using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartBaselineCorpusTests
{
    [Fact]
    public void ChartBaselineDepthCorpusDeck_ExercisesSharedPlannerDecisions()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        charts.Select(chart => chart.ChartType)
            .Should()
            .Contain(new[]
            {
                ChartType.Stock,
                ChartType.Surface3D,
                ChartType.Scatter,
                ChartType.ColumnStacked100,
            });

        var stock = charts.Single(chart => chart.ChartType == ChartType.Stock);
        var stockPlan = ChartRenderPlanner.BuildStockPrimitivePlan(
            stock,
            new ChartPlanRect(0, 0, 360, 220));
        stockPlan.HighLowLines.Should().HaveCount(3);
        stockPlan.CloseTicks.Select(tick => tick.PriceMove)
            .Should()
            .Equal(
                ChartStockPriceMove.Rising,
                ChartStockPriceMove.Falling,
                ChartStockPriceMove.Unchanged);

        var surface = charts.Single(chart => chart.ChartType == ChartType.Surface3D);
        var surfaceCells = ChartRenderPlanner.BuildSurfaceCellPrimitives(
            surface,
            new ChartPlanRect(0, 0, 360, 220));
        surfaceCells.Should().HaveCount(8);
        surfaceCells.Should().NotContain(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 1);
        surfaceCells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 2).Bounds.X
            .Should()
            .BeGreaterThan(surfaceCells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 0).Bounds.X);
        var surfaceGeometry = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surface,
            new ChartPlanRect(0, 0, 360, 220));
        surfaceGeometry.Facets.Should().HaveCount(2);
        surfaceGeometry.WireframeSegments.Should().HaveCountGreaterThan(surfaceGeometry.Facets.Count);
        surfaceGeometry.ContourSegments.Should().NotBeEmpty();
        surfaceGeometry.Facets.Should().OnlyContain(facet => facet.Points.Count == 4);

        var scatter = charts.Single(chart => chart.ChartType == ChartType.Scatter);
        var scatterPlan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            scatter,
            new ChartPlanRect(0, 0, 360, 220));
        scatterPlan.Series[0].IsSmoothed.Should().BeTrue();
        scatterPlan.Series[0].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.CubicBezier);
        scatterPlan.Series[1].IsSmoothed.Should().BeFalse();
        scatterPlan.Series[1].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.Line);

        var stacked = charts.Single(chart => chart.ChartType == ChartType.ColumnStacked100);
        ChartRenderPlanner.ComputePrimaryValueAxisRange(stacked)
            .Should()
            .Be((0, 1, 0.25));
        var stackedBars = ChartRenderPlanner.BuildColumnPrimitives(
            stacked,
            new ChartPlanRect(0, 0, 360, 220));
        stackedBars.Where(bar => bar.CategoryIndex == 0).Sum(bar => bar.Bounds.Height)
            .Should()
            .BeApproximately(220, 0.0001);
    }

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (File.Exists(Path.Combine(candidate, "22-chart-baseline-depth.pptx")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus.");
    }
}
