using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class DrawingShapeGeometryFactory
{
    public static Geometry Create(DrawingShapeKind kind, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return CreateEmptyGeometry();

        return kind switch
        {
            DrawingShapeKind.RoundedRectangle => Freeze(new RectangleGeometry(rect, CornerRadius(rect), CornerRadius(rect))),
            DrawingShapeKind.Ellipse => Freeze(new EllipseGeometry(rect)),
            DrawingShapeKind.Line => OpenPath(rect, [P(rect, 0.02, 0.02), P(rect, 0.98, 0.98)]),
            DrawingShapeKind.ElbowConnector => OpenPath(rect, [P(rect, 0.05, 0.18), P(rect, 0.55, 0.18), P(rect, 0.55, 0.82), P(rect, 0.95, 0.82)]),
            DrawingShapeKind.CurvedConnector => CurvedConnector(rect),
            DrawingShapeKind.Triangle => Polygon(rect, [(0.5, 0), (1, 1), (0, 1)]),
            DrawingShapeKind.RightTriangle => Polygon(rect, [(0, 0), (1, 1), (0, 1)]),
            DrawingShapeKind.Diamond => Polygon(rect, [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)]),
            DrawingShapeKind.Parallelogram => Polygon(rect, [(0.2, 0), (1, 0), (0.8, 1), (0, 1)]),
            DrawingShapeKind.Trapezoid => Polygon(rect, [(0.2, 0), (0.8, 0), (1, 1), (0, 1)]),
            DrawingShapeKind.Pentagon => Polygon(rect, [(0.5, 0), (1, 0.38), (0.82, 1), (0.18, 1), (0, 0.38)]),
            DrawingShapeKind.Hexagon => Polygon(rect, [(0.25, 0), (0.75, 0), (1, 0.5), (0.75, 1), (0.25, 1), (0, 0.5)]),
            DrawingShapeKind.Octagon => Polygon(rect, [(0.3, 0), (0.7, 0), (1, 0.3), (1, 0.7), (0.7, 1), (0.3, 1), (0, 0.7), (0, 0.3)]),
            DrawingShapeKind.Cross or DrawingShapeKind.PlusSign => Plus(rect),
            DrawingShapeKind.RightArrow => Polygon(rect, [(0, 0.25), (0.62, 0.25), (0.62, 0), (1, 0.5), (0.62, 1), (0.62, 0.75), (0, 0.75)]),
            DrawingShapeKind.LeftArrow => Polygon(rect, [(1, 0.25), (0.38, 0.25), (0.38, 0), (0, 0.5), (0.38, 1), (0.38, 0.75), (1, 0.75)]),
            DrawingShapeKind.UpArrow => Polygon(rect, [(0.25, 1), (0.25, 0.38), (0, 0.38), (0.5, 0), (1, 0.38), (0.75, 0.38), (0.75, 1)]),
            DrawingShapeKind.DownArrow => Polygon(rect, [(0.25, 0), (0.75, 0), (0.75, 0.62), (1, 0.62), (0.5, 1), (0, 0.62), (0.25, 0.62)]),
            DrawingShapeKind.LeftRightArrow => Polygon(rect, [(0, 0.5), (0.24, 0), (0.24, 0.28), (0.76, 0.28), (0.76, 0), (1, 0.5), (0.76, 1), (0.76, 0.72), (0.24, 0.72), (0.24, 1)]),
            DrawingShapeKind.UpDownArrow => Polygon(rect, [(0.5, 0), (1, 0.24), (0.72, 0.24), (0.72, 0.76), (1, 0.76), (0.5, 1), (0, 0.76), (0.28, 0.76), (0.28, 0.24), (0, 0.24)]),
            DrawingShapeKind.MinusSign => Minus(rect),
            DrawingShapeKind.MultiplySign => Multiply(rect),
            DrawingShapeKind.DivideSign => Divide(rect),
            DrawingShapeKind.EqualSign => Equal(rect),
            DrawingShapeKind.NotEqualSign => NotEqual(rect),
            DrawingShapeKind.FlowchartDecision => Polygon(rect, [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)]),
            DrawingShapeKind.FlowchartData => Polygon(rect, [(0.22, 0), (1, 0), (0.78, 1), (0, 1)]),
            DrawingShapeKind.FlowchartPredefinedProcess => FlowchartPredefinedProcess(rect),
            DrawingShapeKind.FlowchartDocument => FlowchartDocument(rect),
            DrawingShapeKind.FlowchartTerminator => Freeze(new RectangleGeometry(rect, Math.Max(1, rect.Height / 2), Math.Max(1, rect.Height / 2))),
            DrawingShapeKind.Star5 => Star(rect, 5, 0.42),
            DrawingShapeKind.Star8 => Star(rect, 8, 0.46),
            DrawingShapeKind.Explosion => Star(rect, 12, 0.62, startAngle: -Math.PI / 2 + 0.08),
            DrawingShapeKind.Ribbon => Ribbon(rect),
            DrawingShapeKind.Wave => Wave(rect),
            DrawingShapeKind.RectangularCallout => Polygon(rect, [(0, 0), (1, 0), (1, 0.72), (0.64, 0.72), (0.48, 1), (0.42, 0.72), (0, 0.72)]),
            DrawingShapeKind.RoundedRectangularCallout => RoundedCallout(rect),
            DrawingShapeKind.OvalCallout => OvalCallout(rect),
            DrawingShapeKind.LineCallout => LineCallout(rect),
            _ => Freeze(new RectangleGeometry(rect))
        };
    }

    private static double CornerRadius(Rect rect) =>
        Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.18, 2, 18);

    private static Point P(Rect rect, double x, double y) =>
        new(rect.Left + rect.Width * x, rect.Top + rect.Height * y);

    private static Geometry Polygon(Rect rect, IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
            return CreateEmptyGeometry();

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(rect, points[0].X, points[0].Y), isFilled: true, isClosed: true);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(P(rect, points[i].X, points[i].Y), isStroked: true, isSmoothJoin: false);
        }

        return Freeze(geometry);
    }

    private static Geometry OpenPath(Rect rect, IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return CreateEmptyGeometry();

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (var i = 1; i < points.Count; i++)
                context.LineTo(points[i], isStroked: true, isSmoothJoin: false);
        }

        return Freeze(geometry);
    }

    private static Geometry CurvedConnector(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(rect, 0.05, 0.18), isFilled: false, isClosed: false);
            context.BezierTo(P(rect, 0.36, 0.04), P(rect, 0.52, 0.88), P(rect, 0.95, 0.82), isStroked: true, isSmoothJoin: false);
        }

        return Freeze(geometry);
    }

    private static Geometry Plus(Rect rect) =>
        Polygon(rect, [(0.35, 0), (0.65, 0), (0.65, 0.35), (1, 0.35), (1, 0.65), (0.65, 0.65), (0.65, 1), (0.35, 1), (0.35, 0.65), (0, 0.65), (0, 0.35), (0.35, 0.35)]);

    private static Geometry Minus(Rect rect) =>
        Freeze(new RectangleGeometry(new Rect(rect.Left, rect.Top + rect.Height * 0.38, rect.Width, rect.Height * 0.24)));

    private static Geometry Multiply(Rect rect)
    {
        var thickness = Math.Min(rect.Width, rect.Height) * 0.16;
        var group = new GeometryGroup();
        group.Children.Add(RotatedBar(rect, thickness, 45));
        group.Children.Add(RotatedBar(rect, thickness, -45));
        return Freeze(group);
    }

    private static Geometry Divide(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(Minus(rect));
        var dotSize = Math.Min(rect.Width, rect.Height) * 0.16;
        group.Children.Add(new EllipseGeometry(new Rect(rect.Left + rect.Width * 0.5 - dotSize / 2, rect.Top + rect.Height * 0.12, dotSize, dotSize)));
        group.Children.Add(new EllipseGeometry(new Rect(rect.Left + rect.Width * 0.5 - dotSize / 2, rect.Top + rect.Height * 0.72, dotSize, dotSize)));
        return Freeze(group);
    }

    private static Geometry Equal(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(rect.Left, rect.Top + rect.Height * 0.28, rect.Width, rect.Height * 0.18)));
        group.Children.Add(new RectangleGeometry(new Rect(rect.Left, rect.Top + rect.Height * 0.56, rect.Width, rect.Height * 0.18)));
        return Freeze(group);
    }

    private static Geometry NotEqual(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(Equal(rect));
        group.Children.Add(RotatedBar(rect, Math.Min(rect.Width, rect.Height) * 0.12, -63));
        return Freeze(group);
    }

    private static Geometry RotatedBar(Rect rect, double thickness, double degrees)
    {
        var bar = new RectangleGeometry(new Rect(
            rect.Left + rect.Width * 0.08,
            rect.Top + rect.Height * 0.5 - thickness / 2,
            rect.Width * 0.84,
            thickness));
        bar.Transform = new RotateTransform(degrees, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        return bar;
    }

    private static Geometry FlowchartPredefinedProcess(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(rect));
        group.Children.Add(OpenPath(rect, [P(rect, 0.18, 0), P(rect, 0.18, 1)]));
        group.Children.Add(OpenPath(rect, [P(rect, 0.82, 0), P(rect, 0.82, 1)]));
        return Freeze(group);
    }

    private static Geometry FlowchartDocument(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(rect.TopLeft, isFilled: true, isClosed: true);
            context.LineTo(rect.TopRight, isStroked: true, isSmoothJoin: false);
            context.LineTo(P(rect, 1, 0.82), isStroked: true, isSmoothJoin: false);
            context.BezierTo(P(rect, 0.72, 0.72), P(rect, 0.48, 0.98), P(rect, 0.22, 0.86), isStroked: true, isSmoothJoin: false);
            context.BezierTo(P(rect, 0.12, 0.82), P(rect, 0.05, 0.80), P(rect, 0, 0.86), isStroked: true, isSmoothJoin: false);
        }

        return Freeze(geometry);
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
        Polygon(rect, [(0.08, 0.22), (0.92, 0.22), (0.92, 0.06), (1, 0.24), (0.92, 0.42), (0.92, 0.78), (0.08, 0.78), (0.08, 0.94), (0, 0.76), (0.08, 0.58)]);

    private static Geometry Wave(Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(rect, 0, 0.45), isFilled: true, isClosed: true);
            context.BezierTo(P(rect, 0.22, 0.12), P(rect, 0.38, 0.78), P(rect, 0.58, 0.45), isStroked: true, isSmoothJoin: false);
            context.BezierTo(P(rect, 0.74, 0.18), P(rect, 0.88, 0.24), P(rect, 1, 0.36), isStroked: true, isSmoothJoin: false);
            context.LineTo(P(rect, 1, 0.72), isStroked: true, isSmoothJoin: false);
            context.BezierTo(P(rect, 0.78, 0.56), P(rect, 0.58, 1.02), P(rect, 0.36, 0.72), isStroked: true, isSmoothJoin: false);
            context.BezierTo(P(rect, 0.18, 0.48), P(rect, 0.08, 0.62), P(rect, 0, 0.74), isStroked: true, isSmoothJoin: false);
        }

        return Freeze(geometry);
    }

    private static Geometry RoundedCallout(Rect rect)
    {
        var body = new Rect(rect.Left, rect.Top, rect.Width, rect.Height * 0.74);
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(body, CornerRadius(body), CornerRadius(body)));
        group.Children.Add(Polygon(rect, [(0.42, 0.72), (0.64, 0.72), (0.48, 1)]));
        return Freeze(group);
    }

    private static Geometry OvalCallout(Rect rect)
    {
        var body = new Rect(rect.Left, rect.Top, rect.Width, rect.Height * 0.78);
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(body));
        group.Children.Add(Polygon(rect, [(0.42, 0.70), (0.64, 0.70), (0.48, 1)]));
        return Freeze(group);
    }

    private static Geometry LineCallout(Rect rect)
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(rect.Left + rect.Width * 0.24, rect.Top, rect.Width * 0.76, rect.Height * 0.58)));
        group.Children.Add(OpenPath(rect, [P(rect, 0.02, 1), P(rect, 0.24, 0.58)]));
        return Freeze(group);
    }

    private static Geometry CreateEmptyGeometry()
    {
        var geometry = new StreamGeometry();
        return Freeze(geometry);
    }

    private static T Freeze<T>(T geometry)
        where T : Geometry
    {
        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }
}
