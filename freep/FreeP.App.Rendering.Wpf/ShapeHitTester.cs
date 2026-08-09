using FreeP.Core.Model;
using ShapeBoundsDip = FreeP.App.Compositor.ShapeBoundsDip;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// WPF namespace compatibility facade for the shared compositor shape hit tester.
/// </summary>
public static class ShapeHitTester
{
    public static uint? HitTest(
        Slide slide,
        Presentation presentation,
        double slidePtX,
        double slidePtY) =>
        FreeP.App.Compositor.ShapeHitTester.HitTest(
            slide,
            presentation,
            slidePtX,
            slidePtY);

    public static IReadOnlyList<uint> MarqueeHitTest(
        Slide slide,
        Presentation presentation,
        double left,
        double top,
        double right,
        double bottom) =>
        FreeP.App.Compositor.ShapeHitTester.MarqueeHitTest(
            slide,
            presentation,
            left,
            top,
            right,
            bottom);

    public static SlideShape? FindShape(Slide slide, uint shapeId) =>
        FreeP.App.Compositor.ShapeHitTester.FindShape(slide, shapeId);

    public static ShapeBoundsDip? GetShapeBoundsDip(
        Slide slide,
        Presentation presentation,
        uint shapeId) =>
        FreeP.App.Compositor.ShapeHitTester.GetShapeBoundsDip(slide, presentation, shapeId);

    public static ShapeBoundsDip GetShapeBoundsDip(
        SlideShape shape,
        Slide slide,
        Presentation presentation) =>
        FreeP.App.Compositor.ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation);
}
