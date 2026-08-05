using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct SlideShowCanvasPointer(
    double X,
    double Y,
    double CanvasWidth,
    double CanvasHeight,
    SlideShowSlideMetrics SlideMetrics);

/// <summary>
/// Maps native slideshow pointer coordinates into portable click, hover, trigger, and ink decisions.
/// </summary>
public static class SlideShowPointerInteractionPlanner
{
    public static SlideShowPoint MapToSlide(SlideShowCanvasPointer pointer) =>
        SlideShowHostPlanner.MapCanvasPointToSlide(
            pointer.X,
            pointer.Y,
            pointer.CanvasWidth,
            pointer.CanvasHeight,
            pointer.SlideMetrics);

    public static SlideShowPointerClickIntent PlanClick(
        Slide? slide,
        Presentation presentation,
        SlideShowCanvasPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return SlideShowHostPlanner.PlanPointerClick(slide, MapToSlide(pointer), presentation);
    }

    public static Hyperlink? HitTestHyperlink(
        Slide slide,
        SlideShowCanvasPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return SlideShowHostPlanner.HitTestHyperlink(slide, MapToSlide(pointer));
    }

    public static uint? HitTestTriggerShape(
        Slide slide,
        SlideShowCanvasPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return SlideShowHostPlanner.HitTestTriggerShape(slide, MapToSlide(pointer));
    }

    public static SlideShowInkPoint MapInkPoint(SlideShowCanvasPointer pointer)
    {
        var point = MapToSlide(pointer);
        return new SlideShowInkPoint(point.X, point.Y);
    }
}
