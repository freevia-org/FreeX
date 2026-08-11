using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationMediaPaneSessionCallbacks(
    Action MarkDirty,
    Action RefreshReviewWorkflowPlans,
    Action UpdateHost);

public enum PresentationMediaBookmarkMutationIntentKind
{
    Create,
    Replace,
    Delete
}

public sealed record PresentationMediaTimingInputPlan(
    string TrimStartText,
    string TrimEndText,
    string FadeInText,
    string FadeOutText);

public sealed record PresentationMediaPlaybackInputPlan(
    MediaPlaybackStartMode StartMode,
    int StartModeIndex,
    bool Loop,
    bool ShowWhenStopped,
    bool RewindAfterPlaying,
    bool PlayFullScreen,
    int StopAfterSlides,
    string StopAfterSlidesText);

public sealed record PresentationMediaTimingMutationPlan(
    double TrimStartMilliseconds,
    double TrimEndMilliseconds,
    double FadeInMilliseconds,
    double FadeOutMilliseconds);

public sealed record PresentationMediaBookmarkInputPlan(
    string Name,
    string TimeText);

public sealed record PresentationMediaBookmarkPaneItemPlan(
    int Index,
    string DisplayText,
    string Name,
    double TimeMilliseconds,
    string TimeText);

public sealed record PresentationMediaPaneProjection(
    bool HasMedia,
    int VolumePercent,
    MediaPlaybackStartMode PlaybackStartMode,
    bool Loop,
    bool ShowWhenStopped,
    bool RewindAfterPlaying,
    bool PlayFullScreen,
    int StopAfterSlides,
    bool CanPlayFullScreen,
    bool CanStopAfterSlides,
    PresentationMediaTimingInputPlan Timing,
    IReadOnlyList<PresentationMediaBookmarkPaneItemPlan> Bookmarks,
    int? SelectedBookmarkIndex,
    string BookmarkName,
    string BookmarkTimeText)
{
    public bool HasSelectedBookmark => SelectedBookmarkIndex.HasValue;
}

public sealed record PresentationMediaBookmarkMutationPlan(
    bool ShouldApply,
    IReadOnlyList<MediaBookmarkInfo> Bookmarks,
    int? SelectedBookmarkIndex);

/// <summary>
/// Owns renderer-neutral state and decisions for the media authoring pane. Hosts retain native
/// controls, event/focus wiring, rendering, and their dirty/status callbacks.
/// </summary>
public sealed class PresentationMediaPaneSession
{
    private readonly Func<EditingSession> _getEditor;
    private readonly PresentationMediaPaneSessionCallbacks _callbacks;

    public PresentationMediaPaneSession(
        Func<EditingSession> getEditor,
        PresentationMediaPaneSessionCallbacks callbacks)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public int? SelectedCaptionTrackIndex { get; private set; }

    public int? SelectedBookmarkIndex { get; private set; }

    public PresentationMediaCaptionAuthoringPanePlan? LastCaptionAuthoringPanePlan { get; private set; }

    public PresentationMediaCaptionAuthoringMutationPlan? LastCaptionAuthoringMutationPlan { get; private set; }

    public PresentationMediaCaptionTrackMutationResult? LastCaptionTrackMutationResult { get; private set; }

    public MediaInfo? SelectedMedia => PresentationMediaTranscriptPlanner
        .FindSelectedMediaShape(_getEditor().CurrentSlide, _getEditor().SelectedShapeIds)
        ?.Media;

    public void SelectCaptionTrack(int? trackIndex) => SelectedCaptionTrackIndex = trackIndex;

    public void SelectBookmark(int? bookmarkIndex) => SelectedBookmarkIndex = bookmarkIndex;

    public void ClearCaptionSelection() => SelectedCaptionTrackIndex = null;

