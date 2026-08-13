using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public interface ISlideShowTransitionPlaybackRenderer
{
    void PlayTransitionSound(SlideTransition transition);
    void ResetTransitionVisuals();
    void ShowInstant(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayFade(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayFlash(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayDissolve(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayBox(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayReveal(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayUncover(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayCover(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlaySplit(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayBlinds(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayRandomBars(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayStrips(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayWheel(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayZoom(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan);
    void PlayPan(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan);
    void PlayGallery(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan);
    void PlayConveyor(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan);
    void PlayWindow(Slide slide, SlideShowTransitionPlaybackPlan plan, SlideShowTransformTransitionPlan transformPlan);
    void PlayMorph(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayPerspective(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPerspectiveTransitionPlan perspectivePlan);
    void PlayPolygonClip(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        SlideShowPolygonClipTransitionPlan polygonPlan);
    void PlayPageCurl(Slide slide, SlideShowTransitionPlaybackPlan plan);
    void PlayPush(Slide slide, SlideShowTransitionPlaybackPlan plan);
}

public static class SlideShowTransitionPlaybackCoordinator
{
    public static SlideShowTransitionPlaybackPlan Play(
        Presentation presentation,
        Slide slide,
        SlideTransition transition,
        ISlideShowTransitionPlaybackRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.PlayTransitionSound(transition);
        var plan = SlideShowPlaybackPlanner.PlanTransition(presentation, slide, transition);
        renderer.ResetTransitionVisuals();
        Dispatch(slide, plan, renderer);
        return plan;
    }

    public static void Dispatch(
        Slide slide,
        SlideShowTransitionPlaybackPlan plan,
        ISlideShowTransitionPlaybackRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(renderer);

        switch (plan.ActionKind)
        {
            case SlideShowTransitionPlaybackActionKind.ShowInstant:
                renderer.ShowInstant(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Fade:
                renderer.PlayFade(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Flash:
                renderer.PlayFlash(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Dissolve:
                renderer.PlayDissolve(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Box:
                renderer.PlayBox(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Reveal:
                renderer.PlayReveal(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Uncover:
                renderer.PlayUncover(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Cover:
                renderer.PlayCover(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Split:
                renderer.PlaySplit(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Blinds:
                renderer.PlayBlinds(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.RandomBars:
                renderer.PlayRandomBars(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Strips:
                renderer.PlayStrips(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Wheel:
                renderer.PlayWheel(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Zoom:
                renderer.PlayZoom(slide, plan, SlideShowTransformTransitionPlanner.Build(plan));
                return;
            case SlideShowTransitionPlaybackActionKind.Pan:
                renderer.PlayPan(slide, plan, SlideShowTransformTransitionPlanner.Build(plan));
                return;
            case SlideShowTransitionPlaybackActionKind.Gallery:
                renderer.PlayGallery(slide, plan, SlideShowTransformTransitionPlanner.Build(plan));
                return;
            case SlideShowTransitionPlaybackActionKind.Conveyor:
                renderer.PlayConveyor(slide, plan, SlideShowTransformTransitionPlanner.Build(plan));
                return;
            case SlideShowTransitionPlaybackActionKind.Window:
                renderer.PlayWindow(slide, plan, SlideShowTransformTransitionPlanner.Build(plan));
                return;
            case SlideShowTransitionPlaybackActionKind.Morph:
                renderer.PlayMorph(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Flip:
            case SlideShowTransitionPlaybackActionKind.Cube:
            case SlideShowTransitionPlaybackActionKind.Rotate:
            case SlideShowTransitionPlaybackActionKind.Switch:
            case SlideShowTransitionPlaybackActionKind.Orbit:
            case SlideShowTransitionPlaybackActionKind.Ferris:
            case SlideShowTransitionPlaybackActionKind.Flythrough:
                renderer.PlayPerspective(
                    slide,
                    plan,
                    SlideShowPerspectiveTransitionPlanner.Plan(plan.EffectiveTransition));
                return;
            case SlideShowTransitionPlaybackActionKind.Honeycomb:
            case SlideShowTransitionPlaybackActionKind.Glitter:
            case SlideShowTransitionPlaybackActionKind.Ripple:
            case SlideShowTransitionPlaybackActionKind.Wind:
            case SlideShowTransitionPlaybackActionKind.Curtains:
            case SlideShowTransitionPlaybackActionKind.Shred:
            case SlideShowTransitionPlaybackActionKind.Drape:
            case SlideShowTransitionPlaybackActionKind.Fracture:
            case SlideShowTransitionPlaybackActionKind.Crush:
            case SlideShowTransitionPlaybackActionKind.Prism:
            case SlideShowTransitionPlaybackActionKind.Prestige:
            case SlideShowTransitionPlaybackActionKind.Warp:
            case SlideShowTransitionPlaybackActionKind.Vortex:
                renderer.PlayPolygonClip(
                    slide,
                    plan,
                    SlideShowPolygonClipTransitionPlanner.Build(
                        plan.ActionKind,
                        plan.EffectiveTransition));
                return;
            case SlideShowTransitionPlaybackActionKind.PageCurl:
                renderer.PlayPageCurl(slide, plan);
                return;
            case SlideShowTransitionPlaybackActionKind.Push:
                renderer.PlayPush(slide, plan);
                return;
            default:
                renderer.PlayFade(slide, plan);
                return;
        }
    }
}
