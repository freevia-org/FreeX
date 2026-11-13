using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

// Excel's Paste Options button: "(Ctrl)" at the bottom-right of a pasted range, whose menu redoes
// the paste another way (values, formulas, transpose, ...). Mirrors WPF's MainWindow.PasteOptions.
public partial class MainWindow
{
    private readonly PasteOptionsTracker _pasteOptionsTracker = new();
    private bool _pasteOptionsKeysHooked;
    private bool _pasteOptionsCtrlAlone;

    internal Button? PasteOptionsButtonForTest { get; private set; }

    private void AddPasteOptionsOverlay(Canvas overlay, ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        PasteOptionsButtonForTest = null;
        if (_pasteOptionsTracker.Update(_session, _session.SelectedRange) is not { } range ||
            range.Start.Sheet != _session.ActiveSheet.Id ||
            !TryGetDisplayedCellBounds(viewport, range.End, showHeadings, zoomFactor, out var left, out var top, out var width, out var height))
        {
            return;
        }

        var button = CreatePasteOptionsButton(range);
        Canvas.SetLeft(button, left + width + 1);
        Canvas.SetTop(button, top + height + 1);
        overlay.Children.Add(button);
        PasteOptionsButtonForTest = button;
        if (!_pasteOptionsKeysHooked)
        {
            _pasteOptionsKeysHooked = true;
            AddHandler(KeyDownEvent, PasteOptions_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyUpEvent, PasteOptions_KeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    // Excel opens the Paste Options menu when Ctrl is pressed and released on its own, and Esc
    // dismisses the button.
    private void PasteOptions_KeyDown(object? sender, KeyEventArgs e)
    {
        if (PasteOptionsButtonForTest is null)
            return;

        if (e.Key == Key.Escape)
        {
            _pasteOptionsTracker.Dismiss();
            PasteOptionsButtonForTest.IsVisible = false;
            return;
        }

        _pasteOptionsCtrlAlone = e.Key is Key.LeftCtrl or Key.RightCtrl;
    }

    private void PasteOptions_KeyUp(object? sender, KeyEventArgs e)
    {
        if (_pasteOptionsCtrlAlone && e.Key is Key.LeftCtrl or Key.RightCtrl &&
            PasteOptionsButtonForTest is { IsVisible: true, Flyout: { } flyout } button)
        {
            flyout.ShowAt(button);
        }

        _pasteOptionsCtrlAlone = false;
    }

    private Button CreatePasteOptionsButton(GridRange range)
    {
        var menu = new MenuFlyout();
        foreach (var item in PasteOptionsPlanner.Items)
        {
            if (item.IsGroupHeading)
            {
                if (menu.Items.Count > 0)
                    menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = item.Header, IsEnabled = false, FontWeight = FontWeight.Bold });
                continue;
            }

            var commandId = item.RibbonCommandId!;
            var menuItem = new MenuItem
            {
                Header = item.Header,
                IsEnabled = _ribbonCommandRegistry?.TryGet(new RibbonCommandId(commandId), out _) == true,
            };
            menuItem.Click += (_, _) => ApplyPasteOption(commandId, range);
            menu.Items.Add(menuItem);
        }

        var button = new Button
        {
            Height = PasteOptionsPlanner.ButtonHeight,
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
                    AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Paste, 14),
                    new TextBlock { Text = PasteOptionsPlanner.ButtonText, FontSize = 11, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center },
                    new TextBlock { Text = "▾", FontSize = 9, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center },
                },
            },
            Flyout = menu,
        };
        ToolTip.SetTip(button, PasteOptionsPlanner.ButtonToolTip);
        AutomationProperties.SetAutomationId(button, PasteOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(button, PasteOptionsPlanner.ButtonToolTip);
        return button;
    }

    /// <summary>Replaces the paste the button belongs to with the chosen kind of paste.</summary>
    internal void ApplyPasteOption(string ribbonCommandId, GridRange pastedRange)
    {
        if (_ribbonCommandRegistry?.TryGet(new RibbonCommandId(ribbonCommandId), out var command) != true || command is null)
            return;

        _pasteOptionsTracker.Dismiss();
        var undo = _session.UndoLastEdit();
        if (!undo.Success)
            return;

        _session.SelectRange(pastedRange);
        command.Execute(RibbonCommandContext.Empty);
    }
}
