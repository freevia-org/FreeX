using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

// Excel's Auto Fill Options button: after a fill-handle drag, a small button at the bottom-right of
// the filled range offers Copy Cells, Fill Series, Fill Formatting Only and Fill Without Formatting.
public partial class MainWindow
{
    private Button? _autofillOptionsButton;

    internal Button? AutofillOptionsButtonForTest => _autofillOptionsButton;

    private void RefreshAutofillOptionsButton()
    {
        if (_session.AutofillOptions is not { } state ||
            state.Fill.Start.Sheet != _currentSheetId ||
            TryGetCellOverlayRect(state.Fill.End) is not { } cornerRect)
        {
            if (_autofillOptionsButton is { Visibility: not Visibility.Collapsed })
                _autofillOptionsButton.Visibility = Visibility.Collapsed;
            return;
        }

        EnsureAutofillOptionsButton();
        var zoom = _zoomLevel;
        Canvas.SetLeft(_autofillOptionsButton!, Math.Min((cornerRect.Right * zoom) + 1, Math.Max(0, CommentOverlay.ActualWidth - 40)));
        Canvas.SetTop(_autofillOptionsButton!, Math.Min((cornerRect.Bottom * zoom) + 1, Math.Max(0, CommentOverlay.ActualHeight - AutofillOptionsPlanner.ButtonHeight)));
        _autofillOptionsButton!.Visibility = Visibility.Visible;
    }

    private void EnsureAutofillOptionsButton()
    {
        if (_autofillOptionsButton is not null)
            return;

        _autofillOptionsButton = new Button
        {
            Height = AutofillOptionsPlanner.ButtonHeight,
            Padding = new Thickness(2, 0, 3, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Visibility = Visibility.Collapsed,
            ToolTip = AutofillOptionsPlanner.ButtonToolTip,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new RibbonIcon { Kind = RibbonCommandIconKind.Fill, IconSize = 14, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    new TextBlock { Text = "▾", FontSize = 9, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center },
                },
            },
        };
        AutomationProperties.SetAutomationId(_autofillOptionsButton, AutofillOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(_autofillOptionsButton, AutofillOptionsPlanner.ButtonToolTip);
        _autofillOptionsButton.Click += (_, _) => OpenAutofillOptionsMenu();
        CommentOverlay.Children.Add(_autofillOptionsButton);
    }

    private void OpenAutofillOptionsMenu()
    {
        if (_autofillOptionsButton is not { } button || _session.AutofillOptions is not { } state)
            return;

        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var item in AutofillOptionsPlanner.Items(state.Mode))
        {
            var menuItem = new MenuItem { Header = item.Header, IsCheckable = true, IsChecked = item.IsChecked };
            var mode = item.Mode;
            menuItem.Click += (_, _) => ApplyAutofillOption(mode);
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    internal void ApplyAutofillOption(AutofillMode mode)
    {
        SynchronizeWorkbookSessionSelection();
        var outcome = ToCommandOutcome(_session.ApplyAutofillOption(mode));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, AutofillOptionsPlanner.ButtonToolTip);
            return;
        }

        if (!outcome.IsNoOp)
            ApplySuccessfulWorkbookSessionCommand();
        UpdateViewport();
    }
}
