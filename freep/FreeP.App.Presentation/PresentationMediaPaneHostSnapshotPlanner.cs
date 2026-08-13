namespace FreeP.App.Compositor;

public static class PresentationMediaPaneHostSnapshotPlanner
{
    public const double DefaultVolumePercent = PresentationMediaPaneSession.DefaultVolumePercent;

    public static PresentationMediaCaptionHostSnapshot CaptureCaption(
        string? label,
        string? language,
        string? source,
        string? transcriptText) =>
        new(label, language, source, transcriptText);

    public static PresentationMediaVolumeHostSnapshot CaptureVolume(double? volumePercent) =>
        new(volumePercent ?? DefaultVolumePercent);

    public static PresentationMediaPlaybackHostSnapshot CapturePlayback(
        int? startModeIndex,
        bool? loop,
        bool? showWhenStopped,
        bool? rewindAfterPlaying,
        bool? playFullScreen,
        string? stopAfterSlidesText) =>
        new(
            startModeIndex ?? -1,
            loop == true,
            showWhenStopped != false,
            rewindAfterPlaying == true,
            playFullScreen == true,
            stopAfterSlidesText);

    public static PresentationMediaTimingHostSnapshot CaptureTiming(
        string? trimStartText,
        string? trimEndText,
        string? fadeInText,
        string? fadeOutText) =>
        new(trimStartText, trimEndText, fadeInText, fadeOutText);

    public static PresentationMediaBookmarkHostSnapshot CaptureBookmark(
        string? name,
        string? timeText) =>
        new(name, timeText);
}
