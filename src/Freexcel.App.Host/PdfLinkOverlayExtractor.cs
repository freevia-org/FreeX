using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal sealed record PdfLinkOverlay(
    string Target,
    HyperlinkTargetKind TargetKind,
    double X,
    double Y,
    double Width,
    double Height);

internal static class PdfLinkOverlayExtractor
{
    public static IReadOnlyList<PdfLinkOverlay> Extract(FixedPage page)
    {
        var overlays = new List<PdfLinkOverlay>();
        foreach (UIElement child in page.Children)
            Extract(child, 0, 0, overlays);

        return overlays;
    }

    private static void Extract(UIElement element, double parentX, double parentY, List<PdfLinkOverlay> overlays)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var x = parentX + ReadLeft(element);
        var y = parentY + ReadTop(element);

        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        var renderTranslation = ReadSimpleTranslation(element.RenderTransform);
        x += renderTranslation.X;
        y += renderTranslation.Y;

        if (element is VisualHost { LinkOverlays.Count: > 0 } visualHost)
        {
            foreach (var overlay in visualHost.LinkOverlays)
            {
                overlays.Add(overlay with
                {
                    X = x + overlay.X,
                    Y = y + overlay.Y
                });
            }
        }

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                Extract(child, x, y, overlays);
        }
        else if (element is Decorator { Child: UIElement decoratorChild })
        {
            Extract(decoratorChild, x, y, overlays);
        }
        else if (element is ContentControl { Content: UIElement contentChild })
        {
            Extract(contentChild, x, y, overlays);
        }

        if (element is HeaderedContentControl { Header: UIElement headerChild })
            Extract(headerChild, x, y, overlays);

        if (element is ItemsControl itemsControlWithElementItems)
        {
            foreach (var item in WpfTextContentExtractor.EnumerateVisibleItemElements(itemsControlWithElementItems))
                Extract(item, x, y, overlays);
        }
    }

    private static double ReadLeft(UIElement element)
    {
        var left = Canvas.GetLeft(element);
        return double.IsNaN(left) ? 0 : left;
    }

    private static double ReadTop(UIElement element)
    {
        var top = Canvas.GetTop(element);
        return double.IsNaN(top) ? 0 : top;
    }

    private static Vector ReadSimpleTranslation(Transform? transform)
    {
        return TryReadSimpleTranslation(transform, out var translation)
            ? translation
            : default;
    }

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
