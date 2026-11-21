using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>A pixel-space region a chart element occupies, used for hit-testing and highlight bounds.</summary>
public abstract record ChartHitShape
{
    /// <summary>The axis-aligned bounding box of the shape.</summary>
    public abstract LayoutRect Bounds { get; }

    public abstract bool Contains(LayoutPoint point);

    internal static LayoutRect BoundsOf(IEnumerable<LayoutPoint> points, double inflate)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var p in points)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
                continue;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return double.IsInfinity(minX)
            ? new LayoutRect(0, 0, 0, 0)
            : new LayoutRect(minX - inflate, minY - inflate, (maxX - minX) + (2 * inflate), (maxY - minY) + (2 * inflate));
    }
}

/// <summary>An axis-aligned rectangle, optionally grown by <see cref="Padding"/> on every side for hit-testing.</summary>
public sealed record RectHitShape(LayoutRect Rect, double Padding = 0) : ChartHitShape
{
    public override LayoutRect Bounds => Rect;

    public override bool Contains(LayoutPoint point) =>
        point.X >= Rect.Left - Padding && point.X <= Rect.Right + Padding &&
        point.Y >= Rect.Top - Padding && point.Y <= Rect.Bottom + Padding;
}

/// <summary>A set of line segments hit within <see cref="Tolerance"/> pixels (lines, gridlines, whiskers).</summary>
public sealed record SegmentsHitShape(IReadOnlyList<(LayoutPoint From, LayoutPoint To)> Segments, double Tolerance) : ChartHitShape
{
    public override LayoutRect Bounds =>
        BoundsOf(Segments.SelectMany(s => new[] { s.From, s.To }), 0);

    public override bool Contains(LayoutPoint point)
    {
        foreach (var (from, to) in Segments)
        {
            if (DistanceToSegment(point, from, to) <= Tolerance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds consecutive segments from <paramref name="points"/>; a non-finite point (a line gap) breaks
    /// the polyline instead of bridging it.
    /// </summary>
    public static SegmentsHitShape FromPolyline(IEnumerable<LayoutPoint> points, double tolerance, bool closed = false)
    {
        var segments = new List<(LayoutPoint, LayoutPoint)>();
        LayoutPoint? previous = null;
        LayoutPoint? first = null;
        foreach (var p in points)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
                previous = null;
                continue;
            }

            first ??= p;
            if (previous is { } prev)
                segments.Add((prev, p));
            else
                segments.Add((p, p));
            previous = p;
        }

        if (closed && previous is { } last && first is { } start && last != start)
            segments.Add((last, start));

        return new SegmentsHitShape(segments, tolerance);
    }

    private static double DistanceToSegment(LayoutPoint p, LayoutPoint a, LayoutPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-12)
            return Math.Sqrt(((p.X - a.X) * (p.X - a.X)) + ((p.Y - a.Y) * (p.Y - a.Y)));

        var t = Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        var cx = a.X + (t * dx);
        var cy = a.Y + (t * dy);
        return Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy)));
    }
}

/// <summary>A filled polygon (area series bands).</summary>
public sealed record PolygonHitShape(IReadOnlyList<LayoutPoint> Points) : ChartHitShape
{
    public override LayoutRect Bounds => BoundsOf(Points, 0);

    public override bool Contains(LayoutPoint point)
    {
        var inside = false;
        for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
        {
            var pi = Points[i];
            var pj = Points[j];
            if (!double.IsFinite(pi.Y) || !double.IsFinite(pj.Y) || !double.IsFinite(pi.X) || !double.IsFinite(pj.X))
                continue;
            if ((pi.Y > point.Y) != (pj.Y > point.Y) &&
                point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>A set of circles with individual radii (markers, scatter points, bubbles).</summary>
public sealed record CirclesHitShape(IReadOnlyList<(LayoutPoint Center, double Radius)> Circles) : ChartHitShape
{
    public override LayoutRect Bounds
    {
        get
        {
            var rects = Circles
                .Where(c => double.IsFinite(c.Center.X) && double.IsFinite(c.Center.Y))
                .SelectMany(c => new[]
                {
                    new LayoutPoint(c.Center.X - c.Radius, c.Center.Y - c.Radius),
                    new LayoutPoint(c.Center.X + c.Radius, c.Center.Y + c.Radius),
                });
            return BoundsOf(rects, 0);
        }
    }

    public override bool Contains(LayoutPoint point)
    {
        foreach (var (center, radius) in Circles)
        {
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            if ((dx * dx) + (dy * dy) <= radius * radius)
                return true;
        }

        return false;
    }
}

/// <summary>An annular sector (pie / doughnut slice). Angles are degrees clockwise from 12 o'clock.</summary>
public sealed record ArcHitShape(LayoutArc Arc) : ChartHitShape
{
    public override LayoutRect Bounds
    {
        get
        {
            var r = Arc.OuterRadius;
            return new LayoutRect(Arc.Center.X - r, Arc.Center.Y - r, 2 * r, 2 * r);
        }
    }

    public override bool Contains(LayoutPoint point)
    {
        var dx = point.X - Arc.Center.X;
        var dy = point.Y - Arc.Center.Y;
        var radius = Math.Sqrt((dx * dx) + (dy * dy));
        if (radius > Arc.OuterRadius || radius < Arc.InnerRadius)
            return false;

        if (Arc.SweepAngleDegrees >= 359.999)
            return true;

        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        var delta = (((angle - Arc.StartAngleDegrees) % 360) + 360) % 360;
        return delta <= Arc.SweepAngleDegrees;
    }
}
