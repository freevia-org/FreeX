using System.Diagnostics;
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
    public void Benchmark_RenderDrawingObjectHeavyViewportSelectionRepaints_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 48;
            const int width = 1440;
            const int height = 900;
            const int rowCount = 40;
            const int columnCount = 20;
            var sheetId = SheetId.New();
            var grid = CreateDrawingObjectHeavyGrid(width, height);
            var ranges = Enumerable
                .Range(0, iterations)
                .Select(index =>
                {
                    var row = (uint)((index * 7 % rowCount) + 1);
                    var column = (uint)((index * 5 % columnCount) + 1);
                    return new GridRange(
                        new CellAddress(sheetId, row, column),
                        new CellAddress(sheetId, row, column));
                })
                .ToArray();

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
                grid.SelectedRange = ranges[i];
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
                "PERF GRID_RENDER_DRAWING_OBJECT_SELECTION_REPAINT " +
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
    public void Benchmark_DrawingObjectAnchorRectMetricLookup_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 96;
            const int width = 1440;
            const int height = 900;
            var grid = CreateOffscreenDrawingObjectHeavyGrid(width, height);
            var viewport = grid.Viewport!;
            var rows = viewport.RowMetrics.ToDictionary(row => row.Row);
            var columns = viewport.ColMetrics.ToDictionary(column => column.Col);
            var objects = grid.TextBoxes!
                .Select(textBox => (
                    textBox.Anchor,
                    textBox.Width,
                    textBox.Height,
                    MinimumWidth: 32d,
                    MinimumHeight: 18d))
                .Concat(grid.DrawingShapes!.Select(shape => (
                    shape.Anchor,
                    shape.Width,
                    shape.Height,
                    MinimumWidth: 24d,
                    MinimumHeight: 16d)))
                .ToArray();

            CountAnchorRectsWithScans(viewport, objects).Should().Be(objects.Length);
            CountAnchorRectsWithLookups(rows, columns, objects).Should().Be(objects.Length);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var scanAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var scan = Stopwatch.StartNew();
            var scanCount = 0;
            for (var i = 0; i < iterations; i++)
                scanCount += CountAnchorRectsWithScans(viewport, objects);
            scan.Stop();
            var scanAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - scanAllocatedBefore;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var lookupAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var lookup = Stopwatch.StartNew();
            var lookupCount = 0;
            for (var i = 0; i < iterations; i++)
                lookupCount += CountAnchorRectsWithLookups(rows, columns, objects);
            lookup.Stop();
            var lookupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - lookupAllocatedBefore;

            Console.WriteLine(
                "PERF DRAWING_OBJECT_ANCHOR_RECT_LOOKUP " +
                $"objects={objects.Length} iterations={iterations} " +
                $"scan_total_ms={scan.Elapsed.TotalMilliseconds:F2} " +
                $"lookup_total_ms={lookup.Elapsed.TotalMilliseconds:F2} " +
                $"scan_allocated_bytes={scanAllocatedBytes:N0} " +
                $"lookup_allocated_bytes={lookupAllocatedBytes:N0}");

            scanCount.Should().Be(objects.Length * iterations);
            lookupCount.Should().Be(scanCount);
            lookup.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
            lookupAllocatedBytes.Should().BeLessThanOrEqualTo(scanAllocatedBytes);
        });
    }
}
