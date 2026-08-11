using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowMaskTimelinePlannerTests
{
    [Fact]
    public void BuildRandomBars_CentralizesStaggerAndAuthoredDuration()
    {
        var playback = BuildPlayback(AnimationKind.Entrance, durationMs: 900, delayMs: 75);
        var bars = SlideShowMaskGeometryPlanner.BuildRandomBars(
            960,
            540,
            SlideShowPlaybackPlanner.RandomBarsBandCount,
            horizontal: true);

        var timeline = SlideShowMaskTimelinePlanner.BuildRandomBars(playback, bars);

        timeline.DelayMs.Should().Be(75);
        timeline.DurationMs.Should().Be(900);
        timeline.StaggerMs.Should().Be(100);
        timeline.Bars.Select(bar => bar.Order).Should().Equal(6, 0, 4, 1, 7, 3, 5, 2);
        timeline.Bars[0].Should().Be(new SlideShowMaskElementTimeline(6, 600, 300));
        timeline.Bars[1].Should().Be(new SlideShowMaskElementTimeline(0, 0, 900));
        timeline.Bars[4].Should().Be(new SlideShowMaskElementTimeline(7, 700, 200));
    }

    [Fact]
    public void BuildRandomBars_ResolvesRendererOpacityDriftToCanonicalRamp()
    {
        var entrance = SlideShowMaskTimelinePlanner.BuildRandomBars(
            BuildPlayback(AnimationKind.Entrance, durationMs: 800, delayMs: 0),
            SlideShowMaskGeometryPlanner.BuildRandomBars(960, 540, 8, horizontal: true));
        var exit = SlideShowMaskTimelinePlanner.BuildRandomBars(
            BuildPlayback(AnimationKind.Exit, durationMs: 800, delayMs: 0),
            SlideShowMaskGeometryPlanner.BuildRandomBars(960, 540, 8, horizontal: true));

        entrance.OpacityTrack.KeyFrames.Should().Equal(
            new SlideShowAnimationScalarKeyFrame(0, 0, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(0.35, 0.2, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(0.7, 0.55, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(1, 1, SlideShowAnimationScalarInterpolationKind.Linear));
        exit.OpacityTrack.KeyFrames.Should().Equal(
            new SlideShowAnimationScalarKeyFrame(1, 0, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(0.7, 0.2, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(0.35, 0.55, SlideShowAnimationScalarInterpolationKind.Discrete),
            new SlideShowAnimationScalarKeyFrame(0, 1, SlideShowAnimationScalarInterpolationKind.Linear));

        SlideShowMaskTimelinePlanner.SampleOpacity(entrance, 0.1).Should().Be(0);
        SlideShowMaskTimelinePlanner.SampleOpacity(entrance, 0.2).Should().Be(0.35);
        SlideShowMaskTimelinePlanner.SampleOpacity(entrance, 0.55).Should().Be(0.7);
        SlideShowMaskTimelinePlanner.SampleOpacity(entrance, 0.775).Should().BeApproximately(0.85, 0.0001);
        SlideShowMaskTimelinePlanner.SampleOpacity(exit, 1).Should().Be(0);
    }

    [Fact]
    public void BuildCheckerboard_CentralizesTwoPhaseMaskSchedule()
    {
        var timeline = SlideShowMaskTimelinePlanner.BuildCheckerboard(
            BuildPlayback(AnimationKind.Entrance, durationMs: 900, delayMs: 75));

        timeline.Should().Be(new SlideShowCheckerboardMaskTimelinePlan(75, 900, 300, 600));
        timeline.ResolveCell(isSecondPhase: false)
            .Should().Be(new SlideShowMaskElementTimeline(0, 0, 600));
        timeline.ResolveCell(isSecondPhase: true)
            .Should().Be(new SlideShowMaskElementTimeline(1, 300, 600));
    }

    [Fact]
    public void RendererPlansOwnMaskGeometryDirectionAndElementTiming()
    {
        var randomBars = SlideShowMaskTimelinePlanner.BuildRandomBarsRendererPlan(
            BuildPlayback(AnimationKind.Exit, durationMs: 900, delayMs: 75),
            width: 800,
            height: 400);

        randomBars.DelayMs.Should().Be(75);
        randomBars.DurationMs.Should().Be(900);
        randomBars.InitialOpacity.Should().Be(1);
        randomBars.Elements.Should().HaveCount(SlideShowPlaybackPlanner.RandomBarsBandCount);
        randomBars.Elements[0].From.Height.Should().BeGreaterThan(0);
        randomBars.Elements[0].To.Height.Should().Be(0);
        randomBars.Elements[0].StartOffsetMs.Should().Be(600);
        randomBars.Elements[0].DurationMs.Should().Be(300);
        SlideShowMaskTimelinePlanner.SampleOpacity(randomBars, 1).Should().Be(0);

        var blindsPlayback = BuildPlayback(
            AnimationKind.Entrance,
            durationMs: 640,
            delayMs: 40,
            preset: AnimationPreset.Blinds) with
        {
            BlindsBandCount = 4,
            BlindsHorizontal = false
        };
        var blinds = SlideShowMaskTimelinePlanner.BuildBlindsRendererPlan(
            blindsPlayback,
            width: 800,
            height: 400);

        blinds.DelayMs.Should().Be(40);
        blinds.DurationMs.Should().Be(640);
        blinds.Elements.Should().HaveCount(4);
        blinds.Elements.Should().OnlyContain(element =>
            element.StartOffsetMs == 0 && element.DurationMs == 640);
        blinds.Elements[0].From.Width.Should().Be(0);
        blinds.Elements[0].To.Width.Should().Be(200);

        var checkerboardPlayback = BuildPlayback(
            AnimationKind.Exit,
            durationMs: 900,
            delayMs: 30,
            preset: AnimationPreset.Checkerboard) with
        {
            CheckerboardRowCount = 2,
            CheckerboardColumnCount = 3,
            CheckerboardHorizontal = true
        };
        var checkerboard = SlideShowMaskTimelinePlanner.BuildCheckerboardRendererPlan(
            checkerboardPlayback,
            width: 900,
            height: 600);

        checkerboard.DelayMs.Should().Be(30);
        checkerboard.DurationMs.Should().Be(900);
        checkerboard.Elements.Should().HaveCount(6);
        checkerboard.Elements.Select(element => element.StartOffsetMs)
            .Should().Equal(0, 300, 0, 300, 0, 300);
        checkerboard.Elements.Should().OnlyContain(element => element.DurationMs == 600);
        checkerboard.Elements[0].From.Width.Should().BeGreaterThan(0);
        checkerboard.Elements[0].To.Width.Should().Be(0);
    }

    private static SlideShowShapeAnimationPlaybackPlan BuildPlayback(
        AnimationKind kind,
        int durationMs,
        int delayMs,
        AnimationPreset preset = AnimationPreset.RandomBars) =>
        SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 7,
                Kind = kind,
                Preset = preset,
                Direction = AnimationDirection.Horizontal,
                DurationMs = durationMs
            },
            delayMs);
}
