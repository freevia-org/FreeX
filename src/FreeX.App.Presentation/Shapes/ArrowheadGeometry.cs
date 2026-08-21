using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shapes;

/// <summary>
/// Portable, framework-free helper that computes the filled polygon (or ellipse) vertices for a
/// line/connector arrowhead. The result is consumed by both desktop renderers so the
/// geometry math is not duplicated.
///
/// Coordinate system: tip of the arrowhead is at <paramref name="tip"/>; the line arrives from the
/// direction given by <paramref name="directionRadians"/> (angle from positive-X axis, clockwise).
/// The returned polygon is expressed in screen/layout coordinates; callers fill it with the line's
/// stroke color.
/// </summary>
public static class ArrowheadGeometry
{
    /// <summary>
    /// Returns the polygon vertices for the arrowhead at <paramref name="tip"/>, pointing in
    /// the direction the line travels (tip = the pointy end).
    ///
    /// <paramref name="directionRadians"/>: angle of the line as measured from positive-X,
    /// clockwise (matching screen-space Y-down). 0 = pointing right, π/2 = pointing down.
    ///
    /// <paramref name="strokeWidth"/>: line stroke in DIP pixels. The arrowhead is scaled from this.
    /// </summary>
    /// <returns>
    /// Polygon vertices for Triangle / Arrow / Stealth as a <see cref="LayoutPoint"/> array (closed).
    /// For <see cref="DrawingArrowheadType.Oval"/> use <see cref="OvalCenter"/> and <see cref="OvalRadius"/> instead.
    /// For <see cref="DrawingArrowheadType.None"/> returns an empty array.
    /// </returns>
    public static LayoutPoint[] PolygonPoints(
        DrawingArrowhead arrowhead,
        LayoutPoint tip,
        double directionRadians,
        double strokeWidth)
    {
        if (!arrowhead.IsPresent)
            return [];

        var (halfWidth, length) = ScaleArrowhead(arrowhead, strokeWidth);

        return arrowhead.Type switch
        {
            DrawingArrowheadType.Triangle => TrianglePoints(tip, directionRadians, halfWidth, length),
            DrawingArrowheadType.Arrow => ArrowPoints(tip, directionRadians, halfWidth, length),
            DrawingArrowheadType.Stealth => StealthPoints(tip, directionRadians, halfWidth, length),
            DrawingArrowheadType.Diamond => DiamondPoints(tip, directionRadians, halfWidth, length),
            _ => [] // Oval handled separately; None = no points
        };
    }

    /// <summary>
    /// Returns the center and radius (in DIP pixels) for an <see cref="DrawingArrowheadType.Oval"/>
    /// arrowhead. The oval is drawn tangent to the tip of the line.
    /// </summary>
    public static (LayoutPoint Center, double Radius) OvalCenter(
        DrawingArrowhead arrowhead,
        LayoutPoint tip,
        double directionRadians,
        double strokeWidth)
    {
        var (_, length) = ScaleArrowhead(arrowhead, strokeWidth);
        var radius = length / 2.0;
        // Center sits behind the tip by one radius along the reversed direction.
        var cx = tip.X - Math.Cos(directionRadians) * radius;
        var cy = tip.Y - Math.Sin(directionRadians) * radius;
        return (new LayoutPoint(cx, cy), radius);
    }

    // ── Size scaling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns (halfWidth, length) in DIP pixels for the arrowhead, scaled from the stroke width.
    /// Excel scales arrowheads relative to the line weight. Native medium triangular ends are
    /// substantially tighter than the other supported medium presets, so that one authored
    /// combination uses the calibrated factors below while all other shape/size combinations
    /// retain their established geometry.
    /// </summary>
    public static (double HalfWidth, double Length) ScaleArrowhead(DrawingArrowhead arrowhead, double strokeWidth)
    {
        // Clamp stroke to a minimum so tiny lines still have visible arrowheads.
        var s = Math.Max(1.0, strokeWidth);

        // Excel's DrawingML `triangle` with absent w/len attributes is medium/medium. Its native
        // terminal geometry is about 3.5 × stroke long and 3 × stroke wide. The former generic
        // medium factors (7 × length, 5 × width) made every Excel-authored medium triangle roughly
        // twice as large in both WPF and Avalonia. Keep small/large triangles and the other
        // arrowhead types on their existing factors: only this exercised native combination moves.
        if (arrowhead.Type == DrawingArrowheadType.Triangle &&
            arrowhead.Width == DrawingArrowheadSize.Medium &&
            arrowhead.Length == DrawingArrowheadSize.Medium)
        {
            return (s * 3.0 / 2.0, s * 3.5);
        }

        var lengthFactor = arrowhead.Length switch
        {
            DrawingArrowheadSize.Small => 4.0,
            DrawingArrowheadSize.Large => 10.0,
            _ => 7.0  // Medium
        };
        var widthFactor = arrowhead.Width switch
        {
            DrawingArrowheadSize.Small => 3.0,
            DrawingArrowheadSize.Large => 7.0,
            _ => 5.0  // Medium
        };

        return (s * widthFactor / 2.0, s * lengthFactor);
    }

