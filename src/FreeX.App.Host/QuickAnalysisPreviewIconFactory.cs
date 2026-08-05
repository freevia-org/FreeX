using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Host;

public static class QuickAnalysisPreviewIconFactory
{
    public static FrameworkElement Create(QuickAnalysisPreviewIconPlan plan) =>
        QuickAnalysisPreviewIconRenderAdapter<Canvas, UIElement>.Render(
            plan,
            new WpfQuickAnalysisPreviewIconRenderPrimitives());

    private sealed class WpfQuickAnalysisPreviewIconRenderPrimitives
        : IQuickAnalysisPreviewIconRenderPrimitives<Canvas, UIElement>
    {
        public Canvas CreateRoot(QuickAnalysisPreviewIconPlan plan) =>
            new()
            {
                Width = plan.Width,
                Height = plan.Height,
                Margin = new Thickness(0, 0, 6, 0)
            };

        public UIElement CreateRectangle(QuickAnalysisPreviewIconRectangle rectangle)
        {
            var rect = new Rectangle
            {
                Width = rectangle.Width,
                Height = rectangle.Height,
                Fill = ToBrush(rectangle.Fill),
                Stroke = ToBrush(rectangle.Stroke),
                StrokeThickness = rectangle.StrokeThickness
            };
            Canvas.SetLeft(rect, rectangle.Left);
            Canvas.SetTop(rect, rectangle.Top);
            return rect;
        }

        public UIElement CreateEllipse(QuickAnalysisPreviewIconEllipse ellipse)
        {
            var ellipseShape = new Ellipse
            {
                Width = ellipse.Size,
                Height = ellipse.Size,
                Fill = ToBrush(ellipse.Fill)
            };
            Canvas.SetLeft(ellipseShape, ellipse.Left);
            Canvas.SetTop(ellipseShape, ellipse.Top);
            return ellipseShape;
        }

        public UIElement CreateLine(QuickAnalysisPreviewIconLine line) =>
            new Line
            {
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                Stroke = ToBrush(line.Stroke),
                StrokeThickness = line.StrokeThickness
            };

        public UIElement CreatePolygon(QuickAnalysisPreviewIconPolygon polygon)
        {
            var points = new PointCollection();
            foreach (var point in polygon.Points)
                points.Add(new Point(point.X, point.Y));

            return new Polygon
            {
                Points = points,
                Fill = ToBrush(polygon.Fill)
            };
        }

        public UIElement CreateText(QuickAnalysisPreviewIconText text)
        {
            var textBlock = new TextBlock
            {
                Text = text.Text,
                FontSize = text.FontSize,
                FontWeight = ToFontWeight(text.FontWeight),
                Foreground = ToBrush(text.Foreground)
            };
            Canvas.SetLeft(textBlock, text.Left);
            Canvas.SetTop(textBlock, text.Top);
            return textBlock;
        }

        public void AddChild(Canvas root, UIElement element) =>
            root.Children.Add(element);

        private static Brush ToBrush(QuickAnalysisPreviewIconColor color) =>
            new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

        private static Brush? ToBrush(QuickAnalysisPreviewIconColor? color) =>
            color is { } value ? ToBrush(value) : null;

        private static FontWeight ToFontWeight(QuickAnalysisPreviewIconFontWeight fontWeight) =>
            fontWeight == QuickAnalysisPreviewIconFontWeight.SemiBold
                ? FontWeights.SemiBold
                : FontWeights.Normal;
    }
}
