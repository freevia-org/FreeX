using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freexcel.App.Host;

public static partial class RibbonIconFactory
{
    private static void DrawText(Canvas canvas, string text, Brush brush, double fontSize, FontWeight weight, double x = 0, double y = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            FontWeight = weight,
            Width = Artboard - x,
            Height = Artboard - y,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static void AddRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush, double radius = 0)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Stroke = brush,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void AddFilledRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void DrawCircle(Canvas canvas, double x, double y, double width, double height, Brush brush, double thickness)
    {
        DrawEllipse(canvas, x, y, width, height, brush, thickness);
    }

    private static void DrawEllipse(Canvas canvas, double x, double y, double width, double height, Brush brush, double thickness)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Stroke = brush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        canvas.Children.Add(ellipse);
    }

    private static void AddFilledCircle(Canvas canvas, double centerX, double centerY, double diameter, Brush brush)
    {
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush
        };
        Canvas.SetLeft(ellipse, centerX - diameter / 2);
        Canvas.SetTop(ellipse, centerY - diameter / 2);
        canvas.Children.Add(ellipse);
    }

    private static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        double thickness = 1.5,
        bool dash = false)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (dash)
            line.StrokeDashArray = new DoubleCollection { 2, 2 };
        canvas.Children.Add(line);
    }

    private static void AddArrowStem(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, bool down)
    {
        AddLine(canvas, x1, y1, x2, y2, brush, 1.5);
        if (down)
            AddPath(canvas, $"M{x2 - 3},{y2 - 3} L{x2},{y2} L{x2 + 3},{y2 - 3}", brush, 1.5);
    }

    private static void AddPath(Canvas canvas, string data, Brush brush, double thickness, Brush? fill = null, double fillOpacity = 1)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = fill ?? Brushes.Transparent,
            Opacity = fill is null ? 1 : fillOpacity
        };
        canvas.Children.Add(path);
    }
}
