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

public interface IPresentationMediaPaneHostView
{
    bool IsPaneVisible { get; }

    PresentationMediaCaptionHostSnapshot CaptureCaption();

    PresentationMediaVolumeHostSnapshot CaptureVolume();

    PresentationMediaPlaybackHostSnapshot CapturePlayback();

    PresentationMediaTimingHostSnapshot CaptureTiming();

    PresentationMediaBookmarkHostSnapshot CaptureBookmark();

    void SetPaneVisible(bool visible);

    void SetCaptionInput(PresentationMediaCaptionHostSnapshot input);

    void SetVolumeInput(PresentationMediaVolumeInputPlan input);

    void SetPlaybackInput(PresentationMediaPlaybackInputPlan input);

    void SetTimingInput(PresentationMediaTimingInputPlan input);

    void SetBookmarkInput(PresentationMediaBookmarkInputPlan input);

    void Render(PresentationMediaPaneHostRenderPlan plan);

    void RefreshAccessibilityMetadata();
}

/// <summary>
/// Coordinates media-pane transitions shared by the WPF and Avalonia hosts. Hosts retain only
/// native control snapshots, event wiring, and application of renderer-ready plans.
/// </summary>
public sealed class PresentationMediaPaneHostCoordinator
{
    private readonly PresentationMediaPaneSession _session;
    private readonly PresentationWorkareaPaneSession _panes;
    private readonly IPresentationMediaPaneHostView _view;
    private int _viewUpdateDepth;
    private bool _isApplying;

    public PresentationMediaPaneHostCoordinator(
        PresentationMediaPaneSession session,
        PresentationWorkareaPaneSession panes,
        IPresentationMediaPaneHostView view)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _panes = panes ?? throw new ArgumentNullException(nameof(panes));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool IsUpdating => _viewUpdateDepth > 0 || _isApplying;

    public bool IsPaneVisible => _view.IsPaneVisible;

    public PresentationMediaCaptionAuthoringPanePlan? LastCaptionAuthoringPanePlan =>
        _session.LastCaptionAuthoringPanePlan;

    public PresentationMediaCaptionAuthoringMutationPlan? LastCaptionAuthoringMutationPlan =>
        _session.LastCaptionAuthoringMutationPlan;

    public PresentationMediaCaptionTrackMutationResult? LastCaptionTrackMutationResult =>
        _session.LastCaptionTrackMutationResult;

    public int? SelectedCaptionTrackIndex => _session.SelectedCaptionTrackIndex;

    public void SelectCaptionTrack(int? trackIndex)
    {
        if (IsUpdating)
            return;

        _session.SelectCaptionTrack(trackIndex);
        Refresh();
    }

    public void SelectBookmark(int? bookmarkIndex)
    {
        if (IsUpdating)
            return;

        _session.SelectBookmark(bookmarkIndex);
        Refresh();
    }

    public PresentationMediaCaptionAuthoringPanePlan Show()
    {
        _panes.Show(PresentationWorkareaPane.MediaCaption);
        var plan = BuildRenderPlan(new(null, null, null, null));
        UpdateView(() =>
        {
            _view.Render(plan);
            _view.SetPaneVisible(true);
        });
        _view.RefreshAccessibilityMetadata();
        return plan.Caption;
    }

    public void Hide()
    {
        _panes.Hide(PresentationWorkareaPane.MediaCaption);
        UpdateView(() => _view.SetPaneVisible(false));
        _view.RefreshAccessibilityMetadata();
    }

    public PresentationMediaPaneHostRenderPlan? Refresh()
    {
        if (IsUpdating || !_view.IsPaneVisible)
            return null;

        var plan = BuildRenderPlan(_view.CaptureCaption());
        UpdateView(() => _view.Render(plan));
        return plan;
    }

    public void SetCaptionInput(
        PresentationMediaCaptionHostSnapshot input,
        int? selectedTrackIndex = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureVisible();
        UpdateView(() =>
        {
            if (selectedTrackIndex.HasValue)
                _session.SelectCaptionTrack(selectedTrackIndex);
            _view.SetCaptionInput(input);
        });
        Refresh();
    }

