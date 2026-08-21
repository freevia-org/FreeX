using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Pure-logic state machine for slideshow playback.
/// No WPF dependencies — fully unit-testable without a live window.
///
/// Concepts:
///   • Each slide's animation list is partitioned into "click-steps":
///     An OnClick animation begins a new step; WithPrevious/AfterPrevious animations
///     join the current step and play alongside or immediately after it.
///   • An "advance" action either plays the next pending click-step or, when all
///     steps for the current slide are exhausted, advances to the next slide.
///   • A "back" action always navigates to the previous slide (reset its animation state).
///   • The controller is a plain class; the Window drives it and applies effects.
/// </summary>
public sealed class SlideShowController
{
    // ── Immutable presentation data ───────────────────────────────────────────────

    private readonly IReadOnlyList<Slide> _slides;
    private readonly bool _showWithAnimation;
    private readonly bool _loopUntilStopped;

    // ── Mutable playback state ────────────────────────────────────────────────────

    /// <summary>Zero-based index of the currently displayed slide. -1 means no slides.</summary>
    public int CurrentSlideIndex { get; private set; }

    /// <summary>
    /// Index of the next pending click-step within the current slide's step list.
    /// 0 = no steps played yet.  Equal to StepCount => all steps done.
    /// </summary>
    public int PendingStepIndex { get; private set; }

    /// <summary>Precomputed click-steps for the current slide.</summary>
    private IReadOnlyList<AnimationStep> _currentSteps = Array.Empty<AnimationStep>();

    /// <summary>
    /// Per-trigger step cursors for interactive sequences on the current slide.
    /// Key = TriggerShapeId; value = index of the next step to play for that trigger.
    /// Reset whenever the slide changes (same as PendingStepIndex).
    /// </summary>
    private readonly Dictionary<uint, int> _triggerStepCursors = new();

    /// <summary>
    /// Parent slides for active PowerPoint Zoom objects whose ReturnToParent flag is set.
    /// A stack permits a Zoom target to contain another return-to-parent Zoom.
    /// </summary>
    private readonly Stack<ZoomReturnPoint> _zoomReturnStack = new();

    // ── Construction ─────────────────────────────────────────────────────────────

    /// <param name="slides">The ordered slide list from Presentation.Slides.</param>
    /// <param name="startIndex">Zero-based index to begin playback at.</param>
    /// <param name="animationStartIndex">
    /// Optional flat animation index for Animation Pane playback. Prior click steps
    /// are skipped and the selected step is trimmed to begin at that entry.
    /// </param>
    public SlideShowController(
        IReadOnlyList<Slide> slides,
        int startIndex,
        int animationStartIndex = -1,
        bool showWithAnimation = true,
        bool loopUntilStopped = false)
    {
        _slides = slides ?? throw new ArgumentNullException(nameof(slides));
        _showWithAnimation = showWithAnimation;
        _loopUntilStopped = loopUntilStopped;
        CurrentSlideIndex = _slides.Count == 0
            ? -1
            : Math.Clamp(startIndex, 0, _slides.Count - 1);
        RebuildSteps();
        if (animationStartIndex >= 0)
            StartAtAnimationIndex(animationStartIndex);
    }

    // ── Public state queries ──────────────────────────────────────────────────────

    /// <summary>The current slide, or null if there are no slides.</summary>
    public Slide? CurrentSlide =>
        CurrentSlideIndex >= 0 ? _slides[CurrentSlideIndex] : null;

    /// <summary>Total number of click-steps for the current slide.</summary>
    public int StepCount => _currentSteps.Count;

    /// <summary>
    /// Whether there are still pending animation steps on the current slide before
    /// a slide advance would occur.
    /// </summary>
    public bool HasPendingSteps => PendingStepIndex < _currentSteps.Count;

    /// <summary>Whether we are on the last slide and all its steps are done.</summary>
    public bool IsAtEnd =>
        !_loopUntilStopped && !HasPendingSteps && CurrentSlideIndex >= _slides.Count - 1;

    /// <summary>The ordered click-steps for the current slide (read-only).</summary>
    public IReadOnlyList<AnimationStep> CurrentSteps => _currentSteps;

    /// <summary>Whether advancing can return from the current Zoom target to its parent.</summary>
    public bool HasZoomReturnPath => _zoomReturnStack.Count > 0;

    /// <summary>Whether slide transitions and object animations should play during playback.</summary>
    public bool ShowWithAnimation => _showWithAnimation;

