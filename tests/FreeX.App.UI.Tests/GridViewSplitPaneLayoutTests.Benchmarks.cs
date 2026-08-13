using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Diagnostics;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    private struct CollectingFormulaTraceArrowLayoutConsumer : IFormulaTraceArrowLayoutConsumer
    {
        private List<FormulaTraceArrowLayout>? _layouts;

        public void AcceptLayout(
            LayoutPoint start,
            LayoutPoint end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget,
            FormulaTraceArrowKind arrowKind)
        {
            _layouts ??= [];
            _layouts.Add(new FormulaTraceArrowLayout(start, end, kind, navigationTarget, arrowKind));
        }

        public readonly IReadOnlyList<FormulaTraceArrowLayout> Layouts => _layouts ?? [];
    }

    private struct CollectingSplitPaneCellLayoutConsumer : ISplitPaneCellLayoutConsumer
    {
        private List<SplitPaneCellLayout>? _layouts;

        public void AcceptLayout(SplitPaneCellLayout layout)
        {
            _layouts ??= [];
            _layouts.Add(layout);
        }

        public readonly IReadOnlyList<SplitPaneCellLayout> Layouts => _layouts ?? [];
    }

    private struct CountingSplitPaneCellLayoutConsumer : ISplitPaneCellLayoutConsumer
    {
        public int Count { get; private set; }

        public void AcceptLayout(SplitPaneCellLayout layout) => Count++;
    }

    [BenchmarkFact]
    public void Benchmark_SplitPaneCellLayoutMaterialization_ReportsAllocations()
    {
        const int iterations = 400;
        var viewport = MeasuredSplitPaneViewport();

        SplitPaneCellLayoutPlanner.CalculateLayouts(viewport).Should().HaveCount(2_040);
        SplitPaneCellLayoutPlanner.CalculateLayouts(viewport).Should().HaveCount(2_040);
        var warmVisitor = new CountingSplitPaneCellLayoutConsumer();
        SplitPaneCellLayoutPlanner.VisitLayouts(viewport, null, null, ref warmVisitor);
        warmVisitor.Count.Should().Be(2_040);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var materializedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var materializedTotal = Stopwatch.StartNew();
        var layoutCount = 0;
        for (var i = 0; i < iterations; i++)
            layoutCount += SplitPaneCellLayoutPlanner.CalculateLayouts(viewport).Count;

        materializedTotal.Stop();
        var materializedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - materializedAllocatedBefore;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var visitedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var visitedTotal = Stopwatch.StartNew();
        var visitedCount = 0;
        for (var i = 0; i < iterations; i++)
        {
            var consumer = new CountingSplitPaneCellLayoutConsumer();
            SplitPaneCellLayoutPlanner.VisitLayouts(viewport, null, null, ref consumer);
            visitedCount += consumer.Count;
        }

        visitedTotal.Stop();
        var visitedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - visitedAllocatedBefore;

        Console.WriteLine(
            "PERF SPLIT_PANE_CELL_LAYOUT_MATERIALIZATION " +
            $"steps={iterations} total_ms={materializedTotal.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={materializedAllocatedBytes:N0}");

        Console.WriteLine(
            "PERF SPLIT_PANE_CELL_LAYOUT_VISITOR " +
            $"steps={iterations} total_ms={visitedTotal.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={visitedAllocatedBytes:N0}");

        layoutCount.Should().Be(2_040 * iterations);
        visitedCount.Should().Be(layoutCount);
        materializedAllocatedBytes.Should().BeGreaterThan(0);
        visitedAllocatedBytes.Should().BeLessThan(materializedAllocatedBytes);
        materializedAllocatedBytes.Should().BeLessThan(80_000_000);
        visitedAllocatedBytes.Should().BeLessThan(8_000_000);
    }

    [BenchmarkFact]
    public void Benchmark_SplitPaneScrollbarChrome_ReportsAllocations()
    {
        const int iterations = 50_000;
        const double actualWidth = 1_920;
        const double actualHeight = 1_080;
        var viewport = MeasuredSplitPaneViewport();

        GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth, actualHeight)
            .HorizontalTopRight.Should().NotBeNull();
        GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth, actualHeight)
            .VerticalBottomLeft.Should().NotBeNull();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        SplitPaneScrollbar? horizontal = null;
        SplitPaneScrollbar? vertical = null;
        for (var i = 0; i < iterations; i++)
        {
            var chrome = GridView.CalculateSplitPaneScrollbarChrome(viewport, actualWidth, actualHeight);
            horizontal = chrome.HorizontalTopRight;
            vertical = chrome.VerticalBottomLeft;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF SPLIT_PANE_SCROLLBAR_CHROME " +
            $"steps={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        horizontal.Should().NotBeNull();
        vertical.Should().NotBeNull();
        allocatedBytes.Should().BeLessThan(17_000_000);
    }

    private static DisplayCell Cell(uint row, uint col, string text, CellStyle? style = null) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null, style);

    private static ViewportModel MeasuredSplitPaneViewport()
    {
        var topRows = new List<RowMetric>(20);
        var bottomRows = new List<RowMetric>(80);
        var leftColumns = new List<ColMetric>(12);
        var rightColumns = new List<ColMetric>(90);
        var cells = new List<DisplayCell>(2_040);

        for (uint row = 1; row <= 20; row++)
            topRows.Add(new RowMetric(row, 18, (row - 1) * 18));

        for (uint row = 200; row < 280; row++)
            bottomRows.Add(new RowMetric(row, 18, (row - 200) * 18));

        for (uint col = 1; col <= 12; col++)
            leftColumns.Add(new ColMetric(col, 64, (col - 1) * 64));

        for (uint col = 80; col < 170; col++)
            rightColumns.Add(new ColMetric(col, 64, (col - 80) * 64));

        foreach (var row in topRows)
        {
            foreach (var col in leftColumns)
                cells.Add(Cell(row.Row, col.Col, "pinned"));
            foreach (var col in rightColumns)
                cells.Add(Cell(row.Row, col.Col, "top"));
        }

        return new ViewportModel(
            [],
            bottomRows,
            rightColumns,
            SplitPanes: new SplitPaneState(
                21,
                13,
                topRows,
                leftColumns,
                cells,
                rightColumns,
                bottomRows));
    }

    private static ViewportModel SplitViewport() =>
        new(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)]));

}
