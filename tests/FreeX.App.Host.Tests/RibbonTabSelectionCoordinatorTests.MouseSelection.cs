using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonTabSelectionCoordinatorTests
{
    [Fact]
    public void MouseTabSelection_NormalizesImmediatelyAndQueuesSingleFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectRibbonTabByMouse("Data", 900);

            var queued = harness.FallbackDiagnostics;
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.ExecutedCount.Should().Be(0);
            queued.LastMergedWork.Should().Be("NormalizeSurface");
            queued.IsPending.Should().BeTrue();
            queued.FirstFrameLayoutUpdateCount.Should().BeGreaterThan(
                0,
                "tab switches should settle the normalized ribbon before the queued render fallback runs");
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(1);

            harness.PumpDispatcher();
            harness.FallbackDiagnostics.ExecutedCount.Should().Be(1);
        });
    }

    [Fact]
    public void MainRibbonTabs_FitImmediatelyAcrossCommonExcelWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();
            var tabs = new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" };

            foreach (var width in new[] { 1100d, 900d, 750d })
            foreach (var tab in tabs)
            {
                harness.SelectRibbonTabByMouse(tab, width);

                harness.ActiveRibbonVisibleHorizontalScrollBars
                    .Should()
                    .BeEmpty($"{tab} should not show a ribbon scrollbar on the immediate {width:0}px frame");
                harness.ActiveRibbonPanelOverflow
                    .Should()
                    .BeLessThanOrEqualTo(1, $"{tab} should fit the ribbon viewport on the immediate {width:0}px frame");

                harness.PumpDispatcher();

                harness.ActiveRibbonVisibleHorizontalScrollBars
                    .Should()
                    .BeEmpty($"{tab} should not show a ribbon scrollbar after the first render pass at {width:0}px");
                harness.ActiveRibbonPanelOverflow
                    .Should()
                    .BeLessThanOrEqualTo(1, $"{tab} should fit the ribbon viewport after the first render pass at {width:0}px");
            }
        });
    }
}
