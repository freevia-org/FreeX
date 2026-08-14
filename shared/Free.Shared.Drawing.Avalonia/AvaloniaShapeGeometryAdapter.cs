using Avalonia;
using Avalonia.Media;

namespace Free.Shared.Drawing.Avalonia;

/// <summary>Converts portable shape contours into Avalonia stream geometry.</summary>
public static class AvaloniaShapeGeometryAdapter
{
    public static StreamGeometry? ToGeometry(ShapeGeometry shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (shape.Contours.Count == 0)
            return null;

        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        foreach (var contour in shape.Contours)
        {
            context.BeginFigure(ToPoint(contour.Start), isFilled: contour.Filled);
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case ShapeSegmentKind.Line:
                        context.LineTo(ToPoint(segment.End));
                        break;
                    case ShapeSegmentKind.CubicBezier:
                        context.CubicBezierTo(
                            ToPoint(segment.Control1),
                            ToPoint(segment.Control2),
                            ToPoint(segment.End));
                        break;
                    case ShapeSegmentKind.Arc:
                        context.ArcTo(
                            ToPoint(segment.End),
                            new Size(segment.RadiusX, segment.RadiusY),
                            rotationAngle: 0,
                            segment.LargeArc,
                            segment.SweepClockwise
                                ? SweepDirection.Clockwise
                                : SweepDirection.CounterClockwise);
                        break;
                }
            }

            context.EndFigure(isClosed: contour.Closed);
        }

        return geometry;
    }

    private static Point ToPoint(LayoutPoint point) => new(point.X, point.Y);
}
