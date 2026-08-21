using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// Converts the portable <see cref="ShapeGeometry"/> produced by
/// <see cref="ShapeGeometryBuilder"/> into a WPF <see cref="Geometry"/> for the grid's drawing
/// layer. All shape math lives in the shared presentation layer; this adapter only maps the pure
/// contour/segment description onto <see cref="System.Windows.Media"/> primitives.
/// </summary>
public static class ShapeGeometryWpfAdapter
{
    /// <summary>
    /// Builds the WPF geometry for <paramref name="kind"/> within <paramref name="rect"/> by
    /// delegating the outline math to <see cref="ShapeGeometryBuilder"/>.
    /// </summary>
    public static Geometry Create(DrawingShapeKind kind, Rect rect)
    {
        var shape = ShapeGeometryBuilder.Build(
            kind,
            LayoutRect.FromCorners(rect.Left, rect.Top, rect.Right, rect.Bottom));
        return ToGeometry(shape);
    }

    private static Geometry ToGeometry(ShapeGeometry shape)
    {
        // Preset callouts and compound shapes use overlapping contours. The default even-odd
        // rule turns their overlap into a cutout (for example, a white callout tail); DrawingML
        // treats those contours as a filled union.
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
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
