using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private void SelectColor(CellColor color)
    {
        for (var i = 0; i < ColorOptions.Count; i++)
        {
            if (ColorOptions[i].FillColor == color)
            {
                _colorBox.SelectedIndex = i;
                _customFormatStyle = null;
                break;
            }
        }

        if (_colorBox.SelectedIndex < 0 || ColorOptions[_colorBox.SelectedIndex].FillColor != color)
        {
            _customFormatStyle = new CellStyle { FillColor = color };
            _colorBox.SelectedItem = UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat");
        }
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = SelectedColorPreset();
        var initial = _customFormatStyle?.FillColor
            ?? new CellColor(preset.FillColor.R, preset.FillColor.G, preset.FillColor.B);
        var dialog = new ColorPickerDialog(initial) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedColor is not { } color)
            return;

        _customFormatStyle = new CellStyle { FillColor = color };
        _colorBox.SelectedItem = UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat");
    }

    private ConditionalFormatDialogColorPreset SelectedColorPreset()
    {
        var index = _colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex;
        return ColorOptions[index];
    }

    private CellColor SelectedDataBarColor(CellColor fallback) =>
        _colorBox.SelectedItem as string == UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat") && _customFormatStyle?.FillColor is { } custom
            ? custom
            : fallback;

    private CellStyle BuildSelectedCellStyle()
    {
        if (_colorBox.SelectedItem as string == UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat") && _customFormatStyle is not null)
            return _customFormatStyle.Clone();

        return SelectedColorPreset().ToCellStyle();
    }

    private Button CreateDataBarColorButton()
    {
        var button = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(6, 4, 0, 12),
            ToolTip = UiText.Get("ConditionalFormatDialog_ChooseDataBarColorToolTip")
        };
        button.Click += FormatButton_Click;
        return button;
    }

    private static DockPanel CreateDataBarColorEditor(ComboBox colorBox, Button pickerButton)
    {
        var panel = new DockPanel();
        DockPanel.SetDock(pickerButton, Dock.Right);
        panel.Children.Add(pickerButton);
        panel.Children.Add(colorBox);
        return panel;
    }

    private static Button CreateColorScaleColorButton(TextBox colorBox, string tooltip)
    {
        var button = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(6, 4, 0, 8),
            ToolTip = tooltip,
            Tag = colorBox
        };
        button.Click += ColorScaleColorButton_Click;
        return button;
    }

    private static DockPanel CreateColorScaleColorEditor(TextBox colorBox, Button pickerButton)
    {
        var panel = new DockPanel();
        DockPanel.SetDock(pickerButton, Dock.Right);
        panel.Children.Add(pickerButton);
        panel.Children.Add(colorBox);
        return panel;
    }

    private static void ColorScaleColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox colorBox })
            return;

        CellColor? initialColor = ColorInputParser.TryParseRgbColorText(colorBox.Text, out var parsed)
            ? parsed
            : null;
        var dialog = new ColorPickerDialog(initialColor) { Owner = Window.GetWindow(colorBox) };
        if (dialog.ShowDialog() == true && dialog.SelectedColor is { } selected)
            colorBox.Text = FormatRgb(new RgbColor(selected.R, selected.G, selected.B));
    }

    private void UpdateColorScaleMidpointState()
    {
        var enabled = _colorScaleUseThreeColorBox.IsChecked == true;
        _colorScaleMidTypeBox.IsEnabled = enabled;
        _colorScaleMidValueBox.IsEnabled = enabled;
        _colorScaleMidColorBox.IsEnabled = enabled;
        _colorScaleMidColorButton.IsEnabled = enabled;
    }

    private static Button CreateDataBarOptionalColorButton(TextBox colorBox, string tooltip)
    {
        var button = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(6, 4, 0, 8),
            ToolTip = tooltip,
            Tag = colorBox
        };
        button.Click += DataBarOptionalColorButton_Click;
        return button;
    }

    private static DockPanel CreateDataBarOptionalColorEditor(TextBox colorBox, Button pickerButton)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(pickerButton, Dock.Right);
        panel.Children.Add(pickerButton);
        panel.Children.Add(colorBox);
        return panel;
    }

    private static void DataBarOptionalColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox colorBox })
            return;

        CellColor? initialColor = ColorInputParser.TryParseRgbColorText(colorBox.Text, out var parsed)
            ? parsed
            : null;
        var dialog = new ColorPickerDialog(initialColor, allowNoColor: true) { Owner = Window.GetWindow(colorBox) };
        if (dialog.ShowDialog() != true)
            return;

        colorBox.Text = dialog.SelectedColor is { } selected
            ? FormatRgb(new RgbColor(selected.R, selected.G, selected.B))
            : string.Empty;
    }
}
