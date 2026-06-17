using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds Avalonia <see cref="Geometry"/> outlines for <see cref="DrawingShapeKind"/> values so the
/// Avalonia shell can render real shape silhouettes (triangles, arrows, stars, flowchart symbols,
/// callouts, signs, etc.) instead of falling back to a plain rectangle. The geometry math mirrors the
/// WPF shape factory; geometries are authored inside a (0,0,width,height) box.
/// Returns <c>null</c> for kinds best handled by the existing Ellipse / Line / Rectangle render path.
/// </summary>
internal static class AvaloniaDrawingShapeGeometryFactory
{
    public static Geometry? CreateGeometry(DrawingShapeKind kind, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return null;

        var rect = new Rect(0, 0, width, height);

        return kind switch
        {
            // Ellipse, Line and plain Rectangle stay on the dedicated control path.
            DrawingShapeKind.Rectangle => null,
            DrawingShapeKind.Ellipse => null,
            DrawingShapeKind.Line => null,

            DrawingShapeKind.RoundedRectangle => RoundedRectangle(rect, CornerRadius(rect)),
            DrawingShapeKind.ElbowConnector => OpenPath(rect, new[] { (0.05, 0.18), (0.55, 0.18), (0.55, 0.82), (0.95, 0.82) }),
            DrawingShapeKind.CurvedConnector => CurvedConnector(rect),
            DrawingShapeKind.Triangle => Polygon(rect, new[] { (0.5, 0.0), (1.0, 1.0), (0.0, 1.0) }),
            DrawingShapeKind.RightTriangle => Polygon(rect, new[] { (0.0, 0.0), (1.0, 1.0), (0.0, 1.0) }),
            DrawingShapeKind.Diamond => Polygon(rect, new[] { (0.5, 0.0), (1.0, 0.5), (0.5, 1.0), (0.0, 0.5) }),
            DrawingShapeKind.Parallelogram => Polygon(rect, new[] { (0.2, 0.0), (1.0, 0.0), (0.8, 1.0), (0.0, 1.0) }),
            DrawingShapeKind.Trapezoid => Polygon(rect, new[] { (0.2, 0.0), (0.8, 0.0), (1.0, 1.0), (0.0, 1.0) }),
            DrawingShapeKind.Pentagon => Polygon(rect, new[] { (0.5, 0.0), (1.0, 0.38), (0.82, 1.0), (0.18, 1.0), (0.0, 0.38) }),
            DrawingShapeKind.Hexagon => Polygon(rect, new[] { (0.25, 0.0), (0.75, 0.0), (1.0, 0.5), (0.75, 1.0), (0.25, 1.0), (0.0, 0.5) }),
            DrawingShapeKind.Octagon => Polygon(rect, new[] { (0.3, 0.0), (0.7, 0.0), (1.0, 0.3), (1.0, 0.7), (0.7, 1.0), (0.3, 1.0), (0.0, 0.7), (0.0, 0.3) }),
            DrawingShapeKind.Cross or DrawingShapeKind.PlusSign => Plus(rect),
            DrawingShapeKind.RightArrow => Polygon(rect, new[] { (0.0, 0.25), (0.62, 0.25), (0.62, 0.0), (1.0, 0.5), (0.62, 1.0), (0.62, 0.75), (0.0, 0.75) }),
            DrawingShapeKind.LeftArrow => Polygon(rect, new[] { (1.0, 0.25), (0.38, 0.25), (0.38, 0.0), (0.0, 0.5), (0.38, 1.0), (0.38, 0.75), (1.0, 0.75) }),
            DrawingShapeKind.UpArrow => Polygon(rect, new[] { (0.25, 1.0), (0.25, 0.38), (0.0, 0.38), (0.5, 0.0), (1.0, 0.38), (0.75, 0.38), (0.75, 1.0) }),
            DrawingShapeKind.DownArrow => Polygon(rect, new[] { (0.25, 0.0), (0.75, 0.0), (0.75, 0.62), (1.0, 0.62), (0.5, 1.0), (0.0, 0.62), (0.25, 0.62) }),
            DrawingShapeKind.LeftRightArrow => Polygon(rect, new[] { (0.0, 0.5), (0.24, 0.0), (0.24, 0.28), (0.76, 0.28), (0.76, 0.0), (1.0, 0.5), (0.76, 1.0), (0.76, 0.72), (0.24, 0.72), (0.24, 1.0) }),
            DrawingShapeKind.UpDownArrow => Polygon(rect, new[] { (0.5, 0.0), (1.0, 0.24), (0.72, 0.24), (0.72, 0.76), (1.0, 0.76), (0.5, 1.0), (0.0, 0.76), (0.28, 0.76), (0.28, 0.24), (0.0, 0.24) }),
            DrawingShapeKind.MinusSign => Minus(rect),
            DrawingShapeKind.MultiplySign => Multiply(rect),
            DrawingShapeKind.DivideSign => Divide(rect),
            DrawingShapeKind.EqualSign => Equal(rect),
            DrawingShapeKind.NotEqualSign => NotEqual(rect),
            DrawingShapeKind.FlowchartProcess => Rectangle(rect),
            DrawingShapeKind.FlowchartDecision => Polygon(rect, new[] { (0.5, 0.0), (1.0, 0.5), (0.5, 1.0), (0.0, 0.5) }),
            DrawingShapeKind.FlowchartData => Polygon(rect, new[] { (0.22, 0.0), (1.0, 0.0), (0.78, 1.0), (0.0, 1.0) }),
            DrawingShapeKind.FlowchartPredefinedProcess => FlowchartPredefinedProcess(rect),
            DrawingShapeKind.FlowchartDocument => FlowchartDocument(rect),
            DrawingShapeKind.FlowchartTerminator => RoundedRectangle(rect, Math.Max(1, rect.Height / 2)),
            DrawingShapeKind.Star5 => Star(rect, 5, 0.42),
            DrawingShapeKind.Star8 => Star(rect, 8, 0.46),
            DrawingShapeKind.Explosion => Star(rect, 12, 0.62, startAngle: -Math.PI / 2 + 0.08),
            DrawingShapeKind.Ribbon => Ribbon(rect),
            DrawingShapeKind.Wave => Wave(rect),
            DrawingShapeKind.RectangularCallout => Polygon(rect, new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 0.72), (0.64, 0.72), (0.48, 1.0), (0.42, 0.72), (0.0, 0.72) }),
            DrawingShapeKind.RoundedRectangularCallout => RoundedCallout(rect),
            DrawingShapeKind.OvalCallout => OvalCallout(rect),
            DrawingShapeKind.LineCallout => LineCallout(rect),
            _ => null
        };
    }

    private static double CornerRadius(Rect rect) =>
        Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.18, 2, 18);

    private static Point P(Rect rect, double x, double y) =>
        new(rect.Left + rect.Width * x, rect.Top + rect.Height * y);

    private static Geometry Polygon(Rect rect, IReadOnlyList<(double X, double Y)> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(rect, points[0].X, points[0].Y), isFilled: true);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(P(rect, points[i].X, points[i].Y));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry OpenPath(Rect rect, IReadOnlyList<(double X, double Y)> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(rect, points[0].X, points[0].Y), isFilled: false);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(P(rect, points[i].X, points[i].Y));
            ctx.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private static Geometry Rectangle(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(rect.TopLeft, isFilled: true);
            ctx.LineTo(rect.TopRight);
            ctx.LineTo(rect.BottomRight);
            ctx.LineTo(rect.BottomLeft);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry RoundedRectangle(Rect rect, double radius)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        if (r <= 0)
            return Rectangle(rect);

        var size = new Size(r, r);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + r, rect.Top), isFilled: true);
            ctx.LineTo(new Point(rect.Right - r, rect.Top));
            ctx.ArcTo(new Point(rect.Right, rect.Top + r), size, 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(rect.Right, rect.Bottom - r));
            ctx.ArcTo(new Point(rect.Right - r, rect.Bottom), size, 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(rect.Left + r, rect.Bottom));
            ctx.ArcTo(new Point(rect.Left, rect.Bottom - r), size, 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(rect.Left, rect.Top + r));
            ctx.ArcTo(new Point(rect.Left + r, rect.Top), size, 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry CurvedConnector(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(rect, 0.05, 0.18), isFilled: false);
            ctx.CubicBezierTo(P(rect, 0.36, 0.04), P(rect, 0.52, 0.88), P(rect, 0.95, 0.82));
            ctx.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private static Geometry Plus(Rect rect) =>
        Polygon(rect, new[]
        {
            (0.35, 0.0), (0.65, 0.0), (0.65, 0.35), (1.0, 0.35), (1.0, 0.65),
            (0.65, 0.65), (0.65, 1.0), (0.35, 1.0), (0.35, 0.65), (0.0, 0.65),
            (0.0, 0.35), (0.35, 0.35)
        });

    private static Geometry Minus(Rect rect) =>
        AxisRect(rect, 0.0, 0.38, 1.0, 0.24);

    private static Geometry AxisRect(Rect rect, double x, double y, double w, double h)
    {
        var bar = new Rect(
            rect.Left + rect.Width * x,
            rect.Top + rect.Height * y,
            rect.Width * w,
            rect.Height * h);
        return new RectangleGeometry(bar);
    }

    private static Geometry Multiply(Rect rect)
    {
        var thickness = Math.Min(rect.Width, rect.Height) * 0.16;
        var group = new GeometryGroup();
        group.Children.Add(RotatedBar(rect, thickness, 45));
        group.Children.Add(RotatedBar(rect, thickness, -45));
        return group;
    }

    private static Geometry Divide(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(Minus(rect));
        var dotSize = Math.Min(rect.Width, rect.Height) * 0.16;
        group.Children.Add(new EllipseGeometry(new Rect(
            rect.Left + rect.Width * 0.5 - dotSize / 2, rect.Top + rect.Height * 0.12, dotSize, dotSize)));
        group.Children.Add(new EllipseGeometry(new Rect(
            rect.Left + rect.Width * 0.5 - dotSize / 2, rect.Top + rect.Height * 0.72, dotSize, dotSize)));
        return group;
    }

    private static Geometry Equal(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(AxisRect(rect, 0.0, 0.28, 1.0, 0.18));
        group.Children.Add(AxisRect(rect, 0.0, 0.56, 1.0, 0.18));
        return group;
    }

    private static Geometry NotEqual(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(Equal(rect));
        group.Children.Add(RotatedBar(rect, Math.Min(rect.Width, rect.Height) * 0.12, -63));
        return group;
    }

    private static Geometry RotatedBar(Rect rect, double thickness, double degrees)
    {
        var bar = new RectangleGeometry(new Rect(
            rect.Left + rect.Width * 0.08,
            rect.Top + rect.Height * 0.5 - thickness / 2,
            rect.Width * 0.84,
            thickness))
        {
            Transform = new RotateTransform(degrees, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)
        };
        return bar;
    }

    private static Geometry FlowchartPredefinedProcess(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(Rectangle(rect));
        group.Children.Add(OpenPath(rect, new[] { (0.18, 0.0), (0.18, 1.0) }));
        group.Children.Add(OpenPath(rect, new[] { (0.82, 0.0), (0.82, 1.0) }));
        return group;
    }

    private static Geometry FlowchartDocument(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(rect.TopLeft, isFilled: true);
            ctx.LineTo(rect.TopRight);
            ctx.LineTo(P(rect, 1, 0.82));
            ctx.CubicBezierTo(P(rect, 0.72, 0.72), P(rect, 0.48, 0.98), P(rect, 0.22, 0.86));
            ctx.CubicBezierTo(P(rect, 0.12, 0.82), P(rect, 0.05, 0.80), P(rect, 0, 0.86));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry Star(Rect rect, int points, double innerRadius, double startAngle = -Math.PI / 2)
    {
        var vertices = new (double X, double Y)[points * 2];
        for (var i = 0; i < vertices.Length; i++)
        {
            var radius = i % 2 == 0 ? 0.5 : 0.5 * innerRadius;
            var angle = startAngle + i * Math.PI / points;
            vertices[i] = (0.5 + Math.Cos(angle) * radius, 0.5 + Math.Sin(angle) * radius);
        }

        return Polygon(rect, vertices);
    }

    private static Geometry Ribbon(Rect rect) =>
        Polygon(rect, new[]
        {
            (0.08, 0.22), (0.92, 0.22), (0.92, 0.06), (1.0, 0.24), (0.92, 0.42),
            (0.92, 0.78), (0.08, 0.78), (0.08, 0.94), (0.0, 0.76), (0.08, 0.58)
        });

    private static Geometry Wave(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(P(rect, 0, 0.45), isFilled: true);
            ctx.CubicBezierTo(P(rect, 0.22, 0.12), P(rect, 0.38, 0.78), P(rect, 0.58, 0.45));
            ctx.CubicBezierTo(P(rect, 0.74, 0.18), P(rect, 0.88, 0.24), P(rect, 1, 0.36));
            ctx.LineTo(P(rect, 1, 0.72));
            ctx.CubicBezierTo(P(rect, 0.78, 0.56), P(rect, 0.58, 1.02), P(rect, 0.36, 0.72));
            ctx.CubicBezierTo(P(rect, 0.18, 0.48), P(rect, 0.08, 0.62), P(rect, 0, 0.74));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Geometry RoundedCallout(Rect rect)
    {
        var body = new Rect(rect.Left, rect.Top, rect.Width, rect.Height * 0.74);
        var group = new GeometryGroup();
        group.Children.Add(RoundedRectangle(body, CornerRadius(body)));
        group.Children.Add(Polygon(rect, new[] { (0.42, 0.72), (0.64, 0.72), (0.48, 1.0) }));
        return group;
    }

    private static Geometry OvalCallout(Rect rect)
    {
        var body = new Rect(rect.Left, rect.Top, rect.Width, rect.Height * 0.78);
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(body));
        group.Children.Add(Polygon(rect, new[] { (0.42, 0.70), (0.64, 0.70), (0.48, 1.0) }));
        return group;
    }

    private static Geometry LineCallout(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(
            rect.Left + rect.Width * 0.24, rect.Top, rect.Width * 0.76, rect.Height * 0.58)));
        group.Children.Add(OpenPath(rect, new[] { (0.02, 1.0), (0.24, 0.58) }));
        return group;
    }
}
