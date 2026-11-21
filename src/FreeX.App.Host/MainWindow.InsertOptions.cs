using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

// Excel's Insert Options button: after inserting rows or columns, a paintbrush button by the active
// cell offers Format Same As Above / Below (Left / Right) or Clear Formatting for the new band.
public partial class MainWindow
{
    private readonly InsertOptionsTracker _insertOptionsTracker = new();
    private Button? _insertOptionsButton;

    internal Button? InsertOptionsButtonForTest => _insertOptionsButton;

    private void RefreshInsertOptionsButton()
    {
        if (_insertOptionsTracker.Update(_session) is not { } band ||
            band.SheetId != _currentSheetId ||
            (SheetGrid.ActiveCell ?? SheetGrid.SelectedRange?.Start) is not { } activeCell ||
            TryGetCellOverlayRect(activeCell) is not { } cellRect)
        {
            if (_insertOptionsButton is { Visibility: not Visibility.Collapsed })
                _insertOptionsButton.Visibility = Visibility.Collapsed;
            return;
        }

        EnsureInsertOptionsButton();
        var zoom = _zoomLevel;
        Canvas.SetLeft(_insertOptionsButton!, Math.Min((cellRect.Right * zoom) + 1, Math.Max(0, CommentOverlay.ActualWidth - 40)));
        Canvas.SetTop(_insertOptionsButton!, Math.Min((cellRect.Bottom * zoom) + 1, Math.Max(0, CommentOverlay.ActualHeight - InsertOptionsPlanner.ButtonHeight)));
        _insertOptionsButton!.Visibility = Visibility.Visible;
    }

    private void EnsureInsertOptionsButton()
    {
        if (_insertOptionsButton is not null)
            return;

        _insertOptionsButton = new Button
        {
            Height = InsertOptionsPlanner.ButtonHeight,
            Padding = new Thickness(2, 0, 3, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Visibility = Visibility.Collapsed,
            ToolTip = InsertOptionsPlanner.ButtonToolTip,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new RibbonIcon { Kind = RibbonCommandIconKind.FormatPainter, IconSize = 14, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    new TextBlock { Text = "▾", FontSize = 9, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center },
                },
            },
        };
        AutomationProperties.SetAutomationId(_insertOptionsButton, InsertOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(_insertOptionsButton, InsertOptionsPlanner.ButtonToolTip);
        _insertOptionsButton.Click += (_, _) => OpenInsertOptionsMenu();
        CommentOverlay.Children.Add(_insertOptionsButton);
    }

    private void OpenInsertOptionsMenu()
    {
        if (_insertOptionsButton is not { } button || _insertOptionsTracker.Band is not { } band)
            return;

        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var item in InsertOptionsPlanner.Items(band.Rows))
        {
            var menuItem = new MenuItem { Header = item.Header };
            var format = item.Format;
            menuItem.Click += (_, _) => ApplyInsertOption(band, format);
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    internal void ApplyInsertOption(InsertedBand band, InsertedBandFormat format)
    {
        SynchronizeWorkbookSessionSelection();
        var outcome = ToCommandOutcome(_session.FormatInsertedBand(band, format));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, InsertOptionsPlanner.ButtonToolTip);
            return;
        }

        if (!outcome.IsNoOp)
            ApplySuccessfulWorkbookSessionCommand();
        UpdateViewport();
    }
}
