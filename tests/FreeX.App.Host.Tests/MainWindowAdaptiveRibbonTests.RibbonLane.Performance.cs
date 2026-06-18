using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

// Ribbon UI lane — resize performance & responsiveness.
// The reported "very slow / unresponsive" ribbon came from the adaptive layout re-measuring and
// re-applying state on every resize tick (and, at narrow widths, never converging). These guards pin
// the post-fix behavior: a redundant resize does no layout work, a back-and-forth resize sweep reuses
// its measurement caches instead of re-measuring, and a benchmark reports per-step timing.
public sealed partial class MainWindowAdaptiveRibbonTests
{
    // Back-and-forth across the adaptive bands; revisiting widths must hit caches the second time.
    private static readonly double[] ResizePerfSweepWidths =
        { 1280d, 1160d, 1040d, 920d, 820d, 920d, 1040d, 1160d, 1280d };

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_RepeatedResizeToSameWidth_DoesNoRedundantLayoutWork()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1280);

            // Warm the caches once, then measure: repeatedly compacting at an unchanged width must apply
            // zero new adaptive state (every pass is a cache skip). A regression that recomputes layout on
            // every tick — the old slowness — shows up here as StateApplyCount > 0.
            harness.ForceRibbonCompaction();
            harness.ResetRibbonDiagnostics();

            for (var i = 0; i < 20; i++)
                harness.ForceRibbonCompaction();

            harness.AdaptiveDiagnostics.StateApplyCount.Should().Be(0,
                "compacting repeatedly at the same width must not re-apply adaptive state or re-run layout");
        });
    }

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_ResizeSweep_SecondPassReusesMeasurementCaches()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1280);

            // First sweep warms a measurement cache entry per width band.
            harness.ResizeThroughResizePath(ResizePerfSweepWidths);

            // Second identical sweep should reuse those caches — no group should need re-measuring. The
            // pre-fix panel re-measured the whole strip on every tick, which this would catch as a large
            // GroupMeasurementCount.
            harness.ResetRibbonDiagnostics();
            harness.ResizeThroughResizePath(ResizePerfSweepWidths);

            harness.AdaptiveDiagnostics.GroupMeasurementCount.Should().Be(0,
                "revisiting widths already measured in the first sweep must not trigger any re-measurement");
        });
    }

    [BenchmarkFact]
    [Trait("Category", "RibbonUiLanePerf")]
    public void RibbonLane_ResizeSweep_ReportsPerStepTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1280);

            harness.MeasureResizeStepMilliseconds(ResizePerfSweepWidths, iterations: 1); // warm
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = harness.MeasureResizeStepMilliseconds(ResizePerfSweepWidths, iterations: 3);
            var ordered = timings.OrderBy(t => t).ToList();
            var mean = timings.Average();
            var p95 = ordered[(int)(ordered.Count * 0.95)];
            var max = ordered[^1];

            Console.WriteLine(
                $"PERF RIBBON_LANE_RESIZE steps={timings.Count} mean_ms={mean:F2} p95_ms={p95:F2} max_ms={max:F2}");

            // Generous ceiling: a healthy converged reflow is a few ms/step; this only trips on a gross
            // regression (e.g. re-measuring everything or a near-loop) without being timing-flaky on CI.
            mean.Should().BeLessThan(150d, "a converged ribbon reflow per resize step should be fast");
        });
    }
}
