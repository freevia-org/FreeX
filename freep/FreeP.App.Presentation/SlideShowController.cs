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
    private readonly Stack<int> _zoomReturnStack = new();

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
        int animationStartIndex = -1)
    {
        _slides = slides ?? throw new ArgumentNullException(nameof(slides));
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
        !HasPendingSteps && CurrentSlideIndex >= _slides.Count - 1;

    /// <summary>The ordered click-steps for the current slide (read-only).</summary>
    public IReadOnlyList<AnimationStep> CurrentSteps => _currentSteps;

    /// <summary>Whether advancing can return from the current Zoom target to its parent.</summary>
    public bool HasZoomReturnPath => _zoomReturnStack.Count > 0;

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
            var parentIndex = _zoomReturnStack.Pop();
            GoToSlide(parentIndex);
            return new AdvanceResult.NavigateToSlide(parentIndex, _slides[parentIndex]);
        }

        if (CurrentSlideIndex < _slides.Count - 1)
        {
            int nextIdx = CurrentSlideIndex + 1;
            GoToSlide(nextIdx);
            return new AdvanceResult.NavigateToSlide(nextIdx, _slides[nextIdx]);
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
            var parentIndex = _zoomReturnStack.Pop();
            GoToSlide(parentIndex);
            return new BackResult.NavigateToSlide(parentIndex, _slides[parentIndex]);
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
    /// Enters a Zoom target, optionally recording the current slide for PowerPoint's
    /// Return to Parent behavior.
    /// </summary>
    public void EnterZoomNavigation(int targetIndex, bool returnToParent)
    {
        if (_slides.Count == 0)
            return;

        if (returnToParent && CurrentSlideIndex >= 0)
            _zoomReturnStack.Push(CurrentSlideIndex);

        GoToSlide(targetIndex);
    }

    /// <summary>Clears any active Zoom return path before an unrelated direct jump.</summary>
    public void ClearZoomReturnPath() => _zoomReturnStack.Clear();

    /// <summary>
    /// Starts the current slide's animation sequence at a selected flat animation
    /// entry. Main-sequence entries trim the normal click chain. Trigger-only
    /// entries build and trim their own trigger chain so Animation Pane playback
    /// can start from an interactive animation without requiring a shape click.
    /// </summary>
    public bool StartAtAnimationIndex(int animationIndex)
    {
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
    /// </summary>
    public static IReadOnlyList<AnimationStep> BuildSteps(Slide slide)
    {
        var steps = new List<AnimationStep>();
        if (slide.Animations.Count == 0) return steps;

        List<AnimationEntry>? current = null;
        // Tracks accumulated end-time (StartDelayMs + DurationMs) of the last
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
                // this animation to finish (startDelay + duration).
                accumulatedEndMs = startDelay + Math.Max(0, anim.DurationMs);
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
                int startDelay = accumulatedEndMs + Math.Max(0, anim.DelayMs);
                current.Add(new AnimationEntry(anim, startDelay));
                // Advance the chain end: this animation ends at startDelay + its duration.
                accumulatedEndMs = startDelay + Math.Max(0, anim.DurationMs);
            }
        }
        return steps;
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
    /// Groups a flat list of trigger animations into click-steps (same rules as BuildSteps).
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
                accumulatedEndMs = startDelay + Math.Max(0, anim.DurationMs);
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
                int startDelay = accumulatedEndMs + Math.Max(0, anim.DelayMs);
                current.Add(new AnimationEntry(anim, startDelay));
                accumulatedEndMs = startDelay + Math.Max(0, anim.DurationMs);
            }
        }
        return steps;
    }

    // ── Private ───────────────────────────────────────────────────────────────────

    private void RebuildSteps()
    {
        PendingStepIndex = 0;
        _triggerStepCursors.Clear();
        _currentSteps = CurrentSlide is null
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
        public NavigateToSlide(int index, Slide slide) { SlideIndex = index; Slide = slide; }
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
        public NavigateToSlide(int index, Slide slide) { SlideIndex = index; Slide = slide; }
    }

    /// <summary>Already on the first slide; nothing to go back to.</summary>
    public sealed class AtStart : BackResult
    {
        public static readonly AtStart Instance = new();
        private AtStart() { }
    }
}
