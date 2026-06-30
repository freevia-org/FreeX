namespace Free.Shared.Drawing;

/// <summary>
/// Framework-neutral interaction parts for a selected drawing object.
/// </summary>
public enum DrawingObjectInteractionKind
{
    None,
    Body,
    ResizeN,
    ResizeNE,
    ResizeE,
    ResizeSE,
    ResizeS,
    ResizeSW,
    ResizeW,
    ResizeNW,
    Rotate
}

public readonly record struct DrawingObjectDragTransform(
    LayoutRect Rect,
    bool CrossedHorizontally,
    bool CrossedVertically);

/// <summary>
/// Shared shape/text-box selection math: move/resize transforms, handle centers and hit-testing,
/// rotation angle calculation, and simple point rotation. Hosts adapt their own model and UI types.
/// </summary>
public static class DrawingObjectInteractionPlanner
{
    public const double DefaultMinimumObjectSize = 8.0;
    public const double DefaultRotationHandleOffset = 20.0;

    public static readonly IReadOnlyList<DrawingObjectInteractionKind> ResizeHandleOrder =
    [
        DrawingObjectInteractionKind.ResizeN,
        DrawingObjectInteractionKind.ResizeNE,
        DrawingObjectInteractionKind.ResizeE,
        DrawingObjectInteractionKind.ResizeSE,
        DrawingObjectInteractionKind.ResizeS,
        DrawingObjectInteractionKind.ResizeSW,
        DrawingObjectInteractionKind.ResizeW,
        DrawingObjectInteractionKind.ResizeNW
    ];

    public static LayoutRect CalculateDragRect(
        DrawingObjectInteractionKind dragKind,
        LayoutRect startRect,
        LayoutPoint startPosition,
        LayoutPoint currentPosition,
        double minimumSize = DefaultMinimumObjectSize) =>
        CalculateDragTransform(dragKind, startRect, startPosition, currentPosition, minimumSize).Rect;

    public static DrawingObjectDragTransform CalculateDragTransform(
        DrawingObjectInteractionKind dragKind,
        LayoutRect startRect,
        LayoutPoint startPosition,
        LayoutPoint currentPosition,
        double minimumSize = DefaultMinimumObjectSize)
    {
        var dx = currentPosition.X - startPosition.X;
        var dy = currentPosition.Y - startPosition.Y;

        var left = startRect.Left;
        var top = startRect.Top;
        var right = startRect.Right;
        var bottom = startRect.Bottom;
        var movesLeft = false;
        var movesRight = false;
        var movesTop = false;
        var movesBottom = false;

        switch (dragKind)
        {
            case DrawingObjectInteractionKind.Body:
                return new DrawingObjectDragTransform(
                    new LayoutRect(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height),
                    CrossedHorizontally: false,
                    CrossedVertically: false);
            case DrawingObjectInteractionKind.ResizeNW:
                left += dx;
                top += dy;
                movesLeft = true;
                movesTop = true;
                break;
            case DrawingObjectInteractionKind.ResizeN:
                top += dy;
                movesTop = true;
                break;
            case DrawingObjectInteractionKind.ResizeNE:
                right += dx;
                top += dy;
                movesRight = true;
                movesTop = true;
                break;
            case DrawingObjectInteractionKind.ResizeE:
                right += dx;
                movesRight = true;
                break;
            case DrawingObjectInteractionKind.ResizeSE:
                right += dx;
                bottom += dy;
                movesRight = true;
                movesBottom = true;
                break;
            case DrawingObjectInteractionKind.ResizeS:
                bottom += dy;
                movesBottom = true;
                break;
            case DrawingObjectInteractionKind.ResizeSW:
                left += dx;
                bottom += dy;
                movesLeft = true;
                movesBottom = true;
                break;
            case DrawingObjectInteractionKind.ResizeW:
                left += dx;
                movesLeft = true;
                break;
            default:
                return new DrawingObjectDragTransform(
                    startRect,
                    CrossedHorizontally: false,
                    CrossedVertically: false);
        }

        var horizontal = NormalizeAxis(left, right, movesLeft, movesRight, minimumSize);
        var vertical = NormalizeAxis(top, bottom, movesTop, movesBottom, minimumSize);
        return new DrawingObjectDragTransform(
            new LayoutRect(
                horizontal.Start,
                vertical.Start,
                horizontal.End - horizontal.Start,
                vertical.End - vertical.Start),
            horizontal.Crossed,
            vertical.Crossed);
    }

    public static double CalculateRotationDegrees(LayoutPoint center, LayoutPoint pointer)
    {
        var dx = pointer.X - center.X;
        var dy = pointer.Y - center.Y;
        if (dx == 0 && dy == 0)
            return 0;

        var degrees = Math.Atan2(dx, -dy) * (180.0 / Math.PI);
        return degrees < 0 ? degrees + 360 : degrees;
    }

