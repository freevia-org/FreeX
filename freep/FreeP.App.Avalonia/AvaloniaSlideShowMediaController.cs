using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia's thin slideshow media adapter. It shares source and hit-test
/// planning with WPF and consumes media clicks even while native playback is
/// deferred by the host's media backend boundary.
/// </summary>
internal sealed class AvaloniaSlideShowMediaController
{
    private IReadOnlyList<SlideShowMediaShapePlan> _active = Array.Empty<SlideShowMediaShapePlan>();

    public IReadOnlyList<SlideShowMediaShapePlan> Active => _active;

    public SlideShowMediaClickPlan LastClick { get; private set; } =
        SlideShowMediaClickPlan.NotMedia;

    public void EnterSlide(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH) =>
        _active = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, slideDipW, slideDipH, canvasW, canvasH);

    public bool TryHandleClick(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        double canvasX,
        double canvasY)
    {
        LastClick = SlideShowMediaInteractionPlanner.PlanClick(
            slide,
            slideDipW,
            slideDipH,
            canvasW,
            canvasH,
            canvasX,
            canvasY);
        return LastClick.IsHandled;
    }

    public void Teardown()
    {
        _active = Array.Empty<SlideShowMediaShapePlan>();
        LastClick = SlideShowMediaClickPlan.NotMedia;
    }
}
