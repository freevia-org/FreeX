using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace FreeX.App.Avalonia;

/// <summary>
/// Route-owned visual contract for the Value Field Settings dialog. These values mirror the
/// WPF dialog's logical client metrics without changing the shared Avalonia dialog chrome.
/// </summary>
internal static class PivotValueFieldSettingsVisual
{
    public const double WindowWidth = 430;
    public const double WindowHeight = 430;
    public const double ClientWidth = 414;
    public const double ClientHeight = 391;
    public const double OuterMargin = 14;
    public const double LabelColumnWidth = 118;
    public const double TabContentMargin = 10;
    public const double LabelControlSpacing = 6;
    public const double ControlHeight = 24;
    public const double TextBoxHeight = 18;
    public const double ButtonHeight = 20;
    public const double ButtonWidth = 78;
    public const double NumberFormatButtonWidth = 128;
    public const double ButtonSpacing = 8;
    public const double ButtonTopMargin = 12;

    private static readonly IBrush InputBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(130, 130, 130));
    private static readonly IBrush ComboBoxBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush ButtonBackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(221, 221, 221));
    private static readonly IBrush ButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush DefaultButtonBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 120, 215));

    public static void ApplyTextBox(TextBox textBox, double height = TextBoxHeight)
    {
        textBox.Height = height;
        textBox.MinHeight = height;
        textBox.MaxHeight = height;
        textBox.FontSize = 12;
        textBox.CornerRadius = new CornerRadius(0);
        textBox.Background = Brushes.White;
        textBox.BorderBrush = InputBorderBrush;
        textBox.BorderThickness = new Thickness(1);
    }

    public static void ApplyComboBox(ComboBox comboBox)
    {
        comboBox.Height = ControlHeight;
        comboBox.MinHeight = ControlHeight;
        comboBox.MaxHeight = ControlHeight;
        comboBox.FontSize = 12;
        comboBox.CornerRadius = new CornerRadius(0);
        comboBox.Background = ComboBoxBackgroundBrush;
        comboBox.BorderBrush = InputBorderBrush;
        comboBox.BorderThickness = new Thickness(1);
    }

    public static void ApplyButton(Button button, bool isDefault)
    {
        button.Height = ButtonHeight;
        button.MinHeight = ButtonHeight;
        button.MaxHeight = ButtonHeight;
        button.FontSize = 12;
        button.Padding = new Thickness(12, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.CornerRadius = new CornerRadius(0);
        button.Background = ButtonBackgroundBrush;
        button.BorderBrush = isDefault ? DefaultButtonBorderBrush : ButtonBorderBrush;
        button.BorderThickness = new Thickness(1);
    }
}
