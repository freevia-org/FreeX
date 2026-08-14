namespace FreeP.App.Compositor;

public enum PresentationMediaPaneCaptionField
{
    Label,
    Language,
    Source,
    Transcript,
}

public enum PresentationMediaPaneCaptionAction
{
    Create,
    Replace,
    Delete,
    Close,
}

/// <summary>
/// Renderer-neutral access to the native controls that make up the media pane.
/// Implementations should only translate these semantic properties to WPF or Avalonia controls.
/// </summary>
public interface IPresentationMediaPaneControlSurface
{
    bool IsPaneVisible { get; set; }

    string? CaptionLabel { get; set; }
    string? CaptionLanguage { get; set; }
    string? CaptionSource { get; set; }
    string? CaptionTranscript { get; set; }
    double? VolumePercent { get; set; }
    int? PlaybackStartModeIndex { get; set; }
    bool? Loop { get; set; }
    bool? ShowWhenStopped { get; set; }
    bool? RewindAfterPlaying { get; set; }
    bool? PlayFullScreen { get; set; }
    string? StopAfterSlides { get; set; }
    string? TrimStart { get; set; }
    string? TrimEnd { get; set; }
    string? FadeIn { get; set; }
    string? FadeOut { get; set; }
    string? BookmarkName { get; set; }
    string? BookmarkTime { get; set; }

    string Heading { set; }
    string Message { set; }
    bool PlaybackStartModeEnabled { set; }
    bool LoopEnabled { set; }
    bool ShowWhenStoppedEnabled { set; }
    bool RewindAfterPlayingEnabled { set; }
    bool PlayFullScreenEnabled { set; }
    bool StopAfterSlidesEnabled { set; }
    bool PlaybackApplyEnabled { set; }
    bool VolumeEnabled { set; }
    bool VolumeApplyEnabled { set; }
    bool TimingApplyEnabled { set; }

    void RenderCaptionTracks(PresentationMediaCaptionAuthoringPanePlan plan);
    void RenderCaptionField(
        PresentationMediaPaneCaptionField field,
        PresentationMediaCaptionAuthoringFieldPlan plan);
    void RenderCaptionAction(
        PresentationMediaPaneCaptionAction action,
        PresentationMediaCaptionAuthoringActionPlan plan);
    void RenderBookmarks(PresentationMediaPaneProjection plan);
    void RefreshAccessibilityMetadata();
}

public sealed record PresentationMediaPaneControlBinding<T>(
    Func<T> Read,
    Action<T> Write);

public sealed record PresentationMediaPaneControlBindings(
    PresentationMediaPaneControlBinding<bool> PaneVisible,
    PresentationMediaPaneControlBinding<string?> CaptionLabel,
    PresentationMediaPaneControlBinding<string?> CaptionLanguage,
    PresentationMediaPaneControlBinding<string?> CaptionSource,
    PresentationMediaPaneControlBinding<string?> CaptionTranscript,
    PresentationMediaPaneControlBinding<double?> VolumePercent,
    PresentationMediaPaneControlBinding<int?> PlaybackStartModeIndex,
    PresentationMediaPaneControlBinding<bool?> Loop,
    PresentationMediaPaneControlBinding<bool?> ShowWhenStopped,
    PresentationMediaPaneControlBinding<bool?> RewindAfterPlaying,
    PresentationMediaPaneControlBinding<bool?> PlayFullScreen,
    PresentationMediaPaneControlBinding<string?> StopAfterSlides,
    PresentationMediaPaneControlBinding<string?> TrimStart,
    PresentationMediaPaneControlBinding<string?> TrimEnd,
    PresentationMediaPaneControlBinding<string?> FadeIn,
    PresentationMediaPaneControlBinding<string?> FadeOut,
    PresentationMediaPaneControlBinding<string?> BookmarkName,
    PresentationMediaPaneControlBinding<string?> BookmarkTime,
    Action<string> SetHeading,
    Action<string> SetMessage,
    Action<bool> SetPlaybackStartModeEnabled,
    Action<bool> SetLoopEnabled,
    Action<bool> SetShowWhenStoppedEnabled,
    Action<bool> SetRewindAfterPlayingEnabled,
    Action<bool> SetPlayFullScreenEnabled,
    Action<bool> SetStopAfterSlidesEnabled,
    Action<bool> SetPlaybackApplyEnabled,
    Action<bool> SetVolumeEnabled,
    Action<bool> SetVolumeApplyEnabled,
    Action<bool> SetTimingApplyEnabled,
    Action<PresentationMediaCaptionAuthoringPanePlan> RenderCaptionTracks,
    Action<PresentationMediaPaneCaptionField, PresentationMediaCaptionAuthoringFieldPlan> RenderCaptionField,
    Action<PresentationMediaPaneCaptionAction, PresentationMediaCaptionAuthoringActionPlan> RenderCaptionAction,
    Action<PresentationMediaPaneProjection> RenderBookmarks,
    Action RefreshAccessibilityMetadata);

