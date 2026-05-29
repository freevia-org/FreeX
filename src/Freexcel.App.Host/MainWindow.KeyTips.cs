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
    private List<FrameworkElement>? _visibleKeyTipElementCache;
    private RibbonKeyTipScope _visibleKeyTipElementCacheScope = RibbonKeyTipScope.None;
    private List<MenuItem>? _activeMenuKeyTipItemCache;
    private ItemsControl? _activeMenuKeyTipItemCacheOwner;

    private static Key GetEffectiveKey(System.Windows.Input.KeyEventArgs e) =>
        e.SystemKey == Key.None ? e.Key : e.SystemKey;

    private static bool IsStandaloneAltKey(Key key) =>
        StandaloneAltKeyTipTracker.IsStandaloneAltKey(key);

    private void EnterRibbonKeyTipMode(RibbonKeyTipScope scope)
    {
        _ribbonKeyTipMode.Enter();
        _ribbonKeyTipScope = scope;
        _ribbonKeyTipSequence = "";
        _legacyDataKeyTipSequence = false;
        _activeRibbonKeyTipMenu = null;
        _activeRibbonKeyTipItemsControl = null;
        InvalidateKeyTipCandidateCaches();
        ShowKeyTipOverlay(scope);
    }

    private void ExitRibbonKeyTipMode()
    {
        if (_activeRibbonKeyTipMenu is not null)
            _activeRibbonKeyTipMenu.IsOpen = false;

        _ribbonKeyTipMode.Cancel();
        _ribbonKeyTipScope = RibbonKeyTipScope.None;
        _ribbonKeyTipSequence = "";
        _legacyDataKeyTipSequence = false;
        _activeRibbonKeyTipMenu = null;
        _activeRibbonKeyTipItemsControl = null;
        InvalidateKeyTipCandidateCaches();
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
            if (HasVisibleTopLevelKeyTipLongerPrefix(_ribbonKeyTipSequence))
                return;

            var topLevelSequence = _ribbonKeyTipSequence;
            if (TryHandleTopLevelRibbonKeyTip(topLevelSequence))
            {
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                _legacyDataKeyTipSequence = string.Equals(topLevelSequence, "D", StringComparison.OrdinalIgnoreCase);
            }
            else if (TryInvokeTopLevelQatKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else
                ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu)
        {
            if (TryOpenActiveMenuItemSubmenuKeyTip(_ribbonKeyTipSequence))
                return;
            else if (TryInvokeActiveMenuItemKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else if (!HasActiveMenuItemKeyTipPrefix(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();

            return;
        }

        if (_legacyDataKeyTipSequence &&
            string.Equals(_ribbonKeyTipSequence, "FF", StringComparison.OrdinalIgnoreCase))
        {
            FilterButton_Click(this, new RoutedEventArgs());
            ExitRibbonKeyTipMode();
            return;
        }

        if (TryInvokeVisibleCommandKeyTip(_ribbonKeyTipSequence))
        {
            ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu)
            return;

        if (!HasVisibleCommandKeyTipPrefix(_ribbonKeyTipSequence))
            ExitRibbonKeyTipMode();
    }

    private bool TryHandleDirectRibbonKeyTip(Key key)
    {
        var token = RibbonKeyTipMode.ToKeyTipToken(key);
        if (token is null)
            return false;

        if (TryHandleTopLevelRibbonKeyTip(token))
        {
            EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            _legacyDataKeyTipSequence = string.Equals(token, "D", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        return TryInvokeTopLevelQatKeyTip(token);
    }

    private void ShowKeyTipOverlay(RibbonKeyTipScope scope)
    {
        if (KeyTipOverlay == null || RootGrid == null)
            return;

        UpdateRibbonLayoutIfNeeded(RootGrid);
        KeyTipOverlay.Children.Clear();
        InvalidateVisibleKeyTipElementCache();

        foreach (var element in GetVisibleKeyTipElements(scope))
        {
            var keyTip = RibbonTooltip.GetKeyTip(element);
            if (keyTip is null)
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

    private bool ShouldShowKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope)
    {
        var isQuickAccessButton =
            element is Button button &&
            button.ReadLocalValue(DockPanel.DockProperty) is Dock.Left;
        if (scope == RibbonKeyTipScope.TopLevel)
            return element is TabItem || isQuickAccessButton;

        return element is not TabItem &&
               !isQuickAccessButton &&
               !IsDescendantOf(element, StatusBarGrid);
    }

    private static bool IsEnabledKeyTipTarget(FrameworkElement element) =>
        element switch
        {
            ButtonBase button => button.IsEnabled,
            MenuItem menuItem => menuItem.IsEnabled,
            ComboBox comboBox => comboBox.IsEnabled,
            _ => element.IsEnabled
        };

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
            if (!button.IsEnabled)
                return false;

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
            if (!comboBox.IsEnabled)
                return false;

            comboBox.Focus();
            Keyboard.Focus(comboBox);
            comboBox.IsDropDownOpen = true;
            return true;
        }

        return false;
    }

    private bool TryEnterMenuKeyTipScope(ButtonBase button)
    {
        if (button.ContextMenu is not { } menu)
            return false;

        if (RibbonMetadata.IsCollapsedGroupButton(button))
            EnsureCollapsedRibbonGroupMenuItems(menu);

        if (!GetMenuItems(menu).Any(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item))))
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
        _activeRibbonKeyTipItemsControl = menu;
        _ribbonKeyTipScope = RibbonKeyTipScope.Menu;
        _ribbonKeyTipSequence = "";
        InvalidateVisibleKeyTipElementCache();
        InvalidateActiveMenuKeyTipItems();
        ClearKeyTipOverlay();
    }

    private bool TryOpenActiveMenuItemSubmenuKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipItemsControl is null ||
            !RibbonTooltip.TryOpenSubmenuForKeyTip(_activeRibbonKeyTipItemsControl, keyTip, out var submenu) ||
            submenu is null)
        {
            return false;
        }

        _activeRibbonKeyTipItemsControl = submenu;
        _ribbonKeyTipSequence = "";
        InvalidateActiveMenuKeyTipItems();
        return true;
    }

    private bool TryInvokeActiveMenuItemKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipItemsControl is null)
            return false;

        var match = RibbonKeyTipRouting.ResolveMenuItem(GetEnabledActiveMenuItems(_activeRibbonKeyTipItemsControl), keyTip);
        if (match is null)
            return false;

        if (!match.IsEnabled)
            return false;

        match.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match));
        return true;
    }

    private bool HasActiveMenuItemKeyTipPrefix(string keyTipPrefix) =>
        _activeRibbonKeyTipItemsControl is not null &&
        RibbonKeyTipRouting.HasMenuItemKeyTipPrefix(GetEnabledActiveMenuItems(_activeRibbonKeyTipItemsControl), keyTipPrefix);

    private IReadOnlyList<MenuItem> GetEnabledActiveMenuItems(ItemsControl itemsControl)
    {
        if (ReferenceEquals(_activeMenuKeyTipItemCacheOwner, itemsControl) &&
            _activeMenuKeyTipItemCache is not null)
        {
            return _activeMenuKeyTipItemCache;
        }

        _activeMenuKeyTipItemCacheOwner = itemsControl;
        _activeMenuKeyTipItemCache = GetEnabledMenuItems(itemsControl).ToList();
        return _activeMenuKeyTipItemCache;
    }

    private static IEnumerable<MenuItem> GetEnabledMenuItems(ItemsControl itemsControl) =>
        GetMenuItems(itemsControl).Where(item => item.IsEnabled);

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
        var match = RibbonKeyTipRouting.ResolveKeyTipElement(
            GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)
            .OfType<ButtonBase>()
            .Cast<FrameworkElement>(),
            keyTip) as ButtonBase;

        if (match is null)
            return false;

        if (!match.IsEnabled)
            return false;

        match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));
        return true;
    }

    private bool HasVisibleCommandKeyTipPrefix(string keyTipPrefix) =>
        RibbonKeyTipRouting.HasKeyTipPrefix(GetRoutableKeyTipElements(RibbonKeyTipScope.Commands), keyTipPrefix);

    private bool HasVisibleTopLevelKeyTipLongerPrefix(string keyTipPrefix) =>
        RibbonTopLevelKeyTipRouter.HasLongerKeyTipPrefix(
            keyTipPrefix,
            GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)
                .Select(RibbonTooltip.GetKeyTip));

    private IEnumerable<FrameworkElement> GetRoutableKeyTipElements(RibbonKeyTipScope scope)
        => GetVisibleKeyTipElements(scope);

    private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(RibbonKeyTipScope scope)
    {
        if (CanReuseVisibleKeyTipElementCache(scope))
            return _visibleKeyTipElementCache!;

        var elements = MaterializeVisibleKeyTipElements(scope);
        if (ShouldCacheVisibleKeyTipElements(scope))
        {
            _visibleKeyTipElementCacheScope = scope;
            _visibleKeyTipElementCache = elements;
        }

        return elements;
    }

    private bool CanReuseVisibleKeyTipElementCache(RibbonKeyTipScope scope) =>
        _visibleKeyTipElementCache is not null &&
        _visibleKeyTipElementCacheScope == scope &&
        ShouldCacheVisibleKeyTipElements(scope);

    private bool ShouldCacheVisibleKeyTipElements(RibbonKeyTipScope scope) =>
        scope != RibbonKeyTipScope.None &&
        _ribbonKeyTipMode.IsActive &&
        _ribbonKeyTipScope == scope;

    private List<FrameworkElement> MaterializeVisibleKeyTipElements(RibbonKeyTipScope scope)
    {
        var elements = new List<FrameworkElement>();
        var seen = new HashSet<FrameworkElement>();
        foreach (var element in EnumerateKeyTipCandidateElements(scope))
        {
            if (!seen.Add(element) ||
                !IsVisibleKeyTipElement(element, scope))
                continue;

            elements.Add(element);
        }

        return elements;
    }

    private IEnumerable<FrameworkElement> EnumerateKeyTipCandidateElements(RibbonKeyTipScope scope)
    {
        if (scope == RibbonKeyTipScope.TopLevel)
        {
            if (RibbonTabs is not null)
            {
                foreach (var tabItem in RibbonTabs.Items.OfType<TabItem>())
                    yield return tabItem;
            }

            foreach (var quickAccessButton in EnumerateQuickAccessKeyTipButtons())
                yield return quickAccessButton;

            yield break;
        }

        if (scope == RibbonKeyTipScope.Commands &&
            IsStartScreenVisible() &&
            StartScreenOverlay is not null)
        {
            foreach (var element in EnumerateKeyTipCandidateDescendants(StartScreenOverlay))
                yield return element;

            yield break;
        }

        if (scope == RibbonKeyTipScope.Commands &&
            RibbonTabs?.SelectedItem is TabItem selectedTab)
        {
            var selectedRoot = selectedTab.Content as DependencyObject ?? selectedTab;
            foreach (var element in EnumerateKeyTipCandidateDescendants(selectedRoot))
                yield return element;

            yield break;
        }

        if (RootGrid is not null)
        {
            foreach (var element in EnumerateKeyTipCandidateDescendants(RootGrid))
                yield return element;
        }
    }

    private IEnumerable<FrameworkElement> EnumerateQuickAccessKeyTipButtons()
    {
        if (SaveQatBtn is not null)
            yield return SaveQatBtn;
        if (UndoQatBtn is not null)
            yield return UndoQatBtn;
        if (RedoQatBtn is not null)
            yield return RedoQatBtn;
    }

    private static IEnumerable<FrameworkElement> EnumerateKeyTipCandidateDescendants(DependencyObject root) =>
        EnumerateVisualDescendants(root)
            .Concat(EnumerateLogicalDescendants(root))
            .OfType<FrameworkElement>();

    private bool IsVisibleKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope) =>
        !ReferenceEquals(element, KeyTipOverlay) &&
        element.IsVisible &&
        element.ActualWidth > 0 &&
        element.ActualHeight > 0 &&
        (scope != RibbonKeyTipScope.Commands || !IsStartScreenVisible() || IsInsideStartScreenOverlay(element)) &&
        (scope != RibbonKeyTipScope.Commands || !IsInsideUnselectedTabItem(element)) &&
        ShouldShowKeyTipElement(element, scope) &&
        IsEnabledKeyTipTarget(element) &&
        !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(element));

    private void InvalidateKeyTipCandidateCaches()
    {
        InvalidateVisibleKeyTipElementCache();
        InvalidateActiveMenuKeyTipItems();
    }

    private void InvalidateVisibleKeyTipElementCache()
    {
        _visibleKeyTipElementCache = null;
        _visibleKeyTipElementCacheScope = RibbonKeyTipScope.None;
    }

    private void InvalidateActiveMenuKeyTipItems()
    {
        _activeMenuKeyTipItemCache = null;
        _activeMenuKeyTipItemCacheOwner = null;
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