    public PresentationMediaCaptionAuthoringPanePlan RefreshCaptionAuthoringPanePlan(
        string? proposedLabel,
        string? proposedLanguage,
        string? proposedSource,
        string? proposedTranscriptText)
    {
        var editor = _getEditor();
        LastCaptionAuthoringPanePlan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
            editor.CurrentSlide,
            editor.CurrentSlideIndex,
            editor.SelectedShapeIds,
            SelectedCaptionTrackIndex,
            proposedLabel,
            proposedLanguage,
            proposedSource,
            proposedTranscriptText);
        SelectedCaptionTrackIndex = LastCaptionAuthoringPanePlan.SelectedTrackIndex >= 0
            ? LastCaptionAuthoringPanePlan.SelectedTrackIndex
            : null;
        return LastCaptionAuthoringPanePlan;
    }

    public PresentationMediaCaptionTrackMutationResult ApplyCaptionAuthoring(
        PresentationMediaCaptionAuthoringIntentKind intent,
        string? label,
        string? language,
        string? source,
        string? transcriptText)
    {
        var media = SelectedMedia;
        LastCaptionAuthoringMutationPlan =
            PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(
                media,
                intent,
                SelectedCaptionTrackIndex ?? -1,
                new PresentationMediaCaptionTrackAuthoringDescriptor(
                    label,
                    language,
                    source,
                    transcriptText));
        LastCaptionTrackMutationResult =
            _getEditor().ApplyMediaCaptionAuthoring(LastCaptionAuthoringMutationPlan);

        if (LastCaptionTrackMutationResult.Succeeded)
        {
            SelectedCaptionTrackIndex = NormalizeCaptionSelectionAfterMutation(
                media,
                intent,
                LastCaptionTrackMutationResult.TrackIndex);
            CompleteMutation();
        }

        return LastCaptionTrackMutationResult;
    }

    public bool ApplyVolume(int volumePercent)
    {
        var changed = _getEditor().SetSelectedMediaVolume(NormalizeVolumePercent(volumePercent));
        if (changed)
            CompleteMutation();
        return changed;
    }

    public bool ApplyPlayback(
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped = true,
        bool rewindAfterPlaying = false,
        bool playFullScreen = false,
        int stopAfterSlides = 1)
    {
        var changed = _getEditor().SetSelectedMediaPlaybackOptions(
            startMode,
            loop,
            showWhenStopped,
            rewindAfterPlaying,
            playFullScreen,
            NormalizeStopAfterSlides(stopAfterSlides));
        if (changed)
            CompleteMutation();
        return changed;
    }

    public bool ApplyTiming(
        string? trimStartText,
        string? trimEndText,
        string? fadeInText,
        string? fadeOutText)
    {
        var plan = BuildTimingMutationPlan(
            trimStartText,
            trimEndText,
            fadeInText,
            fadeOutText);
        var changed = _getEditor().SetSelectedMediaTiming(
            plan.TrimStartMilliseconds,
            plan.TrimEndMilliseconds,
            plan.FadeInMilliseconds,
            plan.FadeOutMilliseconds);
        if (changed)
            CompleteMutation();
        return changed;
    }

    public bool ApplyBookmark(
        PresentationMediaBookmarkMutationIntentKind intent,
        string? name,
        string? timeText)
    {
        var plan = BuildBookmarkMutationPlan(
            SelectedMedia,
            intent,
            SelectedBookmarkIndex,
            name,
            timeText);
        if (!plan.ShouldApply)
            return false;

        var changed = _getEditor().SetSelectedMediaBookmarks(plan.Bookmarks);
        if (changed)
        {
            SelectedBookmarkIndex = plan.SelectedBookmarkIndex;
            CompleteMutation();
        }
        return changed;
    }

    public PresentationMediaPaneProjection BuildProjection()
    {
        var media = SelectedMedia;
        SelectedBookmarkIndex = NormalizeBookmarkSelection(media, SelectedBookmarkIndex);
        var bookmarks = media?.Bookmarks
            .Select((bookmark, index) => new PresentationMediaBookmarkPaneItemPlan(
                index,
                $"{index + 1}. {bookmark.Name}",
                bookmark.Name,
                bookmark.TimeMilliseconds,
                FormatTiming(bookmark.TimeMilliseconds)))
            .ToArray() ?? [];
        var selected = SelectedBookmarkIndex is int selectedIndex
            && selectedIndex >= 0
            && selectedIndex < bookmarks.Length
                ? bookmarks[selectedIndex]
                : null;

        return new PresentationMediaPaneProjection(
            media is not null,
            media?.VolumePercent ?? 80,
            media?.PlaybackStartMode ?? MediaPlaybackStartMode.InClickSequence,
            media?.Loop ?? false,
            media?.ShowWhenStopped ?? true,
            media?.RewindAfterPlaying ?? false,
            media?.PlayFullScreen ?? false,
            NormalizeStopAfterSlides(media?.StopAfterSlides ?? 1),
            media is { IsVideo: true },
            media is { IsVideo: false },
            BuildTimingInputPlan(
                media?.TrimStartMilliseconds ?? 0,
                media?.TrimEndMilliseconds ?? 0,
                media?.FadeInMilliseconds ?? 0,
                media?.FadeOutMilliseconds ?? 0),
            bookmarks,
            SelectedBookmarkIndex,
            selected?.Name ?? string.Empty,
            selected?.TimeText ?? FormatTiming(0));
    }

    public static int NormalizeVolumePercent(double volumePercent) =>
        (int)Math.Clamp(Math.Round(volumePercent), 0, 100);

    public static int GetPlaybackStartModeIndex(MediaPlaybackStartMode startMode) =>
        startMode == MediaPlaybackStartMode.Automatically ? 1 : 0;

    public static MediaPlaybackStartMode GetPlaybackStartMode(int selectedIndex) =>
        selectedIndex == 1
            ? MediaPlaybackStartMode.Automatically
            : MediaPlaybackStartMode.InClickSequence;

    public static int NormalizeStopAfterSlides(int stopAfterSlides) =>
        Math.Max(1, stopAfterSlides);

    public static int ParseStopAfterSlides(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? NormalizeStopAfterSlides(value)
            : 1;

    public static PresentationMediaPlaybackInputPlan BuildPlaybackInputPlan(
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped,
        bool rewindAfterPlaying,
        bool playFullScreen,
        int stopAfterSlides)
    {
        var normalizedStopAfterSlides = NormalizeStopAfterSlides(stopAfterSlides);
        return new(
            startMode,
            GetPlaybackStartModeIndex(startMode),
            loop,
            showWhenStopped,
            rewindAfterPlaying,
            playFullScreen,
            normalizedStopAfterSlides,
            normalizedStopAfterSlides.ToString(CultureInfo.CurrentCulture));
    }

    public static double ParseTiming(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
        && double.IsFinite(value)
            ? Math.Max(0, value)
            : 0;

    public static string FormatTiming(double value) =>
        Math.Max(0, value).ToString("0.####", CultureInfo.CurrentCulture);

    public static PresentationMediaTimingInputPlan BuildTimingInputPlan(
        double trimStartMilliseconds,
        double trimEndMilliseconds,
        double fadeInMilliseconds,
        double fadeOutMilliseconds) =>
        new(
            FormatTiming(trimStartMilliseconds),
            FormatTiming(trimEndMilliseconds),
            FormatTiming(fadeInMilliseconds),
            FormatTiming(fadeOutMilliseconds));

    public static PresentationMediaTimingMutationPlan BuildTimingMutationPlan(
        string? trimStartText,
        string? trimEndText,
        string? fadeInText,
        string? fadeOutText) =>
        new(
            ParseTiming(trimStartText),
            ParseTiming(trimEndText),
            ParseTiming(fadeInText),
            ParseTiming(fadeOutText));

    public static PresentationMediaBookmarkInputPlan BuildBookmarkInputPlan(
        string? name,
        double timeMilliseconds) =>
        new(name ?? string.Empty, FormatTiming(timeMilliseconds));

    public static PresentationMediaBookmarkMutationPlan BuildBookmarkMutationPlan(
        MediaInfo? media,
        PresentationMediaBookmarkMutationIntentKind intent,
        int? selectedBookmarkIndex,
        string? name,
        string? timeText)
    {
        if (media is null)
            return new(false, [], null);

        var normalizedName = (name ?? string.Empty).Trim();
        var bookmarks = CloneBookmarks(media.Bookmarks);
        switch (intent)
        {
            case PresentationMediaBookmarkMutationIntentKind.Create when normalizedName.Length > 0:
                bookmarks.Add(new MediaBookmarkInfo
                {
                    Name = normalizedName,
                    TimeMilliseconds = ParseTiming(timeText)
                });
                return new(true, bookmarks, bookmarks.Count - 1);

            case PresentationMediaBookmarkMutationIntentKind.Replace
                when normalizedName.Length > 0
                     && selectedBookmarkIndex is int replaceIndex
                     && replaceIndex >= 0
                     && replaceIndex < bookmarks.Count:
                bookmarks[replaceIndex] = new MediaBookmarkInfo
                {
                    Name = normalizedName,
                    TimeMilliseconds = ParseTiming(timeText)
                };
                return new(true, bookmarks, replaceIndex);

            case PresentationMediaBookmarkMutationIntentKind.Delete
                when selectedBookmarkIndex is int deleteIndex
                     && deleteIndex >= 0
                     && deleteIndex < bookmarks.Count:
                bookmarks.RemoveAt(deleteIndex);
                return new(
                    true,
                    bookmarks,
                    bookmarks.Count == 0 ? null : Math.Min(deleteIndex, bookmarks.Count - 1));

            default:
                return new(false, bookmarks, NormalizeBookmarkSelection(media, selectedBookmarkIndex));
        }
    }

    private static int? NormalizeBookmarkSelection(MediaInfo? media, int? selectedBookmarkIndex)
    {
        if (media is null || media.Bookmarks.Count == 0)
            return null;
        return selectedBookmarkIndex is int index && index >= 0 && index < media.Bookmarks.Count
            ? index
            : 0;
    }

    private static int? NormalizeCaptionSelectionAfterMutation(
        MediaInfo? media,
        PresentationMediaCaptionAuthoringIntentKind intent,
        int changedTrackIndex)
    {
        if (media is null || media.CaptionTracks.Count == 0)
            return null;
        return intent == PresentationMediaCaptionAuthoringIntentKind.Delete
            ? Math.Min(changedTrackIndex, media.CaptionTracks.Count - 1)
            : changedTrackIndex;
    }

    private static List<MediaBookmarkInfo> CloneBookmarks(IEnumerable<MediaBookmarkInfo> bookmarks) =>
        bookmarks.Select(bookmark => new MediaBookmarkInfo
        {
            Name = bookmark.Name,
            TimeMilliseconds = bookmark.TimeMilliseconds
        }).ToList();

    private void CompleteMutation()
    {
        _callbacks.MarkDirty();
        _callbacks.RefreshReviewWorkflowPlans();
        _callbacks.UpdateHost();
    }
}
