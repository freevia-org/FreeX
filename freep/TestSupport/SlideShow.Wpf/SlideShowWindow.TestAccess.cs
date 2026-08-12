using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class SlideShowWindow
{
    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest =>
        _runtime.AnimationRendererSession.LastFrame;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest =>
        _runtime.AnimationRendererSession.LastStep?.Checkpoints ?? [];
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest =>
        _runtime.AnimationRendererSession.LastStep?.Readiness;
    internal SlideShowPlaybackRoute PlaybackRoute => _runtime.PlaybackRoute;
    internal int CurrentPresentationSlideIndex => _runtime.CurrentPresentationSlideIndex;
    internal Slide? RevealedHiddenSlideForTest => _runtime.RevealedHiddenSlide;
}
