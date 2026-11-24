using FreeP.App.Compositor;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

/// <summary>
/// The toolkit-neutral half of the slide-show renderer contracts: the display-renderer timer and
/// forwarding members and the transition-playback forwards onto each shell's native
/// <c>Play*Transition</c> routines. Both shells carried these as identical text.
/// </summary>
public sealed partial class SlideShowWindow
{
    void ISlideShowDisplayRenderer.StopAutoAdvanceTimer() => _autoAdvanceTimer.Stop();

    void ISlideShowDisplayRenderer.StartAutoAdvanceTimer(
        TimeSpan interval,
        long displayVersion)
    {
        _autoAdvanceDisplayVersion = displayVersion;
        _autoAdvanceTimer.Interval = interval;
        _autoAdvanceTimer.Start();
    }

    void ISlideShowDisplayRenderer.StopKioskRestartTimer() => _kioskRestartTimer.Stop();

    void ISlideShowDisplayRenderer.StartKioskRestartTimer(TimeSpan interval)
    {
        _kioskRestartTimer.Interval = interval;
        _kioskRestartTimer.Start();
    }

    void ISlideShowDisplayRenderer.RequestAutoAdvance() =>
        _runtime.ExecuteAdvance(stopAutoAdvance: true);

    void ISlideShowDisplayRenderer.RequestKioskRestart() => _runtime.RestartKioskShow();

    void ISlideShowDisplayRenderer.RefreshInkOverlay() => RefreshInkOverlay();

    void ISlideShowDisplayRenderer.PrepareAnimationOverlay(Slide slide) =>
        PrepareAnimationOverlay(slide);

    void ISlideShowDisplayRenderer.PlayTransition(Slide slide, SlideTransition transition) =>
        PlayTransition(slide, transition);

    void ISlideShowDisplayRenderer.ShowSlideInstant(Slide slide) => ShowSlideInstant(slide);

    private SlideShowSlideMetrics CurrentSlideMetrics() => new(_slideDipW, _slideDipH);

    private void PlayTransition(Slide slide, SlideTransition t)
    {
        SlideShowTransitionPlaybackCoordinator.Play(_presentation, slide, t, this);
    }

    void ISlideShowTransitionPlaybackRenderer.ShowInstant(Slide slide, SlideShowTransitionPlaybackPlan plan) => ShowSlideInstant(slide);
    void ISlideShowTransitionPlaybackRenderer.PlayFade(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFadeTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayFlash(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayFlashTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayDissolve(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayDissolveTransition(slide, plan.DurationMs);
    void ISlideShowTransitionPlaybackRenderer.PlayBox(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayBoxTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayReveal(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRevealTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayUncover(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayUncoverTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayCover(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayCoverTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlaySplit(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlaySplitTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayBlinds(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayBlindsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayRandomBars(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayRandomBarsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayStrips(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayStripsTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayWheel(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayWheelTransition(slide, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayZoom(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayZoomTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayPan(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayPanTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayGallery(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayGalleryTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayConveyor(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayConveyorTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayWindow(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan) => PlayWindowTransition(slide, plan, transformPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayMorph(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayMorphTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPerspective(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPerspectiveTransitionPlan perspectivePlan) =>
        PlayPerspectiveTransition(slide, plan, perspectivePlan);
    void ISlideShowTransitionPlaybackRenderer.PlayPolygonClip(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPolygonClipTransitionPlan polygonPlan) =>
        PlayPolygonClipTransition(slide, plan, polygonPlan);
    void ISlideShowTransitionPlaybackRenderer.PlayPageCurl(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPageCurlTransition(slide, plan.EffectiveTransition, plan);
    void ISlideShowTransitionPlaybackRenderer.PlayPush(Slide slide, SlideShowTransitionPlaybackPlan plan) => PlayPushTransition(slide, plan);
}
