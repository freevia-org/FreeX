using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowAnimationRendererSessionTests
{
    [Fact]
    public void OverlayPlanOwnsInitialVisibilityAndBaseSuppression()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.AddRange([
            new SlideShape { Id = 1 },
            new SlideShape { Id = 2 },
            new SlideShape { Id = 3 },
            new SlideShape
            {
                Id = 4,
                Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4))
            }
        ]);
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            TriggerShapeId = 99
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 3,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 4,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.ChangeFillColor
        });
        presentation.Slides.Add(slide);

        var plan = new SlideShowAnimationRendererSession(presentation).PlanOverlay(slide);

        plan.Shapes.Select(shape => shape.ShapeId).Should().Equal(1u, 2u, 3u, 4u);
        plan.Shapes.Select(shape => shape.InitialOpacity).Should().Equal(0, 1, 1, 1);
        plan.Shapes.Select(shape => shape.SuppressBaseShape).Should().Equal(true, true, false, false);
        var fillLayer = plan.Shapes.Single(shape => shape.ShapeId == 4).AuxiliaryLayers
            .Should().ContainSingle().Which;
        fillLayer.TargetKind.Should().Be(SlideShowAnimationPlaybackTargetKind.Fill);
        fillLayer.UsesOpacityMask.Should().BeTrue();
    }

    [Fact]
    public void StepPlanOwnsParagraphStaggerSpecialTargetsAndFallbackDecisions()
    {
        var presentation = new Presentation();
        var session = new SlideShowAnimationRendererSession(presentation);
        var paragraph = Animation(
            shapeId: 10,
            AnimationKind.Entrance,
            AnimationPreset.Fade,
            durationMs: 120);
        var fill = Animation(
            shapeId: 20,
            AnimationKind.Emphasis,
            AnimationPreset.ChangeFillColor,
            durationMs: 300);
        var exitFallback = Animation(
            shapeId: 30,
            AnimationKind.Exit,
            AnimationPreset.Fade,
            durationMs: 200);
        var emphasisFallback = Animation(
            shapeId: 40,
            AnimationKind.Emphasis,
            AnimationPreset.Pulse,
            durationMs: 250);
        var step = new AnimationStep([
            new AnimationEntry(paragraph, 25),
            new AnimationEntry(fill, 50),
            new AnimationEntry(exitFallback, 75),
            new AnimationEntry(emphasisFallback, 90)
        ]);
        var targets = new SlideShowAnimationPlaybackTargetAvailability(
            PrimaryShapeIds: new HashSet<uint> { 10, 20 },
            ParagraphCounts: new Dictionary<uint, int> { [10] = 2 },
            FillShapeIds: new HashSet<uint> { 20 },
            LineShapeIds: new HashSet<uint>(),
            FontStyleShapeIds: new HashSet<uint>(),
            FontSizeShapeIds: new HashSet<uint>());

        var plan = session.PlanStep(step, 4, 960, 540, targets);

        plan.Operations.Select(operation => operation.TargetKind).Should().Equal(
            SlideShowAnimationPlaybackTargetKind.Paragraph,
            SlideShowAnimationPlaybackTargetKind.Paragraph,
            SlideShowAnimationPlaybackTargetKind.Fill,
            SlideShowAnimationPlaybackTargetKind.Fallback,
            SlideShowAnimationPlaybackTargetKind.Fallback);
        plan.Operations.Take(2).Select(operation => operation.Playback.DelayMs)
            .Should().Equal(25, 145);
        plan.Operations[3].FallbackVisibility.Should().Be(new SlideShowAnimationFallbackVisibilityPlan(
            SuppressAtStart: false,
            SuppressAtCompletion: true));
        plan.Operations[3].FallbackAnimation.Should().BeNull();
        plan.Operations[4].FallbackVisibility.Should().Be(new SlideShowAnimationFallbackVisibilityPlan(
            SuppressAtStart: false,
            SuppressAtCompletion: false));
        plan.Operations[4].FallbackAnimation.Should().Be(new SlideShowFallbackAnimationPlaybackPlan(
            DurationMs: 250,
            DelayMs: 90,
            FromOpacity: 1,
            FlashOpacity: 0.5));
        plan.Checkpoints.Should().NotBeEmpty();
        plan.Readiness.SlideIndex.Should().Be(4);
        session.LastStep.Should().BeSameAs(plan);
    }

    [Fact]
    public void TargetRegistryOwnsClassificationResolutionAndRevealEligibility()
    {
        var primary = new object();
        var paragraph0 = new object();
        var paragraph1 = new object();
        var paragraphRange = new object();
        var fill = new object();
        var line = new object();
        var fontStyle = new object();
        var fontSize = new object();
        var rangeAnimation = Animation(3, AnimationKind.Entrance, AnimationPreset.Fade, 200);
        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(
            rangeAnimation,
            0,
            new Presentation());
        var registry = new SlideShowAnimationTargetRegistry<object>();

        registry.Register(1, SlideShowAnimationPlaybackTargetKind.Primary, primary);
        registry.RegisterParagraphs(2, [paragraph0, paragraph1]);
        registry.RegisterParagraphRange(rangeAnimation, paragraphRange);
        registry.Register(4, SlideShowAnimationPlaybackTargetKind.Fill, fill);
        registry.Register(5, SlideShowAnimationPlaybackTargetKind.Line, line);
        registry.Register(6, SlideShowAnimationPlaybackTargetKind.FontStyle, fontStyle);
        registry.Register(7, SlideShowAnimationPlaybackTargetKind.FontSize, fontSize);

        var availability = registry.BuildAvailability();
        availability.PrimaryShapeIds.Should().ContainSingle().Which.Should().Be(1);
        availability.ParagraphCounts.Should().ContainKey(2);
        availability.ParagraphCounts[2].Should().Be(2);
        availability.ParagraphRangeAnimations.Should().NotBeNull();
        availability.ParagraphRangeAnimations!.Should().Contain(rangeAnimation);
        registry.CanRevealBase(1).Should().BeTrue();
        registry.CanRevealBase(2).Should().BeFalse();
        registry.CanRevealBase(3).Should().BeFalse();

        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Primary, 1, 0, playback))
            .Should().BeSameAs(primary);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Paragraph, 2, 1, playback))
            .Should().BeSameAs(paragraph1);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Paragraph, 2, 3, playback))
            .Should().BeNull();
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.ParagraphRange, 3, 0, playback))
            .Should().BeSameAs(paragraphRange);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Fill, 4, 0, playback))
            .Should().BeSameAs(fill);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Line, 5, 0, playback))
            .Should().BeSameAs(line);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.FontStyle, 6, 0, playback))
            .Should().BeSameAs(fontStyle);
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.FontSize, 7, 0, playback))
            .Should().BeSameAs(fontSize);

        registry.Clear();

        registry.BuildAvailability().PrimaryShapeIds.Should().BeEmpty();
        registry.CanRevealBase(2).Should().BeTrue();
        registry.Resolve(Operation(SlideShowAnimationPlaybackTargetKind.Primary, 1, 0, playback))
            .Should().BeNull();
    }

    [Fact]
    public void RepeatPassOwnsAutoReverseTimingGeometryAndStateReset()
    {
        var presentation = new Presentation();
        var session = new SlideShowAnimationRendererSession(presentation);
        var animation = Animation(
            shapeId: 7,
            AnimationKind.Entrance,
            AnimationPreset.FlyIn,
            durationMs: 400);
        animation.Direction = AnimationDirection.FromTopRight;
        animation.RepeatCount = 3;
        animation.AutoReverse = true;
        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(animation, 80, presentation);

        var first = session.PlanRepeatPass(playback, 0);
        var reverse = session.PlanRepeatPass(playback, 1);
        var third = session.PlanRepeatPass(playback, 2);

        first.DelayMs.Should().Be(80);
        first.RepeatCount.Should().BeNull();
        reverse.DelayMs.Should().Be(0);
        reverse.Animation.Kind.Should().Be(AnimationKind.Exit);
        reverse.FromOpacity.Should().Be(playback.ToOpacity);
        reverse.ToOpacity.Should().Be(playback.FromOpacity);
        reverse.OffsetXFactor.Should().Be(-playback.OffsetXFactor);
        reverse.OffsetYFactor.Should().Be(-playback.OffsetYFactor);
        reverse.AutoReverse.Should().BeFalse();
        third.DelayMs.Should().Be(0);
        third.Animation.Kind.Should().Be(AnimationKind.Entrance);
    }

    [Fact]
    public void ColorTracksOwnAuthoredWavePersistentAndFillKeyframes()
    {
        var presentation = new Presentation();
        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(
            Animation(5, AnimationKind.Emphasis, AnimationPreset.ColorWave, 500),
            0,
            presentation) with
        {
            ColorFromHex = "112233",
            ColorToHex = "AABBCC"
        };

        var wave = SlideShowAnimationColorTrackPlanner.BuildAuthoredColorOverlay(playback)!;
        wave.Colors.Select(frame => frame.Progress).Should().Equal(0, 0.25, 0.5, 0.75, 1);
        wave.Colors.Select(frame => frame.Value).Should().Equal(
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0xAA, 0xBB, 0xCC),
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0xAA, 0xBB, 0xCC),
            new SrgbColor(0x11, 0x22, 0x33));
        wave.Opacities.Select(frame => frame.Value).Should().Equal(0, 0.65, 0, 0.65, 0);

        var persistent = SlideShowAnimationColorTrackPlanner.BuildAuthoredColorOverlay(
            playback with { EffectKind = SlideShowShapeAnimationEffectKind.ChangeColor })!;
        persistent.Colors[^1].Value.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
        persistent.Opacities[^1].Value.Should().Be(0.65);

        var fill = SlideShowAnimationColorTrackPlanner.BuildFillColor(playback)!;
        fill.Colors.Select(frame => frame.Progress).Should().Equal(0, 1);
        fill.Opacities.Select(frame => frame.Value).Should().Equal(0, 1);
    }

    [Fact]
    public void EffectTracksFlowThroughRendererSession()
    {
        var presentation = new Presentation();
        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(
            Animation(8, AnimationKind.Emphasis, AnimationPreset.Teeter, 400),
            25,
            presentation);

        var plan = new SlideShowAnimationRendererSession(presentation)
            .PlanEffectTracks(playback);

        plan.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Teeter);
        plan.DelayMs.Should().Be(25);
        plan.FindTrack(SlideShowAnimationScalarPropertyKind.RotationDegrees)
            .Should().NotBeNull();
    }

    private static ShapeAnimation Animation(
        uint shapeId,
        AnimationKind kind,
        AnimationPreset preset,
        int durationMs) =>
        new()
        {
            ShapeId = shapeId,
            Kind = kind,
            Preset = preset,
            DurationMs = durationMs
        };

    private static SlideShowAnimationPlaybackOperation Operation(
        SlideShowAnimationPlaybackTargetKind targetKind,
        uint shapeId,
        int targetIndex,
        SlideShowShapeAnimationPlaybackPlan playback) =>
        new(
            targetKind,
            shapeId,
            targetIndex,
            playback,
            SuppressBaseBeforePlayback: false,
            RevealBaseUsingPlaybackTiming: false);
}
