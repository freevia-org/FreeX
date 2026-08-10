using Avalonia;
using Avalonia.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

internal static class CustomShapePathAvaloniaAdapter
{
    public static StreamGeometry Build(CustomGeometry geometry, Rect bounds, bool isFilled)
    {
        var nativeGeometry = new StreamGeometry();
        using (var context = nativeGeometry.Open())
        {
            foreach (var figure in CustomShapePathPlanner.Build(
                         geometry,
                         new CustomShapePathBounds(
                             bounds.X,
                             bounds.Y,
                             bounds.Width,
                             bounds.Height)))
            {
                context.BeginFigure(ToPoint(figure.Start), isFilled);
                foreach (var command in figure.Commands)
                {
                    if (command.Kind == CustomShapePathCommandKind.LineTo)
                    {
                        context.LineTo(ToPoint(command.Point));
                    }
                    else if (command.ControlPoint1 is { } control1
                        && command.ControlPoint2 is { } control2)
                    {
                        context.CubicBezierTo(
                            ToPoint(control1),
                            ToPoint(control2),
                            ToPoint(command.Point));
                    }
                }

                if (figure.IsClosed)
                    context.EndFigure(true);
            }
        }

        return nativeGeometry;
    }

    private static Point ToPoint(CustomShapePathPoint point) => new(point.X, point.Y);
}
