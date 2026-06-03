using System.Diagnostics;
using System.Windows;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarDisplayStateTests
{
    [Fact]
    public void Ready_HidesStatsAndShowsReadyText()
    {
        var state = StatusBarDisplayState.Ready("Ready");

        state.ReadyVisibility.Should().Be(Visibility.Visible);
        state.StatsVisibility.Should().Be(Visibility.Collapsed);
        state.ReadyText.Should().Be("Ready");
        state.CountText.Should().BeEmpty();
    }

    [Fact]
    public void Stats_FormatsVisibleAggregateText()
    {
        var stats = new StatusBarCalculator.Stats(
            Count: 4,
            NumericalCount: 3,
            Sum: 12,
            Average: 4,
            Min: 2,
            Max: 6);

        var state = StatusBarDisplayState.Stats(stats);

        state.ReadyVisibility.Should().Be(Visibility.Collapsed);
        state.StatsVisibility.Should().Be(Visibility.Visible);
        state.AverageText.Should().Be("Average: 4");
        state.CountText.Should().Be("Count: 4");
        state.NumericalCountText.Should().Be("Numerical Count: 3");
        state.SumText.Should().Be("Sum: 12");
        state.MinText.Should().Be("Min: 2");
        state.MaxText.Should().Be("Max: 6");
    }

    [Fact]
    public void Cache_ReusesStatsStateWhenStatsAreUnchanged()
    {
        var cache = new StatusBarDisplayStateCache();
        var stats = new StatusBarCalculator.Stats(12, 4, 3, 4, 2, 6);

        var first = cache.GetStats(stats);
        var second = cache.GetStats(stats);
        var changed = cache.GetStats(stats with { Sum = 18 });

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.SumText.Should().Be("Sum: 18");
    }

    [Fact]
    public void Cache_ReusesReadyStateWhenTextIsUnchanged()
    {
        var cache = new StatusBarDisplayStateCache();

        var first = cache.GetReady("Ready");
        var second = cache.GetReady("Ready");
        var changed = cache.GetReady("Edit");

        second.Should().BeSameAs(first);
        changed.Should().NotBeSameAs(first);
        changed.ReadyText.Should().Be("Edit");
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedStatsDisplayState_ReportsCachedTimingAndAllocation()
    {
        const int iterations = 50_000;
        var stats = new StatusBarCalculator.Stats(
            Count: 4,
            NumericalCount: 3,
            Sum: 123456,
            Average: 41152,
            Min: 2,
            Max: 98765);
        var cache = new StatusBarDisplayStateCache();

        for (var i = 0; i < 100; i++)
        {
            _ = StatusBarDisplayState.Stats(stats);
            _ = cache.GetStats(stats);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var uncached = MeasureRepeatedStatsState(iterations, () => StatusBarDisplayState.Stats(stats));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var cached = MeasureRepeatedStatsState(iterations, () => cache.GetStats(stats));

        Console.WriteLine(
            "PERF STATUS_DISPLAY_STATE_STATS_FORMATTING " +
            $"steps={iterations:N0} " +
            $"uncached_ms={uncached.TotalMilliseconds:F2} " +
            $"cached_ms={cached.TotalMilliseconds:F2} " +
            $"uncached_allocated_bytes={uncached.AllocatedBytes:N0} " +
            $"cached_allocated_bytes={cached.AllocatedBytes:N0}");

        cached.LastState.Should().BeSameAs(cache.GetStats(stats));
        cached.AllocatedBytes.Should().BeLessThan(
            uncached.AllocatedBytes / 10,
            "cached status refreshes should reuse formatted display state strings");
    }

    private static StatusDisplayStateMeasurement MeasureRepeatedStatsState(
        int iterations,
        Func<StatusBarDisplayState> createState)
    {
        StatusBarDisplayState? lastState = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            lastState = createState();
        stopwatch.Stop();

        return new StatusDisplayStateMeasurement(
            stopwatch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            lastState!);
    }

    private sealed record StatusDisplayStateMeasurement(
        double TotalMilliseconds,
        long AllocatedBytes,
        StatusBarDisplayState LastState);
}
