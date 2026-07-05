using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPlaybackPlannerTests
{
    [Fact]
    public void PlanTransition_NormalizesRendererActionDurationAndDirection()
    {
        var cut = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Cut,
            DurationMs = 1
        });

        cut.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.ShowInstant);
        cut.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinTransitionDurationMs);

        var push = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Push,
            Direction = TransitionDirection.Right,
            DurationMs = 325
        });

        push.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Push);
        push.DurationMs.Should().Be(325);
        push.IncomingOffsetX.Should().Be(-1);
        push.IncomingOffsetY.Should().Be(0);

        var fallback = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Morph,
            DurationMs = 750
        });

        fallback.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Fade);
        fallback.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.FadeFallback);
    }

    [Fact]
    public void PlanAnimationStep_UsesControllerEntryStartDelays()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DelayMs = 25,
            DurationMs = 100
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.WithPrevious,
            DelayMs = 40,
            DurationMs = 100
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 3,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.AfterPrevious,
            DelayMs = 30,
            DurationMs = 200
        });

        var step = SlideShowController.BuildSteps(slide).Single();
        var plans = SlideShowPlaybackPlanner.PlanAnimationStep(step);

        plans.Select(p => p.DelayMs).Should().Equal(25, 40, 155);
        plans.Select(p => p.EffectKind).Should().Equal(
            SlideShowShapeAnimationEffectKind.Appear,
            SlideShowShapeAnimationEffectKind.Fade,
            SlideShowShapeAnimationEffectKind.FlyIn);
    }

    [Fact]
    public void PlanShapeAnimation_NormalizesEffectIntentAndRevealTiming()
    {
        var exitFade = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 1,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Fade,
                DurationMs = 1
            },
            startDelayMs: -10);

        exitFade.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Fade);
        exitFade.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinShapeAnimationDurationMs);
        exitFade.DelayMs.Should().Be(0);
        exitFade.FromOpacity.Should().Be(1);
        exitFade.ToOpacity.Should().Be(0);
        exitFade.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var flyIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 2,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.FlyIn,
                Direction = AnimationDirection.FromTopRight,
                DurationMs = 120
            },
            startDelayMs: 75);

        flyIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.FlyIn);
        flyIn.OffsetXFactor.Should().Be(1);
        flyIn.OffsetYFactor.Should().Be(-1);
        flyIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalWipe = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 3,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wipe,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalWipe.WipeHorizontal.Should().BeFalse();
    }

    [Fact]
    public void PlanShapeAnimation_MapsAdvancedImportedEffects()
    {
        var split = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 4,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Split,
                Direction = AnimationDirection.Vertical,
                DurationMs = 300
            },
            startDelayMs: 20);

        split.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Split);
        split.WipeHorizontal.Should().BeFalse();
        split.DurationMs.Should().Be(300);
        split.DelayMs.Should().Be(20);
        split.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var randomBars = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 5,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.RandomBars,
                Direction = AnimationDirection.Horizontal
            },
            startDelayMs: 0);

        randomBars.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.RandomBars);
        randomBars.WipeHorizontal.Should().BeTrue();
        randomBars.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var horizontalBlinds = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 6,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Blinds,
                Direction = AnimationDirection.Horizontal,
                DurationMs = 275
            },
            startDelayMs: 5);

        horizontalBlinds.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Blinds);
        horizontalBlinds.BlindsHorizontal.Should().BeTrue();
        horizontalBlinds.BlindsBandCount.Should().Be(SlideShowPlaybackPlanner.BlindsBandCount);
        horizontalBlinds.DurationMs.Should().Be(275);
        horizontalBlinds.DelayMs.Should().Be(5);
        horizontalBlinds.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalBlinds = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 6,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Blinds,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalBlinds.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Blinds);
        verticalBlinds.BlindsHorizontal.Should().BeFalse();
        verticalBlinds.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var boxIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 7,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Box,
                Direction = AnimationDirection.In,
                DurationMs = 260
            },
            startDelayMs: 35);

        boxIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Box);
        boxIn.BoxExpandsFromCenter.Should().BeTrue();
        boxIn.DurationMs.Should().Be(260);
        boxIn.DelayMs.Should().Be(35);
        boxIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var boxOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 8,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Box,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        boxOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Box);
        boxOut.BoxExpandsFromCenter.Should().BeFalse();
        boxOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var growShrink = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 9,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Grow,
                DurationMs = 450
            },
            startDelayMs: 10);

        growShrink.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.GrowShrink);
        growShrink.FromScale.Should().Be(1);
        growShrink.ToScale.Should().Be(1);
        growShrink.PeakScale.Should().BeGreaterThan(1);
        growShrink.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
    }

    [Fact]
    public void PlanShapeAnimation_PreSamplesMotionPathKeyframes()
    {
        var path = new MotionPath();
        path.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        path.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));

        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 9,
                Kind = AnimationKind.Motion,
                Motion = path,
                DurationMs = 250
            },
            startDelayMs: 15);

        plan.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.MotionPath);
        plan.DelayMs.Should().Be(15);
        plan.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
        plan.MotionKeyFrames.Should().HaveCount(SlideShowPlaybackPlanner.MotionPathFrameCount + 1);
        plan.MotionKeyFrames[0].Should().Be(new SlideShowMotionPathKeyFrame(0, 0, 0));
        plan.MotionKeyFrames[^1].OffsetXFactor.Should().BeApproximately(0.5, 0.0001);
        plan.MotionKeyFrames[^1].OffsetYFactor.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void PlanFallbackAnimation_OnlyPlansEmphasisFlash()
    {
        var flash = SlideShowPlaybackPlanner.PlanFallbackAnimation(
            new ShapeAnimation
            {
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Pulse,
                DurationMs = 25
            },
            startDelayMs: -1);

        flash.Should().NotBeNull();
        flash!.DurationMs.Should().Be(SlideShowPlaybackPlanner.MinFallbackAnimationDurationMs);
        flash.DelayMs.Should().Be(0);
        flash.FromOpacity.Should().Be(1);
        flash.FlashOpacity.Should().Be(0.5);

        SlideShowPlaybackPlanner.PlanFallbackAnimation(
            new ShapeAnimation { Kind = AnimationKind.Entrance },
            startDelayMs: 0).Should().BeNull();
    }
}
