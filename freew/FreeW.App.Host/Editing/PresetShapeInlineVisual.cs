using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// WPF projection of an inline shape drawn from an outline (a DrawingML preset or a freeform): the
/// outline path, plus the shape's text centred over it when the shape carries any.
/// </summary>
internal static class PresetShapeInlineVisual
{
    public static FrameworkElement Build(
        Shape shape,
        CustomGeometry outline,
        double widthPx,
        double heightPx,
        Brush fill,
        Brush stroke,
        double strokeThickness)
    {
        var path = new System.Windows.Shapes.Path
        {
            Width = widthPx,
            Height = heightPx,
            Data = CustomShapePathWpfAdapter.Build(outline, 0, 0, widthPx, heightPx),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
        };
        if (!shape.HasText)
            return path;

        var text = new TextBlock
        {
            Text = shape.PlainText,
            Margin = new Thickness(Math.Min(widthPx, heightPx) * 0.14),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (shape.TextDirection == ShapeTextDirection.Rotate90)
            text.LayoutTransform = new RotateTransform(90);
        else if (shape.TextDirection == ShapeTextDirection.Rotate270)
            text.LayoutTransform = new RotateTransform(270);

        var grid = new Grid { Width = widthPx, Height = heightPx };
        grid.Children.Add(path);
        grid.Children.Add(text);
        return grid;
    }
}