    public static double CalculateRotationDelta(LayoutPoint center, LayoutPoint startGrip, LayoutPoint currentGrip)
    {
        var startDx = startGrip.X - center.X;
        var startDy = startGrip.Y - center.Y;
        var currentDx = currentGrip.X - center.X;
        var currentDy = currentGrip.Y - center.Y;
        if ((startDx == 0 && startDy == 0) || (currentDx == 0 && currentDy == 0))
            return 0;

        var startAngle = Math.Atan2(startDx, -startDy);
        var currentAngle = Math.Atan2(currentDx, -currentDy);
        var delta = (currentAngle - startAngle) * (180.0 / Math.PI);

        delta %= 360.0;
        if (delta > 180.0)
            delta -= 360.0;
        else if (delta <= -180.0)
            delta += 360.0;

        return delta;
    }

    public static DrawingObjectInteractionKind HitTestBoundingBoxHandles(
        LayoutPoint position,
        LayoutRect objectRect,
        double handleSize = DefaultMinimumObjectSize,
        double handleHitPadding = 4,
        double rotationDegrees = 0,
        double rotationHandleOffset = DefaultRotationHandleOffset)
    {
        if (IsEmpty(objectRect))
            return DrawingObjectInteractionKind.None;

        if (Math.Abs(rotationDegrees) > 0.0001)
            position = RotatePointAroundCenter(position, objectRect, -rotationDegrees);

        var pad = handleHitPadding + handleSize / 2;
        var rotateCenter = GetHandleCenter(
            DrawingObjectInteractionKind.Rotate,
            objectRect,
            rotationHandleOffset);
        if (Math.Abs(position.X - rotateCenter.X) <= pad &&
            Math.Abs(position.Y - rotateCenter.Y) <= pad)
        {
            return DrawingObjectInteractionKind.Rotate;
        }

        var nearLeft = Math.Abs(position.X - objectRect.Left) <= pad;
        var nearTop = Math.Abs(position.Y - objectRect.Top) <= pad;
        var nearRight = Math.Abs(position.X - objectRect.Right) <= pad;
        var nearBottom = Math.Abs(position.Y - objectRect.Bottom) <= pad;
        var inVertical = position.Y >= objectRect.Top - pad && position.Y <= objectRect.Bottom + pad;
        var inHorizontal = position.X >= objectRect.Left - pad && position.X <= objectRect.Right + pad;

        if (nearLeft && nearTop) return DrawingObjectInteractionKind.ResizeNW;
        if (nearRight && nearTop) return DrawingObjectInteractionKind.ResizeNE;
        if (nearRight && nearBottom) return DrawingObjectInteractionKind.ResizeSE;
        if (nearLeft && nearBottom) return DrawingObjectInteractionKind.ResizeSW;

        if (nearTop && inHorizontal) return DrawingObjectInteractionKind.ResizeN;
        if (nearBottom && inHorizontal) return DrawingObjectInteractionKind.ResizeS;
        if (nearRight && inVertical) return DrawingObjectInteractionKind.ResizeE;
        if (nearLeft && inVertical) return DrawingObjectInteractionKind.ResizeW;
        return ContainsInclusive(objectRect, position)
            ? DrawingObjectInteractionKind.Body
            : DrawingObjectInteractionKind.None;
    }

    public static DrawingObjectInteractionKind HitTestHandleCenters(
        LayoutRect selectionRect,
        LayoutPoint position,
        double hitRadius,
        double rotationHandleOffset,
        bool includeBody = true)
    {
        var radius = double.IsFinite(hitRadius) && hitRadius >= 0 ? hitRadius : 0;

        var rotateCenter = GetRotateHandleCenter(selectionRect, rotationHandleOffset);
        if (Distance(position, rotateCenter) <= radius)
            return DrawingObjectInteractionKind.Rotate;

        var centers = GetResizeHandleCenters(selectionRect);
        for (var i = 0; i < centers.Count; i++)
        {
            if (Distance(position, centers[i]) <= radius)
                return ResizeHandleOrder[i];
        }

        return includeBody && ContainsInclusive(selectionRect, position)
            ? DrawingObjectInteractionKind.Body
            : DrawingObjectInteractionKind.None;
    }

    public static IReadOnlyList<LayoutPoint> GetResizeHandleCenters(LayoutRect rect)
    {
        var mx = rect.Left + rect.Width / 2.0;
        var my = rect.Top + rect.Height / 2.0;

        return
        [
            new LayoutPoint(mx, rect.Top),
            new LayoutPoint(rect.Right, rect.Top),
            new LayoutPoint(rect.Right, my),
            new LayoutPoint(rect.Right, rect.Bottom),
            new LayoutPoint(mx, rect.Bottom),
            new LayoutPoint(rect.Left, rect.Bottom),
            new LayoutPoint(rect.Left, my),
            new LayoutPoint(rect.Left, rect.Top)
        ];
    }

    public static LayoutPoint GetRotateHandleCenter(
        LayoutRect rect,
        double rotationHandleOffset = DefaultRotationHandleOffset) =>
        new(
            rect.Left + rect.Width / 2.0,
            rect.Top - NormalizeOffset(rotationHandleOffset, DefaultRotationHandleOffset));