    // ── Navigation ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance: returns what the caller should do.
    /// • If there are pending animation steps, returns PlayStep with the next step.
    ///   The step is marked consumed (PendingStepIndex incremented).
    /// • If all steps are done and there is a next slide, returns NavigateToSlide.
    /// • If already at the last slide with no more steps, returns AtEnd.
    /// </summary>
    public AdvanceResult Advance()
    {
        if (HasPendingSteps)
        {
            var step = _currentSteps[PendingStepIndex];
            PendingStepIndex++;
            return new AdvanceResult.PlayStep(step);
        }

        if (_zoomReturnStack.Count > 0)
        {
            var returnPoint = _zoomReturnStack.Pop();
            GoToSlide(returnPoint.SlideIndex);
            return new AdvanceResult.NavigateToSlide(
                returnPoint.SlideIndex,
                _slides[returnPoint.SlideIndex],
                returnPoint.TransitionDurationMs,
                returnPoint.ShowBackground);
        }

        if (CurrentSlideIndex < _slides.Count - 1)
        {
            int nextIdx = CurrentSlideIndex + 1;
            GoToSlide(nextIdx);
            return new AdvanceResult.NavigateToSlide(nextIdx, _slides[nextIdx]);
        }

        if (_loopUntilStopped && _slides.Count > 0)
        {
            GoToSlide(0);
            return new AdvanceResult.NavigateToSlide(0, _slides[0]);
        }

        return AdvanceResult.AtEnd.Instance;
    }

    /// <summary>
    /// Back: always navigates to the previous slide (resetting its animation state).
    /// Returns NavigateToSlide or AtStart if already on slide 0.
    /// </summary>
    public BackResult Back()
    {
        if (_zoomReturnStack.Count > 0)
        {
            var returnPoint = _zoomReturnStack.Pop();
            GoToSlide(returnPoint.SlideIndex);
            return new BackResult.NavigateToSlide(
                returnPoint.SlideIndex,
                _slides[returnPoint.SlideIndex],
                returnPoint.TransitionDurationMs,
                returnPoint.ShowBackground);
        }

        if (CurrentSlideIndex <= 0)
            return BackResult.AtStart.Instance;

        int prevIdx = CurrentSlideIndex - 1;
        GoToSlide(prevIdx);
        return new BackResult.NavigateToSlide(prevIdx, _slides[prevIdx]);
    }

    /// <summary>Jump to a specific slide index, resetting animation state.</summary>
    public void GoToSlide(int index)
    {
        if (_slides.Count == 0) return;
        CurrentSlideIndex = Math.Clamp(index, 0, _slides.Count - 1);
        RebuildSteps();
    }

    /// <summary>
    /// If the current slide's first click-step is not actually gated behind a click -- i.e. its
    /// lead entry's stored <see cref="ShapeAnimation.Trigger"/> is <see cref="AnimationTrigger.WithPrevious"/>
    /// or <see cref="AnimationTrigger.AfterPrevious"/> rather than <see cref="AnimationTrigger.OnClick"/>,
    /// meaning it was authored (or promoted -- see round-160/161) to auto-play as soon as the slide
    /// begins -- returns that step and marks it consumed, so a later <see cref="Advance"/> does not
    /// re-deliver it as a click-gated step. Returns <see langword="null"/> when the first step
    /// legitimately requires a click, when the first step has already been consumed (by this method
    /// or by <see cref="Advance"/>), or when there are no steps at all.
    ///
    /// <see cref="BuildSteps"/> always groups a slide's very first main-sequence animation into its
    /// own click-step regardless of its stored trigger (so the file/model and undo/redo commands
    /// that promote a new head never need to rewrite it -- see the round-160/161 notes in
    /// PresentationCommands.cs). That is correct for grouping, but it means a WithPrevious/AfterPrevious
    /// head would otherwise sit in <see cref="CurrentSteps"/>[0] waiting for the same click an OnClick
    /// head waits for -- one Advance() more than real PowerPoint needs for such a head. Callers should
    /// invoke this immediately after navigating onto a slide (the initial slide when the show starts,
    /// and after every <see cref="AdvanceResult.NavigateToSlide"/> / <see cref="BackResult.NavigateToSlide"/>)
    /// and play the returned step alongside the slide's own transition, instead of waiting for the
    /// presenter's next Advance().
    /// </summary>
    public AnimationStep? ConsumeEntryAutoPlayStep()
    {
        if (PendingStepIndex != 0 || _currentSteps.Count == 0)
            return null;

        var head = _currentSteps[0];
        if (head.Entries.Count == 0 || head.Entries[0].Animation.Trigger == AnimationTrigger.OnClick)
            return null;

        PendingStepIndex = 1;
        return head;
    }

