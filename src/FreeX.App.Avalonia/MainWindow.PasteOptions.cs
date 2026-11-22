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

    private Button? _pasteOptionsButton;

    private void AddPasteOptionsOverlay(Canvas overlay, ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        _pasteOptionsButton = null;
        if (_pasteOptionsTracker.Update(_session, _session.SelectedRange) is not { } range ||
            range.Start.Sheet != _session.ActiveSheet.Id ||
            !TryGetDisplayedCellBounds(viewport, range.End, showHeadings, zoomFactor, out var left, out var top, out var width, out var height))
        {
            return;
        }

        var button = CreatePasteOptionsButton(range);
        var desiredLeft = left + width + 1;
        var desiredTop = top + height + 1;
        Canvas.SetLeft(button, desiredLeft);
        Canvas.SetTop(button, desiredTop);
        overlay.Children.Add(button);
        _pasteOptionsButton = button;

        // WPF keeps the button inside the grid overlay (RefreshPasteOptionsButton clamps against the
        // overlay's actual size); the overlay and the button have no size until they are laid out,
        // so re-clamp once either reports one.
        void Reclamp()
        {
            var (clampedLeft, clampedTop) = ClampPasteOptionsButtonPosition(
                desiredLeft,
                desiredTop,
                button.Bounds.Width,
                overlay.Bounds.Width,
                overlay.Bounds.Height);
            Canvas.SetLeft(button, clampedLeft);
            Canvas.SetTop(button, clampedTop);
        }

        overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                Reclamp();
        };
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                Reclamp();
        };
        if (!_pasteOptionsKeysHooked)
        {
            _pasteOptionsKeysHooked = true;
            AddHandler(KeyDownEvent, PasteOptions_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyUpEvent, PasteOptions_KeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    /// <summary>
    /// WPF's RefreshPasteOptionsButton clamp: the button sits just past the pasted range's corner but
    /// never leaves the overlay. A zero overlay size (not laid out yet) leaves that axis unclamped.
    /// </summary>
    internal static (double Left, double Top) ClampPasteOptionsButtonPosition(
        double desiredLeft,
        double desiredTop,
        double buttonWidth,
        double overlayWidth,
        double overlayHeight)
    {
        var left = overlayWidth > 0
            ? Math.Min(desiredLeft, Math.Max(0, overlayWidth - buttonWidth))
            : desiredLeft;
        var top = overlayHeight > 0
            ? Math.Min(desiredTop, Math.Max(0, overlayHeight - PasteOptionsPlanner.ButtonHeight))
            : desiredTop;
        return (Math.Max(0, left), Math.Max(0, top));
    }

    // Excel opens the Paste Options menu when Ctrl is pressed and released on its own, and Esc
    // dismisses the button.
    private void PasteOptions_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_pasteOptionsButton is null)
            return;

        if (e.Key == Key.Escape)
        {
            _pasteOptionsTracker.Dismiss();
            _pasteOptionsButton.IsVisible = false;
            return;
        }

        _pasteOptionsCtrlAlone = e.Key is Key.LeftCtrl or Key.RightCtrl;
    }

    private void PasteOptions_KeyUp(object? sender, KeyEventArgs e)
    {
        if (_pasteOptionsCtrlAlone && e.Key is Key.LeftCtrl or Key.RightCtrl &&
            _pasteOptionsButton is { IsVisible: true, Flyout: { } flyout } button)
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
