using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.DrawingInteraction;

/// <summary>
/// One candidate drawing object the grid can hit-test: an opaque identity token plus its
/// displayed pixel rectangle (in the same scrolled/zoomed pixel space the overlay paints into) and
/// its z-order. Higher <see cref="ZOrder"/> means painted later, i.e. visually on top.
/// </summary>
/// <typeparam name="TId">Caller-chosen identity token (e.g. a tuple of object kind + id).</typeparam>
public readonly record struct DrawingObjectHitCandidate<TId>(TId Id, LayoutRect Bounds, int ZOrder);

/// <summary>
/// The result of a hit-test against a single candidate: which object was hit and which part of it —
/// the body (for a move) or one of the eight resize handles / the rotation grip.
/// </summary>
/// <typeparam name="TId">Caller-chosen identity token.</typeparam>
public readonly record struct DrawingObjectHit<TId>(TId Id, LayoutRect Bounds, ObjectDragKind Part);

/// <summary>
/// Pure, portable hit-testing for in-grid drawing-object interaction. Given the displayed pixel
/// rectangles of the on-sheet drawing objects (charts / pictures / shapes / text boxes) plus a
/// pointer position, it resolves which object is under the pointer and — for the currently selected
/// object — whether the pointer is over a resize handle, the rotation grip, or the body.
///
/// No platform types: everything is expressed with <see cref="LayoutPoint"/>/<see cref="LayoutRect"/>,
/// so the cross-platform shells share one implementation. The per-handle geometry is delegated to
/// <see cref="ObjectDragPlanner"/> (the same math the desktop hosts already use), so handle hit-zones
/// here match the adorner the host renders pixel-for-pixel.
/// </summary>
public static class DrawingObjectHitTestPlanner
{
    /// <summary>
    /// Resolves the topmost object whose body contains <paramref name="position"/>. Candidates are
    /// compared by <see cref="DrawingObjectHitCandidate{TId}.ZOrder"/> (higher wins; ties resolved by
    /// later position in the list, mirroring paint order). Returns null when no body is hit.
    /// </summary>
    public static DrawingObjectHit<TId>? HitTest<TId>(
        IReadOnlyList<DrawingObjectHitCandidate<TId>> candidates,
        LayoutPoint position)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var bestIndex = -1;
        var bestZ = int.MinValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!Contains(candidate.Bounds, position))
                continue;

            // Higher z wins; equal z falls through to the later (last-painted) candidate.
            if (candidate.ZOrder >= bestZ)
            {
                bestZ = candidate.ZOrder;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return null;

        var hit = candidates[bestIndex];
        return new DrawingObjectHit<TId>(hit.Id, hit.Bounds, ObjectDragKind.Move);
    }

    /// <summary>
    /// Resolves the interaction for a click when a single object is currently selected. The selected
    /// object's resize handles / rotation grip take priority over every other object's body (a handle
    /// may overhang a neighbor), so this checks the selected object's handles first via
    /// <see cref="ObjectDragPlanner.HitTestHandle"/>; if the pointer is not on a handle it falls back to
    /// the normal topmost-body <see cref="HitTest{TId}"/>. Returns null when nothing is hit (an
    /// empty-canvas click that should clear the selection).
    /// </summary>
    public static DrawingObjectHit<TId>? HitTestWithSelection<TId>(
        IReadOnlyList<DrawingObjectHitCandidate<TId>> candidates,
        LayoutPoint position,
        TId selectedId,
        LayoutRect selectedBounds,
        double rotationDegrees = 0,
        double handleSize = ObjectDragPlanner.MinimumObjectSize,
        double handleHitPadding = 4)
        where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var handle = ObjectDragPlanner.HitTestHandle(
            position,
            selectedBounds,
            handleSize,
            handleHitPadding,
            rotationDegrees);

        // A handle / rotation grip on the selected object always wins, even over a neighbor's body.
        if (handle is not ObjectDragKind.None and not ObjectDragKind.Move)
            return new DrawingObjectHit<TId>(selectedId, selectedBounds, handle);

        return HitTest(candidates, position);
    }

    private static bool Contains(LayoutRect rect, LayoutPoint point) =>
        rect.Width > 0 && rect.Height > 0 &&
        point.X >= rect.Left && point.X <= rect.Right &&
        point.Y >= rect.Top && point.Y <= rect.Bottom;
}
