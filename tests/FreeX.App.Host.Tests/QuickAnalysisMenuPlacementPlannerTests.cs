using FluentAssertions;
using FreeX.Core.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisMenuPlacementPlannerTests
{
    [Fact]
    public void BuildAnchor_PlacesMenuAtVisibleSelectionBottomRight()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 4));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60)
            ],
            [
                new ColMetric(1, 80, 0),
                new ColMetric(2, 80, 80),
                new ColMetric(3, 80, 160),
                new ColMetric(4, 80, 240)
            ]);

        var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
            selection,
            viewport,
            rowHeaderWidth: 44,
            columnHeaderHeight: 24);

        anchor.Should().Be(new Point(368, 108));
    }

    [Fact]
    public void BuildAnchor_UsesVisibleSelectionEdgeWhenRangeExtendsOffscreen()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 20, 20));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60)
            ],
            [
                new ColMetric(2, 80, 80),
                new ColMetric(3, 80, 160)
            ]);

        var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
            selection,
            viewport,
            rowHeaderWidth: 44,
            columnHeaderHeight: 24);

        anchor.Should().Be(new Point(288, 108));
    }

    [Fact]
    public void BuildAnchor_UsesIndexedMetricScans()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "QuickAnalysisMenuPlacementPlanner.cs"));

        var method = source[
            source.IndexOf("public static Point BuildAnchor", StringComparison.Ordinal)..
            source.IndexOf("private static RowMetric?", StringComparison.Ordinal)];
        method.Should().NotContain(".Where(");
        method.Should().NotContain(".OrderByDescending(");
        source.Should().Contain("private static int FindLastRowIndexAtOrBefore");
        source.Should().Contain("private static int FindLastColumnIndexAtOrBefore");
        source.Should().Contain("while (low <= high)");
    }

    [Fact]
    public void Benchmark_BuildAnchor_WithLargeViewportMetricLists()
    {
        var sheetId = SheetId.New();
        var rows = Enumerable.Range(1, 20_000)
            .Select(index => new RowMetric((uint)index, 20, (index - 1) * 20))
            .ToArray();
        var columns = Enumerable.Range(1, 4_000)
            .Select(index => new ColMetric((uint)index, 80, (index - 1) * 80))
            .ToArray();
        var viewport = new ViewportModel([], rows, columns);
        var selection = new GridRange(new CellAddress(sheetId, 250, 100), new CellAddress(sheetId, 260, 110));

        var sw = Stopwatch.StartNew();
        Point anchor = default;
        for (var i = 0; i < 2_000; i++)
        {
            anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
                selection,
                viewport,
                rowHeaderWidth: 44,
                columnHeaderHeight: 24);
        }

        sw.Stop();
        Console.WriteLine($"Quick Analysis anchor large metric scan: {sw.ElapsedMilliseconds}ms for 2000 runs");
        anchor.Should().Be(new Point(8848, 5228));
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }
}
