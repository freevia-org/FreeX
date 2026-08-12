namespace FreeP.App.Compositor;

/// <summary>
/// Native surface operations required to realize a portable gesture preview.
/// </summary>
public interface ICanvasGesturePreviewSurface
{
    void UpdatePreview(SlideScreenRect? bounds, double rotationDegrees = 0);

    void UpdateSnapGuides(
        IReadOnlyList<SnapGuideLine>? guides,
        SlideTransformCore transform);

    void UpdateTransformPreview(CanvasMultiTransformPlan plan);

    void UpdateGeometryPreview(string handleName, CanvasGesturePoint screenPoint);

    void UpdateMarquee(SlideScreenRect bounds);
}

/// <summary>
/// Owns the renderer-neutral routing from a projected gesture preview to native surface calls.
/// </summary>
public static class CanvasGesturePreviewDispatcher
{
    public static void Apply(
        CanvasGestureVisualPreviewPlan visual,
        SlideTransformCore transform,
        ICanvasGesturePreviewSurface surface)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(surface);

        switch (visual.Kind)
        {
            case CanvasGestureKind.Move:
                surface.UpdatePreview(visual.PreviewBounds);
                surface.UpdateSnapGuides(
                    visual.SnapGuides.Count > 0 ? visual.SnapGuides : null,
                    transform);
                break;

            case CanvasGestureKind.Resize when visual.MultiTransform is { } multiResize:
                surface.UpdateTransformPreview(multiResize);
                break;

            case CanvasGestureKind.Resize when visual.PreviewBounds is { } resizeBounds:
                surface.UpdatePreview(resizeBounds);
                break;

            case CanvasGestureKind.Rotate when visual.MultiTransform is { } multiRotate:
                surface.UpdateTransformPreview(multiRotate);
                break;

            case CanvasGestureKind.Rotate when
                visual.PreviewBounds is { } rotationBounds &&
                visual.RotationDegrees is { } angle:
                surface.UpdatePreview(rotationBounds, angle);
                break;

            case CanvasGestureKind.GeometryAdjustment when
                visual.GeometryHandleName is { } handleName &&
                visual.GeometryScreenPoint is { } geometryScreen:
                surface.UpdateGeometryPreview(handleName, geometryScreen);
                break;

            case CanvasGestureKind.Marquee when visual.PreviewBounds is { } marquee:
                surface.UpdateMarquee(marquee);
                break;
        }
    }
}
