using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum CanvasGestureCursorKind
{
    Default,
    Pointer,
    Move,
    Rotate,
    ResizeNorthSouth,
    ResizeWestEast,
    ResizeNorthEastSouthWest,
    ResizeNorthWestSouthEast
}

public static class CanvasGestureInteractionPlanner
{
    public static CanvasGesturePressRequest BuildPressRequest(
        CanvasGesturePoint screenPoint,
        SlideTransformCore transform,
        SelectionAdornerProjectionPlan selection,
        int clickCount,
        CanvasGestureModifiers modifiers,
        bool editPointsEnabled,
        bool canNotifyChartPointDoubleClick)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(selection);

        var selectionHandle = CanvasGestureHandleKind.None;
        string? geometryHandle = null;
        bool hasSingleSelectionFrame = false;

        if (selection.Selections.Count > 1 && selection.SelectionBounds is { } groupBounds)
        {
            selectionHandle = SelectionAdornerGeometry.HitTestHandle(groupBounds, screenPoint);
        }
        else if (selection.Selections.Count == 1)
        {
            hasSingleSelectionFrame = true;
            selectionHandle = SelectionAdornerGeometry.HitTestHandle(
                selection.Selections[0].ScreenRect,
                screenPoint);
            if (editPointsEnabled)
            {
                geometryHandle = SelectionAdornerGeometry.HitTestGeometryHandle(
                    selection.GeometryHandles,
                    screenPoint);
            }
        }

        var slidePoint = transform.ScreenToSlide(screenPoint.X, screenPoint.Y);
        return new CanvasGesturePressRequest(
            screenPoint,
            new CanvasGesturePoint(slidePoint.X, slidePoint.Y),
            clickCount,
            modifiers,
            selectionHandle,
            geometryHandle,
            hasSingleSelectionFrame,
            canNotifyChartPointDoubleClick);
    }

    public static CanvasGestureCursorKind PlanCursor(
        Slide slide,
        Presentation presentation,
        IReadOnlyList<uint> selectedShapeIds,
        SlideTransformCore transform,
        SelectionAdornerProjectionPlan selection,
        CanvasGesturePoint screenPoint,
        bool editPointsEnabled)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Selections.Count > 1 && selection.SelectionBounds is { } groupBounds)
        {
            var groupHandle = SelectionAdornerGeometry.HitTestHandle(groupBounds, screenPoint);
            if (groupHandle != CanvasGestureHandleKind.None)
                return CursorForHandle(groupHandle);
        }

        if (selection.Selections.Count == 1)
        {
            if (editPointsEnabled &&
                SelectionAdornerGeometry.HitTestGeometryHandle(selection.GeometryHandles, screenPoint) is not null)
            {
                return CanvasGestureCursorKind.Pointer;
            }

            return CursorForHandle(SelectionAdornerGeometry.HitTestHandle(
                selection.Selections[0].ScreenRect,
                screenPoint));
        }

        var slidePoint = transform.ScreenToSlide(screenPoint.X, screenPoint.Y);
        var gesturePoint = new CanvasGesturePoint(slidePoint.X, slidePoint.Y);
        if (CanvasGesturePlanner.HitSelectedShapeBody(
            slide,
            presentation,
            selectedShapeIds,
            gesturePoint,
            includeNestedShapes: false))
        {
            return CanvasGestureCursorKind.Move;
        }

        return ShapeHitTester.HitTest(slide, presentation, slidePoint.X, slidePoint.Y).HasValue
            ? CanvasGestureCursorKind.Pointer
            : CanvasGestureCursorKind.Default;
    }

    private static CanvasGestureCursorKind CursorForHandle(CanvasGestureHandleKind handle) => handle switch
    {
        CanvasGestureHandleKind.Rotate => CanvasGestureCursorKind.Rotate,
        CanvasGestureHandleKind.ResizeN or CanvasGestureHandleKind.ResizeS =>
            CanvasGestureCursorKind.ResizeNorthSouth,
        CanvasGestureHandleKind.ResizeE or CanvasGestureHandleKind.ResizeW =>
            CanvasGestureCursorKind.ResizeWestEast,
        CanvasGestureHandleKind.ResizeNE or CanvasGestureHandleKind.ResizeSW =>
            CanvasGestureCursorKind.ResizeNorthEastSouthWest,
        CanvasGestureHandleKind.ResizeNW or CanvasGestureHandleKind.ResizeSE =>
            CanvasGestureCursorKind.ResizeNorthWestSouthEast,
        CanvasGestureHandleKind.Body => CanvasGestureCursorKind.Move,
        _ => CanvasGestureCursorKind.Default
    };
}
