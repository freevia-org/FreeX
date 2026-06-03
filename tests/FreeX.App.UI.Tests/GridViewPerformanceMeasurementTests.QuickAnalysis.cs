using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.UI.Tests;
public sealed partial class GridViewPerformanceMeasurementTests
{
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
    public void Benchmark_RenderQuickAnalysisDataBarPreview_NoPositiveValues_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 24;
            const int width = 1440;
            const int height = 900;
            var grid = CreateQuickAnalysisGrid(
                width,
                height,
                GridQuickAnalysisPreviewVisualKind.DataBars,
                (row, column) => -((row.Row * 7) + (column.Col * 3)),
                includeDisplayText: false);

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
                "PERF GRID_RENDER_QUICK_ANALYSIS_DATABARS_NONPOSITIVE " +
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
    public void Benchmark_CalculateQuickAnalysisDataBarPreviewRects_NoPositiveValues_ReportsTiming()
    {
        const int iterations = 64;
        const int rowCount = 5_000;
        const int columnCount = 512;
        const double rowHeight = 20;
        const double columnWidth = 64;

        var sheetId = SheetId.New();
        var cells = Enumerable
            .Range(0, rowCount)
            .Select(index =>
            {
                var row = (uint)(index + 1);
                return new DisplayCell(
                    row,
                    1,
                    new NumberValue(-row),
                    (-row).ToString(CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null);
            })
            .ToArray();
        var rows = Enumerable
            .Range(0, rowCount)
            .Select(index => new RowMetric((uint)(index + 1), rowHeight, index * rowHeight))
            .ToArray();
        var columns = Enumerable
            .Range(0, columnCount)
            .Select(index => new ColMetric((uint)(index + 1), columnWidth, index * columnWidth))
            .ToArray();
        var viewport = new ViewportModel(cells, rows, columns);
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
            "PERF QUICK_ANALYSIS_DATABARS_NONPOSITIVE_RECTS " +
            $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F4} p95_ms={p95:F4} max_ms={ordered[^1]:F4} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeLessThan(250_000);
    }
}
