using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon;

namespace FreeX.App.Host;

// Excel's collapsed ribbon ("Show tabs only"): Ctrl+F1 or double-clicking a tab hides the command
// area; a click on a tab then shows it for one use, until a command runs or the user clicks elsewhere.
public partial class MainWindow
{
    private readonly RibbonDisplayState _ribbonDisplayState = new();
    private double _ribbonExpandedMinHeight;

    internal RibbonDisplayState RibbonDisplayState => _ribbonDisplayState;

    private void InitializeRibbonDisplayState()
    {
        _ribbonExpandedMinHeight = RibbonTabs.MinHeight;
        _ribbonDisplayState.Changed += (_, _) => ApplyRibbonDisplayState();
        RibbonTabs.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(RibbonTabs_DisplayStateMouseDown), handledEventsToo: true);
        RibbonTabs.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RibbonTabs_DisplayStateCommandClick), handledEventsToo: true);
        AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Window_RibbonDisplayStateMouseDown), handledEventsToo: true);
        AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Window_RibbonDisplayStateKeyDown), handledEventsToo: true);
    }

    private void ToggleRibbonCollapsed() => _ribbonDisplayState.ToggleCollapsed();

    private void ApplyRibbonDisplayState()
    {
        var shows = _ribbonDisplayState.ShowsCommands;
        RibbonTabs.ApplyTemplate();
        if (RibbonTabs.Template?.FindName("PART_SelectedContentHost", RibbonTabs) is FrameworkElement host)
        {
            // The standard template wraps the selected content in its own border; hide that border too
            // so no empty frame is left under the tabs.
            var area = VisualTreeHelper.GetParent(host) is System.Windows.Controls.Border border ? (FrameworkElement)border : host;
            area.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
        }

        RibbonTabs.MinHeight = shows ? _ribbonExpandedMinHeight : 0;
    }

    private void RibbonTabs_DisplayStateMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsRibbonContentTabHeaderSource(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 2)
        {
            _ribbonDisplayState.ToggleCollapsed();
            e.Handled = true;
            return;
        }

        _ribbonDisplayState.OnTabClicked();
    }

    // A button that runs a command (rather than opening a menu or gallery) closes the temporarily
    // shown ribbon, as in Excel.
    private void RibbonTabs_DisplayStateCommandClick(object sender, RoutedEventArgs e)
    {
        if (_ribbonDisplayState.IsTemporarilyShown &&
            e.OriginalSource is ButtonBase button &&
            button.ContextMenu is null &&
            !RibbonMetadata.IsDropdownMenuButton(button))
        {
            _ribbonDisplayState.OnCommandExecuted();
        }
    }

    private void Window_RibbonDisplayStateMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_ribbonDisplayState.IsTemporarilyShown &&
            !IsWithin(e.OriginalSource as DependencyObject, RibbonTabs))
        {
            _ribbonDisplayState.OnDismissed();
        }
    }

    private void Window_RibbonDisplayStateKeyDown(object sender, KeyEventArgs e)
    {
        if (_ribbonDisplayState.IsTemporarilyShown && e.Key == Key.Escape)
            _ribbonDisplayState.OnDismissed();
    }

    // Tab items render only their header (the selected body is in the TabControl's content host),
    // so a source inside a TabItem is a click on that tab's header. The File tab opens Backstage.
    private bool IsRibbonContentTabHeaderSource(DependencyObject? source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, RibbonTabs); current = GetParentForRibbonHitTest(current))
        {
            if (current is TabItem item)
                return !ReferenceEquals(item, FileTab);
        }

        return false;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParentForRibbonHitTest(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetParentForRibbonHitTest(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
}
