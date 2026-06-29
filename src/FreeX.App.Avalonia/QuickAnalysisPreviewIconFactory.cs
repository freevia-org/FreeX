using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Avalonia;

internal static class QuickAnalysisPreviewIconFactory
{
    public static Control Create(QuickAnalysisPreviewVisual visual)
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(visual);
        var canvas = new Canvas
        {
            Width = plan.Width,
            Height = plan.Height,
            IsHitTestVisible = false,
        };

        foreach (var element in plan.Elements)
            AddElement(canvas, element);

        return canvas;
    }

    private static void AddElement(Canvas canvas, QuickAnalysisPreviewIconElement element)
    {
        switch (element)
        {
            case QuickAnalysisPreviewIconRectangle rectangle:
                var rect = new Rectangle
                {
                    Width = rectangle.Width,
                    Height = rectangle.Height,
                    Fill = ToBrush(rectangle.Fill),
                    Stroke = ToBrush(rectangle.Stroke),
                    StrokeThickness = rectangle.StrokeThickness,
                };
                Canvas.SetLeft(rect, rectangle.Left);
                Canvas.SetTop(rect, rectangle.Top);
                canvas.Children.Add(rect);
                break;

            case QuickAnalysisPreviewIconEllipse ellipse:
                var ellipseShape = new Ellipse
                {
                    Width = ellipse.Size,
                    Height = ellipse.Size,
                    Fill = ToBrush(ellipse.Fill),
                };
                Canvas.SetLeft(ellipseShape, ellipse.Left);
                Canvas.SetTop(ellipseShape, ellipse.Top);
                canvas.Children.Add(ellipseShape);
                break;

            case QuickAnalysisPreviewIconLine line:
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(line.X1, line.Y1),
                    EndPoint = new Point(line.X2, line.Y2),
                    Stroke = ToBrush(line.Stroke),
                    StrokeThickness = line.StrokeThickness,
                });
                break;

            case QuickAnalysisPreviewIconPolygon polygon:
                var points = new Points();
                foreach (var point in polygon.Points)
                    points.Add(new Point(point.X, point.Y));

                canvas.Children.Add(new Polygon
                {
                    Points = points,
                    Fill = ToBrush(polygon.Fill),
                });
                break;

            case QuickAnalysisPreviewIconText text:
                var textBlock = new TextBlock
                {
                    Text = text.Text,
                    FontSize = text.FontSize,
                    FontWeight = ToFontWeight(text.FontWeight),
                    Foreground = ToBrush(text.Foreground),
                };
                Canvas.SetLeft(textBlock, text.Left);
                Canvas.SetTop(textBlock, text.Top);
                canvas.Children.Add(textBlock);
                break;
        }
    }

    private static IBrush ToBrush(QuickAnalysisPreviewIconColor color) =>
        new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

    private static IBrush? ToBrush(QuickAnalysisPreviewIconColor? color) =>
        color is { } value ? ToBrush(value) : null;

    private static FontWeight ToFontWeight(QuickAnalysisPreviewIconFontWeight fontWeight) =>
        fontWeight == QuickAnalysisPreviewIconFontWeight.SemiBold
            ? FontWeight.SemiBold
            : FontWeight.Normal;
}
