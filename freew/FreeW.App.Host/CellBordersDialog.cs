using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

internal sealed class CellBordersDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _style = new();
    private readonly TextBox _width = new() { Text = "0.5", Width = 60 };
    private readonly TextBlock _validation = new() { Foreground = Brushes.Firebrick };
    private int _presetIndex = -1;
    private int _colorIndex;
    private CellBordersDialogResult? _result;

    private CellBordersDialog(Window? owner)
    {
        var text = CellBordersDialogPlanner.ResolveText(UiText.Get);
        Owner = owner;
        Title = text.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, CellBordersDialogPlanner.AutomationId);

        var outer = new StackPanel { Margin = new Thickness(10), MinWidth = 330 };
        outer.Children.Add(Label(text.PresetLabel, semiBold: true));
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Button? apply = null;
        for (var index = 0; index < CellBordersDialogPlanner.Presets.Count; index++)
        {
            var preset = CellBordersDialogPlanner.Presets[index];
            var button = new Button
            {
                Content = preset.Label,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 3, 8, 3),
            };
            AutomationProperties.SetAutomationId(button, $"{CellBordersDialogPlanner.PresetAutomationId}.{index}");
            var selectedIndex = index;
            button.Click += (_, _) =>
            {
                _presetIndex = selectedIndex;
                if (apply is not null)
                    apply.IsEnabled = true;
            };
            presets.Children.Add(button);
        }
        outer.Children.Add(presets);

        outer.Children.Add(Label(text.StyleLabel));
        _style.ItemsSource = CellBordersDialogPlanner.LineStyleNames;
        _style.SelectedIndex = 0;
        _style.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(_style, CellBordersDialogPlanner.StyleAutomationId);
        outer.Children.Add(_style);

        outer.Children.Add(Label(text.ColorLabel));
        var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        // Swatches are focusable Buttons (not Borders with only a mouse handler) so a keyboard-only
        // user can Tab to each color and pick it with Enter/Space -- the same fix already shipped for
        // the Avalonia twin (FreeW.App.Avalonia/CellBordersDialog.cs) and for this dialog's WPF sibling
        // CellShadingDialog.cs; ported here rather than inventing a second mouse-only pattern.
        Button? selectedSwatch = null;
        for (var index = 0; index < CellBordersDialogPlanner.Palette.Count; index++)
        {
            var hex = CellBordersDialogPlanner.Palette[index];
            var swatch = new Button
            {
                Width = 20,
                Height = 20,
                MinWidth = 0,
                MinHeight = 0,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(index == 0 ? 2 : 1),
                ToolTip = hex,
                Focusable = true,
            };
            AutomationProperties.SetName(swatch, hex);
            var selectedIndex = index;
            swatch.Click += (_, _) =>
            {
                _colorIndex = selectedIndex;
                if (selectedSwatch is not null)
                    selectedSwatch.BorderThickness = new Thickness(1);
                swatch.BorderThickness = new Thickness(2);
                selectedSwatch = swatch;
            };
            if (index == 0)
                selectedSwatch = swatch;
            colors.Children.Add(swatch);
        }
        outer.Children.Add(colors);

        outer.Children.Add(Label(text.WidthLabel));
        _width.Margin = new Thickness(0, 0, 0, 4);
        _width.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_width, CellBordersDialogPlanner.WidthAutomationId);
        outer.Children.Add(_width);
        AutomationProperties.SetAutomationId(_validation, CellBordersDialogPlanner.ValidationAutomationId);
        outer.Children.Add(_validation);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        apply = new Button
        {
            Content = text.ApplyLabel,
            IsEnabled = false,
            IsDefault = true,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0),
        };
        apply.Click += (_, _) => Accept();
        var cancel = new Button
        {
            Content = text.CancelLabel,
            IsCancel = true,
            MinWidth = 72,
        };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        outer.Children.Add(actions);
        Content = outer;
    }

    private static TextBlock Label(string text, bool semiBold = false) => new()
    {
        Text = text,
        FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private void Accept()
    {
        if (!CellBordersDialogPlanner.TryBuildResult(
                new CellBordersDialogInput(
                    _presetIndex,
                    _style.SelectedIndex,
                    _colorIndex,
                    _width.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            _validation.Text = validation;
            DialogFocus.FocusAndSelect(_width);
            return;
        }

        _result = result;
        DialogResult = true;
    }

    public static CellBordersDialogResult? Prompt(Window? owner)
    {
        var dialog = new CellBordersDialog(owner);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