    // ── Arrowhead shapes ─────────────────────────────────────────────────────

    /// <summary>Solid filled triangle (closed isoceles).</summary>
    private static LayoutPoint[] TrianglePoints(
        LayoutPoint tip,
        double dir,
        double halfWidth,
        double length)
    {
        // Perpendicular direction (90° clockwise from dir)
        var perpDir = dir + Math.PI / 2;
        var basePt = Offset(tip, dir + Math.PI, length); // base center, back from tip

        return
        [
            tip,
            Offset(basePt, perpDir, halfWidth),
            Offset(basePt, perpDir, -halfWidth)
        ];
    }

    /// <summary>Open/classic arrow: narrower than triangle, with an indented base.</summary>
    private static LayoutPoint[] ArrowPoints(
        LayoutPoint tip,
        double dir,
        double halfWidth,
        double length)
    {
        // Same as triangle but base center indented 25% toward the tip
        var perpDir = dir + Math.PI / 2;
        var backDir = dir + Math.PI;
        var basePt = Offset(tip, backDir, length);
        var midPt = Offset(tip, backDir, length * 0.6);

        return
        [
            tip,
            Offset(basePt, perpDir, halfWidth),
            midPt,
            Offset(basePt, perpDir, -halfWidth)
        ];
    }

    /// <summary>Stealth arrowhead: triangle with a concave (swept-back) base.</summary>
    private static LayoutPoint[] StealthPoints(
        LayoutPoint tip,
        double dir,
        double halfWidth,
        double length)
    {
        var perpDir = dir + Math.PI / 2;
        var backDir = dir + Math.PI;
        var basePt = Offset(tip, backDir, length);
        var midPt = Offset(tip, backDir, length * 0.35);

        return
        [
            tip,
            Offset(basePt, perpDir, halfWidth),
            midPt,
            Offset(basePt, perpDir, -halfWidth)
        ];
    }

    /// <summary>Diamond (rhombus) arrowhead: extends equally forward and back from the tip.</summary>
    private static LayoutPoint[] DiamondPoints(
        LayoutPoint tip,
        double dir,
        double halfWidth,
        double length)
    {
        var perpDir = dir + Math.PI / 2;
        var backDir = dir + Math.PI;
        var halfLength = length / 2.0;
        var frontPt = Offset(tip, dir, halfLength);       // forward point
        var backPt = Offset(tip, backDir, halfLength);    // back point

        return
        [
            frontPt,
            Offset(tip, perpDir, halfWidth),
            backPt,
            Offset(tip, perpDir, -halfWidth)
        ];
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LayoutPoint Offset(LayoutPoint origin, double angleRadians, double distance) =>
        new(origin.X + Math.Cos(angleRadians) * distance,
            origin.Y + Math.Sin(angleRadians) * distance);

    /// <summary>
    /// Computes the line's start/end points and direction angle for a line-like shape given its
    /// bounding rect, flip flags, and rotation. Used by both renderers to determine where to place
    /// arrowheads. Returns (start, end, directionFromStartToEnd).
    /// </summary>
    public static (LayoutPoint Start, LayoutPoint End, double DirectionRadians) LineEndpoints(
        double left,
        double top,
        double width,
        double height,
        bool flipHorizontal,
        bool flipVertical,
        DrawingShapeKind kind)
    {
        // ShapeGeometryBuilder's line-like geometries start at the top-left and end at the
        // bottom-right of their bounds. Arrowheads must use those actual endpoints: using an
        // invented inset for a curved connector detaches the arrowhead from the rendered path.
        LayoutPoint rawStart = new(left, top);
        LayoutPoint rawEnd = new(left + width, top + height);

        // Apply flip transforms about the bounding-box center (mirrors the renderer's transform).
        if (flipHorizontal || flipVertical)
        {
            var cx = left + width / 2.0;
            var cy = top + height / 2.0;
            rawStart = FlipPoint(rawStart, cx, cy, flipHorizontal, flipVertical);
            rawEnd = FlipPoint(rawEnd, cx, cy, flipHorizontal, flipVertical);
        }

        var dx = rawEnd.X - rawStart.X;
        var dy = rawEnd.Y - rawStart.Y;
        var dir = Math.Atan2(dy, dx);
        return (rawStart, rawEnd, dir);
    }

    private static LayoutPoint FlipPoint(LayoutPoint p, double cx, double cy, bool flipH, bool flipV) =>
        new(flipH ? cx - (p.X - cx) : p.X,
            flipV ? cy - (p.Y - cy) : p.Y);
}
