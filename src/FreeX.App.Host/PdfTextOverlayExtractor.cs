using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeX.App.Host;

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

        if (element is VisualHost { TextOverlays.Count: > 0 } visualHost)
        {
            foreach (var overlay in visualHost.TextOverlays)
            {
                overlays.Add(overlay with
                {
                    X = x + overlay.X,
                    Y = y + overlay.Y
                });
            }
        }

        if (element is TextBlock textBlock && WpfTextContentExtractor.ExtractText(textBlock) is { Length: > 0 } text)
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
        else if (element is AccessText accessText && WpfTextContentExtractor.NormalizeAccessText(accessText.Text) is { Length: > 0 } accessTextValue)
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
        else if (element is RichTextBox richTextBox &&
                 WpfTextContentExtractor.ExtractFlowDocumentText(richTextBox.Document) is { Length: > 0 } richText)
        {
            overlays.Add(new PdfTextOverlay(
                richText,
                x + richTextBox.Padding.Left,
                y + richTextBox.Padding.Top,
                richTextBox.FontSize,
                richTextBox.FontFamily.Source,
                richTextBox.FontWeight >= FontWeights.SemiBold,
                richTextBox.FontStyle == FontStyles.Italic || richTextBox.FontStyle == FontStyles.Oblique,
                ResolveColor(richTextBox.Foreground)));
        }
        else if (element is FlowDocumentScrollViewer flowDocumentViewer &&
                 WpfTextContentExtractor.ExtractFlowDocumentText(flowDocumentViewer.Document) is { Length: > 0 } flowText)
        {
            overlays.Add(new PdfTextOverlay(
                flowText,
                x + flowDocumentViewer.Padding.Left,
                y + flowDocumentViewer.Padding.Top,
                flowDocumentViewer.FontSize,
                flowDocumentViewer.FontFamily.Source,
                flowDocumentViewer.FontWeight >= FontWeights.SemiBold,
                flowDocumentViewer.FontStyle == FontStyles.Italic || flowDocumentViewer.FontStyle == FontStyles.Oblique,
                ResolveColor(flowDocumentViewer.Foreground)));
        }
        else if (element is HeaderedContentControl headeredContentControl &&
                 WpfTextContentExtractor.ExtractHeaderedContentText(headeredContentControl) is { Length: > 0 } headeredText)
        {
            overlays.Add(new PdfTextOverlay(
                headeredText,
                x + headeredContentControl.Padding.Left,
                y + headeredContentControl.Padding.Top,
                headeredContentControl.FontSize,
                headeredContentControl.FontFamily.Source,
                headeredContentControl.FontWeight >= FontWeights.SemiBold,
                headeredContentControl.FontStyle == FontStyles.Italic || headeredContentControl.FontStyle == FontStyles.Oblique,
                ResolveColor(headeredContentControl.Foreground)));
        }
        else if (element is ContentControl contentControl &&
                 WpfTextContentExtractor.ExtractContentText(contentControl.Content) is { Length: > 0 } contentText)
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
        else if (element is ComboBox { IsDropDownOpen: false } comboBox)
        {
            if (WpfTextContentExtractor.ExtractComboBoxSelectionText(comboBox) is { Length: > 0 } comboBoxText)
            {
                overlays.Add(new PdfTextOverlay(
                    comboBoxText,
                    x,
                    y,
                    comboBox.FontSize,
                    comboBox.FontFamily.Source,
                    comboBox.FontWeight >= FontWeights.SemiBold,
                    comboBox.FontStyle == FontStyles.Italic || comboBox.FontStyle == FontStyles.Oblique,
                    ResolveColor(comboBox.Foreground)));
            }
        }
        else if (element is ItemsControl itemsControl && WpfTextContentExtractor.ExtractItemsText(itemsControl) is { Length: > 0 } itemsText)
        {
            overlays.Add(new PdfTextOverlay(
                itemsText,
                x,
                y,
                itemsControl.FontSize,
                itemsControl.FontFamily.Source,
                itemsControl.FontWeight >= FontWeights.SemiBold,
                itemsControl.FontStyle == FontStyles.Italic || itemsControl.FontStyle == FontStyles.Oblique,
                ResolveColor(itemsControl.Foreground)));
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

        if (element is ItemsControl itemsControlWithElementItems)
        {
            foreach (var item in WpfTextContentExtractor.EnumerateVisibleItemElements(itemsControlWithElementItems))
            {
                Extract(item, x, y, overlays);
            }
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
