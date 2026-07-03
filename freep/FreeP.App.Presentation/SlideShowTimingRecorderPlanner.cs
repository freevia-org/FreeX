using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowTimingRecorderState(
    int? CurrentSlideIndex,
    DateTimeOffset? EnteredAtUtc,
    IReadOnlyList<SlideShowSlideTimingMutation> RecordedTimings);

public sealed record SlideShowSlideTimingMutation(
    int SlideIndex,
    int AdvanceAfterMs,
    bool ShouldPersist,
    SlideShowTimingIntent TimingIntent);

public sealed record SlideShowTimingRecorderResult(
    SlideShowTimingRecorderState State,
    IReadOnlyList<SlideShowSlideTimingMutation> Mutations);

public static class SlideShowTimingRecorderPlanner
{
    public const int MinRecordedTimingMs = 1;
    public const int MaxRecordedTimingMs = 24 * 60 * 60 * 1000;

    public static SlideShowTimingRecorderState CreateState(
        int currentSlideIndex,
        DateTimeOffset enteredAtUtc) =>
        currentSlideIndex >= 0
            ? new SlideShowTimingRecorderState(currentSlideIndex, enteredAtUtc, Array.Empty<SlideShowSlideTimingMutation>())
            : new SlideShowTimingRecorderState(null, null, Array.Empty<SlideShowSlideTimingMutation>());

    public static SlideShowTimingRecorderResult EnterSlide(
        SlideShowTimingRecorderState state,
        int slideIndex,
        DateTimeOffset enteredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new SlideShowTimingRecorderResult(
            slideIndex >= 0
                ? state with { CurrentSlideIndex = slideIndex, EnteredAtUtc = enteredAtUtc }
                : state with { CurrentSlideIndex = null, EnteredAtUtc = null },
            Array.Empty<SlideShowSlideTimingMutation>());
    }

    public static SlideShowTimingRecorderResult LeaveCurrentSlide(
        SlideShowTimingRecorderState state,
        SlideShowPresenterToolPlan toolPlan,
        DateTimeOffset leftAtUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(toolPlan);

        var leftState = state with { CurrentSlideIndex = null, EnteredAtUtc = null };
        if (state.CurrentSlideIndex is not int slideIndex || state.EnteredAtUtc is not DateTimeOffset enteredAtUtc)
        {
            return new SlideShowTimingRecorderResult(leftState, Array.Empty<SlideShowSlideTimingMutation>());
        }

        if (!toolPlan.Recording.ShouldTrackPerSlideTimings)
        {
            return new SlideShowTimingRecorderResult(leftState, Array.Empty<SlideShowSlideTimingMutation>());
        }

        var elapsedMs = ClampElapsedMilliseconds(leftAtUtc - enteredAtUtc);
        var mutation = new SlideShowSlideTimingMutation(
            slideIndex,
            elapsedMs,
            toolPlan.Recording.ShouldPersistTimings,
            toolPlan.Recording.TimingIntent);
        var recorded = state.RecordedTimings.Concat(new[] { mutation }).ToArray();

        return new SlideShowTimingRecorderResult(
            leftState with { RecordedTimings = recorded },
            new[] { mutation });
    }

    public static void ApplyTimings(
        Presentation presentation,
        IReadOnlyList<SlideShowSlideTimingMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(mutations);

        foreach (var mutation in mutations)
        {
            if (!mutation.ShouldPersist ||
                mutation.SlideIndex < 0 ||
                mutation.SlideIndex >= presentation.Slides.Count)
            {
                continue;
            }

            var slide = presentation.Slides[mutation.SlideIndex];
            slide.Transition = PresentationTransitionCommandPlanner.BuildAdvanceAfterTransition(
                slide.Transition,
                mutation.AdvanceAfterMs);
        }
    }

    public static int ClampElapsedMilliseconds(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return MinRecordedTimingMs;
        }

        var totalMs = elapsed.TotalMilliseconds;
        if (totalMs >= MaxRecordedTimingMs)
        {
            return MaxRecordedTimingMs;
        }

        return Math.Clamp((int)Math.Round(totalMs), MinRecordedTimingMs, MaxRecordedTimingMs);
    }
}
