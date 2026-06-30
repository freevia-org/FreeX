namespace Free.Shared.Drawing;

public readonly record struct DrawingBoundsHitCandidate<TId>(
    TId Id,
    LayoutRect Bounds,
    int ZOrder,
    double RotationDegrees = 0);

public readonly record struct DrawingBoundsHit<TId>(
    TId Id,
    LayoutRect Bounds,
    DrawingObjectInteractionKind Part);

/// <summary>
/// Shared axis-aligned drawing-object hit-testing for hosts that already projected objects into a
/// common layout coordinate space. Rotated candidates are tested by inverse-rotating the point into
/// the candidate's local box before applying the inclusive bounds check.
/// </summary>
public static class DrawingBoundsHitTester
{
    public static DrawingBoundsHit<TId>? HitTest<TId>(
        IReadOnlyList<DrawingBoundsHitCandidate<TId>> candidates,
        LayoutPoint position,
        DrawingObjectInteractionKind bodyPart = DrawingObjectInteractionKind.Body)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var bestIndex = -1;
        var bestZ = int.MinValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!ContainsCandidate(candidate, position))
                continue;

            if (candidate.ZOrder >= bestZ)
            {
                bestZ = candidate.ZOrder;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return null;

        var hit = candidates[bestIndex];
        return new DrawingBoundsHit<TId>(hit.Id, hit.Bounds, bodyPart);
    }

    public static IReadOnlyList<TId> MarqueeHitTest<TId>(
        IReadOnlyList<DrawingBoundsHitCandidate<TId>> candidates,
        LayoutRect marquee)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var normalized = DrawingObjectInteractionPlanner.NormalizeRect(
            marquee.Left,
            marquee.Top,
            marquee.Right,
            marquee.Bottom);
        var result = new List<TId>();
        foreach (var candidate in candidates)
        {
            if (DrawingObjectInteractionPlanner.Intersects(candidate.Bounds, normalized))
                result.Add(candidate.Id);
        }

        return result;
    }

    public static bool Contains(LayoutRect bounds, LayoutPoint position, double rotationDegrees = 0)
    {
        if (Math.Abs(rotationDegrees) > 0.0001)
            position = DrawingObjectInteractionPlanner.RotatePointAroundCenter(
                position,
                bounds,
                -rotationDegrees);

        return DrawingObjectInteractionPlanner.ContainsInclusive(bounds, position);
    }

    private static bool ContainsCandidate<TId>(
        DrawingBoundsHitCandidate<TId> candidate,
        LayoutPoint position) =>
        Contains(candidate.Bounds, position, candidate.RotationDegrees);
}