    /// <summary>
    /// Enters a Zoom target, optionally recording the current slide for PowerPoint's
    /// Return to Parent behavior.
    /// </summary>
    public void EnterZoomNavigation(
        int targetIndex,
        bool returnToParent,
        int? transitionDurationMs = null,
        bool showBackground = true)
    {
        if (_slides.Count == 0)
            return;

        if (returnToParent && CurrentSlideIndex >= 0)
        {
            _zoomReturnStack.Push(new ZoomReturnPoint(
                CurrentSlideIndex,
                transitionDurationMs,
                showBackground));
        }

        GoToSlide(targetIndex);
    }

    /// <summary>Clears any active Zoom return path before an unrelated direct jump.</summary>
    public void ClearZoomReturnPath() => _zoomReturnStack.Clear();

    private sealed record ZoomReturnPoint(
        int SlideIndex,
        int? TransitionDurationMs,
        bool ShowBackground);

    /// <summary>
    /// Starts the current slide's animation sequence at a selected flat animation
    /// entry. Main-sequence entries trim the normal click chain. Trigger-only
    /// entries build and trim their own trigger chain so Animation Pane playback
    /// can start from an interactive animation without requiring a shape click.
    /// </summary>
    public bool StartAtAnimationIndex(int animationIndex)
    {
        if (!_showWithAnimation)
            return false;

        var slide = CurrentSlide;
        if (slide is null
            || animationIndex < 0
            || animationIndex >= slide.Animations.Count)
        {
            return false;
        }

        var selectedAnimation = slide.Animations[animationIndex];
        if (selectedAnimation.TriggerShapeId is uint triggerShapeId)
        {
            var triggerSteps = BuildTriggerSteps(slide.Animations
                .Where(animation => animation.TriggerShapeId == triggerShapeId)
                .ToArray());

            for (var stepIndex = 0; stepIndex < triggerSteps.Count; stepIndex++)
            {
                var step = triggerSteps[stepIndex];
                var entryIndex = -1;
                for (var i = 0; i < step.Entries.Count; i++)
                {
                    if (ReferenceEquals(step.Entries[i].Animation, selectedAnimation))
                    {
                        entryIndex = i;
                        break;
                    }
                }

                if (entryIndex < 0)
                    continue;

                var trimmedSteps = new List<AnimationStep>(triggerSteps.Count - stepIndex)
                {
                    new AnimationStep(step.Entries.Skip(entryIndex).ToArray())
                };
                trimmedSteps.AddRange(triggerSteps.Skip(stepIndex + 1));
                _currentSteps = trimmedSteps;
                PendingStepIndex = 0;
                _triggerStepCursors.Clear();
                return true;
            }

            return false;
        }

        for (var stepIndex = 0; stepIndex < _currentSteps.Count; stepIndex++)
        {
            var step = _currentSteps[stepIndex];
            var entryIndex = -1;
            for (var i = 0; i < step.Entries.Count; i++)
            {
                if (ReferenceEquals(step.Entries[i].Animation, selectedAnimation))
                {
                    entryIndex = i;
                    break;
                }
            }

            if (entryIndex < 0)
                continue;

            var trimmedSteps = new List<AnimationStep>(_currentSteps.Count - stepIndex)
            {
                new AnimationStep(step.Entries.Skip(entryIndex).ToArray())
            };
            trimmedSteps.AddRange(_currentSteps.Skip(stepIndex + 1));
            _currentSteps = trimmedSteps;
            PendingStepIndex = 0;
            _triggerStepCursors.Clear();
            return true;
        }

        return false;
    }

