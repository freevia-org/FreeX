using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

internal static class CustomShapePathWpfAdapter
{
    public static StreamGeometry Build(
        CustomGeometry geometry,
        double x,
        double y,
        double width,
        double height)
    {
        var nativeGeometry = new StreamGeometry();
        using (var context = nativeGeometry.Open())
        {
            foreach (var figure in CustomShapePathPlanner.Build(
                         geometry,
                         new CustomShapePathBounds(x, y, width, height)))
            {
                context.BeginFigure(ToPoint(figure.Start), isFilled: true, isClosed: figure.IsClosed);
                foreach (var command in figure.Commands)
                {
                    if (command.Kind == CustomShapePathCommandKind.LineTo)
                    {
                        context.LineTo(ToPoint(command.Point), isStroked: true, isSmoothJoin: false);
                    }
                    else if (command.ControlPoint1 is { } control1
                        && command.ControlPoint2 is { } control2)
                    {
                        context.BezierTo(
                            ToPoint(control1),
                            ToPoint(control2),
                            ToPoint(command.Point),
                            isStroked: true,
                            isSmoothJoin: false);
                    }
                }
            }
        }

        nativeGeometry.Freeze();
        return nativeGeometry;
    }

    private static Point ToPoint(CustomShapePathPoint point) => new(point.X, point.Y);
}
