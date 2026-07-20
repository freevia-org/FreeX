using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackKind
{
    Cut,
    Fade,
    Flash,
    Dissolve,
    Box,
    Reveal,
    Uncover,
    Cover,
    Push,
    Split,
    Blinds,
    RandomBars,
    Strips,
    Wheel,
    Zoom,
    Pan,
    Gallery,
    Conveyor,
    Window,
    Morph,
    Flip,
    Cube,
    Rotate,
    Honeycomb,
    Switch,
    Orbit,
    Ferris,
    Flythrough,
    Glitter,
    Ripple,
    Wind,
    Curtains,
    Shred,
    Drape,
    Fracture,
    Crush,
    Prism,
    Prestige,
    Warp,
    Vortex,
    PageCurl,
    PushLike,
    FadeFallback
}

public sealed record SlideShowTransitionPlan(
    TransitionKind ResolvedKind,
    ulong? RandomSeed,
    SlideShowTransitionPlaybackKind PlaybackKind,
    double IncomingOffsetX,
    double IncomingOffsetY,
    bool SplitHorizontal,
    bool SplitOut,
    bool BlindsHorizontal,
    bool RandomBarsHorizontal,
    bool StripsSlopeDown,
    int WheelSpokeCount,
    bool WheelReverse,
    bool ZoomIn,
    bool BoxExpandsFromCenter);

public static class SlideShowTransitionPlanner
{
    private const string RandomSeedVersion = "FreeP.RandomTransition.v1";

    private static readonly TransitionKind[] RandomCandidateKindArray =
    [
        TransitionKind.Cut,
        TransitionKind.Fade,
        TransitionKind.Flash,
        TransitionKind.Dissolve,
        TransitionKind.Box,
        TransitionKind.Reveal,
        TransitionKind.Uncover,
        TransitionKind.Cover,
        TransitionKind.Push,
        TransitionKind.Split,
        TransitionKind.Blinds,
        TransitionKind.RandomBar,
        TransitionKind.Strips,
        TransitionKind.Wheel,
        TransitionKind.Zoom,
        TransitionKind.Pan,
        TransitionKind.Gallery,
        TransitionKind.Conveyor,
        TransitionKind.Window,
        TransitionKind.Morph,
        TransitionKind.Flip,
        TransitionKind.Cube,
        TransitionKind.Rotate,
        TransitionKind.Honeycomb,
        TransitionKind.Switch,
        TransitionKind.Orbit,
        TransitionKind.Ferris,
        TransitionKind.Flythrough,
        TransitionKind.Glitter,
        TransitionKind.Ripple,
        TransitionKind.Wind,
        TransitionKind.Curtains,
        TransitionKind.Shred,
        TransitionKind.Drape,
        TransitionKind.Fracture,
        TransitionKind.Crush,
        TransitionKind.Prism,
        TransitionKind.Prestige,
        TransitionKind.Warp,
        TransitionKind.Vortex,
        TransitionKind.PageCurlSingle
    ];

    /// <summary>
    /// One stable representative for each dedicated renderer-neutral transition family.
    /// PowerPoint defines p:random as choosing from transitions available to the renderer;
    /// aliases are omitted here so no playback family receives extra weight.
    /// </summary>
    public static IReadOnlyList<TransitionKind> RandomCandidateKinds { get; } =
        Array.AsReadOnly(RandomCandidateKindArray);

