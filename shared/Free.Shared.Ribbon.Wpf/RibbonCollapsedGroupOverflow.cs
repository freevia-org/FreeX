using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Shared WPF mechanics for legacy collapsed-group buttons. Apps provide only group presentation and
/// menu cloning hooks; discovery, ordering, lazy population, invocation, and focus restoration are shared.
/// </summary>
public static class RibbonCollapsedGroupOverflow
{
    public static List<Button> ReconcileButtons(
        StackPanel panel,
        IReadOnlyList<FrameworkElement> groups,
        Func<FrameworkElement, string> groupNameResolver,
        Func<FrameworkElement, ISet<string>, Button> buttonFactory)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(groupNameResolver);
        ArgumentNullException.ThrowIfNull(buttonFactory);

        var buttons = new List<Button>(groups.Count);
        var expectedGroupNames = groups
            .Select(groupNameResolver)
            .ToHashSet(StringComparer.Ordinal);
        var reusableButtonsByGroupName = new Dictionary<string, Button>(StringComparer.Ordinal);
        var buttonsToRemove = new List<Button>();
        var keyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var button in panel.Children
                     .OfType<Button>()
                     .Where(button => RibbonMetadata.IsCollapsedGroupButton(button)))
        {
            var title = RibbonTooltip.GetTitle(button) ?? "";
            if (!expectedGroupNames.Contains(title) ||
                !reusableButtonsByGroupName.TryAdd(title, button))
            {
                buttonsToRemove.Add(button);
                continue;
            }

            var keyTip = RibbonTooltip.GetKeyTip(button);
            if (!string.IsNullOrWhiteSpace(keyTip))
                keyTips.Add(keyTip!);
        }

        foreach (var button in buttonsToRemove)
            panel.Children.Remove(button);

        foreach (var group in groups)
        {
            var groupName = groupNameResolver(group);
            if (!reusableButtonsByGroupName.TryGetValue(groupName, out var button))
            {
                button = buttonFactory(group, keyTips);
                reusableButtonsByGroupName[groupName] = button;
            }

            var currentIndex = panel.Children.IndexOf(button);
            var targetIndex = panel.Children.IndexOf(group) + 1;
            if (currentIndex != targetIndex)
            {
                if (currentIndex >= 0)
                {
                    panel.Children.RemoveAt(currentIndex);
                    if (currentIndex < targetIndex)
                        targetIndex--;
                }

                panel.Children.Insert(targetIndex, button);
            }

            buttons.Add(button);
        }

        return buttons;
    }

    public static ContextMenu CreateLazyMenu(
        FrameworkElement group,
        Func<FrameworkElement, string> groupNameResolver,
        Func<object, object?> cloneMenuItem,
        Action<ItemCollection, ItemCollection> synchronizeClonedItems,
        Action<MenuItem> restoreFocus)
    {
        ArgumentNullException.ThrowIfNull(group);
        var adapter = new MenuAdapter(
            groupNameResolver,
            cloneMenuItem,
            synchronizeClonedItems,
            restoreFocus);
        var menu = new ContextMenu { Tag = group };
        menu.Opened += (_, _) =>
        {
            EnsureMenuItems(menu, adapter);
            SynchronizeTopLevelMenuItems(menu.Items);
        };
        return menu;
    }

    public static void EnsureMenuItems(
        ContextMenu menu,
        Func<FrameworkElement, string> groupNameResolver,
        Func<object, object?> cloneMenuItem,
        Action<ItemCollection, ItemCollection> synchronizeClonedItems,
        Action<MenuItem> restoreFocus) =>
        EnsureMenuItems(
            menu,
            new MenuAdapter(
                groupNameResolver,
                cloneMenuItem,
                synchronizeClonedItems,
                restoreFocus));

    public static void FocusPlacementTarget(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (DependencyObject? current = item; current is not null; current = GetTreeParent(current))
        {
            if (current is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is UIElement placementTarget)
            {
                placementTarget.Focus();
                return;
            }
        }
    }

    public static void EnsureChevronAdorner(
        Button button,
        Func<FrameworkElement> chevronFactory)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(chevronFactory);

        var layer = AdornerLayer.GetAdornerLayer(button);
        if (layer is null)
            return;

        if (layer.GetAdorners(button)?.Any(adorner => adorner is RibbonCollapsedGroupChevronAdorner) == true)
            return;

        layer.Add(new RibbonCollapsedGroupChevronAdorner(button, chevronFactory()));
        button.IsVisibleChanged += (_, _) => layer.Update(button);
        button.SizeChanged += (_, _) => layer.Update(button);
    }

    private static void EnsureMenuItems(ContextMenu menu, MenuAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (menu.Tag is not FrameworkElement group)
            return;

        if (menu.Items.Count > 0 &&
            EnumerateMenuItems(menu).Any(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item))))
        {
            return;
        }

        menu.Items.Clear();
        group.UpdateLayout();
        PopulateMenu(menu, group, adapter);
    }

    private static void PopulateMenu(ContextMenu menu, FrameworkElement group, MenuAdapter adapter)
    {
        var added = new HashSet<ButtonBase>();
        foreach (var button in RibbonAdaptiveWpfSurface.EnumerateVisualDescendants(group).OfType<ButtonBase>())
        {
            if (button.Visibility != Visibility.Visible)
                continue;

            if (!added.Add(button) ||
                RibbonAdaptiveWpfSurface.FindVisualAncestor<ButtonBase>(button) is { } ancestor &&
                !ReferenceEquals(ancestor, button))
            {
                continue;
            }

            if (CreateMenuItem(button, adapter) is { } item)
                menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = adapter.GroupNameResolver(group),
                IsEnabled = false
            });
        }
    }

    private static MenuItem? CreateMenuItem(ButtonBase button, MenuAdapter adapter)
    {
        var title = RibbonTooltip.GetTitle(button);
        if (string.IsNullOrWhiteSpace(title))
            title = button.Content as string;
        if (string.IsNullOrWhiteSpace(title))
            title = button.Name;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var item = new MenuItem
        {
            Header = title,
            IsEnabled = button.IsEnabled,
            Tag = button
        };

        var keyTip = RibbonTooltip.GetKeyTip(button);
        if (!string.IsNullOrWhiteSpace(keyTip))
            RibbonTooltip.SetKeyTip(item, keyTip);

        if (button.ContextMenu is { Items.Count: > 0 } contextMenu)
        {
            foreach (var child in contextMenu.Items)
            {
                if (adapter.CloneMenuItem(child) is { } childItem)
                    item.Items.Add(childItem);
            }

            item.SubmenuOpened += (_, _) =>
            {
                contextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, contextMenu));
                adapter.SynchronizeClonedItems(contextMenu.Items, item.Items);
            };
        }
        else
        {
            item.Click += (_, _) =>
            {
                InvokeButton(button);
                adapter.RestoreFocus(item);
            };
        }

        return item;
    }

    private static void InvokeButton(ButtonBase button)
    {
        if (button is ToggleButton toggleButton)
            toggleButton.IsChecked = toggleButton.IsChecked != true;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void SynchronizeTopLevelMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is ButtonBase sourceButton)
                item.IsEnabled = sourceButton.IsEnabled;
        }
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl itemsControl)
    {
        foreach (var item in itemsControl.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in EnumerateMenuItems(item))
                yield return child;
        }
    }

    private static DependencyObject? GetTreeParent(DependencyObject element)
    {
        if (element is Visual && VisualTreeHelper.GetParent(element) is { } visualParent)
            return visualParent;

        return LogicalTreeHelper.GetParent(element);
    }

    private sealed record MenuAdapter(
        Func<FrameworkElement, string> GroupNameResolver,
        Func<object, object?> CloneMenuItem,
        Action<ItemCollection, ItemCollection> SynchronizeClonedItems,
        Action<MenuItem> RestoreFocus);
}

