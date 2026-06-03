using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonTabSelectionCoordinatorTests
{
    [Fact]
    public void KeyTipTabSelection_SuppressesSelectionEventAndSkipsAlreadyActiveTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectRibbonTabByHeader("Insert", 900);

            var changed = harness.FallbackDiagnostics;
            changed.RequestCount.Should().Be(1);
            changed.PostedCount.Should().Be(1);
            changed.LastMergedWork.Should().Be("NormalizeSurface");
            changed.FirstFrameLayoutUpdateCount.Should().BeGreaterThan(0);

            harness.PumpDispatcher();
            harness.ResetFallbackDiagnostics();
            var contentBefore = harness.VisibleRibbonButtonContentIdentityHashCodes;

            harness.SelectRibbonTabByHeader("Insert", 900);

            harness.FallbackDiagnostics.RequestCount.Should().Be(0);
            harness.VisibleRibbonButtonContentIdentityHashCodes.Should().Equal(contentBefore);
        });
    }
}
