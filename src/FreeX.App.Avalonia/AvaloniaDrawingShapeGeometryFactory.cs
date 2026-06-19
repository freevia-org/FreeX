using Avalonia;
using Avalonia.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds Avalonia <see cref="Geometry"/> outlines for <see cref="DrawingShapeKind"/> values so the
/// Avalonia shell can render real shape silhouettes (triangles, arrows, stars, flowchart symbols,
/// callouts, signs, etc.) instead of falling back to a plain rectangle. The shape math lives in the
/// portable <see cref="ShapeGeometryBuilder"/>; this adapter only translates the resulting
/// <see cref="ShapeGeometry"/> contours into an Avalonia <see cref="StreamGeometry"/>. Geometry is
/// authored inside a (0,0,width,height) box.
/// Returns <c>null</c> for kinds best handled by the existing Ellipse / Line / Rectangle render path.
/// </summary>
internal static class AvaloniaDrawingShapeGeometryFactory
{
    public static Geometry? CreateGeometry(DrawingShapeKind kind, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return null;

        // Ellipse, Line and plain Rectangle stay on the dedicated control path. The portable builder
        // emits real geometry for these too, so the adapter must opt out explicitly to preserve the
        // call site's null-means-use-the-dedicated-control contract.
        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
            case DrawingShapeKind.Ellipse:
            case DrawingShapeKind.Line:
                return null;
        }

        var shape = ShapeGeometryBuilder.Build(kind, new LayoutRect(0, 0, width, height));
        if (shape.Contours.Count == 0)
            return null;

        return ToGeometry(shape);
    }

    private static Geometry ToGeometry(ShapeGeometry shape)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var contour in shape.Contours)
            {
                ctx.BeginFigure(ToPoint(contour.Start), isFilled: contour.Filled);
                foreach (var segment in contour.Segments)
                {
                    switch (segment.Kind)
                    {
                        case ShapeSegmentKind.Line:
                            ctx.LineTo(ToPoint(segment.End));
                            break;
                        case ShapeSegmentKind.CubicBezier:
                            ctx.CubicBezierTo(ToPoint(segment.Control1), ToPoint(segment.Control2), ToPoint(segment.End));
                            break;
                        case ShapeSegmentKind.Arc:
                            ctx.ArcTo(
                                ToPoint(segment.End),
                                new Size(segment.RadiusX, segment.RadiusY),
                                0,
                                segment.LargeArc,
                                segment.SweepClockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                            break;
                    }
                }

                ctx.EndFigure(isClosed: contour.Closed);
            }
        }

        return geometry;
    }

    private static Point ToPoint(LayoutPoint point) => new(point.X, point.Y);
}
