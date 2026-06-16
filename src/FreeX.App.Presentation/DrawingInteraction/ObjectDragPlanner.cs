using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.DrawingInteraction;

/// <summary>
/// The kind of drag interaction in progress on a selected drawing object: a whole-object move,
/// one of the eight resize handles (corners and edge midpoints), or the rotation grip.
/// </summary>
public enum ObjectDragKind
{
    None,
    Move,
    ResizeNW,
    ResizeN,
    ResizeNE,
    ResizeE,
    ResizeSE,
    ResizeS,
    ResizeSW,
    ResizeW,
    Rotate
}

/// <summary>
/// The result of a resize/move drag: the new object rectangle plus whether the drag pulled a moving
/// edge past the opposite fixed edge on each axis (used to mirror/flip the object's content).
/// </summary>
public readonly record struct ObjectDragTransform(
    LayoutRect Rect,
    bool CrossedHorizontally,
    bool CrossedVertically);

/// <summary>
/// Pure, portable math for editing a drawing object via drag: move/resize transforms, rotation-grip
/// angle math, and handle hit-testing (including inverse-rotation for rotated objects). No platform
/// types — geometry uses <see cref="LayoutPoint"/>/<see cref="LayoutRect"/> so the desktop hosts and
/// other renderers can share it.
/// </summary>
public static class ObjectDragPlanner
{
    public const double MinimumObjectSize = 8;

    /// <summary>
    /// Vertical distance (in pixels) of the rotation grip's center above the top edge of the object.
    /// </summary>
    public const double RotationGripOffset = 20;

    public static LayoutRect CalculateDragRect(
        ObjectDragKind dragKind,
        LayoutRect startRect,
        LayoutPoint startPosition,
        LayoutPoint currentPosition,
        double minimumSize = MinimumObjectSize) =>
        CalculateDragTransform(dragKind, startRect, startPosition, currentPosition, minimumSize).Rect;

