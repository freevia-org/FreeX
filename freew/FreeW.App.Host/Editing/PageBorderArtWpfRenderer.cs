using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

public static class PageBorderArtWpfRenderer
{
    public static bool TryDraw(
        DrawingContext context,
        PageBorder border,
        Rect frame,
        double edgeInsetDip)
    {
        if (!PageBorderArtVisualPlanner.TryBuildFramePlan(
                border.ArtId,
                border.WidthPt,
                frame.Width,
                frame.Height,
                edgeInsetDip,
                out var plan))
        {
            return false;
        }

        DrawFramePlan(context, frame, plan);
        return true;
    }

    private static void DrawFramePlan(
        DrawingContext context,
        Rect frame,
        PageBorderArtFramePlan plan)
    {
        foreach (var fill in plan.Fills)
        {
            context.DrawRectangle(
                FrozenBrush(fill.Red, fill.Green, fill.Blue),
                null,
                new Rect(
                    frame.X + fill.Xdip,
                    frame.Y + fill.Ydip,
                    fill.WidthDip,
                    fill.HeightDip));
        }

        foreach (var polygon in plan.Polygons)
        {
            if (polygon.Points.Count == 0)
                continue;

            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(ToPoint(frame, polygon.Points[0]), true, true);
                path.PolyLineTo(
                    polygon.Points.Skip(1).Select(point => ToPoint(frame, point)).ToList(),
                    true,
                    false);
            }
            geometry.Freeze();
            context.DrawGeometry(FrozenBrush(polygon.Red, polygon.Green, polygon.Blue), null, geometry);
        }

        foreach (var line in plan.Lines)
        {
            context.DrawLine(
                FrozenPen(line.Color, line.WidthDip, line.RoundCaps),
                new Point(frame.X + line.Segment.X1Dip, frame.Y + line.Segment.Y1Dip),
                new Point(frame.X + line.Segment.X2Dip, frame.Y + line.Segment.Y2Dip));
        }

        foreach (var figure in plan.CubicFigures)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(ToPoint(frame, figure.Start), figure.Fill is not null, figure.IsClosed);
                foreach (var segment in figure.Segments)
                {
                    path.BezierTo(
                        ToPoint(frame, segment.Control1),
                        ToPoint(frame, segment.Control2),
                        ToPoint(frame, segment.End),
                        true,
                        false);
                }
            }
            geometry.Freeze();
            context.DrawGeometry(
                figure.Fill is { } fill ? FrozenBrush(fill.Red, fill.Green, fill.Blue) : null,
                figure.Stroke is { } stroke
                    ? FrozenPen(stroke, figure.StrokeWidthDip, figure.RoundCaps)
                    : null,
                geometry);
        }
    }

    private static Point ToPoint(Rect frame, PageBorderArtPoint point) =>
        new(frame.X + point.XDip, frame.Y + point.YDip);

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(PageBorderArtColor color, double thickness, bool roundCaps)
    {
        var pen = new Pen(FrozenBrush(color.Red, color.Green, color.Blue), thickness);
        if (roundCaps)
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }
        pen.Freeze();
        return pen;
    }
}