public sealed class DelegatingPresentationMediaPaneControlSurface : IPresentationMediaPaneControlSurface
{
    private readonly PresentationMediaPaneControlBindings _bindings;

    public DelegatingPresentationMediaPaneControlSurface(PresentationMediaPaneControlBindings bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    public bool IsPaneVisible { get => _bindings.PaneVisible.Read(); set => _bindings.PaneVisible.Write(value); }
    public string? CaptionLabel { get => _bindings.CaptionLabel.Read(); set => _bindings.CaptionLabel.Write(value); }
    public string? CaptionLanguage { get => _bindings.CaptionLanguage.Read(); set => _bindings.CaptionLanguage.Write(value); }
    public string? CaptionSource { get => _bindings.CaptionSource.Read(); set => _bindings.CaptionSource.Write(value); }
    public string? CaptionTranscript { get => _bindings.CaptionTranscript.Read(); set => _bindings.CaptionTranscript.Write(value); }
    public double? VolumePercent { get => _bindings.VolumePercent.Read(); set => _bindings.VolumePercent.Write(value); }
    public int? PlaybackStartModeIndex { get => _bindings.PlaybackStartModeIndex.Read(); set => _bindings.PlaybackStartModeIndex.Write(value); }
    public bool? Loop { get => _bindings.Loop.Read(); set => _bindings.Loop.Write(value); }
    public bool? ShowWhenStopped { get => _bindings.ShowWhenStopped.Read(); set => _bindings.ShowWhenStopped.Write(value); }
    public bool? RewindAfterPlaying { get => _bindings.RewindAfterPlaying.Read(); set => _bindings.RewindAfterPlaying.Write(value); }
    public bool? PlayFullScreen { get => _bindings.PlayFullScreen.Read(); set => _bindings.PlayFullScreen.Write(value); }
    public string? StopAfterSlides { get => _bindings.StopAfterSlides.Read(); set => _bindings.StopAfterSlides.Write(value); }
    public string? TrimStart { get => _bindings.TrimStart.Read(); set => _bindings.TrimStart.Write(value); }
    public string? TrimEnd { get => _bindings.TrimEnd.Read(); set => _bindings.TrimEnd.Write(value); }
    public string? FadeIn { get => _bindings.FadeIn.Read(); set => _bindings.FadeIn.Write(value); }
    public string? FadeOut { get => _bindings.FadeOut.Read(); set => _bindings.FadeOut.Write(value); }
    public string? BookmarkName { get => _bindings.BookmarkName.Read(); set => _bindings.BookmarkName.Write(value); }
    public string? BookmarkTime { get => _bindings.BookmarkTime.Read(); set => _bindings.BookmarkTime.Write(value); }
    public string Heading { set => _bindings.SetHeading(value); }
    public string Message { set => _bindings.SetMessage(value); }
    public bool PlaybackStartModeEnabled { set => _bindings.SetPlaybackStartModeEnabled(value); }
    public bool LoopEnabled { set => _bindings.SetLoopEnabled(value); }
    public bool ShowWhenStoppedEnabled { set => _bindings.SetShowWhenStoppedEnabled(value); }
    public bool RewindAfterPlayingEnabled { set => _bindings.SetRewindAfterPlayingEnabled(value); }
    public bool PlayFullScreenEnabled { set => _bindings.SetPlayFullScreenEnabled(value); }
    public bool StopAfterSlidesEnabled { set => _bindings.SetStopAfterSlidesEnabled(value); }
    public bool PlaybackApplyEnabled { set => _bindings.SetPlaybackApplyEnabled(value); }
    public bool VolumeEnabled { set => _bindings.SetVolumeEnabled(value); }
    public bool VolumeApplyEnabled { set => _bindings.SetVolumeApplyEnabled(value); }
    public bool TimingApplyEnabled { set => _bindings.SetTimingApplyEnabled(value); }

    public void RenderCaptionTracks(PresentationMediaCaptionAuthoringPanePlan plan) =>
        _bindings.RenderCaptionTracks(plan);

    public void RenderCaptionField(
        PresentationMediaPaneCaptionField field,
        PresentationMediaCaptionAuthoringFieldPlan plan) => _bindings.RenderCaptionField(field, plan);

    public void RenderCaptionAction(
        PresentationMediaPaneCaptionAction action,
        PresentationMediaCaptionAuthoringActionPlan plan) => _bindings.RenderCaptionAction(action, plan);

    public void RenderBookmarks(PresentationMediaPaneProjection plan) => _bindings.RenderBookmarks(plan);

    public void RefreshAccessibilityMetadata() => _bindings.RefreshAccessibilityMetadata();
}

/// <summary>
/// Shared implementation of the media-pane host contract. It translates native control values
/// into portable snapshots and applies portable render plans through a thin control surface.
/// </summary>
public sealed class PresentationMediaPaneHostViewAdapter : IPresentationMediaPaneHostView
{
    private readonly IPresentationMediaPaneControlSurface _surface;

