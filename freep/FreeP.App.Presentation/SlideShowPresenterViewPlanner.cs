using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared display contract for the presenter view adapters.</summary>
public sealed record SlideShowPresenterViewPlan(
    string StatusText,
    string CurrentSlideLabel,
    string NextSlideLabel,
    string NotesText,
    string ElapsedText,
    Slide? CurrentSlide,
    Slide? NextSlide,
    bool CanGoBack,
    bool CanAdvance,
    SlideShowPresenterPointerMode PointerMode,
    bool IsRecordingTimings)
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

    public static SlideShowPresenterViewPlan Build(SlideShowPresenterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new SlideShowPresenterViewPlan(
            state.HostState.StatusText,
            BuildSlideLabel(state.CurrentSlide, NoCurrentSlideText),
            BuildSlideLabel(state.NextSlide, EndOfPresentationText),
            string.IsNullOrWhiteSpace(state.NotesText) ? NoNotesText : state.NotesText,
            FormatElapsed(state.Elapsed),
            state.CurrentSlide?.Slide,
            state.NextSlide?.Slide,
            state.HostState.HasSlides && !state.HostState.IsFirstSlide,
            state.HostState.HasSlides && (!state.HostState.IsLastSlide || state.HostState.HasPendingSteps),
            state.ToolPlan.PointerInk.PointerMode,
            state.ToolPlan.Recording.TimingIntent == SlideShowTimingIntent.RecordTimings);
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        var safe = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return safe.TotalHours >= 1
            ? $"{(int)safe.TotalHours:00}:{safe.Minutes:00}:{safe.Seconds:00}"
            : $"{safe.Minutes:00}:{safe.Seconds:00}";
    }

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
