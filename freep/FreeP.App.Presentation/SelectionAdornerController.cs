namespace FreeP.App.Compositor;

/// <summary>
/// Translates native adorner updates into portable selection chrome state and schedules repaint.
/// </summary>
public sealed class SelectionAdornerController<TRect, TPoint>
    where TRect : struct
    where TPoint : struct
{
    private readonly Func<TRect, SelectionAdornerRect> _toRect;
    private readonly Func<TPoint, CanvasGesturePoint> _toPoint;
    private readonly Action _invalidate;

    public SelectionAdornerController(
        Func<TRect, SelectionAdornerRect> toRect,
        Func<TPoint, CanvasGesturePoint> toPoint,
        Action invalidate)
    {
        ArgumentNullException.ThrowIfNull(toRect);
        ArgumentNullException.ThrowIfNull(toPoint);
        ArgumentNullException.ThrowIfNull(invalidate);
        _toRect = toRect;
        _toPoint = toPoint;
        _invalidate = invalidate;
    }

    public SelectionAdornerState State { get; } = new();

    public void UpdateProjection(SelectionAdornerProjectionPlan projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        State.UpdateSelection(projection.Selections);
        State.UpdateGeometryHandles(projection.GeometryHandles);
        _invalidate();
    }

    public void UpdateSelection(IEnumerable<(uint Id, TRect ScreenRect)> selections)
    {
        State.UpdateSelection(selections.Select(item =>
            new SelectionAdornerSelectionPlan(item.Id, _toRect(item.ScreenRect))));
        _invalidate();
    }

    public void UpdateGeometryHandles(IEnumerable<(string Name, TPoint Position)> handles)
    {
        State.UpdateGeometryHandles(handles.Select(handle =>
            new SelectionAdornerGeometryHandlePlan(handle.Name, _toPoint(handle.Position))));
        _invalidate();
    }

    public void UpdateGeometryPreview(string? name, TPoint? position)
    {
        State.UpdateGeometryPreview(name, position is { } point ? _toPoint(point) : null);
        _invalidate();
    }

    public void UpdatePreview(TRect? screenRect, double rotationDeg = 0)
    {
        State.UpdatePreview(screenRect is { } rect ? _toRect(rect) : null, rotationDeg);
        _invalidate();
    }

    public void UpdateTransformPreview(CanvasMultiTransformPlan plan)
    {
        State.UpdateTransformPreview(plan);
        _invalidate();
    }

    public void UpdateMarquee(TRect? screenRect)
    {
        State.UpdateMarquee(screenRect is { } rect ? _toRect(rect) : null);
        _invalidate();
    }

    public void UpdateSnapGuides(
        IReadOnlyList<SnapGuideLine>? guides,
        SlideTransformCore transform)
    {
        State.UpdateSnapGuides(guides, transform);
        _invalidate();
    }
}
