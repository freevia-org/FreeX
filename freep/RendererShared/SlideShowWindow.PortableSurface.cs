using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

public sealed partial class SlideShowWindow
{
    /// <param name="presentation">The presentation to play.</param>
    /// <param name="startIndex">Zero-based slide index to start from.</param>
    public SlideShowWindow(Presentation presentation, int startIndex = 0)
        : this(SlideShowWindowLaunchPlan.FullPresentation(presentation, startIndex))
    {
    }

    internal SlideShowWindow(
        Presentation presentation,
        int startIndex,
        ISlideShowRecordingCaptureBackend? captureBackend)
        : this(SlideShowWindowLaunchPlan.FullPresentation(presentation, startIndex, captureBackend))
    {
    }

    /// <param name="presentation">The presentation that owns slide size, theme, and timing state.</param>
    /// <param name="playbackRoute">The ordered slide route to play.</param>
    public SlideShowWindow(Presentation presentation, SlideShowPlaybackRoute playbackRoute)
        : this(new(presentation, playbackRoute))
    {
    }

    public SlideShowWindow(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        Action<int, string?>? setSlideNotesText,
        int? preferredCaptionSlideIndex = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null)
        : this(new(
            presentation,
            playbackRoute,
            SetSlideNotesText: setSlideNotesText,
            PreferredCaptionSlideIndex: preferredCaptionSlideIndex,
            PreferredCaptionShapeId: preferredCaptionShapeId,
            PreferredCaptionTrackIndex: preferredCaptionTrackIndex))
    {
    }

    internal SlideShowWindow(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        ISlideShowRecordingCaptureBackend? captureBackend,
        Action<int, string?>? setSlideNotesText = null,
        int? preferredCaptionSlideIndex = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null)
        : this(new(
            presentation,
            playbackRoute,
            captureBackend,
            setSlideNotesText,
            preferredCaptionSlideIndex,
            preferredCaptionShapeId,
            preferredCaptionTrackIndex))
    {
    }

    private void CloseSlideShow(DateTimeOffset nowUtc)
    {
        Teardown(nowUtc);
        Close();
    }

    private void DisplayCurrentSlide(
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true) =>
        _runtime.DisplayCurrentSlide(
            animated,
            zoomTransitionDurationMs,
            zoomShowBackground);

    private SlideShowAnimationPlaybackTargetAvailability BuildAnimationTargetAvailability() =>
        _animationTargets.BuildAvailability();
}
