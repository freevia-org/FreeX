using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Test-side positional convenience over <see cref="AvaloniaSlideShowMediaController.EnterSlide(SlideShowMediaNativeEntryRequest, int?)"/>.
/// Production code enters a slide only through <see cref="SlideShowMediaNativeEntryRequest"/>.
/// </summary>
internal static class SlideShowMediaControllerTestExtensions
{
    public static void EnterSlide(
        this AvaloniaSlideShowMediaController controller,
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? captionTracks = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null,
        int? captionSlideIndex = null,
        int? preferredCaptionSlideIndex = null,
        bool showMediaControls = true,
        bool showNarration = true,
        int? presentationSlideIndex = null) =>
        controller.EnterSlide(
            new SlideShowMediaNativeEntryRequest(
                slide, slideDipW, slideDipH, canvasW, canvasH, captionTracks,
                preferredCaptionShapeId, preferredCaptionTrackIndex, captionSlideIndex,
                preferredCaptionSlideIndex, showMediaControls, showNarration),
            presentationSlideIndex);
}
