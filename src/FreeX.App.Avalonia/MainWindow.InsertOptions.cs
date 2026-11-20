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

// Excel's Insert Options button: after inserting rows or columns, a paintbrush button by the active
// cell offers Format Same As Above / Below (Left / Right) or Clear Formatting for the new band.
// Mirrors WPF's MainWindow.InsertOptions.
public partial class MainWindow
{
    private readonly InsertOptionsTracker _insertOptionsTracker = new();
    private Button? _insertOptionsButton;

    private void AddInsertOptionsOverlay(Canvas overlay, ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        _insertOptionsButton = null;
        if (_insertOptionsTracker.Update(_session) is not { } band ||
            band.SheetId != _session.ActiveSheet.Id ||
            !TryGetDisplayedCellBounds(viewport, _session.ActiveCell, showHeadings, zoomFactor, out var left, out var top, out var width, out var height))
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var item in InsertOptionsPlanner.Items(band.Rows))
        {
            var menuItem = new MenuItem { Header = item.Header };
            var format = item.Format;
            menuItem.Click += (_, _) => ApplyInsertOption(band, format);
            menu.Items.Add(menuItem);
        }

        var button = new Button
        {
            Height = InsertOptionsPlanner.ButtonHeight,
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
                    AvaloniaRibbonIcons.Build(RibbonCommandIconKind.FormatPainter, 14),
                    new TextBlock { Text = "▾", FontSize = 9, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center },
                },
            },
            Flyout = menu,
        };
        ToolTip.SetTip(button, InsertOptionsPlanner.ButtonToolTip);
        AutomationProperties.SetAutomationId(button, InsertOptionsPlanner.ButtonAutomationId);
        AutomationProperties.SetName(button, InsertOptionsPlanner.ButtonToolTip);
        Canvas.SetLeft(button, left + width + 1);
        Canvas.SetTop(button, top + height + 1);
        overlay.Children.Add(button);
        _insertOptionsButton = button;
    }

    internal void ApplyInsertOption(InsertedBand band, InsertedBandFormat format)
    {
        var result = _session.FormatInsertedBand(band, format);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainWindowMessage_CommandCouldNotBeCompleted"));
            return;
        }

        RefreshShell(UiText.Get("MainLoc_Ready"));
    }
}