    // ── Step grouping ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups a slide's flat animation list into click-steps.
    /// ONLY includes animations that are in the main sequence (TriggerShapeId == null).
    /// Trigger animations are excluded from the advance chain — they are fired by
    /// <see cref="FireTrigger"/> when the user clicks the trigger shape.
    ///
    /// Rules:
    ///   • OnClick begins a new step.
    ///   • WithPrevious joins the current step and starts simultaneously with the prior animation
    ///     (StartDelayMs = animation's own DelayMs, same as the preceding animation's timeline).
    ///   • AfterPrevious joins the current step but starts only after the preceding animation
    ///     has fully completed: StartDelayMs = accumulated prior durations + prior delays.
    ///     Multiple AfterPrevious animations chain: each waits for the previous to finish.
    ///     A repeating predecessor's true on-screen completion time is its single-pass
    ///     DurationMs multiplied by RepeatCount (see <see cref="ResolveChainEndMs"/>); an
    ///     indefinitely-repeating predecessor never completes on its own, so the chain is
    ///     frozen from that point on (see <see cref="NeverAutoStartsMs"/>).
    /// </summary>
    public static IReadOnlyList<AnimationStep> BuildSteps(Slide slide)
    {
        var steps = new List<AnimationStep>();
        if (slide.Animations.Count == 0) return steps;

        List<AnimationEntry>? current = null;
        // Tracks accumulated end-time (StartDelayMs + total repeated duration) of the last
        // animation in the AfterPrevious chain within the current step.
        // When the next animation is AfterPrevious, it must start at this accumulated time.
        int accumulatedEndMs = 0;

        foreach (var anim in slide.Animations)
        {
            // Skip trigger animations — they are not part of the main advance chain.
            if (anim.TriggerShapeId is not null) continue;

            if (anim.Trigger == AnimationTrigger.OnClick || current is null)
            {
                // New click-step: the first animation starts at its own DelayMs.
                int startDelay = Math.Max(0, anim.DelayMs);
                current = new List<AnimationEntry> { new AnimationEntry(anim, startDelay) };
                steps.Add(new AnimationStep(current));
                // The AfterPrevious chain resets: any subsequent AfterPrevious waits for
                // this animation to finish (startDelay + its full repeated duration).
                accumulatedEndMs = ResolveChainEndMs(startDelay, anim);
            }
            else if (anim.Trigger == AnimationTrigger.WithPrevious)
            {
                // WithPrevious: starts simultaneously with the previous animation —
                // same start-time reference (the current accumulated base is NOT advanced).
                // Use the animation's own DelayMs as its start offset from click.
                // Note: WithPrevious with DelayMs > 0 is a PowerPoint-legal offset FROM the
                // simultaneous start, not from completion of prior. We honour DelayMs as-is.
                int startDelay = Math.Max(0, anim.DelayMs);
                current.Add(new AnimationEntry(anim, startDelay));
                // WithPrevious does NOT extend the chain end time — the AfterPrevious
                // reference tracks the OnClick/AfterPrevious spine, not concurrent sideloads.
                // (PowerPoint: AfterPrevious after a WithPrevious waits for the spine anim,
                // not the With anim. Keep accumulatedEndMs unchanged.)
            }
            else
            {
                // AfterPrevious: must start after the accumulated chain completes.
                // StartDelayMs = accumulated end time of the prior animation + own DelayMs.
                int startDelay = ResolveAfterPreviousStartDelay(accumulatedEndMs, anim);
                current.Add(new AnimationEntry(anim, startDelay));
                // Advance the chain end: this animation ends at startDelay + its full
                // repeated duration (or never, if it repeats indefinitely itself).
                accumulatedEndMs = ResolveChainEndMs(startDelay, anim);
            }
        }
        return steps;
    }

    /// <summary>
    /// Sentinel StartDelayMs meaning "does not start automatically within this click-step".
    /// Used for an AfterPrevious entry whose predecessor repeats indefinitely (PowerPoint
    /// Timing "Repeat: Until Next Click" / "Until End of Slide"): such a predecessor's
    /// timeline never completes on its own, so PowerPoint never fires a sibling chained
    /// after it via "Start: After Previous" — only leaving the slide (or the predecessor's
    /// own click/slide-end stop condition, which is not part of this chain) ends it. Rather
    /// than guess a finite number, downstream entries inherit this sentinel so they, too,
    /// stay dormant until the user manually advances past the frozen chain. A duration this
    /// large (~24.8 days in milliseconds) is effectively "never" for any real slideshow, and
    /// stays within <see cref="TimeSpan"/>'s range so callers can safely schedule against it.
    /// </summary>
    private const int NeverAutoStartsMs = int.MaxValue;

