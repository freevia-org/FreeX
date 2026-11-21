using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Localization;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

// Excel's Auto Fill Options button: after a fill-handle drag, a small button at the bottom-right of
// the filled range offers Copy Cells, Fill Series, Fill Formatting Only and Fill Without Formatting.
// Mirrors WPF's MainWindow.AutofillOptions.
public partial class MainWindow
{
    private Button? _autofillOptionsButton;

    private void AddAutofillOptionsOverlay(Canvas overlay, ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        _autofillOptionsButton = null;
        if (_session.AutofillOptions is not { } state ||
            state.Fill.Start.Sheet != _session.ActiveSheet.Id ||
            !TryGetDisplayedCellBounds(viewport, state.Fill.End, showHeadings, zoomFactor, out var left, out var top, out var width, out var height))
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var item in AutofillOptionsPlanner.Items(state.Mode))
        {
            var menuItem = new MenuItem
            {
                Header = item.Header,
                Icon = item.IsChecked ? new TextBlock { Text = "✓" } : null,
            };
            var mode = item.Mode;
            menuItem.Click += (_, _) => ApplyAutofillOption(mode);
            menu.Items.Add(menuItem);
        }

        var button = new Button
        {
            Height = AutofillOptionsPlanner.ButtonHeight,
            MinHeight = 0,
            Padding = new Thickness(2, 0, 3, 0),
            Background = Brushes.White,
            BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Content = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                Children =
                {
                    AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Fill, 14),
                    new TextBlock { Text = "▾", FontSize = 9, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center },
                },
            },
            Flyout = menu,
        };
        ToolTip.SetTip(button, AutofillOptionsPlanner.ButtonToolTip);
        AutomationProperties.SetAutomationId(button, AutofillOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(button, AutofillOptionsPlanner.ButtonToolTip);
        Canvas.SetLeft(button, left + width + 1);
        Canvas.SetTop(button, top + height + 1);
        overlay.Children.Add(button);
        _autofillOptionsButton = button;
    }

    internal void ApplyAutofillOption(AutofillMode mode)
    {
        var result = _session.ApplyAutofillOption(mode);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_AutofillFailed"));
            return;
        }

        RefreshShell(UiText.Get("MainLoc_Ready"));
    }
}