    public static SlideShowTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return PlanCore(presentation: null, slide: null, transition);
    }

    public static SlideShowTransitionPlan Plan(
        Presentation presentation,
        Slide slide,
        SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(transition);

        return PlanCore(presentation, slide, transition);
    }

    public static ulong ComputeRandomSeed(
        Presentation presentation,
        Slide slide,
        SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(transition);

        return BuildRandomSeed(presentation, slide, transition);
    }

    private static SlideShowTransitionPlan PlanCore(
        Presentation? presentation,
        Slide? slide,
        SlideTransition transition)
    {
        ulong? randomSeed = transition.Kind == TransitionKind.Random
            ? BuildRandomSeed(presentation, slide, transition)
            : null;
        var resolvedKind = randomSeed is ulong seed
            ? RandomCandidateKindArray[(int)(seed % (ulong)RandomCandidateKindArray.Length)]
            : transition.Kind;

        var (x, y) = ResolveIncomingOffset(transition.Direction);
        return new SlideShowTransitionPlan(
            resolvedKind,
            randomSeed,
            PlanPlaybackKind(resolvedKind),
            x,
            y,
            ResolveSplitHorizontal(transition, resolvedKind),
            ResolveSplitOut(transition, resolvedKind),
            ResolveBlindsHorizontal(transition),
            ResolveRandomBarsHorizontal(transition),
            ResolveStripsSlopeDown(transition),
            ResolveWheelSpokeCount(transition),
            resolvedKind == TransitionKind.WheelReverse,
            transition.Direction != TransitionDirection.Out,
            ResolveBoxExpandsFromCenter(transition));
    }

    public static SlideShowTransitionPlaybackKind PlanPlaybackKind(TransitionKind kind)
    {
        if (kind == TransitionKind.Random)
        {
            var seed = BuildRandomSeed(
                presentation: null,
                slide: null,
                new SlideTransition { Kind = TransitionKind.Random });
            kind = RandomCandidateKindArray[(int)(seed % (ulong)RandomCandidateKindArray.Length)];
        }

        return kind switch
        {
            TransitionKind.None or
            TransitionKind.Cut => SlideShowTransitionPlaybackKind.Cut,

            TransitionKind.Fade => SlideShowTransitionPlaybackKind.Fade,

            // PresentationML p14:flash is a distinct transition. Keep it
            // renderer-neutral so both slideshow hosts can produce a brief
            // white flash instead of silently reducing it to a cross-fade.
            TransitionKind.Flash => SlideShowTransitionPlaybackKind.Flash,

            TransitionKind.Dissolve => SlideShowTransitionPlaybackKind.Dissolve,

            TransitionKind.Box => SlideShowTransitionPlaybackKind.Box,

            TransitionKind.Reveal => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Wipe => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Uncover => SlideShowTransitionPlaybackKind.Uncover,

            TransitionKind.Cover => SlideShowTransitionPlaybackKind.Cover,

            TransitionKind.Push => SlideShowTransitionPlaybackKind.Push,

            // There is no standard PresentationML p:fly element. The package
            // writer emits Fly as push, so playback must follow that same
            // interoperable representation instead of falling back to fade.
            TransitionKind.Fly => SlideShowTransitionPlaybackKind.Push,

            TransitionKind.Doors => SlideShowTransitionPlaybackKind.Split,

            TransitionKind.Split => SlideShowTransitionPlaybackKind.Split,

            TransitionKind.Blinds => SlideShowTransitionPlaybackKind.Blinds,

            // OOXML comb is a directional bar wipe; reuse the renderer-neutral
            // blinds geometry so both slideshow hosts preserve horz/vert axes.
            TransitionKind.Comb => SlideShowTransitionPlaybackKind.Blinds,

            TransitionKind.RandomBar => SlideShowTransitionPlaybackKind.RandomBars,

            TransitionKind.Strips => SlideShowTransitionPlaybackKind.Strips,

            TransitionKind.Wheel or
            TransitionKind.WheelReverse => SlideShowTransitionPlaybackKind.Wheel,

            TransitionKind.Zoom => SlideShowTransitionPlaybackKind.Zoom,

            TransitionKind.Pan => SlideShowTransitionPlaybackKind.Pan,

            // Gallery is a two-surface exchange: unlike Cover, the outgoing
            // slide participates in the motion and the incoming slide starts
            // as a centered, reduced panel.
            TransitionKind.Gallery => SlideShowTransitionPlaybackKind.Gallery,

            TransitionKind.Conveyor => SlideShowTransitionPlaybackKind.Conveyor,

            // Window opens the incoming slide through a centered aperture;
            // unlike Box it starts partially open and carries a subtle scale.
            TransitionKind.Window => SlideShowTransitionPlaybackKind.Window,

            // Morph is object-aware when both slides expose stable ids or
            // unique names; the host falls back only when no match exists.
            TransitionKind.Morph => SlideShowTransitionPlaybackKind.Morph,

            TransitionKind.Flip => SlideShowTransitionPlaybackKind.Flip,

            TransitionKind.Cube => SlideShowTransitionPlaybackKind.Cube,

            TransitionKind.Rotate => SlideShowTransitionPlaybackKind.Rotate,

            TransitionKind.Honeycomb => SlideShowTransitionPlaybackKind.Honeycomb,

            TransitionKind.Switch => SlideShowTransitionPlaybackKind.Switch,

            TransitionKind.Orbit => SlideShowTransitionPlaybackKind.Orbit,

            TransitionKind.Ferris => SlideShowTransitionPlaybackKind.Ferris,

            TransitionKind.Flythrough => SlideShowTransitionPlaybackKind.Flythrough,

            TransitionKind.Glitter => SlideShowTransitionPlaybackKind.Glitter,

            TransitionKind.Ripple => SlideShowTransitionPlaybackKind.Ripple,

            TransitionKind.Wind => SlideShowTransitionPlaybackKind.Wind,

            TransitionKind.Curtains => SlideShowTransitionPlaybackKind.Curtains,

            TransitionKind.Shred => SlideShowTransitionPlaybackKind.Shred,

            // Peel Off is the single-sheet page-peel family. Reuse the
            // shared folded-page projection instead of reducing it to fade.
            TransitionKind.PeelOff => SlideShowTransitionPlaybackKind.PageCurl,

            TransitionKind.Drape => SlideShowTransitionPlaybackKind.Drape,

            // Airplane is a motion-through-space transition; use the
            // direction-aware Flythrough projection rather than a fade.
            TransitionKind.Airplane => SlideShowTransitionPlaybackKind.Flythrough,

            // Origami is a multi-fold paper transition; use the shared
            // double-fold page projection instead of reducing it to fade.
            TransitionKind.Origami => SlideShowTransitionPlaybackKind.PageCurl,

            TransitionKind.Vortex => SlideShowTransitionPlaybackKind.Vortex,

            TransitionKind.Warp => SlideShowTransitionPlaybackKind.Warp,

            TransitionKind.Fracture => SlideShowTransitionPlaybackKind.Fracture,

            TransitionKind.Crush => SlideShowTransitionPlaybackKind.Crush,

            TransitionKind.Prism => SlideShowTransitionPlaybackKind.Prism,

            TransitionKind.Prestige => SlideShowTransitionPlaybackKind.Prestige,

            TransitionKind.PageCurlSingle or
            TransitionKind.PageCurlDouble => SlideShowTransitionPlaybackKind.PageCurl,

            _ => SlideShowTransitionPlaybackKind.FadeFallback
        };
    }

    private static bool ResolveSplitHorizontal(
        SlideTransition transition,
        TransitionKind resolvedKind) =>
        resolvedKind == TransitionKind.Doors
        || transition.SplitOrientation == TransitionDirection.Horizontal
        || (transition.SplitOrientation is null
            && transition.Direction != TransitionDirection.Vertical);

    private static bool ResolveSplitOut(
        SlideTransition transition,
        TransitionKind resolvedKind) =>
        resolvedKind == TransitionKind.Doors
        || transition.Direction != TransitionDirection.In;

    private static bool ResolveBlindsHorizontal(SlideTransition transition) =>
        transition.Direction != TransitionDirection.Vertical;

    private static bool ResolveRandomBarsHorizontal(SlideTransition transition) =>
        transition.Direction != TransitionDirection.Vertical;

    private static bool ResolveStripsSlopeDown(SlideTransition transition) =>
        transition.Direction is TransitionDirection.LeftDown or TransitionDirection.RightUp;

    private static int ResolveWheelSpokeCount(SlideTransition transition) =>
        Math.Clamp(transition.WheelSpokeCount is > 0 ? transition.WheelSpokeCount.Value : 4, 1, 32);

    private static bool ResolveBoxExpandsFromCenter(SlideTransition transition) =>
        transition.Direction switch
        {
            TransitionDirection.In => true,
            TransitionDirection.Out => false,
            _ => true
        };

    public static (double X, double Y) ResolveIncomingOffset(TransitionDirection? direction) =>
        direction switch
        {
            TransitionDirection.Right => (-1, 0),
            TransitionDirection.Left => (1, 0),
            TransitionDirection.Down => (0, -1),
            TransitionDirection.Up => (0, 1),
            _ => (1, 0)
        };

    private static ulong BuildRandomSeed(
        Presentation? presentation,
        Slide? slide,
        SlideTransition transition)
    {
        var hash = new StableHashBuilder();
        hash.Add(RandomSeedVersion);

        hash.Add(presentation is not null);
        if (presentation is not null)
        {
            hash.Add(presentation.SlideSizeCxEmu);
            hash.Add(presentation.SlideSizeCyEmu);
            hash.Add(presentation.Properties.Title);
            hash.Add(presentation.Properties.Author);
            hash.Add(presentation.Slides.Count);
            foreach (var presentationSlide in presentation.Slides)
            {
                hash.Add(presentationSlide.Id);
                hash.Add(presentationSlide.NumericId);
            }
        }

        hash.Add(slide is not null);
        if (slide is not null)
        {
            hash.Add(presentation?.Slides.IndexOf(slide) ?? -1);
            hash.Add(slide.Id);
            hash.Add(slide.NumericId);
            hash.Add(slide.Title);
        }

        hash.Add((int)transition.Kind);
        hash.Add((int?)transition.Direction);
        hash.Add((int?)transition.SplitOrientation);
        hash.Add(transition.DurationMs);
        hash.Add(transition.AdvanceOnClick);
        hash.Add(transition.AdvanceAfterMs);
        hash.Add(transition.MorphOption);
        hash.Add(transition.WheelSpokeCount);
        return hash.Value;
    }

    private sealed class StableHashBuilder
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value = OffsetBasis;

        public ulong Value => _value;

        public void Add(bool value) => AddByte(value ? (byte)1 : (byte)0);

        public void Add(int value) => Add((long)value);

        public void Add(int? value)
        {
            Add(value.HasValue);
            if (value.HasValue)
                Add(value.Value);
        }

        public void Add(uint? value)
        {
            Add(value.HasValue);
            if (value.HasValue)
                Add((long)value.Value);
        }

        public void Add(long value)
        {
            unchecked
            {
                var bits = (ulong)value;
                for (var shift = 0; shift < 64; shift += 8)
                    AddByte((byte)(bits >> shift));
            }
        }

        public void Add(string? value)
        {
            Add(value is not null);
            if (value is null)
                return;

            Add(value.Length);
            foreach (var character in value)
            {
                AddByte((byte)character);
                AddByte((byte)(character >> 8));
            }
        }

        private void AddByte(byte value)
        {
            unchecked
            {
                _value ^= value;
                _value *= Prime;
            }
        }
    }
}
