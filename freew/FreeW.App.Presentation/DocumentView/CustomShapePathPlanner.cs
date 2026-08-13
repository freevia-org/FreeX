using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum CustomShapePathCommandKind
{
    LineTo,
    CubicBezierTo
}

public readonly record struct CustomShapePathPoint(double X, double Y);

public sealed record CustomShapePathCommand(
    CustomShapePathCommandKind Kind,
    CustomShapePathPoint Point,
    CustomShapePathPoint? ControlPoint1 = null,
    CustomShapePathPoint? ControlPoint2 = null);

public sealed record CustomShapePathFigure(
    CustomShapePathPoint Start,
    IReadOnlyList<CustomShapePathCommand> Commands,
    bool IsClosed);

public readonly record struct CustomShapePathBounds(
    double X,
    double Y,
    double Width,
    double Height,
    bool InvertY = false);

/// <summary>
/// Interprets FreeW custom-geometry segments into framework-neutral, scaled path figures.
/// Native renderers only project these figures into their own geometry APIs.
/// </summary>
public static class CustomShapePathPlanner
{
    public static IReadOnlyList<CustomShapePathFigure> Build(
        CustomGeometry? geometry,
        CustomShapePathBounds bounds)
    {
        if (geometry is null
            || geometry.Segments.Count == 0
            || geometry.Width == 0
            || geometry.Height == 0)
        {
            return [];
        }

        CustomShapePathPoint Map(CustomPoint point)
        {
            var x = bounds.X + point.X / (double)geometry.Width * bounds.Width;
            var normalizedY = point.Y / (double)geometry.Height * bounds.Height;
            var y = bounds.InvertY
                ? bounds.Y + bounds.Height - normalizedY
                : bounds.Y + normalizedY;
            return new CustomShapePathPoint(x, y);
        }

        var figures = new List<CustomShapePathFigure>();
        CustomShapePathPoint? start = null;
        var commands = new List<CustomShapePathCommand>();
        var isClosed = false;

        void Flush()
        {
            if (start is { } figureStart)
                figures.Add(new CustomShapePathFigure(figureStart, commands.ToArray(), isClosed));
            start = null;
            commands.Clear();
            isClosed = false;
        }

        foreach (var segment in geometry.Segments)
        {
            switch (segment.Kind)
            {
                case CustomSegmentKind.MoveTo when segment.Point is { } point:
                    Flush();
                    start = Map(point);
                    break;
                case CustomSegmentKind.LineTo when start is not null && segment.Point is { } point:
                    commands.Add(new CustomShapePathCommand(
                        CustomShapePathCommandKind.LineTo,
                        Map(point)));
                    break;
                case CustomSegmentKind.CubicBezierTo when start is not null
                    && segment.Point is { } point
                    && segment.ControlPoint1 is { } control1
                    && segment.ControlPoint2 is { } control2:
                    commands.Add(new CustomShapePathCommand(
                        CustomShapePathCommandKind.CubicBezierTo,
                        Map(point),
                        Map(control1),
                        Map(control2)));
                    break;
                case CustomSegmentKind.Close when start is not null:
                    isClosed = true;
                    break;
            }
        }

        Flush();
        return figures;
    }
}
