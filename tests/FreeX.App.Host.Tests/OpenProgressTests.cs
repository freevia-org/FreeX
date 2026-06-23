using FluentAssertions;
using Free.Shared.AppServices;
using System.Diagnostics;

namespace FreeX.App.Host.Tests;

public sealed class OpenProgressTests
{
    [Fact]
    public void CalculateOpenStageProgress_AdvancesLinearlyWithinStage()
    {
        WorkbookProgressPresentationPlanner.CalculateRunningStagePercent(
                startPercent: 16,
                endPercent: 90,
                elapsed: TimeSpan.FromSeconds(5),
                expectedDuration: TimeSpan.FromSeconds(10),
                holdbackPercent: 0.5)
            .Should().Be(53);
    }

    [Fact]
    public void CalculateOpenStageProgress_StaysBelowStageEndUntilWorkCompletes()
    {
        WorkbookProgressPresentationPlanner.CalculateRunningStagePercent(
                startPercent: 16,
                endPercent: 90,
                elapsed: TimeSpan.FromSeconds(30),
                expectedDuration: TimeSpan.FromSeconds(10),
                holdbackPercent: 0.5)
            .Should().Be(89.5);
    }

    [Fact]
    public void FormatLoadingFileDetail_ChangesEveryThreeSeconds()
    {
        FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(0))
            .Should().Be("Loading file (parsing)");
        FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(3))
            .Should().Be("Loading file (reading worksheets)");
        FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(6))
            .Should().Be("Loading file (building workbook)");
    }

    [Fact]
    public void FormatLoadingFileDetail_PreservesTrimmedCaseInsensitivePhaseMatching()
    {
        FormatLoadingFileDetail(" Parsing ", TimeSpan.FromSeconds(9))
            .Should().Be("Loading file (loading styles)");
    }

    [BenchmarkFact]
    public void Benchmark_FormatLoadingFileDetail_RepeatedTimerTicksReportsAllocation()
    {
        const int iterations = 200_000;

        FormatLoadingFileDetail("parsing", TimeSpan.Zero).Should().NotBeEmpty();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (!FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(i % 12))
                    .StartsWith("Loading file", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The progress detail should remain localized.");
            }
        }

        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF OPEN_PROGRESS_DETAIL_FORMAT " +
            $"steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        allocatedBytes.Should().BeLessThan(50_000);
    }

    private static string FormatLoadingFileDetail(string phase, TimeSpan elapsed) =>
        WorkbookProgressTextFormatter.FormatOpen(phase, elapsed, percent: null, UiText.Get).Detail;
}
