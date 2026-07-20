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
        push.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Push);
        push.DurationMs.Should().Be(325);
        push.IncomingOffsetX.Should().Be(-1);
        push.IncomingOffsetY.Should().Be(0);

        var cover = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Cover,
            Direction = TransitionDirection.Right,
            DurationMs = 325
        });

        cover.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Cover);
        cover.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Cover);

        var fallback = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Morph,
            DurationMs = 750
        });

        fallback.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Fade);
        fallback.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.FadeFallback);
    }

    [Fact]
    public void PlanTransition_DissolveUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Dissolve,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Dissolve);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Dissolve);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_FlashUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Flash,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Flash);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Flash);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_FlyUsesSerializedPushPlayback()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Fly,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Push);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Push);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_PanUsesDedicatedActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Pan,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Pan);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Pan);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        SlideShowPlaybackPlanner.PanStartScale.Should().BeApproximately(1.12, 0.0001);
    }

    [Fact]
    public void PlanTransition_GalleryUsesTwoSurfaceActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Gallery,
            Direction = TransitionDirection.Left,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Gallery);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Gallery);
        plan.IncomingOffsetX.Should().Be(1);
        plan.IncomingOffsetY.Should().Be(0);
        SlideShowPlaybackPlanner.GalleryStartScale.Should().BeApproximately(0.78, 0.0001);
        SlideShowPlaybackPlanner.GalleryOutgoingEndScale.Should().BeApproximately(0.88, 0.0001);
        SlideShowPlaybackPlanner.GalleryTravelFactor.Should().BeApproximately(0.55, 0.0001);
    }

    [Fact]
    public void PlanTransition_ConveyorUsesBeltActionAndDirection()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Conveyor,
            Direction = TransitionDirection.Down,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Conveyor);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Conveyor);
        plan.IncomingOffsetX.Should().Be(0);
        plan.IncomingOffsetY.Should().Be(-1);
        SlideShowPlaybackPlanner.ConveyorStartScale.Should().BeApproximately(0.90, 0.0001);
        SlideShowPlaybackPlanner.ConveyorOutgoingEndScale.Should().BeApproximately(0.90, 0.0001);
        SlideShowPlaybackPlanner.ConveyorTravelFactor.Should().BeApproximately(1.0, 0.0001);
        SlideShowPlaybackPlanner.ConveyorCrossAxisFactor.Should().BeApproximately(0.08, 0.0001);
        SlideShowPlaybackPlanner.ConveyorTiltDegrees.Should().BeApproximately(3.0, 0.0001);
    }

    [Theory]
    [InlineData(TransitionDirection.In, true)]
    [InlineData(TransitionDirection.Out, false)]
    [InlineData(null, true)]
    public void PlanTransition_BoxUsesDedicatedActionAndDirection(
        TransitionDirection? direction,
        bool expandsFromCenter)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Box,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Box);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Box);
        plan.BoxExpandsFromCenter.Should().Be(expandsFromCenter);
    }

    [Theory]
    [InlineData(TransitionDirection.Right, -1, 0)]
    [InlineData(TransitionDirection.Left, 1, 0)]
    [InlineData(TransitionDirection.Down, 0, -1)]
    [InlineData(TransitionDirection.Up, 0, 1)]
    public void PlanTransition_RevealUsesDirectionalClipAction(
        TransitionDirection direction,
        double expectedOffsetX,
        double expectedOffsetY)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Reveal,
            Direction = direction
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Reveal);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Reveal);
        plan.IncomingOffsetX.Should().Be(expectedOffsetX);
        plan.IncomingOffsetY.Should().Be(expectedOffsetY);
    }

    [Fact]
    public void PlanTransition_WipeUsesDirectionalRevealClipAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Wipe,
            Direction = TransitionDirection.Left,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Reveal);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Reveal);
        plan.IncomingOffsetX.Should().Be(1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_UncoverUsesOutgoingClipAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Uncover,
            Direction = TransitionDirection.Right,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Uncover);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Uncover);
        plan.IncomingOffsetX.Should().Be(-1);
        plan.IncomingOffsetY.Should().Be(0);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_DoorsUsesVerticalCenterOpening()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Doors,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Split);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Split);
        plan.SplitHorizontal.Should().BeTrue();
        plan.SplitOut.Should().BeTrue();
    }

    [Fact]
    public void PlanTransition_SplitUsesDedicatedActionAndPreservesGeometry()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Split,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Split);
        plan.DurationMs.Should().Be(420);
        plan.SplitHorizontal.Should().BeFalse();
        plan.SplitOut.Should().BeTrue();
    }

    [Fact]
    public void PlanTransition_BlindsUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Blinds,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Blinds);
        plan.BlindsHorizontal.Should().BeFalse();
    }

    [Theory]
    [InlineData(TransitionDirection.Horizontal, true)]
    [InlineData(TransitionDirection.Vertical, false)]
    public void PlanTransition_CombUsesSharedBlindsActionAndAxis(
        TransitionDirection direction,
        bool expectedHorizontal)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Comb,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Blinds);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Blinds);
        plan.BlindsHorizontal.Should().Be(expectedHorizontal);
        plan.DurationMs.Should().Be(420);
    }

    [Fact]
    public void PlanTransition_RandomBarsUsesDedicatedAction()
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.RandomBar,
            Direction = TransitionDirection.Vertical,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.RandomBars);
        plan.RandomBarsHorizontal.Should().BeFalse();
    }

    [Theory]
    [InlineData(TransitionKind.Wheel, false)]
    [InlineData(TransitionKind.WheelReverse, true)]
    public void PlanTransition_WheelUsesDedicatedActionAndPreservesSpokes(
        TransitionKind kind,
        bool expectedReverse)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = kind,
            WheelSpokeCount = 8,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Wheel);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Wheel);
        plan.WheelSpokeCount.Should().Be(8);
        plan.WheelReverse.Should().Be(expectedReverse);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(TransitionDirection.In, true)]
    [InlineData(TransitionDirection.Out, false)]
    public void PlanTransition_ZoomUsesDedicatedActionAndDirection(
        TransitionDirection? direction,
        bool expectedZoomIn)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Zoom,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Zoom);
        plan.SourceKind.Should().Be(SlideShowTransitionPlaybackKind.Zoom);
        plan.ZoomIn.Should().Be(expectedZoomIn);
        plan.DurationMs.Should().Be(420);
    }

    [Theory]
    [InlineData(TransitionDirection.LeftDown, true)]
    [InlineData(TransitionDirection.RightUp, true)]
    [InlineData(TransitionDirection.LeftUp, false)]
    [InlineData(TransitionDirection.RightDown, false)]
    public void PlanTransition_StripsUsesDedicatedActionAndDirectionSlope(
        TransitionDirection direction,
        bool expectedSlopeDown)
    {
        var plan = SlideShowPlaybackPlanner.PlanTransition(new SlideTransition
        {
            Kind = TransitionKind.Strips,
            Direction = direction,
            DurationMs = 420
        });

        plan.ActionKind.Should().Be(SlideShowTransitionPlaybackActionKind.Strips);
        plan.DurationMs.Should().Be(420);
        plan.StripsSlopeDown.Should().Be(expectedSlopeDown);
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

        var horizontalCheckerboard = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 9,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Checkerboard,
                Direction = AnimationDirection.Horizontal,
                DurationMs = 325
            },
            startDelayMs: 15);

        horizontalCheckerboard.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Checkerboard);
        horizontalCheckerboard.CheckerboardHorizontal.Should().BeTrue();
        horizontalCheckerboard.CheckerboardRowCount.Should().Be(SlideShowPlaybackPlanner.CheckerboardRowCount);
        horizontalCheckerboard.CheckerboardColumnCount.Should().Be(SlideShowPlaybackPlanner.CheckerboardColumnCount);
        horizontalCheckerboard.DurationMs.Should().Be(325);
        horizontalCheckerboard.DelayMs.Should().Be(15);
        horizontalCheckerboard.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var verticalCheckerboard = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 10,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Checkerboard,
                Direction = AnimationDirection.Vertical
            },
            startDelayMs: 0);

        verticalCheckerboard.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Checkerboard);
        verticalCheckerboard.CheckerboardHorizontal.Should().BeFalse();
        verticalCheckerboard.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var circleIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 11,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Circle,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        circleIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Circle);
        circleIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Circle);
        circleIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        circleIn.DurationMs.Should().Be(285);
        circleIn.DelayMs.Should().Be(25);
        circleIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var circleOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 12,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Circle,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        circleOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Circle);
        circleOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Circle);
        circleOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        circleOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var diamondIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 13,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Diamond,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        diamondIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Diamond);
        diamondIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Diamond);
        diamondIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        diamondIn.DurationMs.Should().Be(285);
        diamondIn.DelayMs.Should().Be(25);
        diamondIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var diamondOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 14,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Diamond,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        diamondOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Diamond);
        diamondOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Diamond);
        diamondOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        diamondOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var plusIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 15,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Plus,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        plusIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Plus);
        plusIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Plus);
        plusIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        plusIn.DurationMs.Should().Be(285);
        plusIn.DelayMs.Should().Be(25);
        plusIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var plusOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 16,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Plus,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        plusOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Plus);
        plusOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Plus);
        plusOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        plusOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var stripsLeftDown = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 17,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Strips,
                Direction = AnimationDirection.LeftDown,
                DurationMs = 315
            },
            startDelayMs: 30);

        stripsLeftDown.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Strips);
        stripsLeftDown.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Strips);
        stripsLeftDown.GeometricMaskExpandsFromCenter.Should().BeTrue();
        stripsLeftDown.GeometricMaskStripCount.Should().Be(SlideShowPlaybackPlanner.StripsBandCount);
        stripsLeftDown.GeometricMaskStripsSlopeDown.Should().BeTrue();
        stripsLeftDown.DurationMs.Should().Be(315);
        stripsLeftDown.DelayMs.Should().Be(30);
        stripsLeftDown.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var stripsRightDownExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 18,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Strips,
                Direction = AnimationDirection.RightDown
            },
            startDelayMs: 0);

        stripsRightDownExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Strips);
        stripsRightDownExit.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Strips);
        stripsRightDownExit.GeometricMaskExpandsFromCenter.Should().BeFalse();
        stripsRightDownExit.GeometricMaskStripsSlopeDown.Should().BeFalse();
        stripsRightDownExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var wedgeIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 19,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wedge,
                Direction = AnimationDirection.In,
                DurationMs = 285
            },
            startDelayMs: 25);

        wedgeIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wedge);
        wedgeIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wedge);
        wedgeIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        wedgeIn.DurationMs.Should().Be(285);
        wedgeIn.DelayMs.Should().Be(25);
        wedgeIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var wedgeOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 20,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Wedge,
                Direction = AnimationDirection.Out
            },
            startDelayMs: 0);

        wedgeOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wedge);
        wedgeOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wedge);
        wedgeOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        wedgeOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var wheelIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 21,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.In,
                WheelSpokeCount = 8,
                DurationMs = 285
            },
            startDelayMs: 25);

        wheelIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wheel);
        wheelIn.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wheel);
        wheelIn.GeometricMaskExpandsFromCenter.Should().BeTrue();
        wheelIn.GeometricMaskSpokeCount.Should().Be(8);
        wheelIn.DurationMs.Should().Be(285);
        wheelIn.DelayMs.Should().Be(25);
        wheelIn.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var wheelOut = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 22,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.Out,
                WheelSpokeCount = 0
            },
            startDelayMs: 0);

        wheelOut.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Wheel);
        wheelOut.GeometricMaskKind.Should().Be(SlideShowGeometricMaskKind.Wheel);
        wheelOut.GeometricMaskExpandsFromCenter.Should().BeFalse();
        wheelOut.GeometricMaskSpokeCount.Should().Be(SlideShowPlaybackPlanner.WheelSpokeCount);
        wheelOut.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var dissolve = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 23,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Dissolve,
                DurationMs = 240
            },
            startDelayMs: 5);

        dissolve.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Dissolve);
        dissolve.DurationMs.Should().Be(240);
        dissolve.DelayMs.Should().Be(5);
        dissolve.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var flashExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 24,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Flash
            },
            startDelayMs: 0);

        flashExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Flash);
        flashExit.FromOpacity.Should().Be(1);
        flashExit.ToOpacity.Should().Be(0);
        flashExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var spiral = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 25,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Spiral,
                DurationMs = 300
            },
            startDelayMs: 15);

        spiral.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Spiral);
        spiral.RotationDegrees.Should().Be(360);
        spiral.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var swivelExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 26,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Swivel
            },
            startDelayMs: 0);

        swivelExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swivel);
        swivelExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var bounce = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 27,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Bounce,
                Direction = AnimationDirection.FromBottom
            },
            startDelayMs: 0);

        bounce.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Bounce);
        bounce.OffsetXFactor.Should().Be(0);
        bounce.OffsetYFactor.Should().Be(1);
        bounce.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var floatIn = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 28,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Float,
                Direction = AnimationDirection.FromTop
            },
            startDelayMs: 0);

        floatIn.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Float);
        floatIn.OffsetYFactor.Should().Be(-1);

        var swoop = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 29,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Swoop,
                Direction = AnimationDirection.FromBottomRight
            },
            startDelayMs: 0);

        swoop.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swoop);
        swoop.OffsetXFactor.Should().Be(1);
        swoop.OffsetYFactor.Should().Be(1);

        var boomerangExit = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 30,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Boomerang,
                Direction = AnimationDirection.FromLeft
            },
            startDelayMs: 0);

        boomerangExit.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Boomerang);
        boomerangExit.OffsetXFactor.Should().Be(-1);
        boomerangExit.FromOpacity.Should().Be(1);
        boomerangExit.ToOpacity.Should().Be(0);
        boomerangExit.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var peekFromLeft = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 31,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Peek,
                Direction = AnimationDirection.FromLeft,
                DurationMs = 275
            },
            startDelayMs: 35);

        peekFromLeft.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Peek);
        peekFromLeft.OffsetXFactor.Should().Be(-1);
        peekFromLeft.OffsetYFactor.Should().Be(0);
        peekFromLeft.DurationMs.Should().Be(275);
        peekFromLeft.DelayMs.Should().Be(35);
        peekFromLeft.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var peekExitFromBottom = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 32,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Peek,
                Direction = AnimationDirection.FromBottom
            },
            startDelayMs: 0);

        peekExitFromBottom.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Peek);
        peekExitFromBottom.OffsetXFactor.Should().Be(0);
        peekExitFromBottom.OffsetYFactor.Should().Be(1);
        peekExitFromBottom.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var crawlFromRight = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 33,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Crawl,
                Direction = AnimationDirection.FromRight,
                DurationMs = 310
            },
            startDelayMs: 45);

        crawlFromRight.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Crawl);
        crawlFromRight.OffsetXFactor.Should().Be(1);
        crawlFromRight.OffsetYFactor.Should().Be(0);
        crawlFromRight.DurationMs.Should().Be(310);
        crawlFromRight.DelayMs.Should().Be(45);
        crawlFromRight.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);

        var crawlExitFromTop = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 34,
                Kind = AnimationKind.Exit,
                Preset = AnimationPreset.Crawl,
                Direction = AnimationDirection.FromTop
            },
            startDelayMs: 0);

        crawlExitFromTop.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Crawl);
        crawlExitFromTop.OffsetXFactor.Should().Be(0);
        crawlExitFromTop.OffsetYFactor.Should().Be(-1);
        crawlExitFromTop.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);

        var growShrink = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 35,
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
    public void PlanShapeAnimation_PreservesImportedEmphasisPresetFamilies()
    {
        var expected = new Dictionary<AnimationPreset, SlideShowShapeAnimationEffectKind>
        {
            [AnimationPreset.Teeter] = SlideShowShapeAnimationEffectKind.Teeter,
            [AnimationPreset.Blink] = SlideShowShapeAnimationEffectKind.Blink,
            [AnimationPreset.ColorPulse] = SlideShowShapeAnimationEffectKind.ColorPulse,
            [AnimationPreset.ChangeColor] = SlideShowShapeAnimationEffectKind.ChangeColor,
            [AnimationPreset.GrowWithColor] = SlideShowShapeAnimationEffectKind.GrowWithColor,
            [AnimationPreset.Wave] = SlideShowShapeAnimationEffectKind.Wave,
            [AnimationPreset.Shimmer] = SlideShowShapeAnimationEffectKind.Shimmer,
            [AnimationPreset.Bold] = SlideShowShapeAnimationEffectKind.Bold,
            [AnimationPreset.Underline] = SlideShowShapeAnimationEffectKind.Underline
        };

        foreach (var (preset, effectKind) in expected)
        {
            var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
                new ShapeAnimation
                {
                    ShapeId = 70,
                    Kind = AnimationKind.Emphasis,
                    Preset = preset,
                    DurationMs = 600
                },
                startDelayMs: 25);

            plan.EffectKind.Should().Be(effectKind);
            plan.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.AtStart);
        }
    }

    [Fact]
    public void PlanFrame_ProjectsImportedEmphasisTracks()
    {
        var blinkPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 71,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Blink,
                DurationMs = 400
            },
            startDelayMs: 0);
        var blinkFrame = SlideShowPlaybackFramePlanner.PlanFrame(blinkPlan, 100, 960, 540);
        blinkFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Opacity);
        blinkFrame.Opacity.Should().BeApproximately(0.15, 0.0001);

        var teeterPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 72,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Teeter,
                DurationMs = 400
            },
            startDelayMs: 0);
        var teeterFrame = SlideShowPlaybackFramePlanner.PlanFrame(teeterPlan, 150, 960, 540);
        teeterFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Rotate);
        teeterFrame.RotationDegrees.Should().BeApproximately(-10, 0.0001);

        var swivelPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 74,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Swivel,
                DurationMs = 400
            },
            startDelayMs: 0);
        var swivelEdgeFrame = SlideShowPlaybackFramePlanner.PlanFrame(swivelPlan, 100, 960, 540);
        swivelEdgeFrame.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Swivel);
        swivelEdgeFrame.RotationDegrees.Should().BeApproximately(90, 0.0001);
        swivelEdgeFrame.HorizontalScale.Should().BeApproximately(0.04, 0.0001);
        var swivelFaceFrame = SlideShowPlaybackFramePlanner.PlanFrame(swivelPlan, 200, 960, 540);
        swivelFaceFrame.HorizontalScale.Should().BeApproximately(1, 0.0001);

        var colorPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 73,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.GrowWithColor,
                DurationMs = 400
            },
            startDelayMs: 0);
        var colorFrame = SlideShowPlaybackFramePlanner.PlanFrame(colorPlan, 100, 960, 540);
        colorFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Emphasis);
        colorFrame.Scale.Should().BeGreaterThan(1);
        colorFrame.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.GrowWithColor);
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

    [Fact]
    public void PlanFrame_ProjectsTranslateAndMotionPathEvidenceInSlideCoordinates()
    {
        var flyInPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 41,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.FlyIn,
                Direction = AnimationDirection.FromRight,
                DurationMs = 400
            },
            startDelayMs: 0);

        var flyInFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            flyInPlan,
            elapsedMs: 200,
            slideWidthDip: 960,
            slideHeightDip: 540);

        flyInFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Translate);
        flyInFrame.Progress.Should().Be(0.5);
        flyInFrame.TranslateXFactor.Should().Be(0.5);
        flyInFrame.TranslateXDip.Should().Be(480);
        flyInFrame.TranslateYDip.Should().Be(0);
        flyInFrame.EvidenceSummary.Should().Contain("FlyIn Translate");

        var path = new MotionPath();
        path.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        path.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));
        var motionPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 42,
                Kind = AnimationKind.Motion,
                Motion = path,
                DurationMs = 1000
            },
            startDelayMs: 0);

        var motionFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            motionPlan,
            elapsedMs: 500,
            slideWidthDip: 960,
            slideHeightDip: 540);

        motionFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.MotionPath);
        motionFrame.TranslateXFactor.Should().BeApproximately(0.25, 0.0001);
        motionFrame.TranslateYFactor.Should().BeApproximately(0.125, 0.0001);
        motionFrame.TranslateXDip.Should().BeApproximately(240, 0.0001);
        motionFrame.TranslateYDip.Should().BeApproximately(67.5, 0.0001);
    }

    [Fact]
    public void PlanFrame_ProjectsAdvancedClipAndScaleVisualEvidence()
    {
        var wheelPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 51,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Wheel,
                Direction = AnimationDirection.In,
                WheelSpokeCount = 8,
                DurationMs = 300
            },
            startDelayMs: 0);

        var wheelFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            wheelPlan,
            elapsedMs: 150,
            slideWidthDip: 960,
            slideHeightDip: 540);

        wheelFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Clip);
        wheelFrame.ClipKind.Should().Be(SlideShowAnimationClipKind.Wheel);
        wheelFrame.ClipProgress.Should().Be(0.5);
        wheelFrame.ClipSpokeCount.Should().Be(8);
        wheelFrame.EvidenceSummary.Should().Contain("clip Wheel 0.5");

        var growPlan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 52,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Grow,
                DurationMs = 400
            },
            startDelayMs: 0);

        var growFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            growPlan,
            elapsedMs: 200,
            slideWidthDip: 960,
            slideHeightDip: 540);

        growFrame.TrackKind.Should().Be(SlideShowAnimationVisualTrackKind.Scale);
        growFrame.Scale.Should().Be(growPlan.PeakScale);
        growFrame.Opacity.Should().Be(1);
        growFrame.EvidenceSummary.Should().Contain("GrowShrink Scale");
    }

    [Fact]
    public void PlanAnimationStepFrames_UsesControllerDelaysForSharedHostEvidence()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 61,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Appear,
                    DurationMs = 100
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 62,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Fade,
                    DurationMs = 200
                },
                StartDelayMs: 150)
        ]);

        var frames = SlideShowPlaybackFramePlanner.PlanAnimationStepFrames(
            step,
            elapsedMs: 100,
            slideWidthDip: 960,
            slideHeightDip: 540);

        frames.Should().HaveCount(2);
        frames[0].ShapeId.Should().Be(61);
        frames[0].IsComplete.Should().BeTrue();
        frames[1].ShapeId.Should().Be(62);
        frames[1].IsBeforeStart.Should().BeTrue();
        frames[1].Progress.Should().Be(0);
        frames[1].Opacity.Should().Be(0);
    }

    [Fact]
    public void PlanAnimationStepCheckpoints_ProjectsStepLevelPlaybackEvidence()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 71,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Fade,
                    DurationMs = 200
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 72,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.FlyIn,
                    Direction = AnimationDirection.FromBottom,
                    DurationMs = 300
                },
                StartDelayMs: 400)
        ]);

        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(
            step,
            slideWidthDip: 960,
            slideHeightDip: 540);

        checkpoints.Select(checkpoint => checkpoint.Checkpoint)
            .Should()
            .Equal("start", "midpoint", "complete");
        checkpoints.Select(checkpoint => checkpoint.ElapsedMs)
            .Should()
            .Equal(0, 350, 700);
        checkpoints.Should().OnlyContain(checkpoint => checkpoint.Frames.Count == 2);

        checkpoints[0].Frames[0].IsBeforeStart.Should().BeFalse();
        checkpoints[0].Frames[1].IsBeforeStart.Should().BeTrue();
        checkpoints[1].Frames[0].IsComplete.Should().BeTrue();
        checkpoints[1].Frames[1].IsBeforeStart.Should().BeTrue();
        checkpoints[2].Frames.Should().OnlyContain(frame => frame.IsComplete);
        checkpoints[2].Frames[1].TranslateYFactor.Should().Be(0);
        checkpoints[2].EvidenceSummary.Should().Be("complete at 700ms: 2 frame(s); 0 active; 2 complete");
    }

    [Fact]
    public void BuildAnimationStepPlaybackReadinessPlan_ProjectsSharedNoComHostRows()
    {
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 81,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Wheel,
                    Direction = AnimationDirection.In,
                    WheelSpokeCount = 6,
                    DurationMs = 300
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 82,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.FlyIn,
                    Direction = AnimationDirection.FromRight,
                    DurationMs = 250
                },
                StartDelayMs: 175)
        ]);

        var readiness = SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(
            step,
            slideIndex: 2,
            stepIndex: 4,
            slideWidthDip: 960,
            slideHeightDip: 540,
            scenarioId: "Deck A/Playback");

        readiness.ScenarioId.Should().Be("deck-a-playback");
        readiness.SlideIndex.Should().Be(2);
        readiness.StepIndex.Should().Be(4);
        readiness.AnimationEntryCount.Should().Be(2);
        readiness.CheckpointCount.Should().Be(3);
        readiness.DelayedEntryCount.Should().Be(1);
        readiness.TrackKinds.Should().Equal(
            SlideShowAnimationVisualTrackKind.Clip,
            SlideShowAnimationVisualTrackKind.Translate);
        readiness.ClipKinds.Should().Equal(SlideShowAnimationClipKind.Wheel);
        readiness.HasSharedHostParity.Should().BeTrue();
        readiness.HostRows.Select(row => row.Host)
            .Should()
            .Equal(SlideShowPlaybackReadinessHost.Wpf, SlideShowPlaybackReadinessHost.Avalonia);
        readiness.HostRows.Should().OnlyContain(row => row.RequiresPowerPointCom == false);
        readiness.HostRows.Should().OnlyContain(row => row.EvidenceId.StartsWith(
            "deck-a-playback-slide-3-step-5-",
            StringComparison.Ordinal));
        readiness.EvidenceLines.Should().Contain("Shared host rows: WPF/Avalonia; PowerPoint COM required: false");
    }
}