    public PresentationMediaPaneHostViewAdapter(IPresentationMediaPaneControlSurface surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public bool IsPaneVisible => _surface.IsPaneVisible;

    public PresentationMediaCaptionHostSnapshot CaptureCaption() =>
        PresentationMediaPaneHostSnapshotPlanner.CaptureCaption(
            _surface.CaptionLabel,
            _surface.CaptionLanguage,
            _surface.CaptionSource,
            _surface.CaptionTranscript);

    public PresentationMediaVolumeHostSnapshot CaptureVolume() =>
        PresentationMediaPaneHostSnapshotPlanner.CaptureVolume(_surface.VolumePercent);

    public PresentationMediaPlaybackHostSnapshot CapturePlayback() =>
        PresentationMediaPaneHostSnapshotPlanner.CapturePlayback(
            _surface.PlaybackStartModeIndex,
            _surface.Loop,
            _surface.ShowWhenStopped,
            _surface.RewindAfterPlaying,
            _surface.PlayFullScreen,
            _surface.StopAfterSlides);

    public PresentationMediaTimingHostSnapshot CaptureTiming() =>
        PresentationMediaPaneHostSnapshotPlanner.CaptureTiming(
            _surface.TrimStart,
            _surface.TrimEnd,
            _surface.FadeIn,
            _surface.FadeOut);

    public PresentationMediaBookmarkHostSnapshot CaptureBookmark() =>
        PresentationMediaPaneHostSnapshotPlanner.CaptureBookmark(
            _surface.BookmarkName,
            _surface.BookmarkTime);

    public void SetPaneVisible(bool visible) => _surface.IsPaneVisible = visible;

    public void SetCaptionInput(PresentationMediaCaptionHostSnapshot input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _surface.CaptionLabel = input.Label ?? string.Empty;
        _surface.CaptionLanguage = input.Language ?? string.Empty;
        _surface.CaptionSource = input.Source ?? string.Empty;
        _surface.CaptionTranscript = input.TranscriptText ?? string.Empty;
    }

    public void SetVolumeInput(PresentationMediaVolumeInputPlan input) =>
        _surface.VolumePercent = input.VolumePercent;

    public void SetPlaybackInput(PresentationMediaPlaybackInputPlan input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _surface.PlaybackStartModeIndex = input.StartModeIndex;
        _surface.Loop = input.Loop;
        _surface.ShowWhenStopped = input.ShowWhenStopped;
        _surface.RewindAfterPlaying = input.RewindAfterPlaying;
        _surface.PlayFullScreen = input.PlayFullScreen;
        _surface.StopAfterSlides = input.StopAfterSlidesText;
    }

    public void SetTimingInput(PresentationMediaTimingInputPlan input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _surface.TrimStart = input.TrimStartText;
        _surface.TrimEnd = input.TrimEndText;
        _surface.FadeIn = input.FadeInText;
        _surface.FadeOut = input.FadeOutText;
    }

    public void SetBookmarkInput(PresentationMediaBookmarkInputPlan input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _surface.BookmarkName = input.Name;
        _surface.BookmarkTime = input.TimeText;
    }

    public void Render(PresentationMediaPaneHostRenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var caption = plan.Caption;
        var media = plan.Media;
        var playback = plan.Playback;

        _surface.Heading = caption.Heading;
        _surface.Message = caption.Message;
        _surface.RenderCaptionTracks(caption);
        _surface.RenderCaptionField(PresentationMediaPaneCaptionField.Label, caption.Label);
        _surface.RenderCaptionField(PresentationMediaPaneCaptionField.Language, caption.Language);
        _surface.RenderCaptionField(PresentationMediaPaneCaptionField.Source, caption.Source);
        _surface.RenderCaptionField(PresentationMediaPaneCaptionField.Transcript, caption.TranscriptText);

        SetPlaybackInput(playback);
        _surface.PlaybackStartModeEnabled = media.HasMedia;
        _surface.LoopEnabled = media.HasMedia;
        _surface.ShowWhenStoppedEnabled = media.HasMedia;
        _surface.RewindAfterPlayingEnabled = media.HasMedia;
        _surface.PlayFullScreenEnabled = media.CanPlayFullScreen;
        _surface.StopAfterSlidesEnabled = media.CanStopAfterSlides;
        _surface.PlaybackApplyEnabled = media.HasMedia;
        _surface.VolumePercent = media.VolumePercent;
        _surface.VolumeEnabled = media.HasMedia;
        _surface.VolumeApplyEnabled = media.HasMedia;
        _surface.TimingApplyEnabled = media.HasMedia;
        _surface.TrimStart = media.Timing.TrimStartText;
        _surface.TrimEnd = media.Timing.TrimEndText;
        _surface.FadeIn = media.Timing.FadeInText;
        _surface.FadeOut = media.Timing.FadeOutText;
        _surface.RenderBookmarks(media);

        _surface.RenderCaptionAction(
            PresentationMediaPaneCaptionAction.Create,
            caption.GetRequiredAction(PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCreateCommandId));
        _surface.RenderCaptionAction(
            PresentationMediaPaneCaptionAction.Replace,
            caption.GetRequiredAction(PresentationMediaTranscriptPlanner.CaptionAuthoringPaneReplaceCommandId));
        _surface.RenderCaptionAction(
            PresentationMediaPaneCaptionAction.Delete,
            caption.GetRequiredAction(PresentationMediaTranscriptPlanner.CaptionAuthoringPaneDeleteCommandId));
        _surface.RenderCaptionAction(
            PresentationMediaPaneCaptionAction.Close,
            caption.GetRequiredAction(PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCloseCommandId));
    }

    public void RefreshAccessibilityMetadata() => _surface.RefreshAccessibilityMetadata();
}
