namespace Free.Shared.Drawing;

/// <summary>Converts the zero-rotation, SVG-style elliptical arcs used by shape geometry to cubic Béziers.</summary>
public static class ShapeArcToBezier
{
    /// <summary>
    /// Converts an arc into segments spanning at most 90 degrees each. Coincident endpoints produce
    /// no segments; an arc with either radius zero produces a straight line. The final endpoint is
    /// copied exactly so adjacent contours do not acquire a rounding gap.
    /// </summary>
    public static IReadOnlyList<ShapeSegment> Convert(LayoutPoint start, ShapeSegment arc)
    {
        if (arc.Kind != ShapeSegmentKind.Arc)
            throw new ArgumentException("The segment must be an arc.", nameof(arc));
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(arc.End.X) || !double.IsFinite(arc.End.Y) ||
            !double.IsFinite(arc.RadiusX) || !double.IsFinite(arc.RadiusY))
            throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates and radii must be finite.");
        if (start == arc.End)
            return [];

        var rx = Math.Abs(arc.RadiusX);
        var ry = Math.Abs(arc.RadiusY);
        if (rx == 0 || ry == 0)
            return [ShapeSegment.LineTo(arc.End)];

        // SVG endpoint-to-centre conversion for zero ellipse rotation. Scale radii up when the
        // authored ellipse is too small to span the endpoints, as required by the SVG arc rules.
        var x = (start.X / 2) - (arc.End.X / 2);
        var y = (start.Y / 2) - (arc.End.Y / 2);
        var nx = x / rx;
        var ny = y / ry;
        var magnitude = Math.Max(Math.Abs(nx), Math.Abs(ny));
        if (!double.IsFinite(magnitude) || magnitude == 0)
            throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates exceed the supported numeric range.");
        var distance = magnitude * Math.Sqrt(((nx / magnitude) * (nx / magnitude)) + ((ny / magnitude) * (ny / magnitude)));
        if (!double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates exceed the supported numeric range.");
        if (distance > 1)
        {
            rx *= distance;
            ry *= distance;
            if (!double.IsFinite(rx) || !double.IsFinite(ry))
                throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates exceed the supported numeric range.");
            nx /= distance;
            ny /= distance;
            distance = 1;
        }

        var sign = arc.LargeArc == arc.SweepClockwise ? -1d : 1d;
        var coefficient = sign * Math.Sqrt(Math.Max(0, 1 - (distance * distance))) / distance;
        var centerX = (start.X / 2) + (arc.End.X / 2) + (coefficient * rx * ny);
        var centerY = (start.Y / 2) + (arc.End.Y / 2) - (coefficient * ry * nx);
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
            throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates exceed the supported numeric range.");
        var startAngle = Math.Atan2((start.Y - centerY) / ry, (start.X - centerX) / rx);
        var endAngle = Math.Atan2((arc.End.Y - centerY) / ry, (arc.End.X - centerX) / rx);
        var sweep = endAngle - startAngle;
        if (arc.SweepClockwise && sweep < 0)
            sweep += 2 * Math.PI;
        else if (!arc.SweepClockwise && sweep > 0)
            sweep -= 2 * Math.PI;

        // Floating-point centre calculation can overshoot an exact quadrant by a few ulps.
        var count = Math.Max(1, (int)Math.Ceiling((Math.Abs(sweep) / (Math.PI / 2)) - 1e-12));
        var step = sweep / count;
        var segments = new ShapeSegment[count];
        for (var i = 0; i < count; i++)
        {
            var a = startAngle + (i * step);
            var b = a + step;
            var factor = (4d / 3d) * Math.Tan(step / 4);
            var cosA = Math.Cos(a);
            var sinA = Math.Sin(a);
            var cosB = Math.Cos(b);
            var sinB = Math.Sin(b);
            var control1 = new LayoutPoint(centerX + (rx * (cosA - (factor * sinA))), centerY + (ry * (sinA + (factor * cosA))));
            var control2 = new LayoutPoint(centerX + (rx * (cosB + (factor * sinB))), centerY + (ry * (sinB - (factor * cosB))));
            var end = i == count - 1 ? arc.End : new LayoutPoint(centerX + (rx * cosB), centerY + (ry * sinB));
            if (!double.IsFinite(control1.X) || !double.IsFinite(control1.Y) ||
                !double.IsFinite(control2.X) || !double.IsFinite(control2.Y) ||
                !double.IsFinite(end.X) || !double.IsFinite(end.Y))
                throw new ArgumentOutOfRangeException(nameof(arc), "Arc coordinates exceed the supported numeric range.");
            segments[i] = ShapeSegment.BezierTo(control1, control2, end);
        }
        return segments;
    }
}
