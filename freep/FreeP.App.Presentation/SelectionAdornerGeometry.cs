using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public readonly record struct SelectionAdornerRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(CanvasGesturePoint point) =>
        DrawingObjectInteractionPlanner.ContainsInclusive(
            ToLayoutRect(),
            new LayoutPoint(point.X, point.Y));

    internal LayoutRect ToLayoutRect() => new(Left, Top, Width, Height);
}

public static class SelectionAdornerGeometry
{
    public const double HandleSize = 8.0;
    public const double RotateHandleRadius = 4.0;
    public const double RotateHandleOffset = 18.0;
    public const double HandleHitRadius = 8.0;

    public static IReadOnlyList<CanvasGesturePoint> GetHandleCenters(SelectionAdornerRect rect)
        => DrawingObjectInteractionPlanner.GetResizeHandleCenters(rect.ToLayoutRect())
            .Select(ToCanvasPoint)
            .ToArray();

    public static CanvasGesturePoint GetRotateHandleCenter(SelectionAdornerRect rect)
        => ToCanvasPoint(DrawingObjectInteractionPlanner.GetRotateHandleCenter(
            rect.ToLayoutRect(),
            RotateHandleOffset));

    public static CanvasGestureHandleKind HitTestHandle(
        SelectionAdornerRect selectionRect,
        CanvasGesturePoint screenPoint)
    {
        var hit = DrawingObjectInteractionPlanner.HitTestHandleCenters(
            selectionRect.ToLayoutRect(),
            new LayoutPoint(screenPoint.X, screenPoint.Y),
            HandleHitRadius,
            RotateHandleOffset);
        return ToCanvasHandle(hit);
    }

    private static CanvasGesturePoint ToCanvasPoint(LayoutPoint point) =>
        new(point.X, point.Y);

    private static CanvasGestureHandleKind ToCanvasHandle(DrawingObjectInteractionKind kind) =>
        kind switch
        {
            DrawingObjectInteractionKind.Body => CanvasGestureHandleKind.Body,
            DrawingObjectInteractionKind.ResizeN => CanvasGestureHandleKind.ResizeN,
            DrawingObjectInteractionKind.ResizeNE => CanvasGestureHandleKind.ResizeNE,
            DrawingObjectInteractionKind.ResizeE => CanvasGestureHandleKind.ResizeE,
            DrawingObjectInteractionKind.ResizeSE => CanvasGestureHandleKind.ResizeSE,
            DrawingObjectInteractionKind.ResizeS => CanvasGestureHandleKind.ResizeS,
            DrawingObjectInteractionKind.ResizeSW => CanvasGestureHandleKind.ResizeSW,
            DrawingObjectInteractionKind.ResizeW => CanvasGestureHandleKind.ResizeW,
            DrawingObjectInteractionKind.ResizeNW => CanvasGestureHandleKind.ResizeNW,
            DrawingObjectInteractionKind.Rotate => CanvasGestureHandleKind.Rotate,
            _ => CanvasGestureHandleKind.None
        };
}
