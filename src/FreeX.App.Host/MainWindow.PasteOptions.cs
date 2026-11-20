using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Excel's Paste Options button: "(Ctrl)" at the bottom-right of a pasted range, whose menu redoes
// the paste another way (values, formulas, transpose, ...). Ctrl on its own opens it.
public partial class MainWindow
{
    private readonly PasteOptionsTracker _pasteOptionsTracker = new();
    private Button? _pasteOptionsButton;
    private bool _pasteOptionsCtrlAlone;

    internal Button? PasteOptionsButtonForTest => _pasteOptionsButton;

    private void RefreshPasteOptionsButton()
    {
        if (SheetGrid.SelectedRange is not { } selection ||
            _pasteOptionsTracker.Update(_session, selection) is not { } range ||
            range.Start.Sheet != _currentSheetId ||
            TryGetCellOverlayRect(range.End) is not { } cornerRect)
        {
            HidePasteOptionsButton();
            return;
        }

        EnsurePasteOptionsButton();
        var zoom = _zoomLevel;
        var left = Math.Min((cornerRect.Right * zoom) + 1, Math.Max(0, CommentOverlay.ActualWidth - _pasteOptionsButton!.ActualWidth));
        var top = Math.Min((cornerRect.Bottom * zoom) + 1, Math.Max(0, CommentOverlay.ActualHeight - PasteOptionsPlanner.ButtonHeight));
        Canvas.SetLeft(_pasteOptionsButton, Math.Max(0, left));
        Canvas.SetTop(_pasteOptionsButton, Math.Max(0, top));
        _pasteOptionsButton.Visibility = Visibility.Visible;
    }

    private void HidePasteOptionsButton()
    {
        if (_pasteOptionsButton is { Visibility: not Visibility.Collapsed })
            _pasteOptionsButton.Visibility = Visibility.Collapsed;
    }

    private void DismissPasteOptions()
    {
        _pasteOptionsTracker.Dismiss();
        HidePasteOptionsButton();
    }

    private void EnsurePasteOptionsButton()
    {
        if (_pasteOptionsButton is not null)
            return;

        _pasteOptionsButton = new Button
        {
            Height = PasteOptionsPlanner.ButtonHeight,
            Padding = new Thickness(2, 0, 3, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Visibility = Visibility.Collapsed,
            ToolTip = PasteOptionsPlanner.ButtonToolTip,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new RibbonIcon { Kind = RibbonCommandIconKind.Paste, IconSize = 14, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    new TextBlock { Text = PasteOptionsPlanner.ButtonText, FontSize = 11, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    new TextBlock { Text = "▾", FontSize = 9, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                },
            },
        };
        AutomationProperties.SetAutomationId(_pasteOptionsButton, PasteOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(_pasteOptionsButton, PasteOptionsPlanner.ButtonToolTip);
        _pasteOptionsButton.Click += (_, _) => OpenPasteOptionsMenu();
        CommentOverlay.Children.Add(_pasteOptionsButton);
        AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Window_PasteOptionsKeyDown), handledEventsToo: true);
        AddHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler(Window_PasteOptionsKeyUp), handledEventsToo: true);
    }

    internal void OpenPasteOptionsMenu()
    {
        if (_pasteOptionsButton is not { Visibility: Visibility.Visible } button || _pasteOptionsTracker.Range is not { } range)
            return;

        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var item in PasteOptionsPlanner.Items)
        {
            if (item.IsGroupHeading)
            {
                if (menu.Items.Count > 0)
                    menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = item.Header, IsEnabled = false, FontWeight = FontWeights.Bold });
                continue;
            }

            var commandId = item.RibbonCommandId!;
            var menuItem = new MenuItem
            {
                Header = item.Header,
                IsEnabled = FreeXRibbonHandlers.ContainsKey(commandId),
            };
            menuItem.Click += (_, _) => ApplyPasteOption(commandId, range);
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    /// <summary>Replaces the paste the button belongs to with the chosen kind of paste.</summary>
    internal void ApplyPasteOption(string ribbonCommandId, GridRange pastedRange)
    {
        if (!FreeXRibbonHandlers.TryGetValue(ribbonCommandId, out var binding))
            return;

        DismissPasteOptions();
        if (!ExecuteUndo())
            return;

        _selectionAnchor = pastedRange.Start;
        _selectionCursor = pastedRange.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = pastedRange;
        SynchronizeWorkbookSessionSelection();
        binding.Handler(this, this, new RoutedEventArgs());
    }

    // Excel opens the Paste Options menu when Ctrl is pressed and released on its own, and Esc
    // dismisses the button.
    private void Window_PasteOptionsKeyDown(object sender, KeyEventArgs e)
    {
        if (_pasteOptionsButton is not { Visibility: Visibility.Visible })
            return;

        if (e.Key == Key.Escape)
        {
            DismissPasteOptions();
            return;
        }

        _pasteOptionsCtrlAlone = (e.Key is Key.LeftCtrl or Key.RightCtrl) && !e.IsRepeat;
    }

    private void Window_PasteOptionsKeyUp(object sender, KeyEventArgs e)
    {
        if (_pasteOptionsCtrlAlone && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _pasteOptionsCtrlAlone = false;
            OpenPasteOptionsMenu();
        }
    }
}
