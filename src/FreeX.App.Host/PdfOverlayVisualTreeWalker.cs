using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeX.App.Host;

internal static class PdfOverlayVisualTreeWalker
{
    public static void Visit(FixedPage page, Action<UIElement, double, double> visitor)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(visitor);

        foreach (UIElement child in page.Children)
            Visit(child, 0, 0, visitor);
    }

    private static void Visit(
        UIElement element,
        double parentX,
        double parentY,
        Action<UIElement, double, double> visitor)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var x = parentX + ReadCoordinate(Canvas.GetLeft(element));
        var y = parentY + ReadCoordinate(Canvas.GetTop(element));

        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        var renderTranslation = ReadSimpleTranslation(element.RenderTransform);
        x += renderTranslation.X;
        y += renderTranslation.Y;

        visitor(element, x, y);

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                Visit(child, x, y, visitor);
        }
        else if (element is Decorator { Child: UIElement decoratorChild })
        {
            Visit(decoratorChild, x, y, visitor);
        }
        else if (element is ContentControl { Content: UIElement contentChild })
        {
            Visit(contentChild, x, y, visitor);
        }

        if (element is HeaderedContentControl { Header: UIElement headerChild })
            Visit(headerChild, x, y, visitor);

        if (element is ItemsControl itemsControl)
        {
            foreach (var item in WpfTextContentExtractor.EnumerateVisibleItemElements(itemsControl))
                Visit(item, x, y, visitor);
        }
    }

    private static double ReadCoordinate(double value) => double.IsNaN(value) ? 0 : value;

    private static Vector ReadSimpleTranslation(Transform? transform) =>
        TryReadSimpleTranslation(transform, out var translation)
            ? translation
            : default;

    private static bool TryReadSimpleTranslation(Transform? transform, out Vector translation)
    {
        if (transform is null || transform == Transform.Identity)
        {
            translation = default;
            return true;
        }

        switch (transform)
        {
            case TranslateTransform translate:
                translation = new Vector(translate.X, translate.Y);
                return true;
            case MatrixTransform matrixTransform when IsOffsetOnly(matrixTransform.Matrix):
                translation = new Vector(matrixTransform.Matrix.OffsetX, matrixTransform.Matrix.OffsetY);
                return true;
            case TransformGroup group:
                return TryReadSimpleTranslation(group, out translation);
            default:
                translation = default;
                return false;
        }
    }

    private static bool TryReadSimpleTranslation(TransformGroup group, out Vector translation)
    {
        var result = new Vector();
        foreach (var child in group.Children)
        {
            if (!TryReadSimpleTranslation(child, out var childTranslation))
            {
                translation = default;
                return false;
            }

            result += childTranslation;
        }

        translation = result;
        return true;
    }

    private static bool IsOffsetOnly(Matrix matrix) =>
        matrix.M11 == 1 &&
        matrix.M12 == 0 &&
        matrix.M21 == 0 &&
        matrix.M22 == 1;
}
