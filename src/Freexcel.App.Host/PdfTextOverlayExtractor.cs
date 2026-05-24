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
        else if (element is AccessText accessText && NormalizeAccessText(accessText.Text) is { Length: > 0 } accessTextValue)
        {
            overlays.Add(new PdfTextOverlay(
                accessTextValue,
                x,
                y,
                accessText.FontSize,
                accessText.FontFamily.Source,
                accessText.FontWeight >= FontWeights.SemiBold,
                accessText.FontStyle == FontStyles.Italic || accessText.FontStyle == FontStyles.Oblique,
                ResolveColor(accessText.Foreground)));
        }
        else if (element is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
        {
            overlays.Add(new PdfTextOverlay(
                textBox.Text,
                x + textBox.Padding.Left,
                y + textBox.Padding.Top,
                textBox.FontSize,
                textBox.FontFamily.Source,
                textBox.FontWeight >= FontWeights.SemiBold,
                textBox.FontStyle == FontStyles.Italic || textBox.FontStyle == FontStyles.Oblique,
                ResolveColor(textBox.Foreground)));
        }
        else if (element is ContentControl { Content: string contentText } contentControl &&
                 !string.IsNullOrWhiteSpace(contentText))
        {
            overlays.Add(new PdfTextOverlay(
                contentText,
                x + contentControl.Padding.Left,
                y + contentControl.Padding.Top,
                contentControl.FontSize,
                contentControl.FontFamily.Source,
                contentControl.FontWeight >= FontWeights.SemiBold,
                contentControl.FontStyle == FontStyles.Italic || contentControl.FontStyle == FontStyles.Oblique,
                ResolveColor(contentControl.Foreground)));
        }
        else if (element is HeaderedContentControl { Header: string headerText } headeredContentControl &&
                 !string.IsNullOrWhiteSpace(headerText))
        {
            overlays.Add(new PdfTextOverlay(
                headerText,
                x + headeredContentControl.Padding.Left,
                y + headeredContentControl.Padding.Top,
                headeredContentControl.FontSize,
                headeredContentControl.FontFamily.Source,
                headeredContentControl.FontWeight >= FontWeights.SemiBold,
                headeredContentControl.FontStyle == FontStyles.Italic || headeredContentControl.FontStyle == FontStyles.Oblique,
                ResolveColor(headeredContentControl.Foreground)));
        }
        else if (element is Glyphs glyphs && !string.IsNullOrEmpty(glyphs.UnicodeString))
        {
            overlays.Add(new PdfTextOverlay(
                glyphs.UnicodeString,
                x,
                y,
                glyphs.FontRenderingEmSize > 0 ? glyphs.FontRenderingEmSize : 12,
                ResolveGlyphFontFamily(glyphs),
                Bold: false,
                Italic: false,
                ResolveColor(glyphs.Fill)));
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
    }

    private static string ExtractText(TextBlock textBlock)
    {
        if (!string.IsNullOrEmpty(textBlock.Text))
            return textBlock.Text;

        var parts = new List<string>();
        foreach (var inline in textBlock.Inlines)
            AppendInlineText(inline, parts);

        return string.Concat(parts);
    }

    private static void AppendInlineText(Inline inline, List<string> parts)
    {
        switch (inline)
        {
            case Run run:
                parts.Add(run.Text);
                break;
            case LineBreak:
                parts.Add("\n");
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    AppendInlineText(child, parts);
                break;
        }
    }

    private static string NormalizeAccessText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text.Replace("__", "\u0000", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("\u0000", "_", StringComparison.Ordinal);
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

    private static string ResolveGlyphFontFamily(Glyphs glyphs)
    {
        var source = glyphs.FontUri?.ToString();
        if (string.IsNullOrWhiteSpace(source))
            return "Arial";

        var lastSlash = source.LastIndexOf('/');
        var name = lastSlash >= 0 && lastSlash + 1 < source.Length
            ? source[(lastSlash + 1)..]
            : source;

        return string.IsNullOrWhiteSpace(name) ? "Arial" : name;
    }
}
