using System.Diagnostics;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewPerformanceMeasurementTests
{
    [Fact]
    public void Benchmark_RenderCommentIndicatorHeavyViewport_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 16;
            const int width = 1440;
            const int height = 900;
            var grid = CreateCommentIndicatorGrid(width, height);

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
                grid.Viewport = new(
                    grid.Viewport!.Cells,
                    grid.Viewport.RowMetrics,
                    grid.Viewport.ColMetrics);
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
                "PERF GRID_RENDER_COMMENT_INDICATORS " +
                $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            timings.Average().Should().BeGreaterThan(0);
        });
    }
}
