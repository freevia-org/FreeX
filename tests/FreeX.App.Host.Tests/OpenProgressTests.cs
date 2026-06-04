using FluentAssertions;
using System.Diagnostics;

namespace FreeX.App.Host.Tests;

public sealed class OpenProgressTests
{
    [Fact]
    public void CalculateOpenStageProgress_AdvancesLinearlyWithinStage()
    {
        OpenWorkbookProgressPlanner.CalculateStageProgress(
                stageStartPercent: 16,
                stageEndPercent: 90,
                elapsed: TimeSpan.FromSeconds(5),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().Be(53);
    }

    [Fact]
    public void CalculateOpenStageProgress_StaysBelowStageEndUntilWorkCompletes()
    {
        OpenWorkbookProgressPlanner.CalculateStageProgress(
                stageStartPercent: 16,
                stageEndPercent: 90,
                elapsed: TimeSpan.FromSeconds(30),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().Be(89.5);
    }

    [Fact]
    public void FormatLoadingFileDetail_ChangesEveryThreeSeconds()
    {
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(0))
            .Should().Be("Loading file (parsing)");
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(3))
            .Should().Be("Loading file (reading worksheets)");
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(6))
            .Should().Be("Loading file (building workbook)");
    }

    [Fact]
    public void FormatLoadingFileDetail_PreservesTrimmedCaseInsensitivePhaseMatching()
    {
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail(" Parsing ", TimeSpan.FromSeconds(9))
            .Should().Be("Loading file (loading styles)");
    }

    [BenchmarkFact]
    public void Benchmark_FormatLoadingFileDetail_RepeatedTimerTicksReportsAllocation()
    {
        const int iterations = 200_000;

        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.Zero).Should().NotBeEmpty();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (!OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(i % 12))
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
}
