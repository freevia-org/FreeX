using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Host;

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

    private static bool IsRibbonKeyTipContinuationModifierState(ModifierKeys modifiers) =>
        modifiers is ModifierKeys.None or ModifierKeys.Alt;

    private void EnterRibbonKeyTipMode(RibbonKeyTipScope scope)
    {
        _ribbonKeyTipSession.Enter(scope);
        PrepareRibbonKeyTipScope(scope);
    }

    private void PrepareRibbonKeyTipScope(RibbonKeyTipScope scope)
    {
        _activeRibbonKeyTipMenu = null;
        _activeRibbonKeyTipItemsControl = null;
        InvalidateKeyTipCandidateCaches();
        ShowKeyTipOverlay(scope);
    }

    private void ExitRibbonKeyTipMode()
    {
        if (_activeRibbonKeyTipMenu is not null)
            _activeRibbonKeyTipMenu.IsOpen = false;

        _ribbonKeyTipSession.Cancel();
        _activeRibbonKeyTipMenu = null;
        _activeRibbonKeyTipItemsControl = null;
        InvalidateKeyTipCandidateCaches();
        ClearKeyTipOverlay();
    }

    private void HandleActiveRibbonKeyTip(Key key)
    {
        if (key == Key.Escape)
        {
            _ribbonKeyTipSession.HandleEscape();
            ExitRibbonKeyTipMode();
            return;
        }

        var token = ToWpfKeyTipToken(key);
        if (token is null)
        {
            _ribbonKeyTipSession.HandleToken(null);
            ExitRibbonKeyTipMode();
            return;
        }

        var step = _ribbonKeyTipSession.HandleToken(token);
        switch (step.Intent)
        {
            case FreeXRibbonKeyTipInputIntent.EnterLegacyEditPasteSpecial:
                PrepareRibbonKeyTipScope(RibbonKeyTipScope.Commands);
                return;
            case FreeXRibbonKeyTipInputIntent.EnterLegacyDataFilter:
                if (TryHandleTopLevelRibbonKeyTip("D"))
                    PrepareRibbonKeyTipScope(RibbonKeyTipScope.Commands);
                else
                    ExitRibbonKeyTipMode();
                return;
            case FreeXRibbonKeyTipInputIntent.WaitForContinuation:
                return;
            case FreeXRibbonKeyTipInputIntent.InvokeLegacyEditPasteSpecial:
                PasteSpecialBtn_Click(this, new RoutedEventArgs());
                ExitRibbonKeyTipMode();
                return;
            case FreeXRibbonKeyTipInputIntent.InvokeLegacyDataFilter:
                FilterButton_Click(this, new RoutedEventArgs());
                ExitRibbonKeyTipMode();
                return;
            case FreeXRibbonKeyTipInputIntent.Cancel:
                ExitRibbonKeyTipMode();
                return;
        }

        if (step.Scope == RibbonKeyTipScope.TopLevel)
        {
            if (HasVisibleTopLevelKeyTipLongerPrefix(step.Input))
                return;

            if (TryHandleTopLevelRibbonKeyTip(step.Input))
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            else if (TryInvokeTopLevelQatKeyTip(step.Input))
                ExitRibbonKeyTipMode();
            else
                ExitRibbonKeyTipMode();
            return;
        }

        if (step.Scope == RibbonKeyTipScope.Menu)
        {
            if (TryOpenActiveMenuItemSubmenuKeyTip(step.Input))
                return;
            if (TryInvokeActiveMenuItemKeyTip(step.Input))
                ExitRibbonKeyTipMode();
            else if (!HasActiveMenuItemKeyTipPrefix(step.Input))
                ExitRibbonKeyTipMode();
            return;
        }

        if (TryInvokeVisibleCommandKeyTip(step.Input))
        {
            ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipSession.Scope != RibbonKeyTipScope.Menu &&
            !HasVisibleCommandKeyTipPrefix(step.Input))
        {
            ExitRibbonKeyTipMode();
        }
    }

    private bool TryHandleDirectRibbonKeyTip(Key key)
    {
        var token = ToWpfKeyTipToken(key);
        if (token is null)
            return false;

        _ribbonKeyTipSession.Enter(RibbonKeyTipScope.TopLevel);
        var step = _ribbonKeyTipSession.HandleToken(token);
        if (step.Intent == FreeXRibbonKeyTipInputIntent.EnterLegacyEditPasteSpecial)
        {
            PrepareRibbonKeyTipScope(RibbonKeyTipScope.Commands);
            return true;
        }

        if (step.Intent == FreeXRibbonKeyTipInputIntent.EnterLegacyDataFilter)
        {
            if (TryHandleTopLevelRibbonKeyTip("D"))
            {
                PrepareRibbonKeyTipScope(RibbonKeyTipScope.Commands);
                return true;
            }

            _ribbonKeyTipSession.Cancel();
            return false;
        }

        if (TryHandleTopLevelRibbonKeyTip(step.Input))
        {
            EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            return true;
        }

        var invoked = TryInvokeTopLevelQatKeyTip(step.Input);
        _ribbonKeyTipSession.Cancel();
        return invoked;
    }

    internal static string? ToWpfKeyTipToken(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        if (key >= Key.D0 && key <= Key.D9)
            return ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)(key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return null;
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

            var badge = CreateKeyTipBadge(keyTip.Trim().ToUpperInvariant());
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeSize = badge.DesiredSize;
            var badgeKind = GetKeyTipBadgeKind(element);
            var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
                new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)),
                new Size(RootGrid.ActualWidth, RootGrid.ActualHeight),
                badgeSize,
                badgeKind);

            Canvas.SetLeft(badge, point.X);
            Canvas.SetTop(badge, point.Y);
            KeyTipOverlay.Children.Add(badge);
        }

        KeyTipOverlay.Visibility = KeyTipOverlay.Children.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static RibbonKeyTipBadgeKind GetKeyTipBadgeKind(FrameworkElement element) =>
        element switch
        {
            TabItem => RibbonKeyTipBadgeKind.Tab,
            _ when RibbonMetadata.IsCollapsedGroupButton(element) => RibbonKeyTipBadgeKind.CollapsedGroup,
            ComboBox => RibbonKeyTipBadgeKind.ComboBox,
            _ when IsDropdownCommandKeyTipElement(element) => RibbonKeyTipBadgeKind.DropdownCommand,
            _ => RibbonKeyTipBadgeKind.Command
        };

    private static bool IsDropdownCommandKeyTipElement(FrameworkElement element) =>
        element is ButtonBase button &&
        (button.ContextMenu is not null || RibbonMetadata.IsDropdownMenuButton(button));

    private bool ShouldShowKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope)
    {
        var isQuickAccessButton = IsQuickAccessToolbarButton(element);
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
            if (_ribbonKeyTipSession.Scope == RibbonKeyTipScope.Menu &&
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
        RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(menu);
        menu.IsOpen = true;

        if (enterKeyTipMenuScope || _ribbonKeyTipSession.IsActive)
            EnterRibbonMenuKeyTipScope(menu);
    }

    private void EnterRibbonMenuKeyTipScope(ContextMenu menu)
    {
        _activeRibbonKeyTipMenu = menu;
        _activeRibbonKeyTipItemsControl = menu;
        _ribbonKeyTipSession.EnterScope(RibbonKeyTipScope.Menu);
        RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(menu);
        InvalidateVisibleKeyTipElementCache();
        InvalidateActiveMenuKeyTipItems();
        ClearKeyTipOverlay();
    }

    private bool TryOpenActiveMenuItemSubmenuKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipItemsControl is null)
            return false;

        var scopePrefix = RibbonMenuKeyTipScopePlanner.GetScopePrefix(_activeRibbonKeyTipItemsControl);
        if (!RibbonTooltip.TryOpenSubmenuForKeyTip(_activeRibbonKeyTipItemsControl, keyTip, scopePrefix, out var submenu) ||
            submenu is null)
        {
            return false;
        }

        _activeRibbonKeyTipItemsControl = submenu;
        _ribbonKeyTipSession.ResetInput();
        RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(submenu);
        InvalidateActiveMenuKeyTipItems();
        return true;
    }

    private bool TryInvokeActiveMenuItemKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipItemsControl is null)
            return false;

        var scopePrefix = RibbonMenuKeyTipScopePlanner.GetScopePrefix(_activeRibbonKeyTipItemsControl);
        var match = RibbonKeyTipRouting.ResolveMenuItem(
            GetEnabledActiveMenuItems(_activeRibbonKeyTipItemsControl),
            keyTip,
            scopePrefix);
        if (match is null)
            return false;

        if (!match.IsEnabled)
            return false;

        match.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match));
        return true;
    }

    private bool HasActiveMenuItemKeyTipPrefix(string keyTipPrefix) =>
        _activeRibbonKeyTipItemsControl is not null &&
        RibbonKeyTipRouting.HasMenuItemKeyTipPrefix(
            GetEnabledActiveMenuItems(_activeRibbonKeyTipItemsControl),
            keyTipPrefix,
            RibbonMenuKeyTipScopePlanner.GetScopePrefix(_activeRibbonKeyTipItemsControl));

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
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix(
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
        _ribbonKeyTipSession.IsActive &&
        _ribbonKeyTipSession.Scope == scope;

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

            foreach (var quickAccessButton in EnumerateQuickAccessToolbarButtons())
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

}