    public void SetVolumeInput(int volumePercent)
    {
        EnsureVisible();
        var input = BuildVolumeInputPlan(volumePercent);
        UpdateView(() => _view.SetVolumeInput(input));
    }

    public void SetPlaybackInput(
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped = true,
        bool rewindAfterPlaying = false,
        bool playFullScreen = false,
        int stopAfterSlides = 1)
    {
        EnsureVisible();
        var input = BuildPlaybackInputPlan(
            startMode,
            loop,
            showWhenStopped,
            rewindAfterPlaying,
            playFullScreen,
            stopAfterSlides);
        UpdateView(() => _view.SetPlaybackInput(input));
    }

    public void SetTimingInput(
        double trimStartMilliseconds,
        double trimEndMilliseconds,
        double fadeInMilliseconds,
        double fadeOutMilliseconds)
    {
        EnsureVisible();
        var input = BuildTimingInputPlan(
            trimStartMilliseconds,
            trimEndMilliseconds,
            fadeInMilliseconds,
            fadeOutMilliseconds);
        UpdateView(() => _view.SetTimingInput(input));
    }

    public void SetBookmarkInput(string? name, double timeMilliseconds)
    {
        EnsureVisible();
        var input = BuildBookmarkInputPlan(name, timeMilliseconds);
        UpdateView(() => _view.SetBookmarkInput(input));
    }

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
        PresentationMediaCaptionAuthoringIntentKind intent) =>
        ApplyAndRefresh(() =>
        {
            var snapshot = _view.CaptureCaption();
            return _session.ApplyCaptionAuthoring(
                intent,
                snapshot.Label,
                snapshot.Language,
                snapshot.Source,
                snapshot.TranscriptText);
        });

    public bool ApplyVolume() => ApplyAndRefresh(() =>
        _session.ApplyVolume(_view.CaptureVolume().NormalizedVolumePercent));

    public bool ApplyPlayback() => ApplyAndRefresh(() =>
    {
        var snapshot = _view.CapturePlayback();
        return _session.ApplyPlayback(
            snapshot.StartMode,
            snapshot.Loop,
            snapshot.ShowWhenStopped,
            snapshot.RewindAfterPlaying,
            snapshot.PlayFullScreen,
            snapshot.StopAfterSlides);
    });

    public bool ApplyTiming() => ApplyAndRefresh(() =>
    {
        var snapshot = _view.CaptureTiming();
        return _session.ApplyTiming(
            snapshot.TrimStartText,
            snapshot.TrimEndText,
            snapshot.FadeInText,
            snapshot.FadeOutText);
    });

    public bool ApplyBookmark(PresentationMediaBookmarkMutationIntentKind intent) =>
        ApplyAndRefresh(() =>
        {
            var snapshot = _view.CaptureBookmark();
            return _session.ApplyBookmark(intent, snapshot.Name, snapshot.TimeText);
        });

    public PresentationMediaTimingHostSnapshot CaptureTiming() => _view.CaptureTiming();

    public PresentationMediaBookmarkHostSnapshot CaptureBookmark() => _view.CaptureBookmark();

    public int BookmarkCount => _session.BuildProjection().Bookmarks.Count;

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

    private void EnsureVisible()
    {
        if (!_view.IsPaneVisible)
            Show();
    }

    private T ApplyAndRefresh<T>(Func<T> apply)
    {
        if (_isApplying)
            throw new InvalidOperationException("A media-pane mutation is already in progress.");

        _isApplying = true;
        T result;
        try
        {
            result = apply();
        }
        finally
        {
            _isApplying = false;
        }

        Refresh();
        return result;
    }

    private void UpdateView(Action update)
    {
        _viewUpdateDepth++;
        try
        {
            update();
        }
        finally
        {
            _viewUpdateDepth--;
        }
    }
}