    public static ObjectDragTransform CalculateDragTransform(
        ObjectDragKind dragKind,
        LayoutRect startRect,
        LayoutPoint startPosition,
        LayoutPoint currentPosition,
        double minimumSize = MinimumObjectSize)
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
            case ObjectDragKind.Move:
                return new ObjectDragTransform(
                    new LayoutRect(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height),
                    CrossedHorizontally: false,
                    CrossedVertically: false);
            case ObjectDragKind.ResizeNW:
                left += dx;
                top += dy;
                movesLeft = true;
                movesTop = true;
                break;
            case ObjectDragKind.ResizeN:
                top += dy;
                movesTop = true;
                break;
            case ObjectDragKind.ResizeNE:
                right += dx;
                top += dy;
                movesRight = true;
                movesTop = true;
                break;
            case ObjectDragKind.ResizeE:
                right += dx;
                movesRight = true;
                break;
            case ObjectDragKind.ResizeSE:
                right += dx;
                bottom += dy;
                movesRight = true;
                movesBottom = true;
                break;
            case ObjectDragKind.ResizeS:
                bottom += dy;
                movesBottom = true;
                break;
            case ObjectDragKind.ResizeSW:
                left += dx;
                bottom += dy;
                movesLeft = true;
                movesBottom = true;
                break;
            case ObjectDragKind.ResizeW:
                left += dx;
                movesLeft = true;
                break;
            default:
                return new ObjectDragTransform(startRect, CrossedHorizontally: false, CrossedVertically: false);
        }

        var horizontal = NormalizeAxis(left, right, movesLeft, movesRight, minimumSize);
        var vertical = NormalizeAxis(top, bottom, movesTop, movesBottom, minimumSize);
        return new ObjectDragTransform(
            new LayoutRect(
                horizontal.Start,
                vertical.Start,
                horizontal.End - horizontal.Start,
                vertical.End - vertical.Start),
            horizontal.Crossed,
            vertical.Crossed);
    }

    private static AxisDragTransform NormalizeAxis(
        double lower,
        double upper,
        bool movesLower,
        bool movesUpper,
        double minimumSize)
    {
        var minSize = double.IsFinite(minimumSize) && minimumSize > 0 ? minimumSize : MinimumObjectSize;
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

    /// <summary>
    /// Computes the rotation angle (in degrees, clockwise, 0 = pointer straight up) of the
    /// pointer relative to the object center. Returns 0 when the pointer is at the center.
    /// </summary>
    public static double CalculateRotationDegrees(LayoutPoint center, LayoutPoint pointer)
    {
        var dx = pointer.X - center.X;
        var dy = pointer.Y - center.Y;
        if (dx == 0 && dy == 0)
            return 0;

        // Atan2(dx, -dy) gives 0 for straight up and increases clockwise (screen Y grows downward).
        var degrees = Math.Atan2(dx, -dy) * (180.0 / Math.PI);
        return degrees < 0 ? degrees + 360 : degrees;
    }

    /// <summary>
    /// Computes the signed change in rotation (degrees, clockwise positive) implied by dragging the
    /// rotation grip from <paramref name="startGrip"/> to <paramref name="currentGrip"/> about
    /// <paramref name="center"/>. Normalized to the range (-180, 180]. Returns 0 if either grip
    /// position coincides with the center.
    /// </summary>
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

        // Normalize into (-180, 180] so a small drag never reports an almost-full turn.
        delta %= 360.0;
        if (delta > 180.0)
            delta -= 360.0;
        else if (delta <= -180.0)
            delta += 360.0;

        return delta;
    }

    public static ObjectDragKind HitTestHandle(
        LayoutPoint position,
        LayoutRect objectRect,
        double handleSize = 8,
        double handleHitPadding = 4,
        double rotationDegrees = 0)
    {
        if (IsEmpty(objectRect))
            return ObjectDragKind.None;

        if (Math.Abs(rotationDegrees) > 0.0001)
            position = RotatePointAroundCenter(position, objectRect, -rotationDegrees);

        return HitTestUnrotatedHandle(position, objectRect, handleSize, handleHitPadding);
    }

    public static LayoutPoint RotateHandleCenter(
        ObjectDragKind handle,
        LayoutRect objectRect,
        double rotationDegrees)
    {
        var center = GetUnrotatedHandleCenter(handle, objectRect);
        return Math.Abs(rotationDegrees) <= 0.0001
            ? center
            : RotatePointAroundCenter(center, objectRect, rotationDegrees);
    }

    public static LayoutPoint RotatePointAroundCenter(LayoutPoint point, LayoutRect objectRect, double rotationDegrees)
    {
        if (IsEmpty(objectRect) || Math.Abs(rotationDegrees) <= 0.0001)
            return point;

        var radians = rotationDegrees * Math.PI / 180.0;
        var centerX = objectRect.Left + objectRect.Width / 2.0;
        var centerY = objectRect.Top + objectRect.Height / 2.0;
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new LayoutPoint(
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos);
    }

    private static ObjectDragKind HitTestUnrotatedHandle(
        LayoutPoint position,
        LayoutRect objectRect,
        double handleSize,
        double handleHitPadding)
    {
        var pad = handleHitPadding + handleSize / 2;

        // Rotation grip sits above the top-center handle with a connector line.
        var gripCenterX = objectRect.Left + objectRect.Width / 2;
        var gripCenterY = objectRect.Top - RotationGripOffset;
        if (Math.Abs(position.X - gripCenterX) <= pad && Math.Abs(position.Y - gripCenterY) <= pad)
            return ObjectDragKind.Rotate;

        var nearLeft = Math.Abs(position.X - objectRect.Left) <= pad;
        var nearTop = Math.Abs(position.Y - objectRect.Top) <= pad;
        var nearRight = Math.Abs(position.X - objectRect.Right) <= pad;
        var nearBottom = Math.Abs(position.Y - objectRect.Bottom) <= pad;
        var inVertical = position.Y >= objectRect.Top - pad && position.Y <= objectRect.Bottom + pad;
        var inHorizontal = position.X >= objectRect.Left - pad && position.X <= objectRect.Right + pad;

        // Corners take priority over edges (a corner is near two perpendicular edges).
        if (nearLeft && nearTop) return ObjectDragKind.ResizeNW;
        if (nearRight && nearTop) return ObjectDragKind.ResizeNE;
        if (nearRight && nearBottom) return ObjectDragKind.ResizeSE;
        if (nearLeft && nearBottom) return ObjectDragKind.ResizeSW;

        // Edges: anywhere along the edge line within the object's span.
        if (nearTop && inHorizontal) return ObjectDragKind.ResizeN;
        if (nearBottom && inHorizontal) return ObjectDragKind.ResizeS;
        if (nearRight && inVertical) return ObjectDragKind.ResizeE;
        if (nearLeft && inVertical) return ObjectDragKind.ResizeW;
        if (Contains(objectRect, position)) return ObjectDragKind.Move;
        return ObjectDragKind.None;
    }

    private static LayoutPoint GetUnrotatedHandleCenter(ObjectDragKind handle, LayoutRect objectRect)
    {
        var centerX = objectRect.Left + objectRect.Width / 2.0;
        var centerY = objectRect.Top + objectRect.Height / 2.0;
        return handle switch
        {
            ObjectDragKind.ResizeNW => new LayoutPoint(objectRect.Left, objectRect.Top),
            ObjectDragKind.ResizeN => new LayoutPoint(centerX, objectRect.Top),
            ObjectDragKind.ResizeNE => new LayoutPoint(objectRect.Right, objectRect.Top),
            ObjectDragKind.ResizeE => new LayoutPoint(objectRect.Right, centerY),
            ObjectDragKind.ResizeSE => new LayoutPoint(objectRect.Right, objectRect.Bottom),
            ObjectDragKind.ResizeS => new LayoutPoint(centerX, objectRect.Bottom),
            ObjectDragKind.ResizeSW => new LayoutPoint(objectRect.Left, objectRect.Bottom),
            ObjectDragKind.ResizeW => new LayoutPoint(objectRect.Left, centerY),
            ObjectDragKind.Rotate => new LayoutPoint(centerX, objectRect.Top - RotationGripOffset),
            _ => new LayoutPoint(centerX, centerY)
        };
    }

    private static bool IsEmpty(LayoutRect rect) => rect.Width <= 0 || rect.Height <= 0;

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;

    private readonly record struct AxisDragTransform(double Start, double End, bool Crossed);
}
