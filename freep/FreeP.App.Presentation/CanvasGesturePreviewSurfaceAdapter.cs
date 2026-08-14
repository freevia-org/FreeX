namespace FreeP.App.Compositor;

/// <summary>
/// Applies portable gesture-preview transitions through renderer-owned realization callbacks.
/// </summary>
public sealed class CanvasGesturePreviewSurfaceAdapter : ICanvasGesturePreviewSurface
{
    private readonly Action<SlideScreenRect?, double> _updatePreview;
    private readonly Action<IReadOnlyList<SnapGuideLine>?, SlideTransformCore> _updateSnapGuides;
    private readonly Action<CanvasMultiTransformPlan> _updateTransformPreview;
    private readonly Action<string, CanvasGesturePoint> _updateGeometryPreview;
    private readonly Action<SlideScreenRect> _updateMarquee;

    public CanvasGesturePreviewSurfaceAdapter(
        Action<SlideScreenRect?, double> updatePreview,
        Action<IReadOnlyList<SnapGuideLine>?, SlideTransformCore> updateSnapGuides,
        Action<CanvasMultiTransformPlan> updateTransformPreview,
        Action<string, CanvasGesturePoint> updateGeometryPreview,
        Action<SlideScreenRect> updateMarquee)
    {
        ArgumentNullException.ThrowIfNull(updatePreview);
        ArgumentNullException.ThrowIfNull(updateSnapGuides);
        ArgumentNullException.ThrowIfNull(updateTransformPreview);
        ArgumentNullException.ThrowIfNull(updateGeometryPreview);
        ArgumentNullException.ThrowIfNull(updateMarquee);
        _updatePreview = updatePreview;
        _updateSnapGuides = updateSnapGuides;
        _updateTransformPreview = updateTransformPreview;
        _updateGeometryPreview = updateGeometryPreview;
        _updateMarquee = updateMarquee;
    }

    public void UpdatePreview(SlideScreenRect? bounds, double rotationDegrees) =>
        _updatePreview(bounds, rotationDegrees);

    public void UpdateSnapGuides(
        IReadOnlyList<SnapGuideLine>? guides,
        SlideTransformCore transform) =>
        _updateSnapGuides(guides, transform);

    public void UpdateTransformPreview(CanvasMultiTransformPlan plan) =>
        _updateTransformPreview(plan);

    public void UpdateGeometryPreview(string handleName, CanvasGesturePoint screenPoint) =>
        _updateGeometryPreview(handleName, screenPoint);

    public void UpdateMarquee(SlideScreenRect bounds) => _updateMarquee(bounds);
}
