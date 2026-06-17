using System.Diagnostics;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarViewModelCacheTests
{
    private static StatusBarViewModelCache CreateCache() =>
        new(UiTextStatusBarTextProvider.Instance);

    [Fact]
    public void GetStats_ReusesNeutralModelWhenStatsAreUnchanged()
    {
        var cache = CreateCache();
        var stats = new StatusBarCalculator.Stats(12, 4, 3, 4, 2, 6);

        var first = cache.GetStats(stats);
        var second = cache.GetStats(stats);
        var changed = cache.GetStats(stats with { Sum = 18 });

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.AreStatsVisible.Should().BeTrue();
        changed.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value.Should().Be("Sum: 18");
    }

    [Fact]
    public void GetReady_ReusesNeutralModelWhenTextIsUnchanged()
    {
        var cache = CreateCache();

        var first = cache.GetReady("Ready");
        var second = cache.GetReady("Ready");
        var changed = cache.GetReady("Edit");

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.IsReadyVisible.Should().BeTrue();
        changed.ReadyText.Should().Be("Edit");
    }

    [Fact]
    public void Clear_DropsCachedModels()
    {
        var cache = CreateCache();
        var stats = new StatusBarCalculator.Stats(12, 4, 3, 4, 2, 6);

        var first = cache.GetStats(stats);
        cache.Clear();
        var afterClear = cache.GetStats(stats);

        afterClear.Should().NotBeSameAs(first);
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedStatsModel_ReportsCachedTimingAndAllocation()
    {
        const int iterations = 50_000;
        var stats = new StatusBarCalculator.Stats(
            Count: 4,
            NumericalCount: 3,
            Sum: 123456,
            Average: 41152,
            Min: 2,
            Max: 98765);
        var cache = CreateCache();

        for (var i = 0; i < 100; i++)
            _ = cache.GetStats(stats);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        StatusBarViewModel? lastState = null;
        for (var i = 0; i < iterations; i++)
            lastState = cache.GetStats(stats);
        stopwatch.Stop();
        var cachedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF STATUS_VIEW_MODEL_STATS_CACHE " +
            $"steps={iterations:N0} " +
            $"cached_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"cached_allocated_bytes={cachedBytes:N0}");

        lastState.Should().BeSameAs(cache.GetStats(stats));
        cachedBytes.Should().BeLessThan(
            iterations * 8,
            "cached status refreshes should reuse the formatted neutral model instead of rebuilding it");
    }
}
