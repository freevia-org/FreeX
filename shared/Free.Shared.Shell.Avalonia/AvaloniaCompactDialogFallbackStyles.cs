using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaCompactDialogFallbackStyleOptions(
    AvaloniaCompactDialogChromeStyle Chrome,
    IBrush BorderBrush,
    IBrush SelectedItemBackgroundBrush,
    IBrush SelectedItemForegroundBrush,
    IBrush TextSelectionBrush);

/// <summary>
/// Builds low-priority application styles for code-built dialogs that are created as raw controls.
/// Product renderers provide theme brushes; shared shell owns WPF-shaped control metrics and selectors.
/// Local dialog styles and explicit property values still take precedence.
/// </summary>
public static class AvaloniaCompactDialogFallbackStyles
{
    public static IEnumerable<IStyle> Create(AvaloniaCompactDialogFallbackStyleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Chrome);
        ArgumentNullException.ThrowIfNull(options.BorderBrush);
        ArgumentNullException.ThrowIfNull(options.SelectedItemBackgroundBrush);
        ArgumentNullException.ThrowIfNull(options.SelectedItemForegroundBrush);
        ArgumentNullException.ThrowIfNull(options.TextSelectionBrush);

        var chrome = options.Chrome;

        yield return new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(
                    TemplatedControl.TemplateProperty,
                    AvaloniaCompactDialogChrome.CreateCompactCheckBoxTemplate(chrome)),
            },
        };

        yield return DisabledOpacityStyle<CheckBox>();

        yield return new Style(x => x.OfType<RadioButton>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(
                    TemplatedControl.TemplateProperty,
                    AvaloniaCompactDialogChrome.CreateCompactRadioButtonTemplate(chrome)),
            },
        };

        yield return DisabledOpacityStyle<RadioButton>();

        yield return new Style(x => x.OfType<TabControl>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        };

        yield return new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, CompactDialogVisualTokens.TabHeaderHeight),
                new Setter(Layoutable.MaxHeightProperty, CompactDialogVisualTokens.TabHeaderHeight),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 2)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1, 1, 1, 0)),
            },
        };

        yield return new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderBrushProperty, options.BorderBrush),
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
            },
        };

        yield return new Style(x => x.OfType<ListBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(TemplatedControl.BorderBrushProperty, options.BorderBrush),
                new Setter(
                    TemplatedControl.BorderThicknessProperty,
                    new Thickness(CompactDialogVisualTokens.BorderThickness)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        };

        yield return new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, chrome.FontSize),
                new Setter(Layoutable.MinHeightProperty, chrome.ListBoxItemMinHeight),
                new Setter(TemplatedControl.PaddingProperty, chrome.ListBoxItemPadding),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            },
        };

        yield return SelectedListItemStyle(options, requireFocus: false);
        yield return SelectedListItemStyle(options, requireFocus: true);

        yield return new Style(x => x.OfType<TextBox>())
        {
            Setters = { new Setter(TextBox.SelectionBrushProperty, options.TextSelectionBrush) },
        };
    }

    private static Style DisabledOpacityStyle<TControl>() where TControl : Control =>
        new(x => x.OfType<TControl>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(Visual.OpacityProperty, CompactDialogVisualTokens.DisabledToggleOpacity),
            },
        };

    private static Style SelectedListItemStyle(
        AvaloniaCompactDialogFallbackStyleOptions options,
        bool requireFocus)
    {
        var style = requireFocus
            ? new Style(x => x.OfType<ListBoxItem>().Class(":selected").Class(":focus"))
            : new Style(x => x.OfType<ListBoxItem>().Class(":selected"));
        style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, options.SelectedItemBackgroundBrush));
        style.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, options.SelectedItemForegroundBrush));
        return style;
    }
}
