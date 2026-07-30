using System.Reflection;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R92-app-freeze-scroll-perf-5-1: the embedded-chart render cache (GridView.ChartRenderCache.cs)
/// was keyed on the ViewportModel REFERENCE. Every viewport rebuild -- including a pure scroll
/// tick, which only moves the visible window without touching any chart's own source data --
/// produces a brand-new ViewportModel record (fresh Cells/RowMetrics/ColMetrics list instances),
/// so the cache missed and forced a full OxyPlot re-render + PNG re-encode of every visible chart
/// on every scroll frame. These tests assert the cache now HITS (returns the same cached
/// ImageSource, no new dictionary entry) when the chart's own data cells are unchanged across two
/// distinct ViewportModel instances, and correctly MISSES (renders + caches a fresh entry) when a
/// cell inside the chart's data range actually changes -- using deterministic
/// object-identity/dictionary-count assertions rather than wall-clock timing.
/// </summary>
public sealed class R92_ChartRenderCacheDataKeyTests
{
    private static ImageSource? InvokeGetCachedChartImage(
        GridView grid, ChartModel chart, ViewportModel viewport, WorkbookTheme theme, double renderScale)
    {
        var method = typeof(GridView).GetMethod("GetCachedChartImage", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ImageSource?)method!.Invoke(grid, [chart, viewport, theme, renderScale]);
    }

    private static int GetChartRenderCacheCount(GridView grid)
    {
        var field = typeof(GridView).GetField("_chartRenderCache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var dictionary = field!.GetValue(grid).Should().BeAssignableTo<System.Collections.ICollection>().Subject;
        return dictionary.Count;
    }

    private static long InvokeComputeChartDataFingerprint(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(GridView).GetMethod(
            "ComputeChartDataFingerprint", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (long)method!.Invoke(null, [chart, viewport])!;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ChartModel CreateColumnChart(SheetId sheetId) => new()
    {
        Type = ChartType.Column,
        DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
        Width = 200,
        Height = 150
    };

    private static ViewportModel CreateViewport(uint topRow, string q2Value) =>
        new(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, q2Value)
            ],
            // RowMetrics differs with topRow to simulate a genuinely different (freshly built,
            // non-reference-equal) ViewportModel from a scroll tick -- the scroll offset changed
            // but the chart's own data cells (rows 1-3, cols 1-2) did not.
            [new RowMetric(topRow, 20, 0)],
            []);

    [Fact]
    public void ComputeChartDataFingerprint_MatchesAcrossDistinctViewportInstances_WhenChartDataUnchanged()
    {
        var sheetId = SheetId.New();
        var chart = CreateColumnChart(sheetId);

        var viewportA = CreateViewport(topRow: 1, q2Value: "20");
        var viewportB = CreateViewport(topRow: 50, q2Value: "20"); // different scroll offset, same chart data

        ReferenceEquals(viewportA, viewportB).Should().BeFalse();
        InvokeComputeChartDataFingerprint(chart, viewportA).Should().Be(InvokeComputeChartDataFingerprint(chart, viewportB));
    }

    [Fact]
    public void ComputeChartDataFingerprint_DiffersWhenADataCellValueChanges()
    {
        var sheetId = SheetId.New();
        var chart = CreateColumnChart(sheetId);

        var viewportA = CreateViewport(topRow: 1, q2Value: "20");
        var viewportB = CreateViewport(topRow: 1, q2Value: "99"); // a real edit to a chart data cell

        InvokeComputeChartDataFingerprint(chart, viewportA)
            .Should().NotBe(InvokeComputeChartDataFingerprint(chart, viewportB));
    }

    [Fact]
    public void GetCachedChartImage_HitsCacheAcrossDistinctViewportInstances_WhenChartDataUnchanged()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateColumnChart(sheetId);
            var grid = new GridView();

            var viewportA = CreateViewport(topRow: 1, q2Value: "20");
            var viewportB = CreateViewport(topRow: 50, q2Value: "20"); // pure scroll: new instance, same chart data
            ReferenceEquals(viewportA, viewportB).Should().BeFalse();

            var first = InvokeGetCachedChartImage(grid, chart, viewportA, WorkbookTheme.Office, 1.0);
            first.Should().NotBeNull();
            GetChartRenderCacheCount(grid).Should().Be(1);

            var second = InvokeGetCachedChartImage(grid, chart, viewportB, WorkbookTheme.Office, 1.0);

            ReferenceEquals(first, second).Should().BeTrue(
                "a scroll-only viewport rebuild must hit the existing cache entry instead of re-rendering the chart");
            GetChartRenderCacheCount(grid).Should().Be(1, "no new entry should be added on a cache hit");
        });
    }

    [Fact]
    public void GetCachedChartImage_MissesCache_WhenChartDataCellValueChanges()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateColumnChart(sheetId);
            var grid = new GridView();

            var viewportA = CreateViewport(topRow: 1, q2Value: "20");
            var viewportB = CreateViewport(topRow: 1, q2Value: "99"); // a real edit to chart data

            var first = InvokeGetCachedChartImage(grid, chart, viewportA, WorkbookTheme.Office, 1.0);
            first.Should().NotBeNull();
            GetChartRenderCacheCount(grid).Should().Be(1);

            var second = InvokeGetCachedChartImage(grid, chart, viewportB, WorkbookTheme.Office, 1.0);
            second.Should().NotBeNull();

            ReferenceEquals(first, second).Should().BeFalse(
                "a changed chart data cell must produce a freshly rendered image, not a stale cached one");
            GetChartRenderCacheCount(grid).Should().Be(2, "a genuinely different chart data key must add a new cache entry");
        });
    }
}
