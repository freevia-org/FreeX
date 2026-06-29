using System.Diagnostics;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarViewModelCacheTests
{
    private static StatusBarViewModelCache CreateCache() =>
        new(new ResourceKeyStatusBarTextProvider(UiText.Get));

    [Fact]
    public void GetStats_ReusesNeutralModelWhenStatsAreUnchanged()
    {
        var cache = CreateCache();
        var stats = new WorkbookSelectionStats(12, 4, 3, 4, 2, 6);

        var first = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);
        var second = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);
        var changed = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats with { Sum = 18 });

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.AreStatsVisible.Should().BeTrue();
        changed.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: 18");
    }

    [Fact]
    public void GetReady_ReusesNeutralModelWhenTextIsUnchanged()
    {
        var cache = CreateCache();

        var first = cache.GetReady(StatusBarViewMode.Normal, zoomPercent: 0, "Ready");
        var second = cache.GetReady(StatusBarViewMode.Normal, zoomPercent: 0, "Ready");
        var changed = cache.GetReady(StatusBarViewMode.Normal, zoomPercent: 0, "Edit");

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.IsReadyVisible.Should().BeTrue();
        changed.ReadyText.Should().Be("Edit");
    }

    [Fact]
    public void GetReady_UsesProviderReadyTextWhenNoOverrideIsSupplied()
    {
        var cache = new StatusBarViewModelCache(new ResourceKeyStatusBarTextProvider(
            key => key == StatusBarTextResourceKeys.ReadyText ? "Shared Ready" : key));

        var state = cache.GetReady(StatusBarViewMode.Normal, zoomPercent: 0);

        state.ReadyText.Should().Be("Shared Ready");
    }

    [Fact]
    public void Clear_DropsCachedModels()
    {
        var cache = CreateCache();
        var stats = new WorkbookSelectionStats(12, 4, 3, 4, 2, 6);

        var first = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);
        cache.Clear();
        var afterClear = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);

        afterClear.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Cache_LivesInSharedAppServicesAndUsesNeutralStats()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.AppServices", "StatusBarViewModelCache.cs");

        source.Should().Contain("WorkbookSelectionStats");
        source.Should().NotContain("StatusBarCalculator.Stats");
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedStatsModel_ReportsCachedTimingAndAllocation()
    {
        const int iterations = 50_000;
        var stats = new WorkbookSelectionStats(
            Count: 4,
            NumericalCount: 3,
            Sum: 123456,
            Average: 41152,
            Min: 2,
            Max: 98765);
        var cache = CreateCache();

        for (var i = 0; i < 100; i++)
            _ = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        StatusBarViewModel? lastState = null;
        for (var i = 0; i < iterations; i++)
            lastState = cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats);
        stopwatch.Stop();
        var cachedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF STATUS_VIEW_MODEL_STATS_CACHE " +
            $"steps={iterations:N0} " +
            $"cached_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"cached_allocated_bytes={cachedBytes:N0}");

        lastState.Should().BeSameAs(cache.GetStats(StatusBarViewMode.Normal, zoomPercent: 0, stats));
        cachedBytes.Should().BeLessThan(
            iterations * 8,
            "cached status refreshes should reuse the formatted neutral model instead of rebuilding it");
    }
}
