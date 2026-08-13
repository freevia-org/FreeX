using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowAnimationEffectFramePlannerTests
{
    [Theory]
    [InlineData(SlideShowShapeAnimationEffectKind.Float, 0.72, -0.5, -0.06)]
    [InlineData(SlideShowShapeAnimationEffectKind.Swoop, 0.55, -0.5, -0.14)]
    [InlineData(SlideShowShapeAnimationEffectKind.Boomerang, 0.78, 0.08, 0.0)]
    public void Build_NormalizesEntranceTrajectories(
        SlideShowShapeAnimationEffectKind effect,
        double middleProgress,
        double middleX,
        double middleY)
    {
        var plan = SlideShowAnimationEffectFramePlanner.Build(
            effect,
            AnimationKind.Entrance,
            offsetXFactor: -1,
            offsetYFactor: 0);

        plan.Start.NormalizedX.Should().Be(-1);
        plan.Start.NormalizedY.Should().Be(0);
        plan.Frames[1].Progress.Should().Be(middleProgress);
        plan.Frames[1].NormalizedX.Should().BeApproximately(middleX, 1e-9);
        plan.Frames[1].NormalizedY.Should().BeApproximately(middleY, 1e-9);
        plan.End.NormalizedX.Should().Be(0);
        plan.End.NormalizedY.Should().Be(0);
    }

    [Fact]
    public void Build_NormalizesExitBounceFrames()
    {
        var plan = SlideShowAnimationEffectFramePlanner.Build(
            SlideShowShapeAnimationEffectKind.Bounce,
            AnimationKind.Exit,
            offsetXFactor: 0,
            offsetYFactor: 1);

        plan.Frames.Select(frame => frame.Progress).Should().Equal(0, 0.55, 0.72, 0.86, 1);
        plan.Frames.Select(frame => frame.NormalizedY).Should().Equal(0, 1, 1.08, 0.96, 1);
        plan.End.StoryboardInterpolation.Should().Be(SlideShowAnimationEffectFrameInterpolation.Linear);
    }

    [Fact]
    public void SampleSmooth_UsesSegmentLocalSmoothStepAndClamps()
    {
        var plan = SlideShowAnimationEffectFramePlanner.Build(
            SlideShowShapeAnimationEffectKind.Float,
            AnimationKind.Entrance,
            offsetXFactor: -1,
            offsetYFactor: 0);

        SlideShowAnimationEffectFramePlanner.SampleSmooth(plan, -1).Should().Be((-1, 0));
        SlideShowAnimationEffectFramePlanner.SampleSmooth(plan, 0.36).NormalizedX.Should().BeApproximately(-0.75, 1e-9);
        SlideShowAnimationEffectFramePlanner.SampleSmooth(plan, 2).Should().Be((0, 0));
    }

    [Fact]
    public void Build_RejectsEffectsWithoutSharedTrajectories()
    {
        var act = () => SlideShowAnimationEffectFramePlanner.Build(
            SlideShowShapeAnimationEffectKind.Fade,
            AnimationKind.Entrance,
            0,
            0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
