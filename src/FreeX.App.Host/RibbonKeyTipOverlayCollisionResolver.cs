using System.Windows;

namespace FreeX.App.Host;

/// <summary>Separates key-tip badges when adjacent ribbon controls would otherwise overlap.</summary>
public static class RibbonKeyTipOverlayCollisionResolver
{
    private const double BadgeGap = 2;
    private const int MaximumSearchRadius = 12;

    public static IReadOnlyList<Point> Resolve(
        IReadOnlyList<RibbonKeyTipBadgePlacement> placements,
        Size overlaySize)
    {
        ArgumentNullException.ThrowIfNull(placements);

        var resolved = new List<Point>(placements.Count);
        var occupied = new List<Rect>(placements.Count);

        foreach (var placement in placements)
        {
            var point = FindAvailablePoint(placement, overlaySize, occupied);
            resolved.Add(point);
            occupied.Add(new Rect(point, placement.BadgeSize));
        }

        return resolved;
    }

    private static Point FindAvailablePoint(
        RibbonKeyTipBadgePlacement placement,
        Size overlaySize,
        IReadOnlyList<Rect> occupied)
    {
        foreach (var offset in EnumerateOffsets(placement.BadgeSize))
        {
            var point = Clamp(
                new Point(placement.PreferredPoint.X + offset.X, placement.PreferredPoint.Y + offset.Y),
                overlaySize,
                placement.BadgeSize);
            var bounds = new Rect(point, placement.BadgeSize);

            if (occupied.All(existing => !existing.IntersectsWith(bounds)))
                return point;
        }

        // A very small overlay can make a collision-free arrangement impossible.
        // Preserve the authored anchor in that case rather than hiding a keytip.
        return Clamp(placement.PreferredPoint, overlaySize, placement.BadgeSize);
    }

    private static IEnumerable<Vector> EnumerateOffsets(Size badgeSize)
    {
        yield return new Vector();

        var horizontalStep = Math.Max(1, badgeSize.Width + BadgeGap);
        var verticalStep = Math.Max(1, badgeSize.Height + BadgeGap);
        for (var radius = 1; radius <= MaximumSearchRadius; radius++)
        {
            yield return new Vector(-radius * horizontalStep, 0);
            yield return new Vector(radius * horizontalStep, 0);
            yield return new Vector(0, radius * verticalStep);
            yield return new Vector(0, -radius * verticalStep);
            yield return new Vector(-radius * horizontalStep, radius * verticalStep);
            yield return new Vector(radius * horizontalStep, radius * verticalStep);
            yield return new Vector(-radius * horizontalStep, -radius * verticalStep);
            yield return new Vector(radius * horizontalStep, -radius * verticalStep);
        }
    }

    private static Point Clamp(Point point, Size overlaySize, Size badgeSize) =>
        new(
            Math.Round(Math.Clamp(point.X, 0, Math.Max(0, overlaySize.Width - badgeSize.Width)), MidpointRounding.AwayFromZero),
            Math.Round(Math.Clamp(point.Y, 0, Math.Max(0, overlaySize.Height - badgeSize.Height)), MidpointRounding.AwayFromZero));
}

public readonly record struct RibbonKeyTipBadgePlacement(Point PreferredPoint, Size BadgeSize);
