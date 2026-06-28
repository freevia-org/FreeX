namespace FreeP.App.Compositor;

public readonly record struct SelectionAdornerRect(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(CanvasGesturePoint point)
    {
        return point.X >= Left
            && point.X <= Right
            && point.Y >= Top
            && point.Y <= Bottom;
    }
}

public static class SelectionAdornerGeometry
{
    public const double HandleSize = 8.0;
    public const double RotateHandleRadius = 4.0;
    public const double RotateHandleOffset = 18.0;
    public const double HandleHitRadius = 8.0;

    private static readonly CanvasGestureHandleKind[] HandleKinds =
    [
        CanvasGestureHandleKind.ResizeN,
        CanvasGestureHandleKind.ResizeNE,
        CanvasGestureHandleKind.ResizeE,
        CanvasGestureHandleKind.ResizeSE,
        CanvasGestureHandleKind.ResizeS,
        CanvasGestureHandleKind.ResizeSW,
        CanvasGestureHandleKind.ResizeW,
        CanvasGestureHandleKind.ResizeNW
    ];

    public static IReadOnlyList<CanvasGesturePoint> GetHandleCenters(SelectionAdornerRect rect)
    {
        double mx = rect.Left + rect.Width / 2.0;
        double my = rect.Top + rect.Height / 2.0;

        return
        [
            new CanvasGesturePoint(mx, rect.Top),
            new CanvasGesturePoint(rect.Right, rect.Top),
            new CanvasGesturePoint(rect.Right, my),
            new CanvasGesturePoint(rect.Right, rect.Bottom),
            new CanvasGesturePoint(mx, rect.Bottom),
            new CanvasGesturePoint(rect.Left, rect.Bottom),
            new CanvasGesturePoint(rect.Left, my),
            new CanvasGesturePoint(rect.Left, rect.Top)
        ];
    }

    public static CanvasGesturePoint GetRotateHandleCenter(SelectionAdornerRect rect)
    {
        return new CanvasGesturePoint(
            rect.Left + rect.Width / 2.0,
            rect.Top - RotateHandleOffset);
    }

    public static CanvasGestureHandleKind HitTestHandle(
        SelectionAdornerRect selectionRect,
        CanvasGesturePoint screenPoint)
    {
        var rotateCenter = GetRotateHandleCenter(selectionRect);
        if (Distance(screenPoint, rotateCenter) <= HandleHitRadius)
        {
            return CanvasGestureHandleKind.Rotate;
        }

        var centers = GetHandleCenters(selectionRect);
        for (int i = 0; i < centers.Count; i++)
        {
            if (Distance(screenPoint, centers[i]) <= HandleHitRadius)
            {
                return HandleKinds[i];
            }
        }

        return selectionRect.Contains(screenPoint)
            ? CanvasGestureHandleKind.Body
            : CanvasGestureHandleKind.None;
    }

    private static double Distance(CanvasGesturePoint a, CanvasGesturePoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
