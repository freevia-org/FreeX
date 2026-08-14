namespace FreeP.App.Compositor;

public interface ISelectionAdornerSurface<TRect, TPoint>
    where TRect : struct
    where TPoint : struct
{
    SelectionAdornerController<TRect, TPoint> Controller { get; }
}

public static class SelectionAdornerSurfaceExtensions
{
    public static void UpdateSelection<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        IEnumerable<(uint Id, TRect ScreenRect)> selections)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateSelection(selections);

    public static void UpdateProjection<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        SelectionAdornerProjectionPlan projection)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateProjection(projection);

    public static void UpdateGeometryHandles<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        IEnumerable<(string Name, TPoint Position)> handles)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateGeometryHandles(handles);

    public static void UpdateGeometryPreview<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        string? name,
        TPoint? position)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateGeometryPreview(name, position);

    public static void UpdatePreview<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        TRect? screenRect,
        double rotationDeg = 0)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdatePreview(screenRect, rotationDeg);

    public static void UpdateTransformPreview<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        CanvasMultiTransformPlan plan)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateTransformPreview(plan);

    public static void UpdateMarquee<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        TRect? screenRect)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateMarquee(screenRect);

    public static void UpdateSnapGuides<TRect, TPoint>(
        this ISelectionAdornerSurface<TRect, TPoint> surface,
        IReadOnlyList<SnapGuideLine>? guides,
        SlideTransformCore transform)
        where TRect : struct
        where TPoint : struct =>
        surface.Controller.UpdateSnapGuides(guides, transform);
}
