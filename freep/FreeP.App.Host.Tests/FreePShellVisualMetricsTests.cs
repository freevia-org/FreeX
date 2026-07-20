using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class FreePShellVisualMetricsTests
{
    [Fact]
    public void Whole_window_geometry_uses_the_shared_visual_baseline()
    {
        FreePShellVisualMetrics.TitleBarHeight.Should().Be(34);
        FreePShellVisualMetrics.RibbonHeight.Should().Be(123);
        FreePShellVisualMetrics.SlidePaneWidth.Should().Be(180);
        FreePShellVisualMetrics.CanvasMargin.Should().Be(40);
        FreePShellVisualMetrics.NotesPaneHeight.Should().Be(60);
    }
}