    public static LayoutPoint GetHandleCenter(
        DrawingObjectInteractionKind handle,
        LayoutRect rect,
        double rotationHandleOffset = DefaultRotationHandleOffset)
    {
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;
        return handle switch
        {
            DrawingObjectInteractionKind.ResizeNW => new LayoutPoint(rect.Left, rect.Top),
            DrawingObjectInteractionKind.ResizeN => new LayoutPoint(centerX, rect.Top),
            DrawingObjectInteractionKind.ResizeNE => new LayoutPoint(rect.Right, rect.Top),
            DrawingObjectInteractionKind.ResizeE => new LayoutPoint(rect.Right, centerY),
            DrawingObjectInteractionKind.ResizeSE => new LayoutPoint(rect.Right, rect.Bottom),
            DrawingObjectInteractionKind.ResizeS => new LayoutPoint(centerX, rect.Bottom),
            DrawingObjectInteractionKind.ResizeSW => new LayoutPoint(rect.Left, rect.Bottom),
            DrawingObjectInteractionKind.ResizeW => new LayoutPoint(rect.Left, centerY),
            DrawingObjectInteractionKind.Rotate => GetRotateHandleCenter(rect, rotationHandleOffset),
            _ => new LayoutPoint(centerX, centerY)
        };
    }

    public static LayoutPoint GetFixedResizeAnchor(
        DrawingObjectInteractionKind handle,
        LayoutRect rect)
    {
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;
        return handle switch
        {
            DrawingObjectInteractionKind.ResizeSE => new LayoutPoint(rect.Left, rect.Top),
            DrawingObjectInteractionKind.ResizeNW => new LayoutPoint(rect.Right, rect.Bottom),
            DrawingObjectInteractionKind.ResizeNE => new LayoutPoint(rect.Left, rect.Bottom),
            DrawingObjectInteractionKind.ResizeSW => new LayoutPoint(rect.Right, rect.Top),
            DrawingObjectInteractionKind.ResizeN => new LayoutPoint(centerX, rect.Bottom),
            DrawingObjectInteractionKind.ResizeS => new LayoutPoint(centerX, rect.Top),
            DrawingObjectInteractionKind.ResizeW => new LayoutPoint(rect.Right, centerY),
            DrawingObjectInteractionKind.ResizeE => new LayoutPoint(rect.Left, centerY),
            _ => new LayoutPoint(rect.Left, rect.Top)
        };
    }

    public static LayoutPoint RotatePointAroundCenter(
        LayoutPoint point,
        LayoutRect objectRect,
        double rotationDegrees)
    {
        if (IsEmpty(objectRect) || Math.Abs(rotationDegrees) <= 0.0001)
            return point;

        return RotatePoint(point, objectRect.Center, rotationDegrees);
    }

    public static LayoutPoint RotatePoint(LayoutPoint point, LayoutPoint center, double rotationDegrees)
    {
        if (Math.Abs(rotationDegrees) <= 0.0001)
            return point;

        var radians = rotationDegrees * Math.PI / 180.0;
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new LayoutPoint(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    public static bool ContainsInclusive(LayoutRect rect, LayoutPoint point) =>
        rect.Width > 0 &&
        rect.Height > 0 &&
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;

    public static bool Intersects(LayoutRect rect, LayoutRect other) =>
        rect.Right > other.Left &&
        rect.Left < other.Right &&
        rect.Bottom > other.Top &&
        rect.Top < other.Bottom;

    public static LayoutRect NormalizeRect(double left, double top, double right, double bottom) =>
        LayoutRect.FromCorners(left, top, right, bottom);

    private static AxisDragTransform NormalizeAxis(
        double lower,
        double upper,
        bool movesLower,
        bool movesUpper,
        double minimumSize)
    {
        var minSize = double.IsFinite(minimumSize) && minimumSize > 0
            ? minimumSize
            : DefaultMinimumObjectSize;
        if (movesLower == movesUpper)
            return new AxisDragTransform(lower, upper, Crossed: false);

        if (movesLower)
        {
            var signedExtent = upper - lower;
            if (signedExtent >= 0)
            {
                var extent = Math.Max(signedExtent, minSize);
                return new AxisDragTransform(upper - extent, upper, Crossed: false);
            }

            return new AxisDragTransform(upper, upper + Math.Max(-signedExtent, minSize), Crossed: true);
        }

        var upperSignedExtent = upper - lower;
        if (upperSignedExtent >= 0)
        {
            var extent = Math.Max(upperSignedExtent, minSize);
            return new AxisDragTransform(lower, lower + extent, Crossed: false);
        }

        return new AxisDragTransform(lower - Math.Max(-upperSignedExtent, minSize), lower, Crossed: true);
    }

    private static double Distance(LayoutPoint a, LayoutPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsEmpty(LayoutRect rect) => rect.Width <= 0 || rect.Height <= 0;

    private static double NormalizeOffset(double value, double fallback) =>
        double.IsFinite(value) && value >= 0 ? value : fallback;

    private readonly record struct AxisDragTransform(double Start, double End, bool Crossed);
}