internal sealed class RibbonCollapsedGroupChevronAdorner : Adorner
{
    private readonly VisualCollection _children;
    private readonly FrameworkElement _chevron;

    public RibbonCollapsedGroupChevronAdorner(UIElement adornedElement, FrameworkElement chevron)
        : base(adornedElement)
    {
        _chevron = chevron;
        RibbonMetadata.SetRole(_chevron, RibbonMetadataRole.CollapsedChevron);
        _children = new VisualCollection(this) { _chevron };
        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size constraint)
    {
        _chevron.Visibility = ShouldShowChevron() ? Visibility.Visible : Visibility.Collapsed;
        if (_chevron.Visibility != Visibility.Visible)
            return new Size(0, 0);

        _chevron.Measure(new Size(8, 8));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!ShouldShowChevron())
        {
            _chevron.Visibility = Visibility.Collapsed;
            _chevron.Arrange(new Rect(0, 0, 0, 0));
            return finalSize;
        }

        _chevron.Visibility = Visibility.Visible;
        var x = Math.Max(0, (AdornedElement.RenderSize.Width - 8) / 2);
        var y = Math.Max(0, AdornedElement.RenderSize.Height - 9);
        _chevron.Arrange(new Rect(new Point(x, y), new Size(8, 8)));
        return finalSize;
    }

    private bool ShouldShowChevron() =>
        AdornedElement is FrameworkElement { IsVisible: true } &&
        AdornedElement.RenderSize is { Width: > 0, Height: > 0 };
}
