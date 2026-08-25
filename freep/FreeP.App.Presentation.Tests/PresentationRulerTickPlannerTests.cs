using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationRulerTickPlannerTests
{
    [Fact]
    public void Horizontal_ticks_follow_the_live_slide_transform_and_label_whole_inches()
    {
        var transform = new SlideTransformCore(1.5, 40, 20, 192, 96);

        var ticks = PresentationRulerTickPlanner.BuildHorizontal(transform);

        ticks.Should().HaveCount(9);
        ticks[0].Should().Be(new PresentationRulerTick(40, 12, "0"));
        ticks[4].Should().Be(new PresentationRulerTick(184, 12, "1"));
        ticks[8].Should().Be(new PresentationRulerTick(328, 12, "2"));
    }
}
