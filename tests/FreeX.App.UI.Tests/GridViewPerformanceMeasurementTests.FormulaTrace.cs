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
        visitedAllocatedBytes.Should().BeLessThan(2_000);
    }

    [Fact]
    public void Benchmark_RenderFormulaTraceLayerCache_ReportsTimingAndAllocatedBytes()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 48;
            const int arrowCount = 1_000;
            const int width = 1440;
            const int height = 900;
            var grid = CreateFormulaTraceGrid(width, height, arrowCount);

            RenderOnce(grid, width, height);
            RenderOnce(grid, width, height);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
                RenderOnce(grid, width, height);

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Console.WriteLine(
                "PERF GRID_RENDER_FORMULA_TRACE_LAYER_CACHE " +
                $"arrows={arrowCount} steps={iterations} " +
                $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={total.Elapsed.TotalMilliseconds / iterations:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
            allocatedBytes.Should().BeLessThan(1_000_000);
        });
    }

    [Fact]
    public void Benchmark_DrawFormulaTraceVisibleArrows_ReportsTimingAndAllocatedBytes()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 48;
            const int arrowCount = 1_000;
            var drawMethod = typeof(GridView).GetMethod(
                "DrawFormulaTraceArrow",
                BindingFlags.NonPublic | BindingFlags.Instance);
            drawMethod.Should().NotBeNull();
            var grid = new GridView();
            var drawArrow = drawMethod!.CreateDelegate<Action<DrawingContext, Point, Point, FormulaTraceArrowLayoutKind>>(grid);
            var starts = new Point[arrowCount];
            var ends = new Point[arrowCount];
            for (var i = 0; i < arrowCount; i++)
            {
                var row = i / 40;
                var col = i % 40;
                starts[i] = new Point(42 + col * 31, 34 + row * 23);
                ends[i] = new Point(58 + col * 31, 46 + row * 23);
            }

            DrawFormulaTraceArrowsOnce(drawArrow, starts, ends);
            DrawFormulaTraceArrowsOnce(drawArrow, starts, ends);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
                DrawFormulaTraceArrowsOnce(drawArrow, starts, ends);

            total.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Console.WriteLine(
                "PERF FORMULA_TRACE_VISIBLE_ARROW_DRAW " +
                $"arrows={arrowCount} steps={iterations} " +
                $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
                $"mean_ms={total.Elapsed.TotalMilliseconds / iterations:F2} " +
                $"allocated_bytes={allocatedBytes:N0}");

            total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
            allocatedBytes.Should().BeLessThan(45_000_000);
        });
    }
}
