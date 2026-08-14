using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Captures portable slideshow launch state before a native window realizes it.
/// </summary>
public sealed record SlideShowWindowLaunchPlan(
    Presentation Presentation,
    SlideShowPlaybackRoute PlaybackRoute,
    ISlideShowRecordingCaptureBackend? CaptureBackend = null,
    Action<int, string?>? SetSlideNotesText = null,
    int? PreferredCaptionSlideIndex = null,
    uint? PreferredCaptionShapeId = null,
    int? PreferredCaptionTrackIndex = null)
{
    public static SlideShowWindowLaunchPlan FullPresentation(
        Presentation presentation,
        int startIndex,
        ISlideShowRecordingCaptureBackend? captureBackend = null) =>
        new(
            presentation ?? throw new ArgumentNullException(nameof(presentation)),
            SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex),
            captureBackend);

    public SlideShowRuntimeApplication CreateRuntime(
        Func<ISlideShowRecordingCaptureBackend> createDefaultCaptureBackend,
        DateTimeOffset? startedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(Presentation);
        ArgumentNullException.ThrowIfNull(PlaybackRoute);
        ArgumentNullException.ThrowIfNull(createDefaultCaptureBackend);
        return new(
            Presentation,
            PlaybackRoute,
            startedAtUtc ?? DateTimeOffset.UtcNow,
            CaptureBackend ?? createDefaultCaptureBackend(),
            new(
                PreferredCaptionSlideIndex,
                PreferredCaptionShapeId,
                PreferredCaptionTrackIndex));
    }
}
