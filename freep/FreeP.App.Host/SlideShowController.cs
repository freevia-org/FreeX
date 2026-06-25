using FreeP.Core.Model;

namespace FreeP.App.Host;

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

    // ── Construction ─────────────────────────────────────────────────────────────

    /// <param name="slides">The ordered slide list from Presentation.Slides.</param>
    /// <param name="startIndex">Zero-based index to begin playback at.</param>
    public SlideShowController(IReadOnlyList<Slide> slides, int startIndex)
    {
        _slides = slides ?? throw new ArgumentNullException(nameof(slides));
        CurrentSlideIndex = _slides.Count == 0
            ? -1
            : Math.Clamp(startIndex, 0, _slides.Count - 1);
        RebuildSteps();
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

    // ── Step grouping ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups a slide's flat animation list into click-steps.
    /// ONLY includes animations that are in the main sequence (TriggerShapeId == null).
    /// Trigger animations are excluded from the advance chain — they are fired by
    /// <see cref="FireTrigger"/> when the user clicks the trigger shape.
    /// Rule: an OnClick animation begins a new step; WithPrevious and AfterPrevious
    /// animations join the current step and will play together with it.
    /// </summary>
    public static IReadOnlyList<AnimationStep> BuildSteps(Slide slide)
    {
        var steps = new List<AnimationStep>();
        if (slide.Animations.Count == 0) return steps;

        List<ShapeAnimation>? current = null;
        foreach (var anim in slide.Animations)
        {
            // Skip trigger animations — they are not part of the main advance chain.
            if (anim.TriggerShapeId is not null) continue;

            if (anim.Trigger == AnimationTrigger.OnClick || current is null)
            {
                current = new List<ShapeAnimation> { anim };
                steps.Add(new AnimationStep(current));
            }
            else
            {
                // WithPrevious / AfterPrevious: join the current step
                current.Add(anim);
            }
        }
        return steps;
    }

    /// <summary>
    /// Returns the animations that should fire when the shape with <paramref name="triggerShapeId"/>
    /// is clicked, grouped into steps exactly like <see cref="BuildSteps"/>.
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
    /// Groups a flat list of trigger animations into click-steps (same rules as BuildSteps).
    /// </summary>
    public static IReadOnlyList<AnimationStep> BuildTriggerSteps(IReadOnlyList<ShapeAnimation> anims)
    {
        var steps = new List<AnimationStep>();
        if (anims.Count == 0) return steps;

        List<ShapeAnimation>? current = null;
        foreach (var anim in anims)
        {
            if (anim.Trigger == AnimationTrigger.OnClick || current is null)
            {
                current = new List<ShapeAnimation> { anim };
                steps.Add(new AnimationStep(current));
            }
            else
            {
                current.Add(anim);
            }
        }
        return steps;
    }

    // ── Private ───────────────────────────────────────────────────────────────────

    private void RebuildSteps()
    {
        PendingStepIndex = 0;
        _currentSteps = CurrentSlide is null
            ? Array.Empty<AnimationStep>()
            : BuildSteps(CurrentSlide);
    }
}

// ── Value types returned by the controller ────────────────────────────────────

/// <summary>
/// A group of shape animations that play together on a single click advance.
/// Composed of one OnClick animation plus any immediately following
/// WithPrevious / AfterPrevious animations.
/// </summary>
public sealed class AnimationStep
{
    public IReadOnlyList<ShapeAnimation> Animations { get; }

    public AnimationStep(IReadOnlyList<ShapeAnimation> animations)
    {
        Animations = animations ?? throw new ArgumentNullException(nameof(animations));
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
