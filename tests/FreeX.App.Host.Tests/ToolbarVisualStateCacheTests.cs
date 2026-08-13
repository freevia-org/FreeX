using System.Diagnostics;
using FluentAssertions;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ToolbarVisualStateCacheTests
{
    [Fact]
    public void GetOrCreate_ReusesStateWhenStyleSourceIsUnchanged()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(workbookId, styleId, CreateState);
        var second = cache.GetOrCreate(workbookId, styleId, CreateState);

        calls.Should().Be(1);
        second.Should().Be(new ToolbarVisualState(
            Bold: true,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            VerticalAlignment: VerticalAlignment.Bottom,
            HorizontalAlignment: HorizontalAlignment.General,
            WrapText: false,
            FontName: "Calibri",
            FontSizeText: "11"));
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(new CellStyle { Bold = true });
        }
    }

    [Fact]
    public void GetOrCreate_DoesNotRebuildFormattingStateWhenUndoAvailabilityChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleId = new StyleId(4);
        var calls = 0;
        var canUndo = false;

        cache.GetOrCreate(workbookId, styleId, CreateState);
        canUndo = true;
        var second = cache.GetOrCreate(workbookId, styleId, CreateState);

        calls.Should().Be(1);
        second.Bold.Should().BeFalse();
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(new CellStyle { Bold = canUndo });
        }
    }

    [Fact]
    public void GetOrCreate_RebuildsStateWhenStyleChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var calls = 0;

        cache.GetOrCreate(workbookId, new StyleId(4), CreateState);
        cache.GetOrCreate(workbookId, new StyleId(5), CreateState);

        calls.Should().Be(2);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default);
        }
    }

    [Fact]
    public void GetOrCreate_ReusesRecentlySeenStateWhenStyleAlternates()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var calls = 0;

        cache.GetOrCreate(workbookId, new StyleId(4), CreateState);
        cache.GetOrCreate(workbookId, new StyleId(5), CreateState);
        cache.GetOrCreate(workbookId, new StyleId(4), CreateState);

        calls.Should().Be(2);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default);
        }
    }

    [Fact]
    public void AddOrUpdate_KeepsRefreshedSourceAfterCacheTrimming()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var refreshedStyle = new StyleId(1);

        for (var style = 1; style <= 16; style++)
        {
            cache.AddOrUpdate(
                workbookId,
                new StyleId(style),
                ToolbarVisualState.From(CellStyle.Default));
        }

        var refreshedState = ToolbarVisualState.From(new CellStyle { Bold = true });
        cache.AddOrUpdate(workbookId, refreshedStyle, refreshedState);
        cache.AddOrUpdate(workbookId, new StyleId(17), ToolbarVisualState.From(CellStyle.Default));
        cache.AddOrUpdate(workbookId, new StyleId(18), ToolbarVisualState.From(CellStyle.Default));

        cache.TryGet(workbookId, refreshedStyle, out var cached).Should().BeTrue();
        cached.Should().Be(refreshedState);
        cache.TryGet(workbookId, new StyleId(2), out _).Should().BeFalse();
    }

    [Fact]
    public void TryGet_ReusesRecentlySeenStateWithoutCreateCallback()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleA = new StyleId(4);
        var styleB = new StyleId(5);
        var stateA = ToolbarVisualState.From(new CellStyle { Bold = true });
        var stateB = ToolbarVisualState.From(new CellStyle { Italic = true });

        cache.AddOrUpdate(workbookId, styleA, stateA);
        cache.AddOrUpdate(workbookId, styleB, stateB);

        cache.TryGet(workbookId, styleA, out var cachedA).Should().BeTrue();
        cache.TryGet(workbookId, styleB, out var cachedB).Should().BeTrue();
        cachedA.Should().Be(stateA);
        cachedB.Should().Be(stateB);
    }

    [Fact]
    public void TryGetCurrent_TracksPromotedRecentlySeenState()
    {
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleA = new StyleId(4);
        var styleB = new StyleId(5);
        var stateA = ToolbarVisualState.From(new CellStyle { Bold = true });
        var stateB = ToolbarVisualState.From(new CellStyle { Italic = true });

        cache.AddOrUpdate(workbookId, styleA, stateA);
        cache.AddOrUpdate(workbookId, styleB, stateB);

        cache.TryGetCurrent(workbookId, styleB, out var currentB).Should().BeTrue();
        currentB.Should().Be(stateB);

        cache.TryGet(workbookId, styleA, out var cachedA).Should().BeTrue();
        cachedA.Should().Be(stateA);
        cache.TryGetCurrent(workbookId, styleA, out var currentA).Should().BeTrue();
        currentA.Should().Be(stateA);
        cache.TryGetCurrent(workbookId, styleB, out _).Should().BeFalse();
    }

    [Fact]
    public void GetOrCreate_RebuildsStateWhenWorkbookChanges()
    {
        var cache = new ToolbarVisualStateCache();
        var styleId = new StyleId(4);
        var calls = 0;

        cache.GetOrCreate(WorkbookId.New(), styleId, CreateState);
        cache.GetOrCreate(WorkbookId.New(), styleId, CreateState);

        calls.Should().Be(2);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(CellStyle.Default);
        }
    }

    [BenchmarkFact]
    public void Benchmark_AlternatingStyleSources_ReportsTiming()
    {
        const int iterations = 20_000;
        var cache = new ToolbarVisualStateCache();
        var workbookId = WorkbookId.New();
        var styleA = new StyleId(4);
        var styleB = new StyleId(5);
        var calls = 0;

        for (var i = 0; i < 100; i++)
        {
            var styleId = i % 2 == 0 ? styleA : styleB;
            if (!cache.TryGet(workbookId, styleId, out _))
                cache.AddOrUpdate(workbookId, styleId, CreateState());
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        calls = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var styleId = i % 2 == 0 ? styleA : styleB;
            if (!cache.TryGet(workbookId, styleId, out _))
                cache.AddOrUpdate(workbookId, styleId, CreateState());
        }
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF TOOLBAR_STATE_CACHE_ALTERNATING " +
            $"steps={iterations} create_calls={calls:N0} " +
            $"total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        calls.Should().Be(0);
        stopwatch.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
        return;

        ToolbarVisualState CreateState()
        {
            calls++;
            return ToolbarVisualState.From(new CellStyle
            {
                Bold = calls % 2 == 0,
                FontName = calls % 2 == 0 ? "Aptos" : "Calibri",
                FontSize = calls % 2 == 0 ? 12 : 11
            });
        }
    }
}
