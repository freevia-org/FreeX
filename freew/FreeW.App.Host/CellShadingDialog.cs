using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// The app-owned WPF Table Design &gt; Cell Shading picker. Swatches apply immediately, while
/// Escape or closing the window cancels without changing the document. The palette policy and
/// logical geometry are shared with the Avalonia implementation through the presentation planner.
/// </summary>
internal sealed class CellShadingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private CellShadingDialogResult? _result;

    private CellShadingDialog(Window? owner)
    {
        Owner = owner;
        Title = CellShadingDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var layout = CellShadingDialogPlanner.Layout;
        var panel = new StackPanel { Margin = new Thickness(layout.PanelMargin) };
        var palette = new WrapPanel { Width = layout.PaletteWidth };
        for (var index = 0; index < CellShadingDialogPlanner.Palette.Count; index++)
        {
            var choice = CellShadingDialogPlanner.Palette[index];
            var swatch = new Button
            {
                Width = layout.SwatchSize,
                Height = layout.SwatchSize,
                MinWidth = 0,
                MinHeight = 0,
                Margin = new Thickness(layout.SwatchMargin),
                Padding = new Thickness(0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(layout.SwatchBorderHex)),
                BorderThickness = new Thickness(1),
                ToolTip = choice.Hex,
                Focusable = true,
            };
            AutomationProperties.SetAutomationId(swatch, CellShadingDialogPlanner.SwatchAutomationId(index));
            AutomationProperties.SetName(swatch, choice.Label);
            var selectedIndex = index;
            swatch.Click += (_, _) => Accept(CellShadingDialogPlanner.SelectPaletteColor(selectedIndex));
            palette.Children.Add(swatch);
        }

        panel.Children.Add(palette);
        var clear = new Button
        {
            Content = CellShadingDialogPlanner.NoColorLabel,
            Margin = new Thickness(layout.ClearHorizontalMargin, layout.ClearTopMargin, layout.ClearHorizontalMargin, 0),
            Padding = new Thickness(layout.ClearHorizontalPadding, 2, layout.ClearHorizontalPadding, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = true,
        };
        AutomationProperties.SetAutomationId(clear, CellShadingDialogPlanner.NoColorAutomationId);
        clear.Click += (_, _) => Accept(CellShadingDialogPlanner.SelectNoColor());
        panel.Children.Add(clear);

        Content = panel;
        Loaded += (_, _) =>
        {
            if (palette.Children[0] is Button first)
                first.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape)
                return;

            DialogResult = false;
            e.Handled = true;
        };
    }

    private void Accept(CellShadingDialogResult result)
    {
        _result = result;
        DialogResult = true;
    }

    public static CellShadingDialogResult? Prompt(Window? owner)
    {
        var dialog = new CellShadingDialog(owner);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
