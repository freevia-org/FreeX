namespace FreeP.App.Compositor;

/// <summary>
/// Applies the shared ink overlay plan in a stable order while native hosts create
/// framework drawing primitives and cursors.
/// </summary>
public static class SlideShowInkNativeProjectionSession
{
    public static void Apply(
        SlideShowInkExecutionState state,
        double canvasWidth,
        double canvasHeight,
        SlideShowSlideMetrics slideMetrics,
        Action clear,
        Action<double, double> setBounds,
        Action<SlideShowInkOverlayPrimitive> addStroke,
        Action<SlideShowInkOverlayPrimitive> addLaser)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(clear);
        ArgumentNullException.ThrowIfNull(setBounds);
        ArgumentNullException.ThrowIfNull(addStroke);
        ArgumentNullException.ThrowIfNull(addLaser);

        clear();
        var plan = SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(
            state,
            canvasWidth,
            canvasHeight,
            slideMetrics);
        setBounds(canvasWidth, canvasHeight);
        foreach (var primitive in plan.Primitives)
        {
            if (primitive.Kind == SlideShowInkOverlayPrimitiveKind.StrokePath)
            {
                addStroke(primitive);
            }
            else if (primitive.Kind == SlideShowInkOverlayPrimitiveKind.LaserDot)
            {
                addLaser(primitive);
            }
        }
    }
}
