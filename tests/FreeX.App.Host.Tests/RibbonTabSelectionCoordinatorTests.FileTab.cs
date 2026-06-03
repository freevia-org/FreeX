using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonTabSelectionCoordinatorTests
{
    [Fact]
    public void FileTabBounce_ReturnsHomeAndQueuesSingleFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectFileTab();

            var queued = harness.FallbackDiagnostics;
            harness.SelectedTabHeader.Should().Be("Home");
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("NormalizeSurface");
            queued.FirstFrameLayoutUpdateCount.Should().BeGreaterThan(0);
        });
    }
}
