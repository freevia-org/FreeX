using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private static Key GetEffectiveKey(System.Windows.Input.KeyEventArgs e) =>
        e.SystemKey == Key.None ? e.Key : e.SystemKey;

    private static bool IsStandaloneAltKey(Key key) =>
        StandaloneAltKeyTipTracker.IsStandaloneAltKey(key);

    private void EnterRibbonKeyTipMode(RibbonKeyTipScope scope)
    {
        _ribbonKeyTipMode.Enter();
        _ribbonKeyTipScope = scope;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ShowKeyTipOverlay(scope);
    }

    private void ExitRibbonKeyTipMode()
    {
        if (_activeRibbonKeyTipMenu is not null)
            _activeRibbonKeyTipMenu.IsOpen = false;

        _ribbonKeyTipMode.Cancel();
        _ribbonKeyTipScope = RibbonKeyTipScope.None;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ClearKeyTipOverlay();
    }

    private void HandleActiveRibbonKeyTip(Key key)
    {
        if (key == Key.Escape)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        var token = RibbonKeyTipMode.ToKeyTipToken(key);
        if (token is null)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        _ribbonKeyTipSequence += token;

        if (_ribbonKeyTipScope == RibbonKeyTipScope.TopLevel)
        {
            if (TryHandleTopLevelRibbonKeyTip(_ribbonKeyTipSequence))
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            else if (TryInvokeTopLevelQatKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else
                ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu)
        {
            if (TryInvokeActiveMenuItemKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else if (RibbonTooltip.TryOpenSubmenuForKeyTip(_activeRibbonKeyTipMenu!, _ribbonKeyTipSequence))
                return;
            else if (!HasActiveMenuItemKeyTipPrefix(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();

            return;
        }

        if (TryInvokeVisibleCommandKeyTip(_ribbonKeyTipSequence))
        {
            ExitRibbonKeyTipMode();
            return;
        }

        if (!HasVisibleCommandKeyTipPrefix(_ribbonKeyTipSequence))
            ExitRibbonKeyTipMode();
    }

    private bool TryHandleDirectRibbonKeyTip(Key key)
    {
        var token = RibbonKeyTipMode.ToKeyTipToken(key);
        if (token is null || !TryHandleTopLevelRibbonKeyTip(token))
            return false;

        EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
        return true;
    }

    private void ShowKeyTipOverlay(RibbonKeyTipScope scope)
    {
        if (KeyTipOverlay == null || RootGrid == null)
            return;

        RootGrid.UpdateLayout();
        KeyTipOverlay.Children.Clear();

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)))
            {
                continue;
            }

            if (!ShouldShowKeyTipElement(element, scope))
                continue;

            var keyTip = RibbonTooltip.GetKeyTip(element);
            if (string.IsNullOrWhiteSpace(keyTip))
                continue;

            Point origin;
            try
            {
                origin = element.TransformToAncestor(RootGrid).Transform(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var badge = CreateKeyTipBadge(keyTip);
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeSize = badge.DesiredSize;
            var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
                new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)),
                new Size(RootGrid.ActualWidth, RootGrid.ActualHeight),
                badgeSize);

            Canvas.SetLeft(badge, point.X);
            Canvas.SetTop(badge, point.Y);
            KeyTipOverlay.Children.Add(badge);
        }

        KeyTipOverlay.Visibility = KeyTipOverlay.Children.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool ShouldShowKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope)
    {
        var isQuickAccessButton =
            element is Button button &&
            button.ReadLocalValue(DockPanel.DockProperty) is Dock.Left;
        if (scope == RibbonKeyTipScope.TopLevel)
            return element is TabItem || isQuickAccessButton;

        return element is not TabItem && !isQuickAccessButton;
    }

    private void ClearKeyTipOverlay()
    {
        if (KeyTipOverlay == null)
            return;

        KeyTipOverlay.Children.Clear();
        KeyTipOverlay.Visibility = Visibility.Collapsed;
    }

    private static Border CreateKeyTipBadge(string keyTip) =>
        new()
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = keyTip,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black
            }
        };

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            yield return dependencyObject;

            foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                yield return descendant;
        }
    }

    private bool TryInvokeVisibleCommandKeyTip(string keyTip)
    {
        var visibleKeyTipElements = GetRoutableKeyTipElements(RibbonKeyTipScope.Commands).ToList();
        var match = RibbonKeyTipRouting.ResolveKeyTipElement(visibleKeyTipElements, keyTip);
        if (match is null)
            return false;

        if (match is ButtonBase button)
        {
            if (TryEnterMenuKeyTipScope(button))
                return false;

            if (button is ToggleButton toggleButton)
                toggleButton.IsChecked = toggleButton.IsChecked != true;

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu &&
                ReferenceEquals(_activeRibbonKeyTipMenu?.PlacementTarget, button))
            {
                return false;
            }

            return true;
        }

        if (match is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = true;
            return true;
        }

        return false;
    }

    private bool TryEnterMenuKeyTipScope(ButtonBase button)
    {
        if (button.ContextMenu is not { } menu ||
            !GetMenuItems(menu).Any(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item))))
        {
            return false;
        }

        OpenRibbonContextMenu(button, menu, enterKeyTipMenuScope: true);
        return true;
    }

    private void OpenRibbonContextMenu(ButtonBase button, ContextMenu menu, bool enterKeyTipMenuScope = false)
    {
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;

        if (enterKeyTipMenuScope || _ribbonKeyTipMode.IsActive)
            EnterRibbonMenuKeyTipScope(menu);
    }

    private void EnterRibbonMenuKeyTipScope(ContextMenu menu)
    {
        _activeRibbonKeyTipMenu = menu;
        _ribbonKeyTipScope = RibbonKeyTipScope.Menu;
        _ribbonKeyTipSequence = "";
        ClearKeyTipOverlay();
    }

    private bool TryInvokeActiveMenuItemKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipMenu is null)
            return false;

        var match = RibbonKeyTipRouting.ResolveMenuItem(GetMenuItems(_activeRibbonKeyTipMenu), keyTip);
        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match));
        return true;
    }

    private bool HasActiveMenuItemKeyTipPrefix(string keyTipPrefix) =>
        _activeRibbonKeyTipMenu is not null &&
        RibbonKeyTipRouting.HasMenuItemKeyTipPrefix(GetMenuItems(_activeRibbonKeyTipMenu), keyTipPrefix);

    private static IEnumerable<MenuItem> GetMenuItems(ItemsControl itemsControl)
    {
        foreach (var item in itemsControl.Items)
        {
            if (item is MenuItem menuItem)
            {
                yield return menuItem;

                foreach (var child in GetMenuItems(menuItem))
                    yield return child;
            }
        }
    }

    private bool TryInvokeTopLevelQatKeyTip(string keyTip)
    {
        var match = GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)
            .OfType<ButtonBase>()
            .FirstOrDefault(element =>
                string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));
        return true;
    }

    private bool HasVisibleCommandKeyTipPrefix(string keyTipPrefix) =>
        RibbonKeyTipRouting.HasKeyTipPrefix(GetRoutableKeyTipElements(RibbonKeyTipScope.Commands), keyTipPrefix);

    private IEnumerable<FrameworkElement> GetRoutableKeyTipElements(RibbonKeyTipScope scope)
    {
        var seen = new HashSet<FrameworkElement>();
        foreach (var element in GetVisibleKeyTipElements(scope))
        {
            if (seen.Add(element))
                yield return element;
        }

        if (scope != RibbonKeyTipScope.Commands || RibbonTabs?.SelectedItem is not TabItem selectedTab)
            yield break;

        foreach (var element in EnumerateVisualDescendants(selectedTab).OfType<FrameworkElement>())
        {
            if (!IsRoutableSelectedTabKeyTipElement(element, scope, seen))
                continue;

            yield return element;
        }

        foreach (var element in EnumerateLogicalDescendants(selectedTab).OfType<FrameworkElement>())
        {
            if (!IsRoutableSelectedTabKeyTipElement(element, scope, seen))
                continue;

            yield return element;
        }
    }

    private bool IsRoutableSelectedTabKeyTipElement(
        FrameworkElement element,
        RibbonKeyTipScope scope,
        ISet<FrameworkElement> seen) =>
        seen.Add(element) &&
        !ReferenceEquals(element, KeyTipOverlay) &&
        ShouldShowKeyTipElement(element, scope) &&
        !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(element));

    private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(RibbonKeyTipScope scope)
    {
        if (RootGrid == null)
            yield break;

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)) ||
                !ShouldShowKeyTipElement(element, scope) ||
                string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(element)))
            {
                continue;
            }

            yield return element;
        }
    }

    private bool IsStartScreenVisible() =>
        StartScreenOverlay?.Visibility == Visibility.Visible;

    private bool IsInsideStartScreenOverlay(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (ReferenceEquals(current, StartScreenOverlay))
                return true;
        }

        return false;
    }

    private static bool IsInsideUnselectedTabItem(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (current is TabItem tabItem)
                return !tabItem.IsSelected;
        }

        return false;
    }

    private static DependencyObject? GetTreeParent(DependencyObject element)
    {
        try
        {
            if (element is Visual)
                return VisualTreeHelper.GetParent(element);
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private enum RibbonKeyTipScope
    {
        None,
        TopLevel,
        Commands,
        Menu
    }
}
