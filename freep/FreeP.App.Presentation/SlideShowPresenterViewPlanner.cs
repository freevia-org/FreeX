using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared display contract for the presenter view adapters.</summary>
public sealed record SlideShowPresenterViewPlan(
    string StatusText,
    string CurrentSlideLabel,
    string NextSlideLabel,
    string NotesText,
    string ElapsedText,
    int? CurrentSlideNumber,
    Slide? CurrentSlide,
    Slide? NextSlide,
    bool CanGoBack,
    bool CanAdvance,
    SlideShowPresenterPointerMode PointerMode,
    bool IsRecordingTimings,
    bool IsRehearsingTimings,
    string RecordTimingsButtonText,
    string RehearseTimingsButtonText,
    string NarrationButtonText,
    string NarrationAndMediaButtonText,
    string RecordingStatusText,
    bool CanSetTimingIntent,
    bool CanSetMediaIntent,
    bool CanApplyRecording)
{
    public bool HasNotes =>
        !string.IsNullOrWhiteSpace(NotesText)
        && !string.Equals(
            NotesText,
            SlideShowPresenterViewPlanner.NoNotesText,
            StringComparison.Ordinal);

    public bool HasNextSlide => NextSlide is not null;
}

/// <summary>
/// Formats the existing presenter state for native WPF and Avalonia presenter windows.
/// Navigation, timing, and slide selection remain owned by the slideshow controllers.
/// </summary>
public static class SlideShowPresenterViewPlanner
{
    public const string NoCurrentSlideText = "No current slide";
    public const string EndOfPresentationText = "End of presentation";
    public const string NoNotesText = "No speaker notes";

    public static SlideShowPresenterViewPlan Build(
        SlideShowPresenterState state,
        SlideShowRecordingReviewPlan? recordingReview = null,
        bool canGoBack = true,
        bool canGoNext = true,
        bool canSetTimingIntent = true,
        bool canSetMediaIntent = true,
        bool canApplyRecording = true)
    {
        ArgumentNullException.ThrowIfNull(state);

        var timingIntent = state.ToolPlan.Recording.TimingIntent;
        var mediaIntent = state.ToolPlan.Recording.MediaIntent;

        return new SlideShowPresenterViewPlan(
            state.HostState.StatusText,
            BuildSlideLabel(state.CurrentSlide, NoCurrentSlideText),
            BuildSlideLabel(state.NextSlide, EndOfPresentationText),
            string.IsNullOrWhiteSpace(state.NotesText) ? NoNotesText : state.NotesText,
            FormatElapsed(state.Elapsed),
            state.HostState.CurrentSlideIndex >= 0
                ? state.HostState.CurrentSlideIndex + 1
                : null,
            state.CurrentSlide?.Slide,
            state.NextSlide?.Slide,
            canGoBack && state.HostState.HasSlides && !state.HostState.IsFirstSlide,
            canGoNext && state.HostState.HasSlides && (!state.HostState.IsLastSlide || state.HostState.HasPendingSteps),
            state.ToolPlan.PointerInk.PointerMode,
            timingIntent == SlideShowTimingIntent.RecordTimings,
            timingIntent == SlideShowTimingIntent.RehearseTimings,
            timingIntent == SlideShowTimingIntent.RecordTimings ? "Stop recording" : "Record timings",
            timingIntent == SlideShowTimingIntent.RehearseTimings ? "Stop rehearsal" : "Rehearse timings",
            mediaIntent == SlideShowRecordingMediaIntent.Narration ? "Stop narration" : "Narration",
            mediaIntent == SlideShowRecordingMediaIntent.NarrationAndMedia
                ? "Stop narration + camera"
                : "Narration + camera",
            recordingReview is null
                ? "Recording review unavailable."
                : FormatRecordingSummary(recordingReview),
            canSetTimingIntent,
            canSetMediaIntent,
            canApplyRecording && CanApplyRecordingReview(recordingReview));
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        var safe = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return safe.TotalHours >= 1
            ? $"{(int)safe.TotalHours:00}:{safe.Minutes:00}:{safe.Seconds:00}"
            : $"{safe.Minutes:00}:{safe.Seconds:00}";
    }

    public static string FormatRecordingSummary(SlideShowRecordingReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.CompletedSegmentCount == 0)
        {
            return "Recording: no completed slides yet.";
        }

        return $"Recording: {plan.CompletedSegmentCount} slide(s), " +
            $"{plan.TotalRecordedDurationMs / 1000d:F1}s; " +
            $"{plan.PersistableMediaArtifactCount} media + " +
            $"{plan.PersistableCaptionArtifactCount} caption(s) ready" +
            (plan.DeferredMediaArtifactCount > 0
                ? $"; {plan.DeferredMediaArtifactCount} deferred."
                : ".");
    }

    public static bool CanApplyRecordingReview(SlideShowRecordingReviewPlan? plan) =>
        plan is not null &&
        (plan.CanApplyRecordedTimings ||
         plan.PersistableMediaArtifactCount > 0 ||
         plan.PersistableCaptionArtifactCount > 0);

    private static string BuildSlideLabel(
        SlideShowPresenterSlideState? slide,
        string emptyText)
    {
        if (slide is null)
        {
            return emptyText;
        }

        var title = string.IsNullOrWhiteSpace(slide.Title)
            ? "Untitled slide"
            : slide.Title.Trim();
        return $"Slide {slide.SlideIndex + 1}: {title}";
    }
}
