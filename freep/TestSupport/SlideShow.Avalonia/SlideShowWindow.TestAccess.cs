using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class SlideShowWindow
{
    internal int PresenterInkOverlayVisualCount => _inkOverlay.Children.Count;
    internal string? ActiveMediaCaptionForTest(uint shapeId) => _mediaController.CaptionTextForTest(shapeId);
    internal void RefreshMediaCaptionsForTest() => _mediaController.RefreshCaptionsForTest();
    internal SlideShowMediaClickPlan LastMediaClickForTest => _mediaController.LastClick;
    internal ValidationAccessAdapter CreateValidationAccessAdapter() => new(this);
    internal SlideShowShapeAnimationVisualFramePlan? LastAnimationFramePlanForTest =>
        _runtime.AnimationRendererSession.LastFrame;
    internal IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> LastAnimationStepFrameEvidenceForTest =>
        _runtime.AnimationRendererSession.LastStep?.Checkpoints ?? [];
    internal SlideShowAnimationStepPlaybackReadinessPlan? LastAnimationStepPlaybackReadinessPlanForTest =>
        _runtime.AnimationRendererSession.LastStep?.Readiness;
    internal SlideShowPlaybackRoute PlaybackRoute => _runtime.PlaybackRoute;
    internal int CurrentPresentationSlideIndex => _runtime.CurrentPresentationSlideIndex;
    internal Slide? RevealedHiddenSlideForTest => _runtime.RevealedHiddenSlide;
    internal SlideCanvas CanvasForTest => _slideCanvas;

    internal sealed class ValidationAccessAdapter
    {
        private readonly SlideShowWindow _owner;

        internal ValidationAccessAdapter(SlideShowWindow owner) => _owner = owner;

        internal bool IsVisible => _owner.IsVisible;
        internal int CurrentSlideIndex => _owner.Controller.CurrentSlideIndex;

        internal string Advance()
        {
            var result = _owner.ExecuteAdvance();
            return result.GetType().Name;
        }

        internal ValidationMediaPlaybackState CaptureMediaPlayback() => new(
            _owner._mediaController.Availability?.IsAvailable,
            _owner._mediaController.Availability?.FailureReason,
            _owner._mediaController.Active.Count,
            _owner._mediaController.LastFailure is not null);

        internal void Close() => _owner.Close();
    }

    internal sealed record ValidationMediaPlaybackState(
        bool? IsAvailable,
        string? FailureReason,
        int ActiveMediaCount,
        bool HasFailure);
}
