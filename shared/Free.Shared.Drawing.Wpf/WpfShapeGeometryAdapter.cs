using System.Windows;
using System.Windows.Media;

namespace Free.Shared.Drawing.Wpf;

/// <summary>Converts portable shape contours into frozen WPF stream geometry.</summary>
public static class WpfShapeGeometryAdapter
{
    /// <summary>
    /// Builds a frozen <see cref="StreamGeometry"/> from <paramref name="shape"/>. An empty shape
    /// yields an empty geometry.
    /// </summary>
    /// <param name="shape">The portable contour description.</param>
    /// <param name="fillRule">
    /// The fill rule for overlapping contours. <see cref="FillRule.Nonzero"/> paints overlapping
    /// contours (callout tails, compound presets) as a filled union; <see cref="FillRule.EvenOdd"/>
    /// (the WPF default, and the default here) turns same-direction overlap into a cutout, which is
    /// what authored free-form paths that draw holes with same-direction subpaths rely on.
    /// </param>
    public static StreamGeometry ToGeometry(ShapeGeometry shape, FillRule fillRule = FillRule.EvenOdd)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var geometry = new StreamGeometry { FillRule = fillRule };
        using (var context = geometry.Open())
        {
            foreach (var contour in shape.Contours)
                AppendContour(context, contour);
        }

        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }

    private static void AppendContour(StreamGeometryContext context, ShapeContour contour)
    {
        context.BeginFigure(ToPoint(contour.Start), contour.Filled, contour.Closed);
        foreach (var segment in contour.Segments)
        {
            switch (segment.Kind)
            {
                case ShapeSegmentKind.Line:
                    context.LineTo(ToPoint(segment.End), isStroked: true, isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.CubicBezier:
                    context.BezierTo(
                        ToPoint(segment.Control1),
                        ToPoint(segment.Control2),
                        ToPoint(segment.End),
                        isStroked: true,
                        isSmoothJoin: false);
                    break;
                case ShapeSegmentKind.Arc:
                    context.ArcTo(
                        ToPoint(segment.End),
                        new Size(segment.RadiusX, segment.RadiusY),
                        rotationAngle: 0,
                        segment.LargeArc,
                        segment.SweepClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        isStroked: true,
                        isSmoothJoin: false);
                    break;
            }
        }
    }

    private static Point ToPoint(LayoutPoint point) => new(point.X, point.Y);
}
