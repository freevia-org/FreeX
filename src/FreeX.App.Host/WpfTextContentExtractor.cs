using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace FreeX.App.Host;

internal static class WpfTextContentExtractor
{
    public static string ExtractText(TextBlock textBlock)
    {
        if (!string.IsNullOrEmpty(textBlock.Text))
            return textBlock.Text;

        var parts = new List<string>();
        foreach (var inline in textBlock.Inlines)
            AppendInlineText(inline, parts);

        return string.Concat(parts);
    }

    public static string ExtractContentText(object? content)
    {
        if (content is null or UIElement)
            return "";

        var text = content.ToString();
        return string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    public static string ExtractHeaderedContentText(HeaderedContentControl control)
    {
        var parts = new List<string>();
        if (ExtractContentText(control.Header) is { Length: > 0 } headerText)
            parts.Add(headerText);
        if (ExtractContentText(control.Content) is { Length: > 0 } contentText)
            parts.Add(contentText);

        return string.Join("\n", parts);
    }

    public static string ExtractItemsText(ItemsControl itemsControl)
    {
        var parts = new List<string>();
        foreach (var item in itemsControl.Items)
        {
            var text = ExtractContentText(item);
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return string.Join("\n", parts);
    }

    public static string ExtractComboBoxSelectionText(ComboBox comboBox)
    {
        if (comboBox.IsEditable && !string.IsNullOrWhiteSpace(comboBox.Text))
            return comboBox.Text;

        var text = ExtractInlineUiTextPart(comboBox.SelectionBoxItem);
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        return ExtractInlineUiTextPart(comboBox.SelectedItem);
    }

    public static IEnumerable<UIElement> EnumerateVisibleItemElements(ItemsControl itemsControl)
    {
        if (itemsControl is ComboBox { IsDropDownOpen: false })
            yield break;

        foreach (var item in itemsControl.Items)
        {
            if (item is UIElement itemElement)
                yield return itemElement;
        }
    }

    public static string ExtractFlowDocumentText(FlowDocument? document)
    {
        if (document is null)
            return "";

        var range = new TextRange(document.ContentStart, document.ContentEnd);
        return range.Text.TrimEnd('\r', '\n');
    }

    public static string NormalizeAccessText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var markerIndex = text.IndexOf('_', StringComparison.Ordinal);
        if (markerIndex < 0)
            return text;

        var normalized = new StringBuilder(text.Length);
        normalized.Append(text, 0, markerIndex);

        for (var i = markerIndex; i < text.Length; i++)
        {
            if (text[i] != '_')
            {
                normalized.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '_')
            {
                normalized.Append('_');
                i++;
            }
        }

        return normalized.ToString();
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
            case InlineUIContainer { Child: UIElement child }:
                if (ExtractInlineUiText(child) is { Length: > 0 } childText)
                    parts.Add(childText);
                break;
        }
    }

    private static string ExtractInlineUiText(UIElement element)
    {
        var parts = new List<string>();
        AppendInlineUiText(element, parts);
        return string.Concat(parts);
    }

    private static void AppendInlineUiText(object? value, List<string> parts)
    {
        switch (value)
        {
            case null:
                return;
            case UIElement { Visibility: not Visibility.Visible }:
                return;
            case TextBlock textBlock:
                parts.Add(ExtractText(textBlock));
                return;
            case AccessText accessText:
                parts.Add(NormalizeAccessText(accessText.Text));
                return;
            case TextBox textBox:
                parts.Add(textBox.Text);
                return;
            case RichTextBox richTextBox:
                parts.Add(ExtractFlowDocumentText(richTextBox.Document));
                return;
            case FlowDocumentScrollViewer flowDocumentViewer:
                parts.Add(ExtractFlowDocumentText(flowDocumentViewer.Document));
                return;
            case ComboBox { IsDropDownOpen: false } comboBox:
                parts.Add(ExtractComboBoxSelectionText(comboBox));
                return;
            case HeaderedContentControl headeredContentControl:
                AppendJoinedInlineUiText(
                    [
                        ExtractInlineUiTextPart(headeredContentControl.Header),
                        ExtractInlineUiTextPart(headeredContentControl.Content)
                    ],
                    parts);
                return;
            case ContentControl contentControl:
                AppendInlineUiText(contentControl.Content, parts);
                return;
            case ItemsControl itemsControl:
                AppendJoinedInlineUiText(
                    itemsControl.Items.Cast<object?>().Select(ExtractInlineUiTextPart),
                    parts);
                return;
            case Panel panel:
                foreach (UIElement child in panel.Children)
                    AppendInlineUiText(child, parts);
                return;
            case Decorator { Child: UIElement child }:
                AppendInlineUiText(child, parts);
                return;
            case UIElement:
                return;
            default:
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
                return;
        }
    }

    private static string ExtractInlineUiTextPart(object? value)
    {
        var parts = new List<string>();
        AppendInlineUiText(value, parts);
        return string.Concat(parts);
    }

    private static void AppendJoinedInlineUiText(IEnumerable<string> values, List<string> parts)
    {
        var joined = string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (joined.Length > 0)
            parts.Add(joined);
    }
}
