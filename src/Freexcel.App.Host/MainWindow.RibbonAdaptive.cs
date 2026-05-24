using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void UpdateRibbonCompactMode(bool force = false)
    {
        if (RibbonTabs is null)
            return;

        var activePanel = GetActiveRibbonPanel();
        if (activePanel is null)
            return;

        RemoveRibbonCollapsedGroupButtons(activePanel);
        var groups = activePanel.Children
            .OfType<FrameworkElement>()
            .Where(e => e is not System.Windows.Shapes.Rectangle && !IsRibbonCollapsedGroupButton(e))
            .ToList();

        foreach (var group in groups)
        {
            group.Visibility = Visibility.Visible;
            SetRibbonGroupCompact(group, RibbonCompactLevel.Full);
        }

        var collapsedButtons = InsertRibbonCollapsedGroupButtons(activePanel, groups);

        activePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ribbonScrollViewer = FindVisualAncestor<ScrollViewer>(activePanel);
        var availableWidth = ribbonScrollViewer?.ActualWidth > 0
            ? ribbonScrollViewer.ActualWidth
            : ribbonScrollViewer?.ViewportWidth;
        if (availableWidth is null or <= 0)
            availableWidth = RibbonTabs.ActualWidth > 0 ? RibbonTabs.ActualWidth : activePanel.ActualWidth;
        if (RibbonTabs.ActualWidth > 0)
            availableWidth = Math.Min(availableWidth.Value, Math.Max(0, RibbonTabs.ActualWidth - 12));

        var fixedChromeWidth = MeasureRibbonFixedChromeWidth(activePanel) + 24;
        var adaptiveGroups = groups.Select((group, index) => MeasureRibbonAdaptiveGroup(group, collapsedButtons[index])).ToList();
        var plannedStates = RibbonAdaptiveLayoutPlanner.Plan(availableWidth.Value, adaptiveGroups, fixedChromeWidth).ToArray();
        plannedStates = RibbonAdaptiveLayoutPlanner
            .ApplyBreakpointOverrides(availableWidth.Value, adaptiveGroups.Select(group => group.Name).ToList(), plannedStates)
            .ToArray();
        ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates);
        SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth.Value);

        while (RibbonRowOverflows(activePanel, availableWidth.Value) &&
               CollapseOneMoreRibbonGroup(plannedStates, preserveFirstGroup: availableWidth.Value > 760))
        {
            ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth.Value);
        }

        var compacted = plannedStates.Any(state => state != RibbonAdaptiveGroupState.Full);
        _ribbonCompact = compacted;
    }

    private static void ApplyRibbonAdaptiveStates(
        IReadOnlyList<FrameworkElement> groups,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            collapsedButtons[i].Visibility = Visibility.Collapsed;
            groups[i].Visibility = Visibility.Visible;

            switch (plannedStates[i])
            {
                case RibbonAdaptiveGroupState.Full:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.Full);
                    break;
                case RibbonAdaptiveGroupState.SmallWithLabels:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.SmallWithLabels);
                    break;
                case RibbonAdaptiveGroupState.IconOnly:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.IconOnly);
                    break;
                case RibbonAdaptiveGroupState.Collapsed:
                    groups[i].Visibility = Visibility.Collapsed;
                    collapsedButtons[i].Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private static bool RibbonRowOverflows(StackPanel activePanel, double availableWidth)
    {
        activePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return activePanel.DesiredSize.Width > Math.Max(0, availableWidth - 4);
    }

    private static bool CollapseOneMoreRibbonGroup(RibbonAdaptiveGroupState[] states, bool preserveFirstGroup)
    {
        var firstCollapsibleIndex = preserveFirstGroup ? 1 : 0;
        for (var i = states.Length - 1; i >= firstCollapsibleIndex; i--)
        {
            if (states[i] == RibbonAdaptiveGroupState.Collapsed)
                continue;

            states[i] = RibbonAdaptiveGroupState.Collapsed;
            return true;
        }

        return false;
    }

    private static void SetCollapsedRibbonButtonFootprint(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var normalNarrow = availableWidth <= 920;
        foreach (var button in collapsedButtons)
        {
            button.Width = normalNarrow ? 44 : 64;
            button.Margin = normalNarrow ? new Thickness(0, 0, 2, 0) : new Thickness(1, 0, 3, 0);
            button.Padding = normalNarrow ? new Thickness(1, 2, 1, 2) : new Thickness(3, 2, 3, 2);

            var textBlocks = button.Content is StackPanel panel
                ? panel.Children.OfType<TextBlock>()
                : EnumerateVisualDescendants(button).OfType<TextBlock>();

            foreach (var textBlock in textBlocks)
            {
                if (textBlock.Tag?.ToString() == "RibbonLabel")
                {
                    textBlock.Visibility = normalNarrow ? Visibility.Collapsed : Visibility.Visible;
                    textBlock.FontSize = normalNarrow ? 9 : 10;
                    textBlock.MaxWidth = normalNarrow ? 40 : 60;
                }
                else if (textBlock.Tag?.ToString() == "RibbonIcon" && textBlock.Text != "\uE70D")
                {
                    textBlock.FontSize = normalNarrow ? 18 : 22;
                }
            }
        }
    }

    private static RibbonAdaptiveGroup MeasureRibbonAdaptiveGroup(FrameworkElement group, Button collapsedButton)
    {
        var name = GetRibbonGroupName(group);
        var fullWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.Full);
        var smallWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.SmallWithLabels);
        var iconWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.IconOnly);
        collapsedButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var collapsedWidth = Math.Max(48, collapsedButton.DesiredSize.Width);
        SetRibbonGroupCompact(group, RibbonCompactLevel.Full);

        return new RibbonAdaptiveGroup(name, fullWidth, smallWidth, iconWidth, collapsedWidth);
    }

    private static double MeasureRibbonGroupWidth(FrameworkElement group, RibbonCompactLevel level)
    {
        SetRibbonGroupCompact(group, level);
        group.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(0, group.DesiredSize.Width);
    }

    private static double MeasureRibbonFixedChromeWidth(StackPanel panel)
    {
        var fixedWidth = 0.0;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (child.Visibility != Visibility.Visible ||
                child is Grid ||
                IsRibbonCollapsedGroupButton(child))
            {
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            fixedWidth += child.DesiredSize.Width;
        }

        return fixedWidth;
    }

    private static List<Button> InsertRibbonCollapsedGroupButtons(StackPanel panel, IReadOnlyList<FrameworkElement> groups)
    {
        var buttons = new List<Button>(groups.Count);
        foreach (var group in groups)
        {
            var button = CreateRibbonCollapsedGroupButton(group);
            var index = panel.Children.IndexOf(group);
            panel.Children.Insert(index + 1, button);
            buttons.Add(button);
        }

        return buttons;
    }

    private static void RemoveRibbonCollapsedGroupButtons(StackPanel panel)
    {
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is FrameworkElement element && IsRibbonCollapsedGroupButton(element))
                panel.Children.RemoveAt(i);
        }
    }

    private static bool IsRibbonCollapsedGroupButton(FrameworkElement element) =>
        element.Tag is string tag && string.Equals(tag, "RibbonCollapsedGroupButton", StringComparison.Ordinal);

    private static Button CreateRibbonCollapsedGroupButton(FrameworkElement group)
    {
        var groupName = GetRibbonGroupName(group);
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var button = new Button
        {
            Tag = "RibbonCollapsedGroupButton",
            Width = 64,
            Height = 76,
            Margin = new Thickness(1, 0, 3, 0),
            Padding = new Thickness(3, 2, 3, 2),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            ContextMenu = CreateCollapsedRibbonGroupMenu(group),
            Content = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Width = 34,
                        Height = 34,
                        CornerRadius = new CornerRadius(3),
                        Background = slotBackground,
                        BorderBrush = slotBorder,
                        BorderThickness = slotBorder is null ? new Thickness(0) : new Thickness(1),
                        Child = RibbonIconFactory.CreateCommandIcon(groupName, icon, 28, glyphBrush),
                        SnapsToDevicePixels = true,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 2)
                    },
                    new TextBlock
                    {
                        Text = groupName,
                        Tag = "RibbonLabel",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = 60,
                        LineHeight = 14,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    }
                }
            }
        };

        button.SetResourceReference(StyleProperty, "RibbonTallButton");
        RibbonTooltip.SetTitle(button, groupName);
        RibbonTooltip.SetDescription(button, $"Show the {groupName} commands.");
        RibbonTooltip.SetKeyTip(button, CreateGroupKeyTip(groupName));
        button.Click += (_, _) =>
        {
            if (button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private static ContextMenu CreateCollapsedRibbonGroupMenu(FrameworkElement group)
    {
        var menu = new ContextMenu();
        var added = new HashSet<ButtonBase>();

        foreach (var button in EnumerateVisualDescendants(group).OfType<ButtonBase>())
        {
            if (button.Visibility != Visibility.Visible)
                continue;

            if (!added.Add(button) || FindVisualAncestor<ButtonBase>(button) is { } ancestor && !ReferenceEquals(ancestor, button))
                continue;

            if (CreateMenuItemForRibbonButton(button) is { } item)
                menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = GetRibbonGroupName(group),
                IsEnabled = false
            });
        }

        menu.Opened += (_, _) => SynchronizeCollapsedRibbonMenuItems(menu.Items);
        return menu;
    }

    private static MenuItem? CreateMenuItemForRibbonButton(ButtonBase button)
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
                if (CloneRibbonMenuItem(child) is { } childItem)
                    item.Items.Add(childItem);
            }

            item.SubmenuOpened += (_, _) =>
            {
                contextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, contextMenu));
                SynchronizeClonedMenuItems(contextMenu.Items, item.Items);
            };
        }
        else
        {
            item.Click += (_, _) =>
            {
                InvokeRibbonButton(button);
                FocusCollapsedRibbonMenuPlacementTarget(item);
            };
        }

        return item;
    }

    private static void FocusCollapsedRibbonMenuPlacementTarget(MenuItem item)
    {
        for (DependencyObject? current = item; current is not null; current = GetTreeParentForCollapsedRibbonMenu(current))
        {
            if (current is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is UIElement placementTarget)
            {
                placementTarget.Focus();
                return;
            }
        }
    }

    private static DependencyObject? GetTreeParentForCollapsedRibbonMenu(DependencyObject element)
    {
        if (element is Visual)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
                return visualParent;
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private static void SynchronizeCollapsedRibbonMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is ButtonBase sourceButton)
            {
                item.IsEnabled = sourceButton.IsEnabled;
                if (sourceButton.ContextMenu is { } sourceMenu)
                    SynchronizeClonedMenuItems(sourceMenu.Items, item.Items);
            }

            SynchronizeCollapsedRibbonMenuItems(item.Items);
        }
    }

    private static object? CloneRibbonMenuItem(object source)
    {
        if (source is Separator)
            return new Separator();

        if (source is not MenuItem sourceItem)
            return null;

        var item = new MenuItem
        {
            Header = CloneRibbonMenuContent(sourceItem.Header),
            Icon = CloneRibbonMenuContent(sourceItem.Icon),
            IsEnabled = sourceItem.IsEnabled,
            IsCheckable = sourceItem.IsCheckable,
            IsChecked = sourceItem.IsChecked,
            InputGestureText = sourceItem.InputGestureText
        };

        var keyTip = RibbonTooltip.GetKeyTip(sourceItem);
        if (!string.IsNullOrWhiteSpace(keyTip))
            RibbonTooltip.SetKeyTip(item, keyTip);

        foreach (var child in sourceItem.Items)
        {
            if (CloneRibbonMenuItem(child) is { } childItem)
                item.Items.Add(childItem);
        }

        item.SubmenuOpened += (_, _) =>
        {
            sourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, sourceItem));
            SynchronizeMenuItemState(sourceItem, item);
        };

        item.Click += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, item))
                return;

            if (sourceItem.Items.Count > 0)
                return;

            sourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, sourceItem));
        };

        return item;
    }

    private static object? CloneRibbonMenuContent(object? content)
    {
        if (content is null or string)
            return content;

        if (content is Image image)
        {
            return new Image
            {
                Source = image.Source,
                Width = image.Width,
                Height = image.Height,
                MaxWidth = image.MaxWidth,
                MaxHeight = image.MaxHeight,
                Stretch = image.Stretch,
                SnapsToDevicePixels = image.SnapsToDevicePixels,
                UseLayoutRounding = image.UseLayoutRounding,
                Margin = image.Margin,
                Opacity = image.Opacity
            };
        }

        if (content is TextBlock textBlock)
        {
            return new TextBlock
            {
                Text = textBlock.Text,
                FontSize = textBlock.FontSize,
                FontWeight = textBlock.FontWeight,
                FontStyle = textBlock.FontStyle,
                Foreground = textBlock.Foreground,
                Margin = textBlock.Margin,
                VerticalAlignment = textBlock.VerticalAlignment,
                HorizontalAlignment = textBlock.HorizontalAlignment
            };
        }

        if (content is FrameworkElement element)
        {
            try
            {
                return System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(element));
            }
            catch (InvalidOperationException)
            {
                return ExtractRibbonMenuText(element) ?? element.ToString();
            }
        }

        return content;
    }

    private static string? ExtractRibbonMenuText(DependencyObject element)
    {
        if (element is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            return textBlock.Text;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (ExtractRibbonMenuText(VisualTreeHelper.GetChild(element, i)) is { } text)
                return text;
        }

        return null;
    }

    private static void SynchronizeClonedMenuItems(ItemCollection sourceItems, ItemCollection clonedItems)
    {
        var clonedIndex = 0;
        foreach (var source in sourceItems)
        {
            if (source is Separator)
            {
                clonedIndex++;
                continue;
            }

            if (source is not MenuItem sourceItem)
                continue;

            while (clonedIndex < clonedItems.Count && clonedItems[clonedIndex] is not MenuItem)
                clonedIndex++;

            if (clonedIndex >= clonedItems.Count)
                break;

            if (clonedItems[clonedIndex] is MenuItem clonedItem)
                SynchronizeMenuItemState(sourceItem, clonedItem);

            clonedIndex++;
        }
    }

    private static void SynchronizeMenuItemState(MenuItem sourceItem, MenuItem clonedItem)
    {
        clonedItem.IsEnabled = sourceItem.IsEnabled;
        clonedItem.IsCheckable = sourceItem.IsCheckable;
        clonedItem.IsChecked = sourceItem.IsChecked;
        clonedItem.InputGestureText = sourceItem.InputGestureText;

        SynchronizeClonedMenuItems(sourceItem.Items, clonedItem.Items);
    }

    private static void InvokeRibbonButton(ButtonBase button)
    {
        if (button is ToggleButton toggleButton)
            toggleButton.IsChecked = toggleButton.IsChecked != true;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static string GetRibbonGroupName(FrameworkElement group)
    {
        if (group is Grid grid)
        {
            foreach (var border in grid.Children.OfType<Border>())
            {
                if (Grid.GetRow(border) == 1 &&
                    border.Child is TextBlock groupLabel &&
                    !string.IsNullOrWhiteSpace(groupLabel.Text))
                {
                    return groupLabel.Text.Trim();
                }
            }
        }

        var label = EnumerateVisualDescendants(group)
            .OfType<TextBlock>()
            .LastOrDefault(textBlock => FindVisualAncestor<Border>(textBlock) is not null &&
                                        FindVisualAncestor<ButtonBase>(textBlock) is null &&
                                        textBlock.Style is not null);

        return string.IsNullOrWhiteSpace(label?.Text) ? "Commands" : label.Text.Trim();
    }

    private static string CreateGroupKeyTip(string groupName)
    {
        var letters = new string(groupName.Where(char.IsLetterOrDigit).Take(2).ToArray());
        return string.IsNullOrWhiteSpace(letters) ? "G" : letters.ToUpperInvariant();
    }

    private StackPanel? GetActiveRibbonPanel()
    {
        if (RibbonTabs.SelectedItem is not TabItem tabItem)
            return null;

        if (string.Equals(tabItem.Header?.ToString(), "Home", StringComparison.Ordinal) &&
            HomeRibbonPanel is not null)
        {
            return HomeRibbonPanel;
        }

        var contentRoot = GetRibbonTabContentRoot(tabItem);
        return EnumerateVisualDescendants(contentRoot)
            .Concat(EnumerateLogicalDescendants(contentRoot))
            .OfType<StackPanel>()
            .Distinct()
            .OrderByDescending(panel => panel.Children.OfType<Grid>().Count())
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Grid>().Any());
    }

    private static DependencyObject GetRibbonTabContentRoot(TabItem tabItem) =>
        tabItem.Content as DependencyObject ?? tabItem;

    private enum RibbonCompactLevel
    {
        Full,
        SmallWithLabels,
        IconOnly
    }

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level)
    {
        foreach (var element in EnumerateVisualDescendants(group).OfType<FrameworkElement>())
        {
            if (element is TextBlock { Tag: string labelTag } label &&
                string.Equals(labelTag, "RibbonLabel", StringComparison.Ordinal))
            {
                label.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
                continue;
            }

            if (element is ButtonBase button)
            {
                var isLargeButton = button.Content is StackPanel cs &&
                    string.Equals(cs.Tag?.ToString(), "RibbonCommandContent:L", StringComparison.Ordinal);

                if (button.Tag is string tag &&
                    RibbonCommandPresentationPlanner.TryParseCompactWidths(tag, out var fullWidth, out var compactWidth))
                {
                    button.Width = level switch
                    {
                        RibbonCompactLevel.Full => fullWidth,
                        RibbonCompactLevel.SmallWithLabels => isLargeButton ? double.NaN : fullWidth,
                        _ => compactWidth
                    };
                }

                SetRibbonButtonCompact(button, level);
            }
        }
    }

    private static void SetRibbonButtonCompact(ButtonBase button, RibbonCompactLevel level)
    {
        foreach (var textBlock in EnumerateVisualDescendants(button).OfType<TextBlock>())
        {
            if (IsRibbonButtonLabel(textBlock))
                textBlock.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
        }

        var contentTag = (button.Content as FrameworkElement)?.Tag?.ToString() ?? "";
        bool isSmallOrMedium = contentTag is "RibbonCommandContent:S" or "RibbonCommandContent:M";

        if (!isSmallOrMedium)
        {
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;

            if (button.Content is FrameworkElement content)
                content.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            foreach (var stack in EnumerateVisualDescendants(button).OfType<StackPanel>())
            {
                if (stack.Orientation == Orientation.Horizontal)
                    stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            }
        }

        if (button.Content is StackPanel largeStack &&
            string.Equals(largeStack.Tag?.ToString(), "RibbonCommandContent:L", StringComparison.Ordinal))
        {
            ApplyLargeButtonCompactLayout(largeStack, button, level);
        }
    }

    private static void ApplyLargeButtonCompactLayout(
        StackPanel contentStack, ButtonBase button, RibbonCompactLevel level)
    {
        if (contentStack.Children.Count < 2 ||
            contentStack.Children[0] is not Border iconSlot ||
            contentStack.Children[1] is not TextBlock labelBlock)
        {
            return;
        }

        if (level == RibbonCompactLevel.Full)
        {
            contentStack.Orientation = Orientation.Vertical;
            contentStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            button.Height = 76;
            iconSlot.Width = 34;
            iconSlot.Height = 34;
            iconSlot.Margin = new Thickness(0, 0, 0, 2);
            if (iconSlot.Child is FrameworkElement iconChild)
            {
                iconChild.Width = 32;
                iconChild.Height = 32;
            }
            labelBlock.TextWrapping = TextWrapping.Wrap;
            labelBlock.MaxWidth = 96;
            labelBlock.TextTrimming = TextTrimming.None;
            labelBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            labelBlock.TextAlignment = TextAlignment.Center;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            contentStack.Orientation = Orientation.Horizontal;
            contentStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            button.Height = 48;
            iconSlot.Width = 24;
            iconSlot.Height = 24;
            iconSlot.Margin = new Thickness(0, 0, 5, 0);
            if (iconSlot.Child is FrameworkElement iconChild)
            {
                iconChild.Width = 24;
                iconChild.Height = 24;
            }
            labelBlock.TextWrapping = TextWrapping.NoWrap;
            labelBlock.MaxWidth = 90;
            labelBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            labelBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            labelBlock.TextAlignment = TextAlignment.Left;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        }
    }

    private static bool IsRibbonButtonLabel(TextBlock textBlock)
    {
        if (textBlock.Tag is string tag)
        {
            if (string.Equals(tag, "RibbonLabel", StringComparison.Ordinal))
                return true;
            if (string.Equals(tag, "RibbonIcon", StringComparison.Ordinal))
                return false;
        }

        var text = textBlock.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length <= 1)
            return false;

        var fontFamily = textBlock.FontFamily?.Source ?? "";
        if (fontFamily.Contains("MDL2", StringComparison.OrdinalIgnoreCase) ||
            fontFamily.Contains("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FindVisualAncestor<ButtonBase>(textBlock) is not null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        var current = element;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
