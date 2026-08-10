using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationMediaCaptionHostSnapshot(
    string? Label,
    string? Language,
    string? Source,
    string? TranscriptText);

public sealed record PresentationMediaVolumeHostSnapshot(double VolumePercent)
{
    public int NormalizedVolumePercent =>
        PresentationMediaPaneSession.NormalizeVolumePercent(VolumePercent);
}

public sealed record PresentationMediaPlaybackHostSnapshot(
    int StartModeIndex,
    bool Loop,
    bool ShowWhenStopped,
    bool RewindAfterPlaying,
    bool PlayFullScreen,
    string? StopAfterSlidesText)
{
    public MediaPlaybackStartMode StartMode =>
        PresentationMediaPaneSession.GetPlaybackStartMode(StartModeIndex);

    public int StopAfterSlides =>
        PresentationMediaPaneSession.ParseStopAfterSlides(StopAfterSlidesText);
}

public sealed record PresentationMediaTimingHostSnapshot(
    string? TrimStartText,
    string? TrimEndText,
    string? FadeInText,
    string? FadeOutText)
{
    public PresentationMediaTimingMutationPlan MutationPlan =>
        PresentationMediaPaneSession.BuildTimingMutationPlan(
            TrimStartText,
            TrimEndText,
            FadeInText,
            FadeOutText);
}

public sealed record PresentationMediaBookmarkHostSnapshot(
    string? Name,
    string? TimeText)
{
    public double TimeMilliseconds => PresentationMediaPaneSession.ParseTiming(TimeText);
}

public sealed record PresentationMediaVolumeInputPlan(int VolumePercent);

public sealed record PresentationMediaPaneHostRenderPlan(
    PresentationMediaCaptionAuthoringPanePlan Caption,
    PresentationMediaPaneProjection Media,
    PresentationMediaPlaybackInputPlan Playback);

/// <summary>
/// Coordinates media-pane transitions shared by the WPF and Avalonia hosts. Hosts retain only
/// native control snapshots, event wiring, and application of renderer-ready plans.
/// </summary>
public sealed class PresentationMediaPaneHostCoordinator
{
    private readonly PresentationMediaPaneSession _session;

    public PresentationMediaPaneHostCoordinator(PresentationMediaPaneSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public PresentationMediaCaptionAuthoringPanePlan? LastCaptionAuthoringPanePlan =>
        _session.LastCaptionAuthoringPanePlan;

    public PresentationMediaCaptionAuthoringMutationPlan? LastCaptionAuthoringMutationPlan =>
        _session.LastCaptionAuthoringMutationPlan;

    public PresentationMediaCaptionTrackMutationResult? LastCaptionTrackMutationResult =>
        _session.LastCaptionTrackMutationResult;

    public int? SelectedCaptionTrackIndex => _session.SelectedCaptionTrackIndex;

    public void SelectCaptionTrack(int? trackIndex) => _session.SelectCaptionTrack(trackIndex);

    public void SelectBookmark(int? bookmarkIndex) => _session.SelectBookmark(bookmarkIndex);

    public PresentationMediaPaneHostRenderPlan BuildRenderPlan(
        PresentationMediaCaptionHostSnapshot caption)
    {
        ArgumentNullException.ThrowIfNull(caption);

        var captionPlan = _session.RefreshCaptionAuthoringPanePlan(
            caption.Label,
            caption.Language,
            caption.Source,
            caption.TranscriptText);
        var mediaPlan = _session.BuildProjection();
        return new(
            captionPlan,
            mediaPlan,
            PresentationMediaPaneSession.BuildPlaybackInputPlan(
                mediaPlan.PlaybackStartMode,
                mediaPlan.Loop,
                mediaPlan.ShowWhenStopped,
                mediaPlan.RewindAfterPlaying,
                mediaPlan.PlayFullScreen,
                mediaPlan.StopAfterSlides));
    }

    public PresentationMediaPaneProjection BuildProjection() => _session.BuildProjection();

    public PresentationMediaCaptionTrackMutationResult ApplyCaption(
        PresentationMediaCaptionAuthoringIntentKind intent,
        PresentationMediaCaptionHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session.ApplyCaptionAuthoring(
            intent,
            snapshot.Label,
            snapshot.Language,
            snapshot.Source,
            snapshot.TranscriptText);
    }

    public bool ApplyVolume(PresentationMediaVolumeHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session.ApplyVolume(snapshot.NormalizedVolumePercent);
    }

    public bool ApplyPlayback(PresentationMediaPlaybackHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session.ApplyPlayback(
            snapshot.StartMode,
            snapshot.Loop,
            snapshot.ShowWhenStopped,
            snapshot.RewindAfterPlaying,
            snapshot.PlayFullScreen,
            snapshot.StopAfterSlides);
    }

    public bool ApplyTiming(PresentationMediaTimingHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session.ApplyTiming(
            snapshot.TrimStartText,
            snapshot.TrimEndText,
            snapshot.FadeInText,
            snapshot.FadeOutText);
    }

    public bool ApplyBookmark(
        PresentationMediaBookmarkMutationIntentKind intent,
        PresentationMediaBookmarkHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _session.ApplyBookmark(intent, snapshot.Name, snapshot.TimeText);
    }

    public static PresentationMediaVolumeInputPlan BuildVolumeInputPlan(int volumePercent) =>
        new(PresentationMediaPaneSession.NormalizeVolumePercent(volumePercent));

    public static PresentationMediaPlaybackInputPlan BuildPlaybackInputPlan(
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped,
        bool rewindAfterPlaying,
        bool playFullScreen,
        int stopAfterSlides) =>
        PresentationMediaPaneSession.BuildPlaybackInputPlan(
            startMode,
            loop,
            showWhenStopped,
            rewindAfterPlaying,
            playFullScreen,
            stopAfterSlides);

    public static PresentationMediaTimingInputPlan BuildTimingInputPlan(
        double trimStartMilliseconds,
        double trimEndMilliseconds,
        double fadeInMilliseconds,
        double fadeOutMilliseconds) =>
        PresentationMediaPaneSession.BuildTimingInputPlan(
            trimStartMilliseconds,
            trimEndMilliseconds,
            fadeInMilliseconds,
            fadeOutMilliseconds);

    public static PresentationMediaBookmarkInputPlan BuildBookmarkInputPlan(
        string? name,
        double timeMilliseconds) =>
        PresentationMediaPaneSession.BuildBookmarkInputPlan(name, timeMilliseconds);
}
