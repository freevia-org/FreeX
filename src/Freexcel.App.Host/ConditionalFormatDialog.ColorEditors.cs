using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private void SelectColor(CellColor color)
    {
        var wc = Color.FromRgb(color.R, color.G, color.B);
        for (var i = 0; i < ColorOptions.Length; i++)
        {
            if (ColorOptions[i].FillColor == wc)
            {
                _colorBox.SelectedIndex = i;
                _customFormatStyle = null;
                break;
            }
        }

        if (_colorBox.SelectedIndex < 0 || ColorOptions[_colorBox.SelectedIndex].FillColor != wc)
        {
            _customFormatStyle = new CellStyle { FillColor = color };
            _colorBox.SelectedItem = "Custom Format...";
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
        _colorBox.SelectedItem = "Custom Format...";
    }

    private (string Label, Color FillColor, Color? FontColor, bool Bold) SelectedColorPreset()
    {
        var index = _colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex;
        return ColorOptions[index];
    }

    private CellColor SelectedDataBarColor(Color fallback) =>
        _colorBox.SelectedItem as string == "Custom Format..." && _customFormatStyle?.FillColor is { } custom
            ? custom
            : new CellColor(fallback.R, fallback.G, fallback.B);

    private CellStyle BuildSelectedCellStyle()
    {
        if (_colorBox.SelectedItem as string == "Custom Format..." && _customFormatStyle is not null)
            return _customFormatStyle.Clone();

        var selected = SelectedColorPreset();
        var style = new CellStyle
        {
            FillColor = new CellColor(selected.FillColor.R, selected.FillColor.G, selected.FillColor.B),
            Bold = selected.Bold
        };
        if (selected.FontColor is { } fontColor)
            style.FontColor = new CellColor(fontColor.R, fontColor.G, fontColor.B);

        return style;
    }

    private Button CreateDataBarColorButton()
    {
        var button = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(6, 4, 0, 12),
            ToolTip = "Choose data bar color"
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
}
