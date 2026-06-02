using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridViewPerformanceMeasurementTests
{
    [Fact]
    public void Benchmark_RenderTextHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateTextHeavyGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_TEXT_HEAVY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderSelectionOnlyRepaints_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 80;
            const int width = 1440;
            const int height = 900;
            var grid = CreateSelectionOnlyGrid(width, height, out var ranges);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                grid.SelectedRange = ranges[i % ranges.Length];
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_SELECTION_ONLY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderChartViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 8;
            const int width = 1440;
            const int height = 900;
            var grid = CreateChartGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_CHART " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderDefaultStyledTextHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateTextHeavyGrid(width, height, CellStyle.Default);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_DEFAULT_STYLED_TEXT_HEAVY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderWrappedTextHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 8;
            const int width = 1440;
            const int height = 900;
            var grid = CreateWrappedTextHeavyGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_WRAPPED_TEXT_HEAVY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderChartViewportDuringDimensionResize_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 8;
            const int width = 1440;
            const int height = 900;
            var grid = CreateChartGrid(width, height);
            SetResizeTarget(grid, "Column");

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_CHART_DIMENSION_RESIZE " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderShrinkToFitViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateShrinkToFitGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_SHRINK_TEXT_HEAVY " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderQuickAnalysisDataBarPreview_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateQuickAnalysisGrid(width, height, GridQuickAnalysisPreviewVisualKind.DataBars);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_QUICK_ANALYSIS_DATABARS " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_CalculateQuickAnalysisDataBarPreviewRects_NoNumericCells_ReportsTiming()
    {
        const int iterations = 64;
        const int rowCount = 5_000;
        const int columnCount = 512;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var viewport = new ViewportModel([], rows, columns);
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rowCount, columnCount));

        GridView.CalculateQuickAnalysisDataBarPreviewRects(viewport, range, 30, 18).Should().BeEmpty();
        GridView.CalculateQuickAnalysisDataBarPreviewRects(viewport, range, 30, 18).Should().BeEmpty();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            var rects = GridView.CalculateQuickAnalysisDataBarPreviewRects(viewport, range, 30, 18);
            step.Stop();
            rects.Should().BeEmpty();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF QUICK_ANALYSIS_DATABARS_EMPTY " +
            $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F4} p95_ms={p95:F4} max_ms={ordered[^1]:F4} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeLessThan(250_000);
    }

    [Fact]
    public void Benchmark_RenderSparklineHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 12;
            const int width = 1440;
            const int height = 900;
            var grid = CreateSparklineGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_SPARKLINES " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderDrawingObjectHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 8;
            const int width = 1440;
            const int height = 900;
            var grid = CreateDrawingObjectHeavyGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_DRAWING_OBJECTS " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RenderOffscreenDrawingObjectHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 6;
            const int width = 1440;
            const int height = 900;
            var grid = CreateOffscreenDrawingObjectHeavyGrid(width, height);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                RenderOnce(grid, width, height);
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

            Console.WriteLine(
                "PERF GRID_RENDER_OFFSCREEN_DRAWING_OBJECTS " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_FormulaTraceLayoutVisitor_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 160;
        const int arrowCount = 2_500;
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 160)
                .Select(row => new RowMetric((uint)row, 20, (row - 1) * 20))
                .ToArray(),
            Enumerable.Range(1, 80)
                .Select(col => new ColMetric((uint)col, 72, (col - 1) * 72))
                .ToArray());
        var arrows = Enumerable.Range(0, arrowCount)
            .Select(index =>
            {
                var from = new CellAddress(
                    sheetId,
                    (uint)(index % 160 + 1),
                    (uint)(index % 80 + 1));
                var to = (index % 3) switch
                {
                    0 => new CellAddress(sheetId, (uint)((index + 17) % 160 + 1), (uint)((index + 11) % 80 + 1)),
                    1 => new CellAddress(sheetId, (uint)(400 + index), (uint)(index % 80 + 1)),
                    _ => new CellAddress(otherSheetId, (uint)(index % 160 + 1), (uint)(index % 80 + 1))
                };
                return new FormulaTraceArrow(from, to);
            })
            .ToArray();

        FormulaTraceLayoutPlanner.CalculateLayouts(viewport, arrows, sheetId).Count.Should().Be(arrowCount);
        var warmupConsumer = new CountingFormulaTraceArrowLayoutConsumer();
        FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref warmupConsumer);
        warmupConsumer.Count.Should().Be(arrowCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var materializedCount = 0;
        var materializedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var materialized = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            materializedCount += FormulaTraceLayoutPlanner.CalculateLayouts(viewport, arrows, sheetId).Count;
        materialized.Stop();
        var materializedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - materializedAllocatedBefore;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var visitedCount = 0;
        var visitedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var visited = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var consumer = new CountingFormulaTraceArrowLayoutConsumer();
            FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref consumer);
            visitedCount += consumer.Count;
        }
        visited.Stop();
        var visitedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - visitedAllocatedBefore;

        Console.WriteLine(
            "PERF FORMULA_TRACE_LAYOUT_VISITOR " +
            $"arrows={arrowCount} steps={iterations} " +
            $"materialized_total_ms={materialized.Elapsed.TotalMilliseconds:F2} " +
            $"visitor_total_ms={visited.Elapsed.TotalMilliseconds:F2} " +
            $"materialized_allocated_bytes={materializedAllocatedBytes:N0} " +
            $"visitor_allocated_bytes={visitedAllocatedBytes:N0}");

        materializedCount.Should().Be(arrowCount * iterations);
        visitedCount.Should().Be(materializedCount);
        visitedAllocatedBytes.Should().BeLessThan(materializedAllocatedBytes);
    }

    private static GridView CreateTextHeavyGrid(double width, double height)
        => CreateTextHeavyGrid(width, height, null);

    private static GridView CreateSelectionOnlyGrid(double width, double height, out GridRange[] selectionSteps)
    {
        const int rowCount = 240;
        const int columnCount = 120;
        const double rowHeight = 18;
        const double columnWidth = 48;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        selectionSteps = Enumerable
            .Range(0, 80)
            .Select(index =>
            {
                var row = (uint)((index * 7 % rowCount) + 1);
                var column = (uint)((index * 11 % columnCount) + 1);
                return new GridRange(
                    new CellAddress(sheetId, row, column),
                    new CellAddress(sheetId, row, column));
            })
            .ToArray();

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            SelectedRange = selectionSteps[0]
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateTextHeavyGrid(double width, double height, CellStyle? style)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"R{row.Row}C{column.Col}";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateWrappedTextHeavyGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 42;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        var style = new CellStyle { WrapText = true };
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"Wrapped value R{row.Row:D2} C{column.Col:D2} forecast pipeline";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateSparklineGrid(double width, double height)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var value = row.Row * column.Col;
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new NumberValue(value),
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var sparklines = new List<SparklineModel>(rowCount);
        var sparklineValues = new Dictionary<Guid, IReadOnlyList<double>>(rowCount);
        foreach (var row in rows)
        {
            var id = Guid.NewGuid();
            var kind = (row.Row % 3u) switch
            {
                0 => SparklineKind.WinLoss,
                1 => SparklineKind.Line,
                _ => SparklineKind.Column
            };
            sparklines.Add(new SparklineModel
            {
                Id = id,
                DataRange = new GridRange(
                    new CellAddress(sheetId, row.Row, 1),
                    new CellAddress(sheetId, row.Row, 16)),
                Location = new CellAddress(sheetId, row.Row, 26),
                Kind = kind
            });
            sparklineValues[id] = Enumerable
                .Range(0, 16)
                .Select(index => (double)(((index + 1) * ((int)row.Row % 11 + 1)) - (row.Row % 5 == 0 ? 35 : 0)))
                .ToArray();
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            Sparklines = sparklines,
            SparklineValues = sparklineValues,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateChartGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 12;
        const double rowHeight = 20;
        const double columnWidth = 72;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        for (uint row = 1; row <= rowCount; row++)
        {
            for (uint col = 1; col <= columnCount; col++)
            {
                ScalarValue rawValue;
                string displayText;
                if (row == 1)
                {
                    rawValue = new TextValue(col == 1 ? "Month" : $"Series {col - 1}");
                    displayText = rawValue.ToString() ?? "";
                }
                else if (col == 1)
                {
                    rawValue = new TextValue($"M{row - 1}");
                    displayText = rawValue.ToString() ?? "";
                }
                else
                {
                    var value = (row - 1) * (col + 2);
                    rawValue = new NumberValue(value);
                    displayText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                cells.Add(new DisplayCell(
                    row,
                    col,
                    rawValue,
                    displayText,
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Render Benchmark",
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 30, 8)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            ShowLegend = true,
            Left = 96,
            Top = 72,
            Width = 560,
            Height = 340
        };

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            Charts = [chart],
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateDrawingObjectHeavyGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 20;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();

        var fills = new[]
        {
            new CellColor(91, 155, 213),
            new CellColor(112, 173, 71),
            new CellColor(237, 125, 49),
            new CellColor(165, 165, 165)
        };
        var outlines = new[]
        {
            new CellColor(68, 114, 196),
            new CellColor(84, 130, 53),
            new CellColor(191, 95, 32),
            new CellColor(89, 89, 89)
        };

        var textBoxes = new List<TextBoxModel>(120);
        for (var index = 0; index < 120; index++)
        {
            var row = (uint)(1 + index % 36);
            var col = (uint)(1 + index * 3 % 17);
            textBoxes.Add(new TextBoxModel
            {
                Name = $"TextBox{index}",
                Anchor = new CellAddress(sheetId, row, col),
                Text = $"Benchmark text box {index % 24}",
                Width = 108 + index % 4 * 16,
                Height = 38 + index % 3 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                RotationDegrees = index % 18 == 0 ? 3 : 0
            });
        }

        var shapes = new List<DrawingShapeModel>(150);
        for (var index = 0; index < 150; index++)
        {
            var row = (uint)(1 + index * 2 % 37);
            var col = (uint)(1 + index * 5 % 18);
            shapes.Add(new DrawingShapeModel
            {
                Name = $"Shape{index}",
                Anchor = new CellAddress(sheetId, row, col),
                Kind = index % 5 == 0
                    ? DrawingShapeKind.Line
                    : index % 2 == 0
                        ? DrawingShapeKind.Ellipse
                        : DrawingShapeKind.Rectangle,
                Width = 72 + index % 5 * 12,
                Height = 28 + index % 4 * 10,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                GradientFillEndColor = index % 7 == 0 ? fills[(index + 1) % fills.Length] : null,
                EffectPreset = index % 11 == 0
                    ? DrawingShapeEffectPreset.Glow
                    : index % 13 == 0
                        ? DrawingShapeEffectPreset.SoftEdges
                        : index % 4 == 0
                            ? DrawingShapeEffectPreset.Shadow
                            : DrawingShapeEffectPreset.None,
                RotationDegrees = index % 23 == 0 ? -4 : 0
            });
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            DrawingShapes = shapes,
            TextBoxes = textBoxes,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateOffscreenDrawingObjectHeavyGrid(double width, double height)
    {
        const int rowCount = 96;
        const int columnCount = 160;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();

        var fills = new[]
        {
            new CellColor(91, 155, 213),
            new CellColor(112, 173, 71),
            new CellColor(237, 125, 49),
            new CellColor(165, 165, 165)
        };
        var outlines = new[]
        {
            new CellColor(68, 114, 196),
            new CellColor(84, 130, 53),
            new CellColor(191, 95, 32),
            new CellColor(89, 89, 89)
        };

        var textBoxes = new List<TextBoxModel>(900);
        for (var index = 0; index < 900; index++)
        {
            var anchor = index % 3 == 0
                ? new CellAddress(sheetId, (uint)(62 + index % 28), (uint)(2 + index % 18))
                : new CellAddress(sheetId, (uint)(1 + index % 28), (uint)(74 + index % 70));
            textBoxes.Add(new TextBoxModel
            {
                Name = $"OffscreenTextBox{index}",
                Anchor = anchor,
                Text = $"Offscreen benchmark text box {index:D4}",
                Width = 128 + index % 5 * 18,
                Height = 34 + index % 4 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                RotationDegrees = index % 17 == 0 ? 7 : 0
            });
        }

        var shapes = new List<DrawingShapeModel>(900);
        for (var index = 0; index < 900; index++)
        {
            var anchor = index % 4 == 0
                ? new CellAddress(sheetId, (uint)(64 + index % 24), (uint)(3 + index % 16))
                : new CellAddress(sheetId, (uint)(2 + index % 30), (uint)(82 + index % 60));
            shapes.Add(new DrawingShapeModel
            {
                Name = $"OffscreenShape{index}",
                Anchor = anchor,
                Kind = index % 5 == 0
                    ? DrawingShapeKind.Line
                    : index % 2 == 0
                        ? DrawingShapeKind.Ellipse
                        : DrawingShapeKind.Rectangle,
                Width = 80 + index % 6 * 14,
                Height = 30 + index % 5 * 8,
                FillColor = fills[index % fills.Length],
                OutlineColor = outlines[index % outlines.Length],
                GradientFillEndColor = index % 9 == 0 ? fills[(index + 1) % fills.Length] : null,
                EffectPreset = index % 10 == 0
                    ? DrawingShapeEffectPreset.Glow
                    : index % 12 == 0
                        ? DrawingShapeEffectPreset.SoftEdges
                        : index % 3 == 0
                            ? DrawingShapeEffectPreset.Shadow
                            : DrawingShapeEffectPreset.None,
                RotationDegrees = index % 19 == 0 ? -6 : 0
            });
        }

        var charts = new List<ChartModel>(20);
        for (var index = 0; index < 20; index++)
        {
            charts.Add(new ChartModel
            {
                Type = ChartType.Column,
                Title = $"Offscreen Chart {index}",
                DataRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 8, 4)),
                Left = 2600 + index * 48,
                Top = 80 + index % 8 * 72,
                Width = 360,
                Height = 220
            });
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel([], rows, columns),
            Charts = charts,
            DrawingShapes = shapes,
            TextBoxes = textBoxes,
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateQuickAnalysisGrid(
        double width,
        double height,
        GridQuickAnalysisPreviewVisualKind visual)
    {
        const int rowCount = 80;
        const int columnCount = 26;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var value = (row.Row * 7) + (column.Col * 3);
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new NumberValue(value),
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null,
                    null));
            }
        }

        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rowCount, columnCount));
        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = previewRange,
            QuickAnalysisPreviewRange = previewRange,
            QuickAnalysisPreviewVisual = visual
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateShrinkToFitGrid(double width, double height)
    {
        const int rowCount = 40;
        const int columnCount = 12;
        const double rowHeight = 20;
        const double columnWidth = 56;

        var sheetId = SheetId.New();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var cells = new List<DisplayCell>(rowCount * columnCount);
        var style = new CellStyle { ShrinkToFit = true };
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var text = $"Shrink text R{row.Row:D2} C{column.Col:D2} 1234567890";
                cells.Add(new DisplayCell(
                    row.Row,
                    column.Col,
                    new TextValue(text),
                    text,
                    null,
                    StyleId.Default,
                    null,
                    style));
            }
        }

        var grid = new GridView
        {
            Width = width,
            Height = height,
            Viewport = new ViewportModel(cells, rows, columns),
            SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1))
        };
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static void RenderOnce(GridView grid, int width, int height)
    {
        grid.InvalidateVisual();
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
    }

    private static void SetResizeTarget(GridView grid, string target)
    {
        var field = typeof(GridView).GetField("_resizeTarget", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(GridView), "_resizeTarget");
        field.SetValue(grid, Enum.Parse(field.FieldType, target));
    }

    private struct CountingFormulaTraceArrowLayoutConsumer : IFormulaTraceArrowLayoutConsumer
    {
        public int Count { get; private set; }

        public void AcceptLayout(
            Point start,
            Point end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget) =>
            Count++;
    }

    private static class StaTestRunner
    {
        private static readonly Lazy<System.Windows.Threading.Dispatcher> StaDispatcher = new(CreateDispatcher);

        public static void Run(Action action)
        {
            Exception? exception = null;
            StaDispatcher.Value.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            if (exception is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private static System.Windows.Threading.Dispatcher CreateDispatcher()
        {
            System.Windows.Threading.Dispatcher? dispatcher = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();

            return dispatcher ?? throw new InvalidOperationException("STA dispatcher was not created.");
        }
    }
}
