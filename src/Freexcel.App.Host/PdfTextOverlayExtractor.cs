using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Freexcel.App.Host;

internal sealed record PdfTextOverlay(
    string Text,
    double X,
    double Y,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic,
    Color Color);

internal static class PdfTextOverlayExtractor
{
    public static IReadOnlyList<PdfTextOverlay> Extract(FixedPage page)
    {
        var overlays = new List<PdfTextOverlay>();
        foreach (UIElement child in page.Children)
            Extract(child, 0, 0, overlays);

        return overlays;
    }

    private static void Extract(UIElement element, double parentX, double parentY, List<PdfTextOverlay> overlays)
    {
        var x = parentX + ReadLeft(element);
        var y = parentY + ReadTop(element);

        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        if (element is TextBlock textBlock && ExtractText(textBlock) is { Length: > 0 } text)
        {
            overlays.Add(new PdfTextOverlay(
                text,
                x,
                y,
                textBlock.FontSize,
                textBlock.FontFamily.Source,
                textBlock.FontWeight >= FontWeights.SemiBold,
                textBlock.FontStyle == FontStyles.Italic || textBlock.FontStyle == FontStyles.Oblique,
                ResolveColor(textBlock.Foreground)));
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
    }

    private static string ExtractText(TextBlock textBlock)
    {
        if (!string.IsNullOrEmpty(textBlock.Text))
            return textBlock.Text;

        var parts = new List<string>();
        foreach (var inline in textBlock.Inlines)
        {
            switch (inline)
            {
                case Run run:
                    parts.Add(run.Text);
                    break;
                case LineBreak:
                    parts.Add("\n");
                    break;
            }
        }

        return string.Concat(parts);
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

    private static Color ResolveColor(Brush brush) =>
        brush is SolidColorBrush solid
            ? solid.Color
            : Colors.Black;
}
