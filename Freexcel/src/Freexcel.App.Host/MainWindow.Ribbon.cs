using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Freexcel.Core.Model;

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
               CollapseOneMoreRibbonGroup(plannedStates))
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

    private static bool CollapseOneMoreRibbonGroup(RibbonAdaptiveGroupState[] states)
    {
        for (var i = states.Length - 1; i >= 0; i--)
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
        var veryNarrow = availableWidth <= 700;
        foreach (var button in collapsedButtons)
        {
            button.Width = veryNarrow ? 56 : 64;
            button.Margin = veryNarrow ? new Thickness(0) : new Thickness(1, 0, 3, 0);
            button.Padding = veryNarrow ? new Thickness(1, 2, 1, 2) : new Thickness(3, 2, 3, 2);

            var textBlocks = button.Content is StackPanel panel
                ? panel.Children.OfType<TextBlock>()
                : EnumerateVisualDescendants(button).OfType<TextBlock>();

            foreach (var textBlock in textBlocks)
            {
                if (textBlock.Tag?.ToString() == "RibbonLabel")
                {
                    textBlock.Visibility = veryNarrow ? Visibility.Collapsed : Visibility.Visible;
                    textBlock.FontSize = veryNarrow ? 9 : 10;
                    textBlock.MaxWidth = veryNarrow ? 54 : 60;
                }
                else if (textBlock.Tag?.ToString() == "RibbonIcon" && textBlock.Text != "\uE70D")
                {
                    textBlock.FontSize = veryNarrow ? 18 : 22;
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
            IsEnabled = button.IsEnabled
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
            item.Click += (_, _) => InvokeRibbonButton(button);
        }

        return item;
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
            if (args.OriginalSource is MenuItem original && original.Items.Count > 0)
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

    private void NormalizeRibbonCommandButtons()
    {
        if (RibbonTabs is null)
            return;

        foreach (var button in EnumerateVisualDescendants(RibbonTabs).OfType<ButtonBase>())
        {
            if (button is CheckBox or RadioButton)
                continue;

            if (button.Content is not string label || string.IsNullOrWhiteSpace(label))
                continue;

            var title = RibbonTooltip.GetTitle(button);
            var commandName = string.IsNullOrWhiteSpace(title) ? label : title;
            var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
            ApplyRibbonCommandSize(button, layoutKind);
            if (layoutKind is RibbonCommandLayoutKind.Small)
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetSmallRibbonCommandWidth(label));
            var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
            var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24;
            SetRibbonCompactWidthTag(button, fullWidth, compactWidth);

            button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
            button.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Center;
        }

    }

    private void NormalizeRibbonSurface(bool forceCompact = false)
    {
        if (_normalizingRibbonSurface)
            return;

        _normalizingRibbonSurface = true;
        try
        {
            NormalizeRibbonCommandButtons();
            NormalizeExistingRibbonIconText();
            ConfigureInsertRibbonSurface();
            NormalizeRibbonCommandGroups();
            AlignRibbonIconColumns();
            HideRibbonScrollBars();
            ApplyToolbarDropdownWhiteBackgrounds();
            UpdateRibbonCompactMode(force: forceCompact);
        }
        finally
        {
            _normalizingRibbonSurface = false;
        }
    }

    private void HideRibbonScrollBars()
    {
        if (RibbonTabs is null)
            return;

        foreach (var scrollViewer in EnumerateVisualDescendants(RibbonTabs).OfType<ScrollViewer>())
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

    private void NormalizeRibbonSurfaceAfterTabSelection()
    {
        NormalizeRibbonSurface(forceCompact: true);
        Dispatcher.BeginInvoke(
            (Action)(() => NormalizeRibbonSurface(forceCompact: true)),
            DispatcherPriority.Loaded);
    }

    private void ConfigureInsertRibbonSurface()
    {
        var insertTab = RibbonTabs?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Insert", StringComparison.Ordinal));

        if (insertTab is null)
            return;

        var contentRoot = GetRibbonTabContentRoot(insertTab);
        foreach (var button in EnumerateVisualDescendants(contentRoot)
                     .Concat(EnumerateLogicalDescendants(contentRoot))
                     .OfType<Button>()
                     .Distinct())
        {
            var title = GetRibbonButtonTitleOrLabel(button);
            var groupName = FindRibbonOwningGroupName(button);
            if ((string.Equals(groupName, "Charts", StringComparison.Ordinal) &&
                 !RibbonCommandPresentationPlanner.IsInsertRibbonChartCommand(title)) ||
                RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static string FindRibbonOwningGroupName(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is Grid grid &&
                grid.Children.OfType<Border>().Any(border => Grid.GetRow(border) == 1))
            {
                return GetRibbonGroupName(grid);
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return "";
    }

    private static string GetRibbonButtonTitleOrLabel(ButtonBase button)
    {
        var title = RibbonTooltip.GetTitle(button);
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        var label = FindRibbonContentLabel(button.Content);

        return label ?? "";
    }

    private static string? FindRibbonContentLabel(object? content)
    {
        if (content is TextBlock textBlock &&
            string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return textBlock.Text.Trim();
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (FindRibbonContentLabel(child) is { } label)
                    return label;
            }
        }

        if (content is ContentControl contentControl &&
            !ReferenceEquals(contentControl.Content, content))
        {
            return FindRibbonContentLabel(contentControl.Content);
        }

        return null;
    }

    private void AlignRibbonIconColumns()
    {
        if (RibbonTabs is null)
            return;

        foreach (var stack in EnumerateVisualDescendants(RibbonTabs).OfType<StackPanel>())
        {
            if (stack.Tag?.ToString()?.StartsWith("RibbonCommandContent", StringComparison.Ordinal) is true)
                continue;

            if (stack.Orientation != Orientation.Horizontal || stack.Children.Count < 2)
                continue;

            if (stack.Children[0] is not FrameworkElement icon || stack.Children[1] is not TextBlock label)
                continue;

            if (FindVisualAncestor<ButtonBase>(stack) is null)
                continue;

            if (icon is not Image)
                icon.Width = Math.Max(icon.Width is > 0 ? icon.Width : 0, 18);
            icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, icon.Margin.Top, 4, icon.Margin.Bottom);
            label.MinWidth = Math.Max(label.MinWidth, 84);
            label.FontSize = Math.Max(label.FontSize, 12);
            label.TextTrimming = TextTrimming.None;
            label.TextWrapping = TextWrapping.NoWrap;
            stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }
    }

    private void NormalizeExistingRibbonIconText()
    {
        foreach (var button in EnumerateVisualDescendants(this).OfType<ButtonBase>())
        {
            if (TryNormalizeHomeCompactIconButton(button))
                continue;

            if (TryNormalizeHomeSmallCommandButton(button))
                continue;

            if (TryNormalizeStaticRibbonCommandButton(button))
                continue;

            var tall = button is FrameworkElement element && element.Height >= 46;
            ReplaceRibbonGlyphIcons(button.Content, button, tall);
            NormalizeRibbonButtonSizeForCommandIcons(button, tall);
            foreach (var textBlock in EnumerateRibbonTextContent(button.Content))
            {
                if (string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal))
                {
                    textBlock.FontSize = 12;
                    textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                    textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    if (tall)
                    {
                        textBlock.TextAlignment = TextAlignment.Center;
                        textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    }

                    continue;
                }

                var isIcon = string.Equals(textBlock.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal);
                if (!isIcon)
                    continue;

                textBlock.Tag = "RibbonIcon";
                textBlock.FontSize = tall ? 22 : Math.Max(12, textBlock.FontSize);
                textBlock.Width = tall ? Math.Max(24, textBlock.Width) : Math.Max(16, textBlock.Width);
                textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                textBlock.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                textBlock.TextAlignment = TextAlignment.Center;
            }
        }
    }

    private bool TryNormalizeHomeCompactIconButton(ButtonBase button)
    {
        if (HomeRibbonPanel is null ||
            !ReferenceEquals(FindHomeRibbonAncestor(button), HomeRibbonPanel) ||
            button is not FrameworkElement element ||
            element.Width > 46 ||
            element.Height > 32)
        {
            return false;
        }

        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        element.Width = 26;
        element.Height = 26;
        SetRibbonCompactWidthTag(button, 26, 24);
        element.Margin = new Thickness(1, 0, 1, 0);
        if (button is Control control)
            control.Padding = new Thickness(1);

        button.Content = CreateRibbonIconOnlyContent(commandName, 20);
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        return true;
    }

    private bool TryNormalizeHomeSmallCommandButton(ButtonBase button)
    {
        if (HomeRibbonPanel is null ||
            !ReferenceEquals(FindHomeRibbonAncestor(button), HomeRibbonPanel) ||
            button.Content is string ||
            ContainsUnreplacedRibbonIcon(button.Content) ||
            button is not FrameworkElement element ||
            element.Height > 34)
        {
            return false;
        }

        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        element.Width = GetSmallRibbonCommandWidth(commandName);
        element.Height = 24;
        SetRibbonCompactWidthTag(button, element.Width, 24);
        if (button is Control control)
            control.Padding = new Thickness(4, 2, 4, 2);
        button.Content = CreateRibbonCommandContent(commandName, commandName, RibbonCommandLayoutKind.Small);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        return true;
    }

    private static StackPanel? FindHomeRibbonAncestor(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is StackPanel { Name: "HomeRibbonPanel" } stack)
                return stack;

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private bool TryNormalizeStaticRibbonCommandButton(ButtonBase button)
    {
        if (RibbonTabs is null ||
            button is not Button commandButton ||
            FindVisualAncestor<TabControl>(commandButton) != RibbonTabs ||
            !ContainsUnreplacedRibbonIcon(commandButton.Content))
        {
            return false;
        }

        var commandName = GetRibbonButtonTitleOrLabel(commandButton);
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var label = commandName;
        var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
        ApplyRibbonCommandSize(commandButton, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
            commandButton.Width = Math.Max(commandButton.Width is > 0 ? commandButton.Width : 0, GetSmallRibbonCommandWidth(label));
        SetRibbonCompactWidthTag(
            commandButton,
            commandButton.Width is > 0 ? commandButton.Width : Math.Max(commandButton.ActualWidth, 64),
            layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 24);

        commandButton.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
        if (layoutKind is RibbonCommandLayoutKind.Small)
            commandButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        commandButton.HorizontalContentAlignment = layoutKind is RibbonCommandLayoutKind.Small
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        return true;
    }

    private static void NormalizeRibbonButtonSizeForCommandIcons(ButtonBase button, bool tall)
    {
        if (button is not FrameworkElement element || !ContainsRibbonCommandIcon(button.Content))
            return;

        if (tall)
        {
            var tallLabel = FindRibbonContentLabel(button.Content) ?? GetRibbonButtonTitleOrLabel(button);
            element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, GetLargeRibbonCommandWidth(tallLabel));
            element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 76);
            SetRibbonCompactWidthTag(button, element.Width, 38);
            return;
        }

        var label = FindRibbonContentLabel(button.Content);
        if (string.IsNullOrWhiteSpace(label))
            return;

        var minWidth = label.Length switch
        {
            <= 3 => 58,
            <= 6 => 66,
            <= 10 => 92,
            <= 14 => 126,
            _ => Math.Min(150, 44 + label.Length * 6)
        };

        element.Width = Math.Max(element.Width is > 0 ? element.Width : 0, minWidth);
        element.Height = Math.Max(element.Height is > 0 ? element.Height : 0, 24);
        SetRibbonCompactWidthTag(button, element.Width, 24);
    }

    private static void SetRibbonCompactWidthTag(ButtonBase button, double fullWidth, double compactWidth)
    {
        button.Tag = $"RibbonCompact:{fullWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{compactWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static bool ContainsRibbonCommandIcon(object? content)
    {
        switch (content)
        {
            case FrameworkElement element when string.Equals(element.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal):
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsRibbonCommandIcon);
            case Decorator decorator:
                return ContainsRibbonCommandIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsRibbonCommandIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static bool ContainsUnreplacedRibbonIcon(object? content)
    {
        switch (content)
        {
            case RibbonIcon:
                return true;
            case Panel panel:
                return panel.Children.Cast<object>().Any(ContainsUnreplacedRibbonIcon);
            case Decorator decorator:
                return ContainsUnreplacedRibbonIcon(decorator.Child);
            case ContentControl contentControl when !ReferenceEquals(contentControl.Content, content):
                return ContainsUnreplacedRibbonIcon(contentControl.Content);
            default:
                return false;
        }
    }

    private static void ReplaceRibbonGlyphIcons(object? content, ButtonBase owner, bool tall)
    {
        switch (content)
        {
            case null:
                return;
            case RibbonIcon ribbonIcon:
                owner.Content = CreateStaticRibbonCommandIcon(owner, ribbonIcon, tall);
                return;
            case TextBlock textBlock when IsRibbonIconTextBlock(textBlock):
                owner.Content = CreateStaticRibbonVectorIcon(owner, textBlock, tall);
                return;
            case Panel panel:
                for (var i = 0; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is RibbonIcon childRibbonIcon)
                    {
                        var replacement = CreateStaticRibbonCommandIcon(owner, childRibbonIcon, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    if (panel.Children[i] is TextBlock childText && IsRibbonIconTextBlock(childText))
                    {
                        var replacement = CreateStaticRibbonVectorIcon(owner, childText, tall);
                        panel.Children.RemoveAt(i);
                        panel.Children.Insert(i, replacement);
                        continue;
                    }

                    ReplaceRibbonGlyphIcons(panel.Children[i], owner, tall);
                }

                return;
            case Decorator decorator:
                if (decorator.Child is RibbonIcon decoratorRibbonIcon)
                    decorator.Child = CreateStaticRibbonCommandIcon(owner, decoratorRibbonIcon, tall);
                else if (decorator.Child is TextBlock decoratorText && IsRibbonIconTextBlock(decoratorText))
                    decorator.Child = CreateStaticRibbonVectorIcon(owner, decoratorText, tall);
                else
                    ReplaceRibbonGlyphIcons(decorator.Child, owner, tall);
                return;
            case ContentControl contentControl when !ReferenceEquals(contentControl, owner):
                if (contentControl.Content is RibbonIcon contentRibbonIcon)
                    contentControl.Content = CreateStaticRibbonCommandIcon(owner, contentRibbonIcon, tall);
                else if (contentControl.Content is TextBlock contentText && IsRibbonIconTextBlock(contentText))
                    contentControl.Content = CreateStaticRibbonVectorIcon(owner, contentText, tall);
                else
                    ReplaceRibbonGlyphIcons(contentControl.Content, owner, tall);
                return;
        }
    }

    private static bool IsRibbonIconTextBlock(TextBlock textBlock)
    {
        return string.Equals(textBlock.Tag?.ToString(), "RibbonIcon", StringComparison.Ordinal);
    }

    private static FrameworkElement CreateStaticRibbonCommandIcon(ButtonBase owner, RibbonIcon source, bool tall)
    {
        var commandName = source.Kind == RibbonCommandIconKind.Previous
            ? "Back to workbook"
            : GetStaticRibbonIconCommandName(owner, source.Kind.ToString());
        var fallbackIcon = new RibbonCommandIcon(source.Kind);
        var iconSize = IsWhiteBrush(source.Foreground) ? source.IconSize : tall ? 32 : 22;
        if (IsWhiteBrush(source.Foreground))
        {
            var fallbackElement = RibbonIconFactory.CreateIcon(
                fallbackIcon,
                iconSize,
                source.Foreground ?? owner.Foreground);
            fallbackElement.Tag = "RibbonIcon";
            fallbackElement.HorizontalAlignment = source.HorizontalAlignment;
            fallbackElement.VerticalAlignment = source.VerticalAlignment;
            fallbackElement.Margin = source.Margin;
            return fallbackElement;
        }

        var commandIcon = RibbonIconFactory.CreateCommandIcon(
            commandName,
            fallbackIcon,
            iconSize,
            source.Foreground ?? owner.Foreground);
        commandIcon.Tag = "RibbonIcon";
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static FrameworkElement CreateStaticRibbonVectorIcon(ButtonBase owner, TextBlock source, bool tall)
    {
        var commandName = GetStaticRibbonIconCommandName(owner, source.Text);
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var iconSize = tall ? 32 : 22;
        var commandIcon = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, source.Foreground);
        commandIcon.Tag = "RibbonIcon";
        commandIcon.HorizontalAlignment = source.HorizontalAlignment;
        commandIcon.VerticalAlignment = source.VerticalAlignment;
        commandIcon.Margin = source.Margin;
        return commandIcon;
    }

    private static string GetStaticRibbonIconCommandName(ButtonBase owner, string fallback)
    {
        var title = owner is FrameworkElement element
            ? RibbonTooltip.GetTitle(element)
            : null;
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        if (!string.IsNullOrWhiteSpace(owner.Name))
        {
            var name = owner.Name;
            foreach (var suffix in new[] { "Button", "Btn" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            return name;
        }

        return fallback switch
        {
            nameof(RibbonCommandIconKind.WindowMinimize) => "Minimize",
            nameof(RibbonCommandIconKind.WindowMaximize) => "Maximize",
            nameof(RibbonCommandIconKind.WindowClose) => "Close",
            nameof(RibbonCommandIconKind.Previous) => "Back to workbook",
            _ => fallback
        };
    }

    private static bool IsWhiteBrush(Brush brush)
    {
        return brush is SolidColorBrush solid &&
               solid.Color.R >= 245 &&
               solid.Color.G >= 245 &&
               solid.Color.B >= 245;
    }


    private static IEnumerable<TextBlock> EnumerateRibbonTextContent(object? content)
    {
        if (content is TextBlock textBlock)
        {
            yield return textBlock;
            yield break;
        }

        if (content is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var text in EnumerateRibbonTextContent(child))
                    yield return text;
            }
        }
        else if (content is ContentControl contentControl &&
                 !ReferenceEquals(contentControl.Content, content))
        {
            foreach (var text in EnumerateRibbonTextContent(contentControl.Content))
                yield return text;
        }
        else if (content is Decorator decorator)
        {
            foreach (var text in EnumerateRibbonTextContent(decorator.Child))
                yield return text;
        }
    }

    private void ApplyToolbarDropdownWhiteBackgrounds()
    {
        if (RibbonTabs is null)
            return;

        foreach (var comboBox in EnumerateVisualDescendants(RibbonTabs).OfType<ComboBox>())
        {
            comboBox.Background = Brushes.White;
            comboBox.Foreground = Brushes.Black;
            comboBox.Resources[SystemColors.WindowBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.ControlBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.MenuBrushKey] = Brushes.White;
            comboBox.DropDownOpened -= ToolbarComboBox_DropDownOpened;
            comboBox.DropDownOpened += ToolbarComboBox_DropDownOpened;
        }
    }

    private static void ToolbarComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        comboBox.Dispatcher.BeginInvoke((Action)(() =>
        {
            comboBox.ApplyTemplate();
            if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup ||
                popup.Child is not DependencyObject popupRoot)
            {
                return;
            }

            ForceDropdownWhite(popupRoot);
        }));
    }

    private static void ForceDropdownWhite(DependencyObject root)
    {
        if (root is Control control)
        {
            control.Background = Brushes.White;
            control.Foreground = Brushes.Black;
        }
        else if (root is Border border)
        {
            border.Background = Brushes.White;
        }
        else if (root is Panel panel)
        {
            panel.Background = Brushes.White;
        }

        if (root is ComboBoxItem item)
        {
            item.Background = Brushes.White;
            item.Foreground = Brushes.Black;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ForceDropdownWhite(VisualTreeHelper.GetChild(root, i));
    }

    private static FrameworkElement CreateRibbonCommandContent(string commandName, string label, RibbonCommandLayoutKind layoutKind)
    {
        var tall = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium;
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconSize = layoutKind == RibbonCommandLayoutKind.Large ? 32 : 20;
        var slotSize = layoutKind == RibbonCommandLayoutKind.Large ? 34 : 20;
        var iconSlot = new Border
        {
            Width = slotSize,
            Height = slotSize,
            CornerRadius = tall ? new CornerRadius(3) : new CornerRadius(2),
            Background = slotBackground,
            BorderBrush = slotBorder,
            BorderThickness = slotBorder is null ? new Thickness(0) : new Thickness(1),
            Child = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush),
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = tall ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 5, 0)
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            Tag = "RibbonLabel",
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = tall ? 96 : double.PositiveInfinity,
            TextTrimming = tall ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            HorizontalAlignment = tall ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextAlignment = tall ? TextAlignment.Center : TextAlignment.Left,
            LineHeight = tall ? 14 : double.NaN
        };

        var contentTag = layoutKind == RibbonCommandLayoutKind.Large
            ? "RibbonCommandContent:L"
            : layoutKind == RibbonCommandLayoutKind.Medium
                ? "RibbonCommandContent:M"
                : "RibbonCommandContent:S";

        if (tall)
        {
            return new StackPanel
            {
                Tag = contentTag,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    iconSlot,
                    labelBlock
                }
            };
        }

        return new StackPanel
        {
            Tag = contentTag,
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children =
            {
                iconSlot,
                labelBlock
            }
        };
    }

    private static FrameworkElement CreateRibbonIconOnlyContent(string commandName, double iconSize)
    {
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var (_, _, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var iconElement = RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush);
        iconElement.Tag = "RibbonIcon";

        return new Grid
        {
            Tag = "RibbonCommandContent",
            Width = 24,
            Height = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children = { iconElement }
        };
    }

    private static (Brush? SlotBackground, Brush? SlotBorder, Brush GlyphBrush) GetRibbonIconAccentBrushes(
        RibbonCommandIconAccent accent)
    {
        return accent switch
        {
            RibbonCommandIconAccent.Green => (BrushFromRgb(232, 244, 239), BrushFromRgb(33, 115, 70), BrushFromRgb(24, 92, 55)),
            RibbonCommandIconAccent.Chart => (BrushFromRgb(232, 241, 252), BrushFromRgb(68, 114, 196), BrushFromRgb(47, 84, 150)),
            RibbonCommandIconAccent.Data => (BrushFromRgb(229, 243, 250), BrushFromRgb(0, 120, 170), BrushFromRgb(0, 92, 135)),
            RibbonCommandIconAccent.Theme => (BrushFromRgb(241, 236, 250), BrushFromRgb(112, 48, 160), BrushFromRgb(85, 35, 125)),
            RibbonCommandIconAccent.Fill => (BrushFromRgb(255, 248, 218), BrushFromRgb(191, 144, 0), BrushFromRgb(116, 88, 0)),
            RibbonCommandIconAccent.Color => (BrushFromRgb(255, 235, 235), BrushFromRgb(192, 0, 0), BrushFromRgb(150, 0, 0)),
            RibbonCommandIconAccent.Border => (BrushFromRgb(245, 245, 245), BrushFromRgb(96, 96, 96), BrushFromRgb(31, 31, 31)),
            RibbonCommandIconAccent.Warning => (BrushFromRgb(255, 244, 214), BrushFromRgb(214, 157, 0), BrushFromRgb(138, 91, 0)),
            RibbonCommandIconAccent.Protect => (BrushFromRgb(232, 244, 239), BrushFromRgb(33, 115, 70), BrushFromRgb(24, 92, 55)),
            RibbonCommandIconAccent.Help => (BrushFromRgb(235, 242, 255), BrushFromRgb(68, 114, 196), BrushFromRgb(47, 84, 150)),
            _ => (Brushes.Transparent, null, Brushes.Black)
        };
    }

    private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static void ApplyRibbonCommandSize(ButtonBase button, RibbonCommandLayoutKind layoutKind)
    {
        switch (layoutKind)
        {
            case RibbonCommandLayoutKind.Large:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, GetLargeRibbonCommandWidth(GetRibbonButtonTitleOrLabel(button)));
                button.Height = 76;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            case RibbonCommandLayoutKind.Medium:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 74);
                button.Height = 48;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            default:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 72);
                button.Height = Math.Max(button.Height is > 0 ? button.Height : 0, 24);
                button.Padding = new Thickness(4, 2, 4, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                break;
        }
    }

    private void NormalizeRibbonCommandGroups()
    {
        if (RibbonTabs is null)
            return;

        NormalizeRibbonCommandColumns();
    }

    private void NormalizeRibbonCommandColumns()
    {
        if (RibbonTabs is null)
            return;

        var panels = EnumerateVisualDescendants(RibbonTabs)
            .OfType<StackPanel>()
            .Where(panel => panel != HomeRibbonPanel &&
                            panel.Orientation == Orientation.Vertical &&
                            FindVisualAncestor<ButtonBase>(panel) is null)
            .ToList();

        foreach (var panel in panels)
        {
            var directButtons = panel.Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToList();
            if (directButtons.Count <= 3)
                continue;

            var parent = VisualTreeHelper.GetParent(panel) ?? LogicalTreeHelper.GetParent(panel);
            if (parent is not Panel parentPanel)
                continue;

            var index = parentPanel.Children.IndexOf(panel);
            if (index < 0)
                continue;

            var row = Grid.GetRow(panel);
            var column = Grid.GetColumn(panel);
            var rowSpan = Grid.GetRowSpan(panel);
            var columnSpan = Grid.GetColumnSpan(panel);
            var margin = panel.Margin;
            var verticalAlignment = panel.VerticalAlignment;
            var horizontalAlignment = panel.HorizontalAlignment;

            panel.Children.Clear();
            var grid = new UniformGrid
            {
                Rows = 3,
                Columns = (int)Math.Ceiling(directButtons.Count / 3.0),
                Margin = margin,
                VerticalAlignment = verticalAlignment,
                HorizontalAlignment = horizontalAlignment
            };

            Grid.SetRow(grid, row);
            Grid.SetColumn(grid, column);
            Grid.SetRowSpan(grid, rowSpan);
            Grid.SetColumnSpan(grid, columnSpan);

            foreach (var button in directButtons)
            {
                NormalizeDenseRibbonColumnButton(button);
                grid.Children.Add(button);
            }

            parentPanel.Children.RemoveAt(index);
            parentPanel.Children.Insert(index, grid);
        }
    }

    private static void NormalizeDenseRibbonColumnButton(Button button)
    {
        var commandName = GetRibbonButtonTitleOrLabel(button);
        if (string.IsNullOrWhiteSpace(commandName))
            return;

        var label = FindRibbonContentLabel(button.Content) ?? commandName;
        button.Height = 24;
        button.Width = GetSmallRibbonCommandWidth(label);
        SetRibbonCompactWidthTag(button, button.Width, 24);
        button.Padding = new Thickness(4, 2, 4, 2);
        button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        button.Content = CreateRibbonCommandContent(commandName, label, RibbonCommandLayoutKind.Small);
    }

    private static double GetSmallRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 3 => 58,
            <= 6 => 66,
            <= 10 => 92,
            <= 14 => 126,
            _ => Math.Min(150, 44 + length * 6)
        };
    }

    private static double GetLargeRibbonCommandWidth(string label)
    {
        var length = string.IsNullOrWhiteSpace(label) ? 0 : label.Trim().Length;
        return length switch
        {
            <= 5 => 62,
            <= 9 => 76,
            <= 14 => 88,
            _ => 96
        };
    }
}
