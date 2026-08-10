using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowTransitionPlaybackCoordinatorTests
{
    [Fact]
    public void Play_sequences_sound_planning_reset_and_rendering()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        presentation.Slides.Add(slide);
        var transition = new SlideTransition
        {
            Kind = TransitionKind.Fade,
            DurationMs = 730
        };
        var renderer = new RecordingRenderer();

        var plan = SlideShowTransitionPlaybackCoordinator.Play(
            presentation,
            slide,
            transition,
            renderer);

        renderer.Events.Should().Equal("Sound", "Reset", "PlayFade");
        renderer.LastSlide.Should().BeSameAs(slide);
        renderer.LastPlan.Should().BeSameAs(plan);
        renderer.LastTransition.Should().BeSameAs(transition);
        plan.DurationMs.Should().Be(730);
        plan.EffectiveTransition.Should().BeSameAs(transition);
    }

    [Fact]
    public void Dispatch_covers_every_playback_action_exactly_once()
    {
        var slide = new Slide();
        var renderer = new RecordingRenderer();

        foreach (var action in Enum.GetValues<SlideShowTransitionPlaybackActionKind>())
        {
            renderer.Clear();
            var plan = BuildPlan(action);

            SlideShowTransitionPlaybackCoordinator.Dispatch(slide, plan, renderer);

            var expected = action switch
            {
                SlideShowTransitionPlaybackActionKind.ShowInstant =>
                    nameof(ISlideShowTransitionPlaybackRenderer.ShowInstant),
                SlideShowTransitionPlaybackActionKind.Honeycomb
                    or SlideShowTransitionPlaybackActionKind.Glitter
                    or SlideShowTransitionPlaybackActionKind.Ripple
                    or SlideShowTransitionPlaybackActionKind.Wind
                    or SlideShowTransitionPlaybackActionKind.Curtains
                    or SlideShowTransitionPlaybackActionKind.Shred
                    or SlideShowTransitionPlaybackActionKind.Drape
                    or SlideShowTransitionPlaybackActionKind.Fracture
                    or SlideShowTransitionPlaybackActionKind.Crush
                    or SlideShowTransitionPlaybackActionKind.Prism
                    or SlideShowTransitionPlaybackActionKind.Prestige
                    or SlideShowTransitionPlaybackActionKind.Warp
                    or SlideShowTransitionPlaybackActionKind.Vortex =>
                    nameof(ISlideShowTransitionPlaybackRenderer.PlayPolygonClip),
                SlideShowTransitionPlaybackActionKind.Flip
                    or SlideShowTransitionPlaybackActionKind.Cube
                    or SlideShowTransitionPlaybackActionKind.Rotate
                    or SlideShowTransitionPlaybackActionKind.Switch
                    or SlideShowTransitionPlaybackActionKind.Orbit
                    or SlideShowTransitionPlaybackActionKind.Ferris
                    or SlideShowTransitionPlaybackActionKind.Flythrough =>
                    nameof(ISlideShowTransitionPlaybackRenderer.PlayPerspective),
                _ => $"Play{action}"
            };
            renderer.Events.Should().Equal(expected);
            renderer.LastSlide.Should().BeSameAs(slide);
            renderer.LastPlan.Should().BeSameAs(plan);
            if (expected == nameof(ISlideShowTransitionPlaybackRenderer.PlayPolygonClip))
                renderer.LastPolygonPlan!.ActionKind.Should().Be(action);
            if (action is SlideShowTransitionPlaybackActionKind.Zoom
                or SlideShowTransitionPlaybackActionKind.Pan
                or SlideShowTransitionPlaybackActionKind.Gallery
                or SlideShowTransitionPlaybackActionKind.Conveyor
                or SlideShowTransitionPlaybackActionKind.Window)
            {
                renderer.LastTransformPlan!.ActionKind.Should().Be(action);
            }
            if (expected == nameof(ISlideShowTransitionPlaybackRenderer.PlayPerspective))
                renderer.LastPerspectivePlan!.Kind.Should().Be(ResolvePerspectiveKind(action));
        }
    }

    [Theory]
    [InlineData(SlideShowTransitionPlaybackActionKind.Flip, TransitionKind.Flip, SlideShowPerspectiveTransitionKind.Flip)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Cube, TransitionKind.Cube, SlideShowPerspectiveTransitionKind.Cube)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Rotate, TransitionKind.Rotate, SlideShowPerspectiveTransitionKind.Rotate)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Switch, TransitionKind.Switch, SlideShowPerspectiveTransitionKind.Switch)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Orbit, TransitionKind.Orbit, SlideShowPerspectiveTransitionKind.Orbit)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Ferris, TransitionKind.Ferris, SlideShowPerspectiveTransitionKind.Ferris)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Flythrough, TransitionKind.Flythrough, SlideShowPerspectiveTransitionKind.Flythrough)]
    public void Dispatch_builds_perspective_plan_before_invoking_renderer(
        SlideShowTransitionPlaybackActionKind action,
        TransitionKind transitionKind,
        SlideShowPerspectiveTransitionKind expectedKind)
    {
        var renderer = new RecordingRenderer();

        SlideShowTransitionPlaybackCoordinator.Dispatch(
            new Slide(),
            BuildPlan(action, transitionKind),
            renderer);

        renderer.Events.Should().Equal(nameof(ISlideShowTransitionPlaybackRenderer.PlayPerspective));
        renderer.LastPerspectivePlan!.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void Dispatch_unknown_action_preserves_fade_fallback()
    {
        var renderer = new RecordingRenderer();

        SlideShowTransitionPlaybackCoordinator.Dispatch(
            new Slide(),
            BuildPlan((SlideShowTransitionPlaybackActionKind)int.MaxValue),
            renderer);

        renderer.Events.Should().Equal("PlayFade");
    }

    private static SlideShowTransitionPlaybackPlan BuildPlan(
        SlideShowTransitionPlaybackActionKind action,
        TransitionKind? transitionKind = null)
    {
        var effectiveKind = transitionKind ?? ResolveTransitionKind(action);
        return new(
            action,
            DurationMs: 500,
            IncomingOffsetX: 0,
            IncomingOffsetY: 0,
            SourceKind: SlideShowTransitionPlaybackKind.Fade,
            SplitHorizontal: false,
            SplitOut: false,
            BlindsHorizontal: false,
            RandomBarsHorizontal: false,
            StripsSlopeDown: false,
            WheelSpokeCount: 1,
            WheelReverse: false,
            ZoomIn: true,
            BoxExpandsFromCenter: true,
            ResolvedKind: effectiveKind,
            RandomSeed: null,
            EffectiveTransition: new SlideTransition
            {
                Kind = effectiveKind,
                Direction = TransitionDirection.Left
            });
    }

    private static TransitionKind ResolveTransitionKind(
        SlideShowTransitionPlaybackActionKind action) =>
        action switch
        {
            SlideShowTransitionPlaybackActionKind.Flip => TransitionKind.Flip,
            SlideShowTransitionPlaybackActionKind.Cube => TransitionKind.Cube,
            SlideShowTransitionPlaybackActionKind.Rotate => TransitionKind.Rotate,
            SlideShowTransitionPlaybackActionKind.Switch => TransitionKind.Switch,
            SlideShowTransitionPlaybackActionKind.Orbit => TransitionKind.Orbit,
            SlideShowTransitionPlaybackActionKind.Ferris => TransitionKind.Ferris,
            SlideShowTransitionPlaybackActionKind.Flythrough => TransitionKind.Flythrough,
            _ => TransitionKind.Fade
        };

    private static SlideShowPerspectiveTransitionKind ResolvePerspectiveKind(
        SlideShowTransitionPlaybackActionKind action) =>
        action switch
        {
            SlideShowTransitionPlaybackActionKind.Flip => SlideShowPerspectiveTransitionKind.Flip,
            SlideShowTransitionPlaybackActionKind.Cube => SlideShowPerspectiveTransitionKind.Cube,
            SlideShowTransitionPlaybackActionKind.Rotate => SlideShowPerspectiveTransitionKind.Rotate,
            SlideShowTransitionPlaybackActionKind.Switch => SlideShowPerspectiveTransitionKind.Switch,
            SlideShowTransitionPlaybackActionKind.Orbit => SlideShowPerspectiveTransitionKind.Orbit,
            SlideShowTransitionPlaybackActionKind.Ferris => SlideShowPerspectiveTransitionKind.Ferris,
            SlideShowTransitionPlaybackActionKind.Flythrough => SlideShowPerspectiveTransitionKind.Flythrough,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private sealed class RecordingRenderer : ISlideShowTransitionPlaybackRenderer
    {
        public List<string> Events { get; } = new();
        public Slide? LastSlide { get; private set; }
        public SlideTransition? LastTransition { get; private set; }
        public SlideShowTransitionPlaybackPlan? LastPlan { get; private set; }
        public SlideShowPolygonClipTransitionPlan? LastPolygonPlan { get; private set; }
        public SlideShowPerspectiveTransitionPlan? LastPerspectivePlan { get; private set; }
        public SlideShowTransformTransitionPlan? LastTransformPlan { get; private set; }

        public void Clear()
        {
            Events.Clear();
            LastSlide = null;
            LastTransition = null;
            LastPlan = null;
            LastPolygonPlan = null;
            LastPerspectivePlan = null;
            LastTransformPlan = null;
        }

        public void PlayTransitionSound(SlideTransition transition)
        {
            LastTransition = transition;
            Events.Add("Sound");
        }

        public void ResetTransitionVisuals() => Events.Add("Reset");
        public void ShowInstant(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(ShowInstant), slide, plan);
        public void PlayFade(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayFade), slide, plan);
        public void PlayFlash(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayFlash), slide, plan);
        public void PlayDissolve(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayDissolve), slide, plan);
        public void PlayBox(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayBox), slide, plan);
        public void PlayReveal(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayReveal), slide, plan);
        public void PlayUncover(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayUncover), slide, plan);
        public void PlayCover(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayCover), slide, plan);
        public void PlaySplit(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlaySplit), slide, plan);
        public void PlayBlinds(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayBlinds), slide, plan);
        public void PlayRandomBars(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayRandomBars), slide, plan);
        public void PlayStrips(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayStrips), slide, plan);
        public void PlayWheel(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayWheel), slide, plan);
        public void PlayZoom(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => RecordTransform(nameof(PlayZoom), slide, plan, transformPlan);
        public void PlayPan(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => RecordTransform(nameof(PlayPan), slide, plan, transformPlan);
        public void PlayGallery(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => RecordTransform(nameof(PlayGallery), slide, plan, transformPlan);
        public void PlayConveyor(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => RecordTransform(nameof(PlayConveyor), slide, plan, transformPlan);
        public void PlayWindow(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => RecordTransform(nameof(PlayWindow), slide, plan, transformPlan);
        public void PlayMorph(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayMorph), slide, plan);
        public void PlayPerspective(
            Slide slide,
            SlideShowTransitionPlaybackPlan plan,
            SlideShowPerspectiveTransitionPlan perspectivePlan)
        {
            LastPerspectivePlan = perspectivePlan;
            Record(nameof(PlayPerspective), slide, plan);
        }
        public void PlayPolygonClip(
            Slide slide,
            SlideShowTransitionPlaybackPlan plan,
            SlideShowPolygonClipTransitionPlan polygonPlan)
        {
            LastPolygonPlan = polygonPlan;
            Record(nameof(PlayPolygonClip), slide, plan);
        }
        public void PlayPageCurl(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayPageCurl), slide, plan);
        public void PlayPush(Slide slide, SlideShowTransitionPlaybackPlan plan) => Record(nameof(PlayPush), slide, plan);

        private void Record(string action, Slide slide, SlideShowTransitionPlaybackPlan plan)
        {
            LastSlide = slide;
            LastPlan = plan;
            Events.Add(action);
        }

        private void RecordTransform(
            string action,
            Slide slide,
            SlideShowTransitionPlaybackPlan plan,
            SlideShowTransformTransitionPlan transformPlan)
        {
            LastTransformPlan = transformPlan;
            Record(action, slide, plan);
        }
    }
}
