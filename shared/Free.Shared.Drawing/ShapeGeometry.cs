namespace Free.Shared.Drawing;

/// <summary>
/// The kind of a single drawing segment within a contour: a straight line, a cubic Bézier curve, or
/// an elliptical arc. The renderers map each to their own path primitive.
/// Ported from FreeX.App.Presentation.Shapes.ShapeSegmentKind.
/// </summary>
public enum ShapeSegmentKind
{
    Line,
    CubicBezier,
    Arc
}

/// <summary>
/// One segment of a shape contour, expressed purely as coordinates. The previous segment's
/// <see cref="End"/> (or the contour start) is the implicit starting point.
/// <list type="bullet">
/// <item><see cref="ShapeSegmentKind.Line"/> uses <see cref="End"/> only.</item>
/// <item><see cref="ShapeSegmentKind.CubicBezier"/> uses <see cref="Control1"/>, <see cref="Control2"/>, <see cref="End"/>.</item>
/// <item>
/// <see cref="ShapeSegmentKind.Arc"/> describes an elliptical arc to <see cref="End"/> with
/// per-axis <see cref="RadiusX"/>/<see cref="RadiusY"/>, <see cref="LargeArc"/>, and
/// <see cref="SweepClockwise"/> flags (SVG-style arc parameters; rotation is always zero).
/// </item>
/// </list>
/// Ported from FreeX.App.Presentation.Shapes.ShapeSegment.
/// </summary>
public readonly record struct ShapeSegment(
    ShapeSegmentKind Kind,
    LayoutPoint End,
    LayoutPoint Control1 = default,
    LayoutPoint Control2 = default,
    double RadiusX = 0,
    double RadiusY = 0,
    bool LargeArc = false,
    bool SweepClockwise = true)
{
    public static ShapeSegment LineTo(LayoutPoint end) => new(ShapeSegmentKind.Line, end);

    public static ShapeSegment BezierTo(LayoutPoint control1, LayoutPoint control2, LayoutPoint end) =>
        new(ShapeSegmentKind.CubicBezier, end, control1, control2);

    public static ShapeSegment ArcTo(LayoutPoint end, double radiusX, double radiusY, bool sweepClockwise, bool largeArc = false) =>
        new(ShapeSegmentKind.Arc, end, RadiusX: radiusX, RadiusY: radiusY, LargeArc: largeArc, SweepClockwise: sweepClockwise);
}

/// <summary>
/// A single connected contour of a shape: a starting point, a sequence of <see cref="Segments"/>,
/// a <see cref="Closed"/> flag (closed contours are filled and their last point joins the start),
/// and a <see cref="Filled"/> flag (false for open strokes such as connectors and guide lines).
/// Ported from FreeX.App.Presentation.Shapes.ShapeContour.
/// </summary>
public sealed record ShapeContour(
    LayoutPoint Start,
    IReadOnlyList<ShapeSegment> Segments,
    bool Closed,
    bool Filled);

/// <summary>
/// Portable outline of a drawing shape: one or more contours. The renderers convert this into their
/// own geometry type. Empty geometry (degenerate bounds) is represented by an empty contour list.
/// Ported from FreeX.App.Presentation.Shapes.ShapeGeometry.
/// </summary>
public sealed record ShapeGeometry(IReadOnlyList<ShapeContour> Contours)
{
    public static readonly ShapeGeometry Empty = new([]);
}