    /// <summary>
    /// Resolves the StartDelayMs for an AfterPrevious entry given the accumulated end-time
    /// of the chain so far. Once the chain is frozen (<see cref="NeverAutoStartsMs"/>), every
    /// subsequent AfterPrevious entry inherits the same frozen sentinel rather than adding its
    /// own DelayMs on top (which could silently overflow back into a finite, wrong value).
    /// </summary>
    private static int ResolveAfterPreviousStartDelay(int accumulatedEndMs, ShapeAnimation anim)
    {
        if (accumulatedEndMs == NeverAutoStartsMs)
            return NeverAutoStartsMs;

        long startDelay = (long)accumulatedEndMs + Math.Max(0, anim.DelayMs);
        return startDelay >= NeverAutoStartsMs ? NeverAutoStartsMs : (int)startDelay;
    }

    /// <summary>
    /// Resolves the accumulated chain end-time after <paramref name="anim"/> plays, starting
    /// at <paramref name="startDelayMs"/>. A finite <see cref="ShapeAnimation.RepeatCount"/>
    /// multiplies the single-pass DurationMs (PowerPoint replays the whole pass that many
    /// times before the effect is done); <see cref="ShapeAnimation.RepeatIndefinitely"/> — or
    /// an already-frozen incoming chain — freezes the chain via <see cref="NeverAutoStartsMs"/>.
    /// </summary>
    private static int ResolveChainEndMs(int startDelayMs, ShapeAnimation anim)
    {
        if (anim.RepeatIndefinitely || startDelayMs == NeverAutoStartsMs)
            return NeverAutoStartsMs;

        int passCount = Math.Max(1, anim.RepeatCount ?? 1);
        long endMs = startDelayMs + (long)Math.Max(0, anim.DurationMs) * passCount;
        return endMs >= NeverAutoStartsMs ? NeverAutoStartsMs : (int)endMs;
    }

    /// <summary>
    /// Returns ALL animation steps registered for the given trigger shape (query only,
    /// does not advance the per-trigger cursor). Used for testing and initial inspection.
    /// Returns empty when no trigger group is registered for that shape.
    /// </summary>
    public IReadOnlyList<AnimationStep> FireTrigger(uint triggerShapeId)
    {
        var slide = CurrentSlide;
        if (slide is null) return Array.Empty<AnimationStep>();

        var triggerAnims = slide.Animations
            .Where(a => a.TriggerShapeId == triggerShapeId)
            .ToList();

        return BuildTriggerSteps(triggerAnims);
    }

    /// <summary>
    /// Advances the per-trigger click cursor for <paramref name="triggerShapeId"/> by one step
    /// and returns that step, mirroring how <see cref="Advance"/> works for the main sequence.
    /// Returns <see langword="null"/> when all steps for this trigger have already been played
    /// (subsequent clicks on an exhausted trigger are silently ignored, matching PowerPoint behaviour).
    /// </summary>
    public AnimationStep? AdvanceTrigger(uint triggerShapeId)
    {
        var allSteps = FireTrigger(triggerShapeId);
        if (allSteps.Count == 0) return null;

        _triggerStepCursors.TryGetValue(triggerShapeId, out int cursor);
        if (cursor >= allSteps.Count) return null;  // already exhausted

        var step = allSteps[cursor];
        _triggerStepCursors[triggerShapeId] = cursor + 1;
        return step;
    }

    /// <summary>
    /// Groups a flat list of trigger animations into click-steps (same rules as BuildSteps,
    /// including repeat-aware chain timing — see <see cref="ResolveChainEndMs"/>).
    /// AfterPrevious entries get accumulated start delays; WithPrevious entries are simultaneous.
    /// </summary>
    public static IReadOnlyList<AnimationStep> BuildTriggerSteps(IReadOnlyList<ShapeAnimation> anims)
    {
        var steps = new List<AnimationStep>();
        if (anims.Count == 0) return steps;

        List<AnimationEntry>? current = null;
        int accumulatedEndMs = 0;

        foreach (var anim in anims)
        {
            if (anim.Trigger == AnimationTrigger.OnClick || current is null)
            {
                int startDelay = Math.Max(0, anim.DelayMs);
                current = new List<AnimationEntry> { new AnimationEntry(anim, startDelay) };
                steps.Add(new AnimationStep(current));
                accumulatedEndMs = ResolveChainEndMs(startDelay, anim);
            }
            else if (anim.Trigger == AnimationTrigger.WithPrevious)
            {
                int startDelay = Math.Max(0, anim.DelayMs);
                current.Add(new AnimationEntry(anim, startDelay));
                // accumulatedEndMs unchanged (WithPrevious doesn't advance the AfterPrevious spine)
            }
            else
            {
                // AfterPrevious
                int startDelay = ResolveAfterPreviousStartDelay(accumulatedEndMs, anim);
                current.Add(new AnimationEntry(anim, startDelay));
                accumulatedEndMs = ResolveChainEndMs(startDelay, anim);
            }
        }
        return steps;
    }

