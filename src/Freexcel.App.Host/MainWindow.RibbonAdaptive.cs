using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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

        var groups = GetCachedRibbonAdaptiveGroups(activePanel);
        if (groups.Count == 0)
            return;

        var controlCacheKey = _ribbonAdaptiveControlCacheKey ??
            CreateRibbonAdaptiveMeasurementCacheKey(activePanel, groups);
        var collapsedButtons = GetCachedRibbonCollapsedGroupButtons(activePanel, groups, controlCacheKey);
        var availableWidth = GetRibbonAvailableWidth(activePanel);
        if (availableWidth <= 0)
            return;

        var cacheKey = controlCacheKey;
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups;
        double fixedChromeWidth;
        if (string.Equals(_ribbonAdaptiveMeasurementCacheKey, cacheKey, StringComparison.Ordinal) &&
            _ribbonAdaptiveGroupCache is not null)
        {
            adaptiveGroups = _ribbonAdaptiveGroupCache;
            fixedChromeWidth = _ribbonAdaptiveFixedChromeWidthCache;
        }
        else
        {
            ApplyRibbonAdaptiveStates(
                groups,
                collapsedButtons,
                Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groups.Count).ToArray(),
                previousStates: null);
            fixedChromeWidth = MeasureRibbonFixedChromeWidth(activePanel) + 24;
            adaptiveGroups = groups.Select((group, index) => MeasureRibbonAdaptiveGroup(group, collapsedButtons[index])).ToList();
            _ribbonAdaptiveMeasurementCacheKey = cacheKey;
            _ribbonAdaptiveGroupCache = adaptiveGroups;
            _ribbonAdaptiveFixedChromeWidthCache = fixedChromeWidth;
            _ribbonCorrectedStateCache.Clear();
        }

        UpdateRibbonResizeThresholdCache(cacheKey, adaptiveGroups, fixedChromeWidth);
        var layout = RibbonAdaptiveLayoutEngine.Plan(availableWidth, adaptiveGroups, fixedChromeWidth);
        var plannedStates = layout.States.ToArray();

        var correctionCacheKey = CreateRibbonCorrectionCacheKey(cacheKey, availableWidth, plannedStates);
        if (_ribbonCorrectedStateCache.TryGetValue(correctionCacheKey, out var correctedStates))
            plannedStates = correctedStates.ToArray();

        var appliedStateKey = CreateRibbonAppliedStateKey(cacheKey, availableWidth, plannedStates);
        if (!force &&
            !_ribbonAdaptiveStateDiffInvalidated &&
            string.Equals(_lastRibbonAdaptiveAppliedStateKey, appliedStateKey, StringComparison.Ordinal))
        {
            return;
        }

        ApplyRibbonAdaptiveStates(
            groups,
            collapsedButtons,
            plannedStates,
            _ribbonAdaptiveStateDiffInvalidated ? null : _lastRibbonAdaptiveAppliedStates);
        SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        var requiresMeasuredCorrection = correctedStates is null || layout.RequiresMeasuredCorrection;
        if (requiresMeasuredCorrection)
        {
            ApplyRibbonMeasuredOverflowFallback(activePanel, groups, collapsedButtons, plannedStates, adaptiveGroups, availableWidth);
            ApplyRibbonMeasuredExpansionFallback(activePanel, groups, collapsedButtons, plannedStates, adaptiveGroups, availableWidth);
        }

        ApplyRibbonRuntimeVisibilityOverrides(groups, collapsedButtons, plannedStates, adaptiveGroups, availableWidth);
        SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        appliedStateKey = CreateRibbonAppliedStateKey(cacheKey, availableWidth, plannedStates);
        if (correctedStates is null)
            _ribbonCorrectedStateCache[correctionCacheKey] = plannedStates.ToArray();
        _lastRibbonAdaptiveAppliedStateKey = appliedStateKey;
        _lastRibbonAdaptiveAppliedStates = plannedStates.ToArray();
        _ribbonAdaptiveStateDiffInvalidated = false;

        var compacted = plannedStates.Any(state => state != RibbonAdaptiveGroupState.Full);
        _ribbonCompact = compacted;
    }

    private static IReadOnlyList<FrameworkElement> GetRibbonAdaptiveGroups(StackPanel activePanel) =>
        activePanel.Children
            .OfType<FrameworkElement>()
            .Where(e => !IsRibbonCollapsedGroupButton(e) &&
                        RibbonMetadata.IsRibbonGroup(e))
            .ToList();

    private IReadOnlyList<FrameworkElement> GetCachedRibbonAdaptiveGroups(StackPanel activePanel)
    {
        if (ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel) &&
            _ribbonAdaptiveGroupControlCache is not null)
        {
            _ribbonAdaptiveScrollViewerCache ??= FindVisualAncestor<ScrollViewer>(activePanel);
            return _ribbonAdaptiveGroupControlCache;
        }

        var groups = GetRibbonAdaptiveGroups(activePanel);
        _ribbonAdaptiveControlCachePanel = activePanel;
        _ribbonAdaptiveScrollViewerCache = FindVisualAncestor<ScrollViewer>(activePanel);
        _ribbonAdaptiveGroupControlCache = groups;
        _ribbonAdaptiveControlCacheKey = null;
        _ribbonAdaptiveCollapsedButtonCache = null;
        _lastRibbonAdaptiveAppliedStateKey = null;
        _lastRibbonAdaptiveAppliedStates = null;
        _lastRibbonCollapsedFootprintMode = null;
        _ribbonCorrectedStateCache.Clear();
        return groups;
    }

    private IReadOnlyList<Button> GetCachedRibbonCollapsedGroupButtons(
        StackPanel activePanel,
        IReadOnlyList<FrameworkElement> groups,
        string controlCacheKey)
    {
        if (ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel) &&
            string.Equals(_ribbonAdaptiveControlCacheKey, controlCacheKey, StringComparison.Ordinal) &&
            _ribbonAdaptiveCollapsedButtonCache is not null)
        {
            return _ribbonAdaptiveCollapsedButtonCache;
        }

        var collapsedButtons = EnsureRibbonCollapsedGroupButtons(activePanel, groups);
        _ribbonAdaptiveControlCachePanel = activePanel;
        _ribbonAdaptiveControlCacheKey = controlCacheKey;
        _ribbonAdaptiveCollapsedButtonCache = collapsedButtons;
        _lastRibbonAdaptiveAppliedStateKey = null;
        _lastRibbonAdaptiveAppliedStates = null;
        _lastRibbonCollapsedFootprintMode = null;
        return collapsedButtons;
    }

    private double GetRibbonAvailableWidth(StackPanel activePanel)
    {
        var ribbonScrollViewer = ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel)
            ? _ribbonAdaptiveScrollViewerCache
            : null;
        ribbonScrollViewer ??= FindVisualAncestor<ScrollViewer>(activePanel);
        _ribbonAdaptiveScrollViewerCache = ribbonScrollViewer;
        var availableWidth = ribbonScrollViewer?.ActualWidth > 0
            ? ribbonScrollViewer.ActualWidth
            : ribbonScrollViewer?.ViewportWidth;
        if (availableWidth is null or <= 0)
            availableWidth = RibbonTabs.ActualWidth > 0 ? RibbonTabs.ActualWidth : activePanel.ActualWidth;
        if (RibbonTabs.ActualWidth > 0)
            availableWidth = Math.Min(availableWidth.Value, Math.Max(0, RibbonTabs.ActualWidth - 12));

        return Math.Max(0, availableWidth ?? 0);
    }

    private static void ApplyRibbonMeasuredOverflowFallback(
        StackPanel activePanel,
        IReadOnlyList<FrameworkElement> groups,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double availableWidth)
    {
        var protectedGroupIndexes = RibbonAdaptiveLayoutEngine.GetFallbackProtectedGroupIndexes(adaptiveGroups, availableWidth);
        while (RibbonRowOverflowsMeasured(activePanel, availableWidth) &&
               RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(plannedStates, preserveFirstGroup: availableWidth > 760, protectedGroupIndexes))
        {
            ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }

        while (RibbonRowOverflowsMeasured(activePanel, availableWidth) &&
               RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(plannedStates, preserveFirstGroup: false))
        {
            ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }
    }

    private static void ApplyRibbonMeasuredExpansionFallback(
        StackPanel activePanel,
        IReadOnlyList<FrameworkElement> groups,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double availableWidth)
    {
        foreach (var index in RibbonAdaptiveLayoutEngine.GetExpandableGroupIndexes(adaptiveGroups, availableWidth))
        {
            var currentState = plannedStates[index];
            if (!RibbonAdaptiveLayoutEngine.TryGetNextExpandedState(currentState, out var expandedState))
                continue;

            plannedStates[index] = expandedState;
            ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
            if (!RibbonRowOverflowsMeasured(activePanel, availableWidth))
                continue;

            plannedStates[index] = currentState;
            ApplyRibbonAdaptiveStates(groups, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }
    }

    private static void ApplyRibbonRuntimeVisibilityOverrides(
        IReadOnlyList<FrameworkElement> groups,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double availableWidth)
    {
        var groupNames = adaptiveGroups.Select(group => group.Name).ToList();
        foreach (var decision in RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(availableWidth, groupNames))
        {
            if (decision.Index < 0 || decision.Index >= adaptiveGroups.Count)
                continue;

            if (decision.State == RibbonAdaptiveGroupState.Collapsed)
            {
                plannedStates[decision.Index] = RibbonAdaptiveGroupState.Collapsed;
                groups[decision.Index].Visibility = Visibility.Collapsed;
                collapsedButtons[decision.Index].Visibility = Visibility.Visible;
            }
            else if (decision.State == RibbonAdaptiveGroupState.IconOnly)
            {
                plannedStates[decision.Index] = RibbonAdaptiveGroupState.IconOnly;
                groups[decision.Index].Visibility = Visibility.Visible;
                collapsedButtons[decision.Index].Visibility = Visibility.Collapsed;
                SetRibbonGroupCompact(groups[decision.Index], RibbonCompactLevel.IconOnly);
            }
        }
    }

    private static bool RibbonRowOverflowsMeasured(StackPanel activePanel, double availableWidth)
    {
        activePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return activePanel.DesiredSize.Width > Math.Max(0, availableWidth - 4);
    }

    private static string CreateRibbonAppliedStateKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        var footprintMode = GetCollapsedRibbonFootprintMode(availableWidth);
        return string.Join(
            "|",
            measurementCacheKey,
            Math.Round(availableWidth, 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            footprintMode,
            string.Join(",", states.Select(state => ((int)state).ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static string CreateRibbonCorrectionCacheKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        string.Join(
            "|",
            measurementCacheKey,
            Math.Round(availableWidth, 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(",", states.Select(state => ((int)state).ToString(System.Globalization.CultureInfo.InvariantCulture))));

    private static void ApplyRibbonAdaptiveStates(
        IReadOnlyList<FrameworkElement> groups,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveGroupState>? previousStates)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (previousStates is not null &&
                i < previousStates.Count &&
                previousStates[i] == plannedStates[i])
            {
                continue;
            }

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

    private static void SetCollapsedRibbonButtonFootprint(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprint = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(availableWidth);
        foreach (var button in collapsedButtons)
        {
            button.Width = footprint.Width;
            button.Margin = footprint.Margin;
            button.Padding = footprint.Padding;

            if (TryGetCollapsedRibbonButtonCaption(button, out var caption))
                ApplyCollapsedRibbonButtonCaptionFootprint(caption, footprint);

            if (TryGetCollapsedRibbonButtonTextIcon(button, out var icon))
                icon.FontSize = footprint.IconFontSize;
        }
    }

    private static bool TryGetCollapsedRibbonButtonCaption(Button button, out TextBlock caption)
    {
        caption = null!;
        if (button.Content is not Panel content)
            return false;

        caption = content.Children
            .OfType<TextBlock>()
            .FirstOrDefault(RibbonMetadata.IsCommandLabel)!;
        return caption is not null;
    }

    private static bool TryGetCollapsedRibbonButtonTextIcon(Button button, out TextBlock icon)
    {
        icon = null!;
        if (button.Content is not Panel content)
            return false;

        icon = content.Children
            .OfType<TextBlock>()
            .Concat(content.Children
                .OfType<Border>()
                .Select(border => border.Child)
                .OfType<TextBlock>())
            .FirstOrDefault(textBlock => RibbonMetadata.IsCommandIcon(textBlock) &&
                                         !RibbonMetadata.IsCollapsedChevron(textBlock))!;
        return icon is not null;
    }

    private static void ApplyCollapsedRibbonButtonCaptionFootprint(
        TextBlock caption,
        RibbonCollapsedGroupFootprint footprint)
    {
        caption.Visibility = footprint.CaptionVisibility;
        caption.FontSize = footprint.CaptionFontSize;
        caption.MaxWidth = footprint.CaptionMaxWidth;
        caption.TextWrapping = TextWrapping.NoWrap;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.TextAlignment = TextAlignment.Center;
    }

    private void SetCollapsedRibbonButtonFootprintIfNeeded(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprintMode = GetCollapsedRibbonFootprintMode(availableWidth);
        if (string.Equals(_lastRibbonCollapsedFootprintMode, footprintMode, StringComparison.Ordinal))
            return;

        SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        _lastRibbonCollapsedFootprintMode = footprintMode;
    }

    private static string GetCollapsedRibbonFootprintMode(double availableWidth) =>
        RibbonCollapsedGroupPresentationPlanner.GetCacheKey(availableWidth);

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

    private static string CreateRibbonAdaptiveMeasurementCacheKey(StackPanel activePanel, IReadOnlyList<FrameworkElement> groups)
    {
        var tabName = FindVisualAncestor<TabItem>(activePanel)?.Header?.ToString() ?? "";
        return string.Join(
            "|",
            tabName,
            groups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", groups.Select(group => $"{GetRibbonGroupName(group)}:{group.GetHashCode():X}")));
    }

    private void UpdateRibbonResizeThresholdCache(
        string cacheKey,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double fixedChromeWidth)
    {
        if (string.Equals(_ribbonResizeThresholdCacheKey, cacheKey, StringComparison.Ordinal) &&
            _ribbonResizeThresholds.Count > 0)
        {
            return;
        }

        _ribbonResizeThresholdCacheKey = cacheKey;
        _ribbonResizeThresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(adaptiveGroups, fixedChromeWidth);
    }

    private static List<Button> EnsureRibbonCollapsedGroupButtons(StackPanel panel, IReadOnlyList<FrameworkElement> groups)
    {
        var buttons = new List<Button>(groups.Count);
        var groupNames = groups
            .Select(GetRibbonGroupName)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is not Button button || !IsRibbonCollapsedGroupButton(button))
                continue;

            var title = RibbonTooltip.GetTitle(button) ?? "";
            if (!groupNames.Contains(title))
                panel.Children.RemoveAt(i);
        }

        var keyTips = panel.Children
            .OfType<Button>()
            .Where(IsRibbonCollapsedGroupButton)
            .Select(RibbonTooltip.GetKeyTip)
            .Where(keyTip => !string.IsNullOrWhiteSpace(keyTip))
            .Select(keyTip => keyTip!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var groupName = GetRibbonGroupName(group);
            var button = FindReusableCollapsedGroupButton(panel, groupName) ??
                CreateRibbonCollapsedGroupButton(group, keyTips);

            var currentIndex = panel.Children.IndexOf(button);
            var groupIndex = panel.Children.IndexOf(group);
            if (currentIndex >= 0)
            {
                if (currentIndex != groupIndex + 1)
                {
                    panel.Children.RemoveAt(currentIndex);
                    groupIndex = panel.Children.IndexOf(group);
                    panel.Children.Insert(groupIndex + 1, button);
                }
            }
            else
            {
                panel.Children.Insert(groupIndex + 1, button);
            }

            buttons.Add(button);
        }

        return buttons;
    }

    private static Button? FindReusableCollapsedGroupButton(StackPanel panel, string groupName)
    {
        foreach (var child in panel.Children.OfType<Button>())
        {
            if (IsRibbonCollapsedGroupButton(child) &&
                string.Equals(RibbonTooltip.GetTitle(child), groupName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsRibbonCollapsedGroupButton(FrameworkElement element) =>
        RibbonMetadata.IsCollapsedGroupButton(element);

    private static Button CreateRibbonCollapsedGroupButton(FrameworkElement group, ISet<string>? usedKeyTips = null)
    {
        var groupName = GetRibbonGroupName(group);
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var label = new TextBlock
        {
            Text = groupName,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 60,
            LineHeight = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);

        var button = new Button
        {
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
                    label
                }
            }
        };
        RibbonMetadata.SetRole(button, RibbonMetadataRole.CollapsedGroupButton);

        button.SetResourceReference(StyleProperty, "RibbonTallButton");
        RibbonTooltip.SetTitle(button, groupName);
        RibbonTooltip.SetDescription(button, $"Show the {groupName} commands.");
        RibbonTooltip.SetKeyTip(button, CreateGroupKeyTip(groupName, usedKeyTips));
        button.Loaded += (_, _) => EnsureCollapsedGroupChevronAdorner(button);
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

    private static void EnsureCollapsedGroupChevronAdorner(Button button)
    {
        var layer = AdornerLayer.GetAdornerLayer(button);
        if (layer is null)
            return;

        if (layer.GetAdorners(button)?.Any(adorner => adorner is RibbonCollapsedGroupChevronAdorner) == true)
            return;

        layer.Add(new RibbonCollapsedGroupChevronAdorner(button));
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
        => RibbonMenuItemCloner.CloneRibbonMenuItem(source);

    private static void SynchronizeClonedMenuItems(ItemCollection sourceItems, ItemCollection clonedItems)
        => RibbonMenuItemCloner.SynchronizeClonedMenuItems(sourceItems, clonedItems);

    private static void InvokeRibbonButton(ButtonBase button)
    {
        if (button is ToggleButton toggleButton)
            toggleButton.IsChecked = toggleButton.IsChecked != true;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static string GetRibbonGroupName(FrameworkElement group)
    {
        if (RibbonMetadata.TryGetGroupName(group, out var groupName))
            return groupName;

        return "Commands";
    }

    private static string CreateGroupKeyTip(string groupName, ISet<string>? usedKeyTips = null)
    {
        var letters = groupName.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        var candidates = new List<string>();
        if (letters.Length >= 2)
        {
            candidates.Add(new string([letters[0], letters[1]]));
            for (var i = 2; i < letters.Length; i++)
                candidates.Add(new string([letters[0], letters[i]]));
        }
        else if (letters.Length == 1)
        {
            candidates.Add(new string([letters[0]]));
        }

        candidates.Add("G");
        for (var index = 1; index <= 9; index++)
            candidates.Add($"G{index}");

        foreach (var candidate in candidates)
        {
            if (usedKeyTips is not null && !usedKeyTips.Add(candidate))
                continue;

            return candidate;
        }

        return "G";
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
            .Where(panel => FindVisualAncestor<Button>(panel) is not { } button ||
                            !RibbonMetadata.IsCollapsedGroupButton(button))
            .OrderByDescending(panel => panel.Children.OfType<DependencyObject>().Count(RibbonMetadata.IsRibbonGroup))
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<DependencyObject>().Any(RibbonMetadata.IsRibbonGroup));
    }

    private static DependencyObject GetRibbonTabContentRoot(TabItem tabItem) =>
        tabItem.Content as DependencyObject ?? tabItem;

    private enum RibbonCompactLevel
    {
        Full,
        SmallWithLabels,
        IconOnly
    }

    private sealed class RibbonCollapsedGroupChevronAdorner : Adorner
    {
        private readonly VisualCollection _children;
        private readonly TextBlock _chevron = new()
        {
            Text = "\uE70D",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 8,
            Width = 8,
            Height = 8,
            LineHeight = 8,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };

        public RibbonCollapsedGroupChevronAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            RibbonMetadata.SetRole(_chevron, RibbonMetadataRole.CollapsedChevron);
            _children = new VisualCollection(this) { _chevron };
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => _children?.Count ?? 0;

        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _chevron.Measure(new Size(8, 8));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var x = Math.Max(0, (AdornedElement.RenderSize.Width - 8) / 2);
            var y = Math.Max(0, AdornedElement.RenderSize.Height - 9);
            _chevron.Arrange(new Rect(new Point(x, y), new Size(8, 8)));
            return finalSize;
        }
    }

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level)
    {
        foreach (var element in EnumerateVisualDescendants(group).OfType<FrameworkElement>())
        {
            if (element is TextBlock label &&
                RibbonMetadata.IsCommandLabel(label))
            {
                label.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
                continue;
            }

            if (element is ButtonBase button)
            {
                var isLargeButton = button.Content is DependencyObject contentRoot &&
                    RibbonMetadata.TryGetCommandContentLayout(contentRoot, out var layout) &&
                    layout == RibbonCommandContentLayout.Large;

                if (RibbonMetadata.TryGetCompactWidths(button, out var fullWidth, out var compactWidth))
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
        if (button is CheckBox or RadioButton)
        {
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            if (button.Content is FrameworkElement content)
                content.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            return;
        }

        foreach (var textBlock in EnumerateVisualDescendants(button).OfType<TextBlock>())
        {
            if (IsRibbonButtonLabel(textBlock))
                textBlock.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
        }

        var contentLayout = RibbonCommandContentLayout.None;
        var hasContentLayout = button.Content is DependencyObject contentRoot &&
            RibbonMetadata.TryGetCommandContentLayout(contentRoot, out contentLayout);
        bool isSmallOrMedium = contentLayout is RibbonCommandContentLayout.Small or RibbonCommandContentLayout.Medium;

        if (button.Content is Grid smallGrid &&
            hasContentLayout &&
            contentLayout == RibbonCommandContentLayout.Small)
        {
            ApplySmallButtonCompactLayout(smallGrid, button, level);
        }

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
            hasContentLayout &&
            contentLayout == RibbonCommandContentLayout.Large)
        {
            ApplyLargeButtonCompactLayout(largeStack, button, level);
        }
    }

    private static void ApplySmallButtonCompactLayout(
        Grid contentGrid,
        ButtonBase button,
        RibbonCompactLevel level)
    {
        var spacerColumn = contentGrid.ColumnDefinitions
            .Cast<ColumnDefinition>()
            .FirstOrDefault(RibbonMetadata.IsCommandSpacer);
        if (spacerColumn is null && contentGrid.ColumnDefinitions.Count >= 2)
        {
            spacerColumn = contentGrid.ColumnDefinitions[1];
        }

        if (spacerColumn is not null)
        {
            spacerColumn.Width = level == RibbonCompactLevel.IconOnly
                ? new GridLength(0)
                : new GridLength(5);
        }

        if (level == RibbonCompactLevel.IconOnly)
        {
            contentGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            contentGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        }
    }

    private static void ApplyLargeButtonCompactLayout(
        StackPanel contentStack, ButtonBase button, RibbonCompactLevel level)
    {
        var iconSlot = contentStack.Children
            .OfType<Border>()
            .FirstOrDefault(RibbonMetadata.IsCommandIcon);
        var labelBlock = contentStack.Children
            .OfType<TextBlock>()
            .FirstOrDefault(RibbonMetadata.IsCommandLabel);
        if (iconSlot is null || labelBlock is null)
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
        if (RibbonMetadata.IsCommandLabel(textBlock))
            return true;
        if (RibbonMetadata.IsCommandIcon(textBlock))
            return false;

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
