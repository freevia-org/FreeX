using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowPresenterViewField
{
    Status,
    Elapsed,
    SlideNumber,
    PointerMode,
    RecordingStatus,
    CurrentPreview,
    NextPreview,
    SpeakerNotes,
}

public enum SlideShowPresenterViewAction
{
    Previous,
    Next,
    GoToSlide,
    RecordTimings,
    RehearseTimings,
    Narration,
    NarrationAndMedia,
    ApplyRecording,
    ShowScreen,
    BlackScreen,
    WhiteScreen,
    ClearInk,
}

public sealed record SlideShowPresenterViewSurfacePlan(
    PresentationDialogSurfacePlan<SlideShowPresenterViewField, SlideShowPresenterViewAction> Schema)
{
    public string Title => Schema.Title;

    public PresentationDialogFieldPlan<SlideShowPresenterViewField> Field(
        SlideShowPresenterViewField id) => Schema.Field(id);

    public PresentationDialogActionPlan<SlideShowPresenterViewAction> Action(
        SlideShowPresenterViewAction id) => Schema.Action(id);

    public string FormatElapsed(string elapsedText) =>
        $"{Field(SlideShowPresenterViewField.Elapsed).Label} {elapsedText}";
}

public static class SlideShowPresenterViewSurfaceCatalog
{
    public static SlideShowPresenterViewSurfacePlan Surface { get; } = new(
        new PresentationDialogSurfacePlan<SlideShowPresenterViewField, SlideShowPresenterViewAction>(
            "Presenter View",
            "Presenter View",
            "FreeP.PresenterView.Window",
            [
                Field(SlideShowPresenterViewField.Status, PresentationDialogControlKind.Status,
                    string.Empty, "Slide show status"),
                Field(SlideShowPresenterViewField.Elapsed, PresentationDialogControlKind.Status,
                    "Elapsed", "Elapsed slide show time"),
                Field(SlideShowPresenterViewField.SlideNumber, PresentationDialogControlKind.Text,
                    "Slide", "Go to slide number", "Enter a slide number and activate Go."),
                Field(SlideShowPresenterViewField.PointerMode, PresentationDialogControlKind.Choice,
                    "Pointer mode", "Presenter pointer mode"),
                Field(SlideShowPresenterViewField.RecordingStatus, PresentationDialogControlKind.Status,
                    string.Empty, "Recording review status"),
                Field(SlideShowPresenterViewField.CurrentPreview, PresentationDialogControlKind.Label,
                    "Current", "Current slide preview"),
                Field(SlideShowPresenterViewField.NextPreview, PresentationDialogControlKind.Label,
                    "Next", "Next slide preview"),
                Field(SlideShowPresenterViewField.SpeakerNotes, PresentationDialogControlKind.Text,
                    "Speaker notes", "Speaker notes"),
            ],
            [
                Action(SlideShowPresenterViewAction.Previous, "Previous", "Show previous slide"),
                Action(SlideShowPresenterViewAction.Next, "Next", "Show next slide"),
                Action(SlideShowPresenterViewAction.GoToSlide, "Go", "Go to slide number", isDefault: true),
                Action(SlideShowPresenterViewAction.RecordTimings, "Record timings", "Toggle timing recording"),
                Action(SlideShowPresenterViewAction.RehearseTimings, "Rehearse timings", "Toggle timing rehearsal"),
                Action(SlideShowPresenterViewAction.Narration, "Narration", "Toggle narration recording"),
                Action(SlideShowPresenterViewAction.NarrationAndMedia, "Narration + camera", "Toggle narration and camera recording"),
                Action(SlideShowPresenterViewAction.ApplyRecording, "Apply recording", "Apply recorded timings and media"),
                Action(SlideShowPresenterViewAction.ShowScreen, "Show", "Show the current slide"),
                Action(SlideShowPresenterViewAction.BlackScreen, "Black", "Show a black screen"),
                Action(SlideShowPresenterViewAction.WhiteScreen, "White", "Show a white screen"),
                Action(SlideShowPresenterViewAction.ClearInk, "Clear ink", "Clear presenter ink"),
            ]));

    private static PresentationDialogFieldPlan<SlideShowPresenterViewField> Field(
        SlideShowPresenterViewField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName,
        string? helpText = null) =>
        new(id, kind, label, accessibleName, $"FreeP.PresenterView.{id}", helpText);

    private static PresentationDialogActionPlan<SlideShowPresenterViewAction> Action(
        SlideShowPresenterViewAction id,
        string label,
        string accessibleName,
        bool isDefault = false) =>
        new(id, label, accessibleName, $"FreeP.PresenterView.{id}", IsDefault: isDefault);
}

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