    // ── Private ───────────────────────────────────────────────────────────────────

    private void RebuildSteps()
    {
        PendingStepIndex = 0;
        _triggerStepCursors.Clear();
        _currentSteps = !_showWithAnimation || CurrentSlide is null
            ? Array.Empty<AnimationStep>()
            : BuildSteps(CurrentSlide);
    }
}

// ── Value types returned by the controller ────────────────────────────────────

/// <summary>
/// A single animation within a click-step, together with its computed start delay.
/// </summary>
/// <param name="Animation">The shape animation to play.</param>
/// <param name="StartDelayMs">
/// Computed start delay in milliseconds relative to when the click-step begins.
/// For OnClick and WithPrevious entries this equals the animation's own <see cref="ShapeAnimation.DelayMs"/>.
/// For AfterPrevious entries it is the sum of all preceding animations' durations (plus their
/// own delays) in the AfterPrevious chain, so the entry begins only after the previous
/// animation completes — matching PowerPoint semantics.
/// </param>
public sealed record AnimationEntry(ShapeAnimation Animation, int StartDelayMs);

/// <summary>
/// A group of shape animations that play on a single click advance.
/// Composed of one OnClick animation plus any immediately following
/// WithPrevious / AfterPrevious animations.
/// WithPrevious entries share their start time with the preceding animation (same step, simultaneous).
/// AfterPrevious entries are scheduled to begin after the preceding animation's duration completes,
/// with their StartDelayMs already accounting for the accumulated prior durations.
/// </summary>
public sealed class AnimationStep
{
    public IReadOnlyList<AnimationEntry> Entries { get; }

    /// <summary>Back-compat accessor: the raw animations in this step (without start-delay data).</summary>
    public IReadOnlyList<ShapeAnimation> Animations => Entries.Select(e => e.Animation).ToList();

    public AnimationStep(IReadOnlyList<AnimationEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }
}

/// <summary>Discriminated result from <see cref="SlideShowController.Advance"/>.</summary>
public abstract class AdvanceResult
{
    private AdvanceResult() { }

    /// <summary>Play the next animation step (the step to execute is included).</summary>
    public sealed class PlayStep : AdvanceResult
    {
        public AnimationStep Step { get; }
        public PlayStep(AnimationStep step) { Step = step; }
    }

    /// <summary>Move to the given slide (no animation step to play on departure).</summary>
    public sealed class NavigateToSlide : AdvanceResult
    {
        public int SlideIndex { get; }
        public Slide Slide { get; }
        public int? TransitionDurationMs { get; }
        public bool UseDestinationBackground { get; }
        public NavigateToSlide(
            int index,
            Slide slide,
            int? transitionDurationMs = null,
            bool useDestinationBackground = true)
        {
            SlideIndex = index;
            Slide = slide;
            TransitionDurationMs = transitionDurationMs;
            UseDestinationBackground = useDestinationBackground;
        }
    }

    /// <summary>Already at the end of the presentation; nothing more to do.</summary>
    public sealed class AtEnd : AdvanceResult
    {
        public static readonly AtEnd Instance = new();
        private AtEnd() { }
    }
}

/// <summary>Discriminated result from <see cref="SlideShowController.Back"/>.</summary>
public abstract class BackResult
{
    private BackResult() { }

    /// <summary>Navigate to the given previous slide.</summary>
    public sealed class NavigateToSlide : BackResult
    {
        public int SlideIndex { get; }
        public Slide Slide { get; }
        public int? TransitionDurationMs { get; }
        public bool UseDestinationBackground { get; }
        public NavigateToSlide(
            int index,
            Slide slide,
            int? transitionDurationMs = null,
            bool useDestinationBackground = true)
        {
            SlideIndex = index;
            Slide = slide;
            TransitionDurationMs = transitionDurationMs;
            UseDestinationBackground = useDestinationBackground;
        }
    }

    /// <summary>Already on the first slide; nothing to go back to.</summary>
    public sealed class AtStart : BackResult
    {
        public static readonly AtStart Instance = new();
        private AtStart() { }
    }
}
