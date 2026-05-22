using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class ChartDialogHelpers
{
    public static StackPanel DialogStack() => new() { Margin = new Thickness(16) };

    public static GroupBox CreateGroupBox(string header, UIElement content) =>
        new()
        {
            Header = header,
            Content = content,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

    public static TextBlock CreateInlineHelp(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 6)
        };

    public static void AddCheck(Panel stack, CheckBox checkBox)
    {
        checkBox.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(checkBox);
    }

    public static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
    {
        stack.Children.Add(new Label { Content = label, Target = comboBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 4) });
        comboBox.ItemsSource = items;
        comboBox.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(comboBox);
    }

    public static void AddText(Panel stack, string label, TextBox textBox)
    {
        stack.Children.Add(new Label { Content = label, Target = textBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 4) });
        textBox.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(textBox);
    }

    public static void AddNumericText(Panel stack, string label, TextBox textBox, string helpText)
    {
        AddText(stack, label, textBox);
        AutomationProperties.SetHelpText(textBox, helpText);
        textBox.ToolTip = helpText;
    }

    public static void AddColorText(Panel stack, string label, TextBox textBox)
    {
        stack.Children.Add(new Label { Content = label, Target = textBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 0, 4) });
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, $"Pick {label}");
        pickerButton.Click += ColorPickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        stack.Children.Add(panel);
    }

    private static void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        var initialColor = ParseColor(textBox.Text);
        var dialog = new ColorPickerDialog(initialColor, allowNoColor: true)
        {
            Owner = Window.GetWindow(textBox)
        };
        if (dialog.ShowDialog() != true)
            return;

        textBox.Text = dialog.SelectedColor is { } color
            ? FormatColor(color)
            : "none";
    }

    public static T Selected<T>(ComboBox comboBox, T fallback) =>
        comboBox.SelectedItem is T value ? value : fallback;

    public static CellColor? ParseColor(string text) =>
        ColorInputParser.TryParseOptionalHexColor(text, out var color) ? color : null;

    public static string FormatColor(CellColor? color) =>
        color is null ? "none" : ColorInputParser.FormatHexColor(color.Value);

    public static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public static double? ParseNullableDouble(string text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : ChartDialogHelpers.ParseDouble(text, 0);

    public static string FormatNullable(double? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
