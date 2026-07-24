using FreeX.Core.Model;

using SharedInteractionKind = Free.Shared.Drawing.DrawingObjectInteractionKind;
using SharedInteractionPlanner = Free.Shared.Drawing.DrawingObjectInteractionPlanner;

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
    public const double MinimumObjectSize = SharedInteractionPlanner.DefaultMinimumObjectSize;

    /// <summary>
    /// Vertical distance (in pixels) of the rotation grip's center above the top edge of the object.
    /// </summary>
    public const double RotationGripOffset = SharedInteractionPlanner.DefaultRotationHandleOffset;

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
        var shared = SharedInteractionPlanner.CalculateDragTransform(
            ToShared(dragKind),
            startRect,
            startPosition,
            currentPosition,
            minimumSize);
        return new ObjectDragTransform(
            shared.Rect,
            shared.CrossedHorizontally,
            shared.CrossedVertically);
    }

    public static bool ShouldCommitMove(CellAddress startAnchor, CellAddress currentAnchor) =>
        startAnchor != currentAnchor;

    public static bool ShouldCommitResize(
        LayoutRect startRect,
        LayoutRect currentRect,
        bool startFlipHorizontal,
        bool startFlipVertical,
        bool currentFlipHorizontal,
        bool currentFlipVertical,
        double threshold = 1) =>
        Math.Abs(currentRect.Left - startRect.Left) > threshold ||
        Math.Abs(currentRect.Top - startRect.Top) > threshold ||
        Math.Abs(currentRect.Width - startRect.Width) > threshold ||
        Math.Abs(currentRect.Height - startRect.Height) > threshold ||
        currentFlipHorizontal != startFlipHorizontal ||
        currentFlipVertical != startFlipVertical;

    /// <summary>
    /// Computes the rotation angle (in degrees, clockwise, 0 = pointer straight up) of the
    /// pointer relative to the object center. Returns 0 when the pointer is at the center.
    /// </summary>
    public static double CalculateRotationDegrees(LayoutPoint center, LayoutPoint pointer)
        => SharedInteractionPlanner.CalculateRotationDegrees(center, pointer);

    /// <summary>
    /// Computes the signed change in rotation (degrees, clockwise positive) implied by dragging the
    /// rotation grip from <paramref name="startGrip"/> to <paramref name="currentGrip"/> about
    /// <paramref name="center"/>. Normalized to the range (-180, 180]. Returns 0 if either grip
    /// position coincides with the center.
    /// </summary>
    public static double CalculateRotationDelta(LayoutPoint center, LayoutPoint startGrip, LayoutPoint currentGrip)
        => SharedInteractionPlanner.CalculateRotationDelta(center, startGrip, currentGrip);

    public static ObjectDragKind HitTestHandle(
        LayoutPoint position,
        LayoutRect objectRect,
        double handleSize = 8,
        double handleHitPadding = 4,
        double rotationDegrees = 0)
    {
        return ToFreeX(SharedInteractionPlanner.HitTestBoundingBoxHandles(
            position,
            objectRect,
            handleSize,
            handleHitPadding,
            rotationDegrees,
            RotationGripOffset));
    }

    public static LayoutPoint RotateHandleCenter(
        ObjectDragKind handle,
        LayoutRect objectRect,
        double rotationDegrees)
    {
        var center = SharedInteractionPlanner.GetHandleCenter(
            ToShared(handle),
            objectRect,
            RotationGripOffset);
        return Math.Abs(rotationDegrees) <= 0.0001
            ? center
            : SharedInteractionPlanner.RotatePointAroundCenter(center, objectRect, rotationDegrees);
    }

    public static LayoutPoint RotatePointAroundCenter(LayoutPoint point, LayoutRect objectRect, double rotationDegrees)
        => SharedInteractionPlanner.RotatePointAroundCenter(point, objectRect, rotationDegrees);

    private static SharedInteractionKind ToShared(ObjectDragKind kind) =>
        kind switch
        {
            ObjectDragKind.Move => SharedInteractionKind.Body,
            ObjectDragKind.ResizeNW => SharedInteractionKind.ResizeNW,
            ObjectDragKind.ResizeN => SharedInteractionKind.ResizeN,
            ObjectDragKind.ResizeNE => SharedInteractionKind.ResizeNE,
            ObjectDragKind.ResizeE => SharedInteractionKind.ResizeE,
            ObjectDragKind.ResizeSE => SharedInteractionKind.ResizeSE,
            ObjectDragKind.ResizeS => SharedInteractionKind.ResizeS,
            ObjectDragKind.ResizeSW => SharedInteractionKind.ResizeSW,
            ObjectDragKind.ResizeW => SharedInteractionKind.ResizeW,
            ObjectDragKind.Rotate => SharedInteractionKind.Rotate,
            _ => SharedInteractionKind.None
        };

    private static ObjectDragKind ToFreeX(SharedInteractionKind kind) =>
        kind switch
        {
            SharedInteractionKind.Body => ObjectDragKind.Move,
            SharedInteractionKind.ResizeNW => ObjectDragKind.ResizeNW,
            SharedInteractionKind.ResizeN => ObjectDragKind.ResizeN,
            SharedInteractionKind.ResizeNE => ObjectDragKind.ResizeNE,
            SharedInteractionKind.ResizeE => ObjectDragKind.ResizeE,
            SharedInteractionKind.ResizeSE => ObjectDragKind.ResizeSE,
            SharedInteractionKind.ResizeS => ObjectDragKind.ResizeS,
            SharedInteractionKind.ResizeSW => ObjectDragKind.ResizeSW,
            SharedInteractionKind.ResizeW => ObjectDragKind.ResizeW,
            SharedInteractionKind.Rotate => ObjectDragKind.Rotate,
            _ => ObjectDragKind.None
        };
}
