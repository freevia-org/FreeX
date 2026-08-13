using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    private static readonly double[] ResizePerfSweepWidths =
        { 1280d, 1160d, 1040d, 920d, 820d, 920d, 1040d, 1160d, 1280d };

    [BenchmarkFact]
    [Trait("Category", "RibbonUiLanePerf")]
    public void RibbonLane_ResizeSweep_ReportsSharedPanelTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1280);

            harness.MeasureResizeStepMilliseconds(ResizePerfSweepWidths, iterations: 1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = harness.MeasureResizeStepMilliseconds(ResizePerfSweepWidths, iterations: 3);
            var ordered = timings.OrderBy(timing => timing).ToList();
            var mean = timings.Average();
            var p95 = ordered[(int)(ordered.Count * 0.95)];
            var max = ordered[^1];

            Console.WriteLine(
                $"PERF RIBBON_LANE_RESIZE steps={timings.Count} mean_ms={mean:F2} p95_ms={p95:F2} max_ms={max:F2}");

            mean.Should().BeLessThan(150d, "the shared adaptive panel should converge quickly per resize step");
        });
    }
}
