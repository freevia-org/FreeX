using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPolygonClipTransitionPlannerTests
{
    public static TheoryData<SlideShowTransitionPlaybackActionKind> PolygonActions =>
        new()
        {
            SlideShowTransitionPlaybackActionKind.Honeycomb,
            SlideShowTransitionPlaybackActionKind.Glitter,
            SlideShowTransitionPlaybackActionKind.Ripple,
            SlideShowTransitionPlaybackActionKind.Wind,
            SlideShowTransitionPlaybackActionKind.Curtains,
            SlideShowTransitionPlaybackActionKind.Shred,
            SlideShowTransitionPlaybackActionKind.Drape,
            SlideShowTransitionPlaybackActionKind.Fracture,
            SlideShowTransitionPlaybackActionKind.Crush,
            SlideShowTransitionPlaybackActionKind.Prism,
            SlideShowTransitionPlaybackActionKind.Prestige,
            SlideShowTransitionPlaybackActionKind.Warp,
            SlideShowTransitionPlaybackActionKind.Vortex
        };

    [Theory]
    [MemberData(nameof(PolygonActions))]
    public void Build_ProjectsEveryPolygonTransitionThroughOnePlan(
        SlideShowTransitionPlaybackActionKind action)
    {
        var plan = SlideShowPolygonClipTransitionPlanner.Build(
            action,
            new SlideTransition
            {
                Kind = TransitionKind.Fade,
                Direction = TransitionDirection.Right
            });

        plan.ActionKind.Should().Be(action);
        plan.BuildPolygons(960, 540, 0.5).Should().NotBeEmpty();
    }

    [Fact]
    public void FrameSampling_OwnsSharedStoryboardAndTimerProgress()
    {
        SlideShowPolygonClipTransitionPlanner.ResolveTimerStepCount(1).Should().Be(1);
        SlideShowPolygonClipTransitionPlanner.ResolveTimerStepCount(160).Should().Be(10);

        var frameCount = SlideShowPolygonClipTransitionPlanner.StoryboardFrameCount;
        SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(0, frameCount).Should().Be(0);
        SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(frameCount / 2, frameCount)
            .Should().Be(0.5);
        SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(frameCount, frameCount)
            .Should().Be(1);
    }

    [Fact]
    public void Build_RejectsTransitionsWithoutPolygonClipPlayback()
    {
        var act = () => SlideShowPolygonClipTransitionPlanner.Build(
            SlideShowTransitionPlaybackActionKind.Fade,
            new SlideTransition { Kind = TransitionKind.Fade });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
