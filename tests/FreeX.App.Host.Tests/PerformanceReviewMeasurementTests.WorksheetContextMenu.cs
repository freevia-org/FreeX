using System.Diagnostics;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class PerformanceReviewMeasurementTests
{
    [Fact]
    public void Benchmark_WorksheetContextMenuPlanning_ReportsTimingAndAllocatedBytes()
    {
        var targetKinds = new[]
        {
            WorksheetContextMenuTargetKind.Worksheet,
            WorksheetContextMenuTargetKind.RowSelection,
            WorksheetContextMenuTargetKind.ColumnSelection,
            WorksheetContextMenuTargetKind.Picture,
            WorksheetContextMenuTargetKind.Shape,
            WorksheetContextMenuTargetKind.TextBox
        };
        var states = new[]
        {
            WorksheetContextMenuState.Default,
            new WorksheetContextMenuState(HasThreadedComment: true),
            new WorksheetContextMenuState(HasThreadedComment: true, IsThreadedCommentResolved: true),
            new WorksheetContextMenuState(HasNote: true, HasHyperlink: true),
            new WorksheetContextMenuState(HasAutoFilterHeaderTarget: true, HasDropdownTarget: true),
            new WorksheetContextMenuState(
                HasThreadedComment: true,
                IsThreadedCommentResolved: true,
                HasNote: true,
                HasHyperlink: true,
                HasAutoFilterHeaderTarget: true,
                HasDropdownTarget: true)
        };

        MeasureWorksheetContextMenuPlanning(targetKinds, states, iterations: 50);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var result = MeasureWorksheetContextMenuPlanning(targetKinds, states, iterations: 2_000);
        Console.WriteLine(
            "PERF WORKSHEET_CONTEXT_MENU_PLANNING " +
            $"steps={result.Measurement.StepCount} calls={result.CallCount:N0} " +
            $"command_items={result.CommandItemCount:N0} " +
            $"total_ms={result.Measurement.TotalMilliseconds:F2} " +
            $"mean_ms={result.Measurement.MeanMilliseconds:F4} " +
            $"p95_ms={result.Measurement.P95Milliseconds:F4} " +
            $"max_ms={result.Measurement.MaxMilliseconds:F4} " +
            $"allocated_bytes={result.Measurement.AllocatedBytes:N0}");

        result.Measurement.StepCount.Should().Be(2_000);
        result.CallCount.Should().Be(72_000);
        result.CommandItemCount.Should().BeGreaterThan(0);
        result.Measurement.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    private static WorksheetContextMenuPlanningResult MeasureWorksheetContextMenuPlanning(
        IReadOnlyList<WorksheetContextMenuTargetKind> targetKinds,
        IReadOnlyList<WorksheetContextMenuState> states,
        int iterations)
    {
        var timings = new List<double>(iterations);
        var callCount = 0;
        var commandItemCount = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var stepStarted = Stopwatch.GetTimestamp();
            foreach (var targetKind in targetKinds)
            {
                foreach (var state in states)
                {
                    var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind, state);
                    commandItemCount += commands.Count;
                    callCount++;
                }
            }

            timings.Add(Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
        }

        total.Stop();
        return new WorksheetContextMenuPlanningResult(
            MeasurementResult.From(
                timings,
                total.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore),
            callCount,
            commandItemCount);
    }

    private sealed record WorksheetContextMenuPlanningResult(
        MeasurementResult Measurement,
        int CallCount,
        int CommandItemCount);
}
