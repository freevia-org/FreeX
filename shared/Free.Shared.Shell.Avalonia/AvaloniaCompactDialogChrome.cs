using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaCompactDialogChromeStyle(FontFamily FontFamily)
{
    public double ControlHeight { get; init; } = 24;
    public double FontSize { get; init; } = 12;
    public Thickness ButtonPadding { get; init; } = new(4, 1);
    public Thickness TextBoxPadding { get; init; } = new(4, 1);
    public Thickness ComboBoxPadding { get; init; } = new(5, 0, 4, 0);
    public Thickness ListBoxItemPadding { get; init; } = new(4, 1);
    public double ListBoxItemMinHeight { get; init; } = 24;
}

/// <summary>
/// Shared compact dialog chrome for Avalonia dialog controls that mirror Excel/WPF 24px metrics.
/// </summary>
public static class AvaloniaCompactDialogChrome
{
    private static readonly IBrush DefaultButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IBrush ButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(112, 112, 112));
    private static readonly IBrush InputBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(130, 130, 130));

    public static void ApplyButton(
        Button button,
        AvaloniaCompactDialogChromeStyle style,
        double minWidth,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(style);

        button.MinWidth = minWidth;
        button.Height = style.ControlHeight;
        button.MinHeight = style.ControlHeight;
        button.MaxHeight = style.ControlHeight;
        button.Padding = style.ButtonPadding;
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? DefaultButtonBorderBrush : ButtonBorderBrush;
        button.BorderThickness = new Thickness(1);
        button.FontSize = style.FontSize;
        button.FontFamily = style.FontFamily;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyTextBox(TextBox textBox, AvaloniaCompactDialogChromeStyle style, bool fixedHeight = true)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(style);

        if (fixedHeight)
        {
            textBox.Height = style.ControlHeight;
            textBox.MinHeight = style.ControlHeight;
            textBox.MaxHeight = style.ControlHeight;
        }
        textBox.Padding = style.TextBoxPadding;
        textBox.FontSize = style.FontSize;
        textBox.FontFamily = style.FontFamily;
        textBox.BorderBrush = InputBorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyComboBox(ComboBox comboBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(style);

        comboBox.Height = style.ControlHeight;
        comboBox.MinHeight = style.ControlHeight;
        comboBox.MaxHeight = style.ControlHeight;
        comboBox.Padding = style.ComboBoxPadding;
        comboBox.FontSize = style.FontSize;
        comboBox.FontFamily = style.FontFamily;
        comboBox.BorderBrush = InputBorderBrush;
        comboBox.BorderThickness = new Thickness(1);
        comboBox.VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static void ApplyCheckBox(CheckBox checkBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(style);

        checkBox.FontSize = style.FontSize;
        checkBox.FontFamily = style.FontFamily;
    }

    public static void ApplyRadioButton(RadioButton radioButton, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(style);

        radioButton.FontSize = style.FontSize;
        radioButton.FontFamily = style.FontFamily;
    }

    public static void ApplyListBox(ListBox listBox, AvaloniaCompactDialogChromeStyle style)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(style);

        listBox.FontSize = style.FontSize;
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, style.ListBoxItemPadding),
                new Setter(Layoutable.MinHeightProperty, style.ListBoxItemMinHeight),
                new Setter(TemplatedControl.FontSizeProperty, style.FontSize),
            },
        });
    }

    public static StackPanel CreateActionRow(IReadOnlyList<Control> controls, Thickness margin = default)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = margin,
        };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }
}
