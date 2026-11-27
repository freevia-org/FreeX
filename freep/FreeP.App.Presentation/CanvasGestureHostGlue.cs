using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral glue shared by the WPF and Avalonia canvas gesture handlers. It owns
/// everything that is not native event, cursor, or transform conversion: building the press
/// request and dispatching its action, OLE double-click activation, projecting and applying the
/// live gesture preview, building the selection-adorner projection, and deciding which cursor
/// kind the pointer position maps to. Each native handler keeps only its toolkit event wiring and
/// maps <see cref="CanvasGestureCursorKind"/> onto its own cursor type.
/// </summary>
public sealed class CanvasGestureHostGlue
{
    private readonly ICanvasGestureEditingSession _editor;
    private readonly CanvasGestureRouter _router;
    private readonly ICanvasGesturePreviewSurface _previewSurface;
    private readonly Action<ChartPointHit>? _onChartPointDoubleClick;
    private readonly Func<SlideShape, bool>? _tryOpenOleInPlace;
    private readonly Func<OleObjectInfo?, bool>? _tryActivateOleExternally;

    public CanvasGestureHostGlue(
        ICanvasGestureEditingSession editor,
        CanvasGestureRouter router,
        ICanvasGesturePreviewSurface previewSurface,
        Action<ChartPointHit>? onChartPointDoubleClick,
        Func<SlideShape, bool>? tryOpenOleInPlace,
        Func<OleObjectInfo?, bool>? tryActivateOleExternally)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _previewSurface = previewSurface ?? throw new ArgumentNullException(nameof(previewSurface));
        _onChartPointDoubleClick = onChartPointDoubleClick;
        _tryOpenOleInPlace = tryOpenOleInPlace;
        _tryActivateOleExternally = tryActivateOleExternally;
    }

    public static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape);

    public CanvasGesturePressRequest CreatePressRequest(
        CanvasGesturePoint screenPoint,
        SlideTransformCore transform,
        Slide slide,
        int clickCount,
        Free.Shared.Shell.FreeKeyModifiers modifiers)
    {
        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            _editor.Presentation!,
            _editor.SelectedShapeIds,
            transform,
            _router.EditPointsEnabled);
        return CanvasGestureInteractionPlanner.BuildPressRequest(
            screenPoint,
            transform,
            projection,
            clickCount,
            modifiers,
            _router.EditPointsEnabled,
            _onChartPointDoubleClick is not null);
    }

    public void ApplyPressAction(CanvasGesturePressPlan plan)
        => CanvasGesturePressActionDispatcher.Dispatch(
            plan,
            _onChartPointDoubleClick,
            shape => HandleOleDoubleClick(shape));

    public bool HandleOleDoubleClick(SlideShape shape) =>
        OleActivationCoordinator.TryActivate(
            shape,
            _tryOpenOleInPlace,
            _tryActivateOleExternally);

    public void ApplyPreviewPlan(CanvasGesturePreviewPlan plan, SlideTransformCore transform)
    {
        var visual = CanvasGesturePreviewProjector.Project(
            plan,
            _editor.CurrentSlide,
            _editor.Presentation,
            transform);
        CanvasGesturePreviewDispatcher.Apply(visual, transform, _previewSurface);
    }

    public SelectionAdornerProjectionPlan BuildAdornerProjection(SlideTransformCore transform)
    {
        var slide = _editor.CurrentSlide;
        return slide is null || _editor.Presentation is not { } presentation
            ? SelectionAdornerProjectionPlan.Empty
            : SelectionAdornerGeometry.BuildProjection(
                slide,
                presentation,
                _editor.SelectedShapeIds,
                transform,
                _router.EditPointsEnabled);
    }

    public CanvasGestureCursorKind PlanCursor(CanvasGesturePoint screenPoint, SlideTransformCore transform)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return CanvasGestureCursorKind.Default;

        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            _editor.Presentation,
            _editor.SelectedShapeIds,
            transform,
            _router.EditPointsEnabled);
        return CanvasGestureInteractionPlanner.PlanCursor(
            slide,
            _editor.Presentation,
            _editor.SelectedShapeIds,
            transform,
            projection,
            screenPoint,
            _router.EditPointsEnabled);
    }
}
