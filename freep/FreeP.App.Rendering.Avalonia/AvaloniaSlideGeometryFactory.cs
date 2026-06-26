using Avalonia;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Converts a <see cref="ShapeGeometry"/> (portable contours of Line/CubicBezier/Arc)
/// to an Avalonia <see cref="StreamGeometry"/>.
///
/// This mirrors <c>AvaloniaDrawingShapeGeometryFactory</c> in FreeX.App.Avalonia:
/// contour Start → BeginFigure, segments → LineTo/CubicBezierTo/ArcTo, EndFigure.
/// </summary>
internal static class AvaloniaSlideGeometryFactory
{
    /// <summary>
    /// Converts all contours in <paramref name="shape"/> into a single Avalonia
    /// <see cref="StreamGeometry"/> ready for <see cref="DrawingContext.DrawGeometry"/>.
    /// Returns null when the shape has no contours.
    /// </summary>
    internal static StreamGeometry? ToGeometry(ShapeGeometry shape)
    {
        if (shape.Contours.Count == 0)
            return null;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var contour in shape.Contours)
            {
                ctx.BeginFigure(ToPoint(contour.Start), isFilled: contour.Filled);

                foreach (var seg in contour.Segments)
                {
                    switch (seg.Kind)
                    {
                        case ShapeSegmentKind.Line:
                            ctx.LineTo(ToPoint(seg.End));
                            break;

                        case ShapeSegmentKind.CubicBezier:
                            ctx.CubicBezierTo(
                                ToPoint(seg.Control1),
                                ToPoint(seg.Control2),
                                ToPoint(seg.End));
                            break;

                        case ShapeSegmentKind.Arc:
                            ctx.ArcTo(
                                ToPoint(seg.End),
                                new Size(seg.RadiusX, seg.RadiusY),
                                rotationAngle: 0,
                                seg.LargeArc,
                                seg.SweepClockwise
                                    ? SweepDirection.Clockwise
                                    : SweepDirection.CounterClockwise);
                            break;
                    }
                }

                ctx.EndFigure(isClosed: contour.Closed);
            }
        }

        return geometry;
    }

    private static Point ToPoint(LayoutPoint p) => new(p.X, p.Y);
}
