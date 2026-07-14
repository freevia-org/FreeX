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

    [Fact]
    public void ChartBaselineDepthCorpusDeck_ProjectsPowerPointWpfAvaloniaBaselineReadiness()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
            charts,
            slideIndex: 0,
            scenarioId: "Chart Baseline Depth");

        readiness.ScenarioId.Should().Be("chart-baseline-depth");
        readiness.SlideIndex.Should().Be(0);
        readiness.ChartCount.Should().Be(4);
        readiness.CaptureRequests.Should().HaveCount(12);
        readiness.PowerPointRequestCount.Should().Be(4);
        readiness.SharedHostRequestCount.Should().Be(8);
        readiness.IsPowerPointAuthoritativeReady.Should().BeTrue();
        readiness.CaptureRequests.Select(request => request.Host)
            .Should()
            .ContainInOrder(
                ChartVisualBaselineCaptureHost.PowerPoint,
                ChartVisualBaselineCaptureHost.Wpf,
                ChartVisualBaselineCaptureHost.Avalonia);

        var stockPowerPoint = readiness.CaptureRequests.First(request =>
            request.Host == ChartVisualBaselineCaptureHost.PowerPoint
            && request.ChartType == ChartType.Stock);
        stockPowerPoint.CaptureId.Should().Be("freep.chart-baseline-depth.slide-1.chart-1.stock.powerpoint");
        stockPowerPoint.SurfaceId.Should().Be("freep.chart-baseline-depth.slide-1.chart-1.stock");
        stockPowerPoint.RequiresPowerPointCom.Should().BeTrue();
        stockPowerPoint.EvidenceSummary.Should()
            .Contain("stock high-low/open-close tick plan")
            .And.Contain("4 series; 3 categories");

        var surfaceAvalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia
            && request.ChartType == ChartType.Surface3D);
        surfaceAvalonia.CaptureId.Should().Be("freep.chart-baseline-depth.slide-1.chart-2.surface3d.avalonia");
        surfaceAvalonia.RequiresPowerPointCom.Should().BeFalse();
        surfaceAvalonia.EvidenceSummary.Should().Contain("3-D surface projected facet");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.Wpf
                && request.ChartType == ChartType.Scatter)
            .EvidenceSummary
            .Should()
            .Contain("scatter smoothed Bezier path plan");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint
                && request.ChartType == ChartType.ColumnStacked100)
            .EvidenceSummary
            .Should()
            .Contain("100% stacked normalized axis");

        readiness.EvidenceLines.Should().Equal(
            "Scenario chart-baseline-depth: slide 1; charts 4",
            "Capture requests: 12; PowerPoint 4; WPF 4; Avalonia 4",
            "PowerPoint requests are readiness contracts and require desktop PowerPoint COM on the baseline machine");
    }

    [Fact]
    public void RadarBaselineReadiness_ProjectsStyleSpecificSharedHostDecisionsWithoutPowerPointCom()
    {
        var filledRadar = BuildRadarChart(RadarStyle.Filled);
        var markerRadar = BuildRadarChart(RadarStyle.Marker);
        var charts = new[] { filledRadar, markerRadar };

        var filledPlan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            filledRadar,
            new ChartPlanRect(0, 0, 240, 180));
        filledPlan.Series[0].Path.Fill.Should()
            .Be(new ChartFillPlan(ChartRenderPlanner.ResolveSeriesColor(0, null), ChartRenderPlanner.RadarFillAlpha));
        filledPlan.Series[0].Markers.Should().BeEmpty();

        var markerPlan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            markerRadar,
            new ChartPlanRect(0, 0, 240, 180));
        markerPlan.Series[0].Markers.Should().HaveCount(4);
        markerPlan.Series[0].Path.Fill.Should().BeNull();

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
            charts,
            slideIndex: 2,
            scenarioId: "Radar Type Decisions");

        readiness.ChartCount.Should().Be(2);
        readiness.CaptureRequests.Should().HaveCount(6);
        readiness.PowerPointRequestCount.Should().Be(2);
        readiness.SharedHostRequestCount.Should().Be(4);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var filledAvalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia
            && request.ChartIndex == 0);
        filledAvalonia.CaptureId.Should().Be("freep.radar-type-decisions.slide-3.chart-1.radar.avalonia");
        filledAvalonia.EvidenceSummary.Should().Contain("filled radar area opacity");

        var markerWpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf
            && request.ChartIndex == 1);
        markerWpf.CaptureId.Should().Be("freep.radar-type-decisions.slide-3.chart-2.radar.wpf");
        markerWpf.EvidenceSummary.Should().Contain("radar marker");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint
                && request.ChartIndex == 0)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    [Fact]
    public void DoughnutBaselineReadiness_ProjectsHoleSizeAndRingOrderDecisionsWithoutPowerPointCom()
    {
        var doughnut = BuildDoughnutChart();
        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            doughnut,
            new ChartPlanRect(0, 0, 240, 180));

        slices.Should().HaveCount(6);
        slices[0].SeriesIndex.Should().Be(0);
        slices[0].PointIndex.Should().Be(0);
        slices[0].InnerRadius.Should().BeApproximately(30.6, 0.0001);
        slices[0].StartAngle.Should().BeApproximately(0, 0.0001);
        slices[3].SeriesIndex.Should().Be(1);
        slices[3].InnerRadius.Should().BeGreaterThan(slices[0].OuterRadius);

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
            [doughnut],
            slideIndex: 3,
            scenarioId: "Doughnut Ring Decisions");

        readiness.ChartCount.Should().Be(1);
        readiness.CaptureRequests.Should().HaveCount(3);
        readiness.PowerPointRequestCount.Should().Be(1);
        readiness.SharedHostRequestCount.Should().Be(2);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var avalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia);
        avalonia.CaptureId.Should().Be("freep.doughnut-ring-decisions.slide-4.chart-1.doughnut.avalonia");
        avalonia.EvidenceSummary.Should()
            .Contain("doughnut ring and first-slice plan")
            .And.Contain("2 series; 3 categories");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
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

    private static ChartShape BuildRadarChart(RadarStyle style)
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = style
        };
        chart.Categories.AddRange(["North", "East", "South", "West"]);
        chart.Series.Add(new ChartSeries { Name = "Coverage" });
        chart.Series[0].Values.AddRange([4, 6, 3, 5]);
        return chart;
    }

    private static ChartShape BuildDoughnutChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            DoughnutHolePercent = 40,
            FirstSliceAngleDegrees = 90
        };
        chart.Categories.AddRange(["Alpha", "Beta", "Gamma"]);
        chart.Series.Add(new ChartSeries { Name = "Inner" });
        chart.Series[0].Values.AddRange([3, 2, 1]);
        chart.Series.Add(new ChartSeries { Name = "Outer" });
        chart.Series[1].Values.AddRange([1, 2, 3]);
        return chart;
    }
}
