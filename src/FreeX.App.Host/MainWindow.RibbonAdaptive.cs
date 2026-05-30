using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeX.App.Host;

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
        var groupSnapshots = GetCachedRibbonCompactGroupSnapshots(groups, controlCacheKey);
        var availableWidth = GetRibbonAvailableWidth(activePanel);
        if (availableWidth <= 0)
            return;

        var selectedTabHeader = GetRibbonAdaptiveTabHeader(activePanel);
        _ribbonMeasuredOverflowCache.Clear();
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
                groupSnapshots,
                collapsedButtons,
                Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groups.Count).ToArray(),
                previousStates: null);
            fixedChromeWidth = MeasureRibbonFixedChromeWidth(activePanel) + 24;
            _ribbonAdaptiveGroupMeasurementCount += groupSnapshots.Count;
            adaptiveGroups = groupSnapshots
                .Select((snapshot, index) => MeasureRibbonAdaptiveGroup(snapshot, collapsedButtons[index]))
                .ToList();
            _ribbonAdaptiveMeasurementCacheKey = cacheKey;
            _ribbonAdaptiveGroupCache = adaptiveGroups;
            _ribbonAdaptiveFixedChromeWidthCache = fixedChromeWidth;
            _ribbonCorrectedStateCache.Clear();
            _ribbonMeasuredOverflowCache.Clear();
        }

        UpdateRibbonResizeThresholdCache(cacheKey, adaptiveGroups, fixedChromeWidth, selectedTabHeader);
        if (_ribbonAdaptiveStateDiffInvalidated)
            _ribbonMeasuredOverflowCache.Clear();

        var layout = RibbonAdaptiveLayoutEngine.Plan(availableWidth, adaptiveGroups, fixedChromeWidth, selectedTabHeader);
        var layoutStates = layout.States.ToArray();
        var plannedStates = layoutStates.ToArray();

        var correctionCacheKey = CreateRibbonCorrectionCacheKey(cacheKey, availableWidth, plannedStates);
        var hasCachedCorrection = _ribbonCorrectedStateCache.TryGetValue(correctionCacheKey, out var correctedStates);
        if (hasCachedCorrection)
            _ribbonCorrectedStateCacheHitCount++;
        var cachedCorrectionNeedsExpansion = false;
        if (hasCachedCorrection && correctedStates is not null)
        {
            plannedStates = correctedStates.ToArray();
            cachedCorrectionNeedsExpansion = RibbonStatesAreMoreCollapsedThan(plannedStates, layoutStates);
        }

        var appliedStateKey = CreateRibbonAppliedStateKey(cacheKey, availableWidth, plannedStates);
        if (!force &&
            !_ribbonAdaptiveStateDiffInvalidated &&
            string.Equals(_lastRibbonAdaptiveAppliedStateKey, appliedStateKey, StringComparison.Ordinal))
        {
            _ribbonAppliedStateSkipCount++;
            return;
        }

        ApplyRibbonAdaptiveStates(
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            _ribbonAdaptiveStateDiffInvalidated ? null : _lastRibbonAdaptiveAppliedStates);
        SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        var requiresMeasuredCorrection = cachedCorrectionNeedsExpansion ||
            layout.RequiresMeasuredCorrection &&
            (!hasCachedCorrection || RibbonRowOverflowsMeasuredCached(activePanel, cacheKey, availableWidth, plannedStates));
        if (requiresMeasuredCorrection)
        {
            ApplyRibbonMeasuredOverflowFallback(activePanel, groupSnapshots, collapsedButtons, plannedStates, adaptiveGroups, cacheKey, availableWidth, selectedTabHeader);
            ApplyRibbonMeasuredExpansionFallback(activePanel, groupSnapshots, collapsedButtons, plannedStates, adaptiveGroups, cacheKey, availableWidth, selectedTabHeader);
        }

        SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        appliedStateKey = CreateRibbonAppliedStateKey(cacheKey, availableWidth, plannedStates);
        if (!hasCachedCorrection || requiresMeasuredCorrection)
            _ribbonCorrectedStateCache[correctionCacheKey] = plannedStates.ToArray();
        _lastRibbonAdaptiveAppliedStateKey = appliedStateKey;
        _lastRibbonAdaptiveAppliedStates = plannedStates.ToArray();
        _ribbonAdaptiveStateDiffInvalidated = false;

        var compacted = plannedStates.Any(state => state != RibbonAdaptiveGroupState.Full);
        _ribbonCompact = compacted;
    }

    private static bool RibbonStatesAreMoreCollapsedThan(
        IReadOnlyList<RibbonAdaptiveGroupState> states,
        IReadOnlyList<RibbonAdaptiveGroupState> baselineStates)
    {
        var count = Math.Min(states.Count, baselineStates.Count);
        for (var i = 0; i < count; i++)
        {
            if ((int)states[i] > (int)baselineStates[i])
                return true;
        }

        return false;
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
        _ribbonCompactSnapshotCacheKey = null;
        _ribbonCompactGroupSnapshotCache = null;
        _lastRibbonAdaptiveAppliedStateKey = null;
        _lastRibbonAdaptiveAppliedStates = null;
        _lastRibbonCollapsedFootprintMode = null;
        _ribbonCorrectedStateCache.Clear();
        _ribbonMeasuredOverflowCache.Clear();
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

    private IReadOnlyList<RibbonCompactGroupSnapshot> GetCachedRibbonCompactGroupSnapshots(
        IReadOnlyList<FrameworkElement> groups,
        string controlCacheKey)
    {
        if (string.Equals(_ribbonCompactSnapshotCacheKey, controlCacheKey, StringComparison.Ordinal) &&
            _ribbonCompactGroupSnapshotCache is not null &&
            _ribbonCompactGroupSnapshotCache.Count == groups.Count)
        {
            return _ribbonCompactGroupSnapshotCache;
        }

        var snapshots = groups
            .Select(CaptureRibbonCompactGroupSnapshot)
            .ToList();
        _ribbonCompactSnapshotCaptureCount += snapshots.Count;
        _ribbonCompactSnapshotCacheKey = controlCacheKey;
        _ribbonCompactGroupSnapshotCache = snapshots;
        return snapshots;
    }

    private void InvalidateRibbonAdaptiveMeasurementCaches()
    {
        _ribbonAdaptiveMeasurementInvalidationCount++;
        _ribbonAdaptiveMeasurementCacheKey = null;
        _ribbonAdaptiveGroupCache = null;
        _ribbonAdaptiveFixedChromeWidthCache = 0;
        _ribbonResizeThresholdCacheKey = null;
        _ribbonResizeThresholds = [];
        _ribbonCompactSnapshotCacheKey = null;
        _ribbonCompactGroupSnapshotCache = null;
        _lastRibbonAdaptiveAppliedStateKey = null;
        _lastRibbonAdaptiveAppliedStates = null;
        _lastRibbonCollapsedFootprintMode = null;
        _ribbonCorrectedStateCache.Clear();
        _ribbonMeasuredOverflowCache.Clear();
        _ribbonAdaptiveStateDiffInvalidated = true;
    }

    internal RibbonAdaptiveDiagnosticsSnapshot GetRibbonAdaptiveDiagnosticsForTests() =>
        new(
            _ribbonAdaptiveMeasurementInvalidationCount,
            _ribbonAdaptiveGroupMeasurementCount,
            _ribbonCompactSnapshotCaptureCount,
            _ribbonResizeThresholdRebuildCount,
            _ribbonMeasuredOverflowMeasurementCount,
            _ribbonCorrectedStateCacheHitCount,
            _ribbonAppliedStateSkipCount,
            _ribbonAdaptiveMeasurementCacheKey,
            _ribbonResizeThresholdCacheKey,
            _ribbonCompactSnapshotCacheKey);

    internal void ResetRibbonAdaptiveDiagnosticsForTests(bool resetSelectedStaticNormalization = false)
    {
        _ribbonAdaptiveMeasurementInvalidationCount = 0;
        _ribbonAdaptiveGroupMeasurementCount = 0;
        _ribbonCompactSnapshotCaptureCount = 0;
        _ribbonResizeThresholdRebuildCount = 0;
        _ribbonMeasuredOverflowMeasurementCount = 0;
        _ribbonCorrectedStateCacheHitCount = 0;
        _ribbonAppliedStateSkipCount = 0;

        if (resetSelectedStaticNormalization &&
            RibbonTabs?.SelectedItem is TabItem selectedTab)
        {
            _normalizedRibbonStaticTabs.Remove(selectedTab);
        }
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

    private static string GetRibbonAdaptiveTabHeader(DependencyObject element) =>
        FindVisualAncestor<TabItem>(element)?.Header?.ToString() ?? "";

    private void ApplyRibbonMeasuredOverflowFallback(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string measurementCacheKey,
        double availableWidth,
        string? selectedTabHeader)
    {
        var protectedGroupIndexes = RibbonAdaptiveLayoutEngine.GetFallbackProtectedGroupIndexes(adaptiveGroups, availableWidth, selectedTabHeader);
        var runtimeVisibilityProtectedGroupIndexes = RibbonAdaptiveLayoutEngine.GetRuntimeVisibilityProtectedGroupIndexes(adaptiveGroups, availableWidth, selectedTabHeader);
        protectedGroupIndexes.UnionWith(runtimeVisibilityProtectedGroupIndexes);
        while (RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, plannedStates) &&
               RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(plannedStates, preserveFirstGroup: availableWidth > 760, protectedGroupIndexes))
        {
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }

        while (RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, plannedStates) &&
               RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(plannedStates, preserveFirstGroup: false, runtimeVisibilityProtectedGroupIndexes))
        {
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }
    }

    private void ApplyRibbonMeasuredExpansionFallback(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string measurementCacheKey,
        double availableWidth,
        string? selectedTabHeader)
    {
        foreach (var index in RibbonAdaptiveLayoutEngine.GetExpandableGroupIndexes(adaptiveGroups, availableWidth, selectedTabHeader))
        {
            var currentState = plannedStates[index];
            if (!RibbonAdaptiveLayoutEngine.TryGetNextExpandedState(currentState, out var expandedState))
                continue;

            plannedStates[index] = expandedState;
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
            if (!RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, plannedStates))
                continue;

            plannedStates[index] = currentState;
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null);
            SetCollapsedRibbonButtonFootprint(collapsedButtons, availableWidth);
        }
    }

    private bool RibbonRowOverflowsMeasuredCached(
        StackPanel activePanel,
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        var overflowCacheKey = CreateRibbonMeasuredOverflowCacheKey(measurementCacheKey, availableWidth, states);
        if (_ribbonMeasuredOverflowCache.TryGetValue(overflowCacheKey, out var overflows))
            return overflows;

        overflows = RibbonRowOverflowsMeasured(activePanel, availableWidth);
        _ribbonMeasuredOverflowMeasurementCount++;
        _ribbonMeasuredOverflowCache[overflowCacheKey] = overflows;
        return overflows;
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

    private static string CreateRibbonMeasuredOverflowCacheKey(
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

    private static void ApplyRibbonAdaptiveStates(
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveGroupState>? previousStates)
    {
        for (var i = 0; i < groupSnapshots.Count; i++)
        {
            if (previousStates is not null &&
                i < previousStates.Count &&
                previousStates[i] == plannedStates[i])
            {
                continue;
            }

            collapsedButtons[i].Visibility = Visibility.Collapsed;
            groupSnapshots[i].Group.Visibility = Visibility.Visible;

            switch (plannedStates[i])
            {
                case RibbonAdaptiveGroupState.Full:
                    ApplyRibbonGroupCompactSnapshot(groupSnapshots[i], RibbonCompactLevel.Full);
                    break;
                case RibbonAdaptiveGroupState.SmallWithLabels:
                    ApplyRibbonGroupCompactSnapshot(groupSnapshots[i], RibbonCompactLevel.SmallWithLabels);
                    break;
                case RibbonAdaptiveGroupState.IconOnly:
                    ApplyRibbonGroupCompactSnapshot(groupSnapshots[i], RibbonCompactLevel.IconOnly);
                    break;
                case RibbonAdaptiveGroupState.Collapsed:
                    groupSnapshots[i].Group.Visibility = Visibility.Collapsed;
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

    private static RibbonAdaptiveGroup MeasureRibbonAdaptiveGroup(RibbonCompactGroupSnapshot snapshot, Button collapsedButton)
    {
        var name = GetRibbonGroupName(snapshot.Group);
        var fullWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.Full);
        var smallWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.SmallWithLabels);
        var iconWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.IconOnly);
        collapsedButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var collapsedWidth = Math.Max(48, collapsedButton.DesiredSize.Width);
        ApplyRibbonGroupCompactSnapshot(snapshot, RibbonCompactLevel.Full);

        return new RibbonAdaptiveGroup(name, fullWidth, smallWidth, iconWidth, collapsedWidth);
    }

    private static double MeasureRibbonGroupWidth(RibbonCompactGroupSnapshot snapshot, RibbonCompactLevel level)
    {
        ApplyRibbonGroupCompactSnapshot(snapshot, level);
        snapshot.Group.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(0, snapshot.Group.DesiredSize.Width);
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
        var tabName = GetRibbonAdaptiveTabHeader(activePanel);
        return string.Join(
            "|",
            tabName,
            groups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", groups.Select(group => $"{GetRibbonGroupName(group)}:{group.GetHashCode():X}")));
    }

    private void UpdateRibbonResizeThresholdCache(
        string cacheKey,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        if (string.Equals(_ribbonResizeThresholdCacheKey, cacheKey, StringComparison.Ordinal) &&
            _ribbonResizeThresholds.Count > 0)
        {
            return;
        }

        _ribbonResizeThresholdCacheKey = cacheKey;
        _ribbonResizeThresholdRebuildCount++;
        _ribbonResizeThresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(adaptiveGroups, fixedChromeWidth, selectedTabHeader);
    }

    private static List<Button> EnsureRibbonCollapsedGroupButtons(StackPanel panel, IReadOnlyList<FrameworkElement> groups)
    {
        var buttons = new List<Button>(groups.Count);
        var expectedGroupNames = groups
            .Select(GetRibbonGroupName)
            .ToHashSet(StringComparer.Ordinal);
        var reusableButtonsByGroupName = new Dictionary<string, Button>(StringComparer.Ordinal);
        var buttonsToRemove = new List<Button>();
        var keyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var button in panel.Children.OfType<Button>().Where(IsRibbonCollapsedGroupButton))
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
            var groupName = GetRibbonGroupName(group);
            if (!reusableButtonsByGroupName.TryGetValue(groupName, out var button))
            {
                button = CreateRibbonCollapsedGroupButton(group, keyTips);
                reusableButtonsByGroupName[groupName] = button;
            }

            var currentIndex = panel.Children.IndexOf(button);
            var groupIndex = panel.Children.IndexOf(group);
            var targetIndex = groupIndex + 1;
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
            ContextMenu = CreateLazyCollapsedRibbonGroupMenu(group),
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
        button.IsVisibleChanged += (_, _) => layer.Update(button);
        button.SizeChanged += (_, _) => layer.Update(button);
    }

    private static ContextMenu CreateLazyCollapsedRibbonGroupMenu(FrameworkElement group)
    {
        var menu = new ContextMenu { Tag = group };
        menu.Opened += (_, _) =>
        {
            EnsureCollapsedRibbonGroupMenuItems(menu);
            SynchronizeCollapsedRibbonTopLevelMenuItems(menu.Items);
        };
        return menu;
    }

    private static void EnsureCollapsedRibbonGroupMenuItems(ContextMenu menu)
    {
        if (menu.Items.Count > 0 || menu.Tag is not FrameworkElement group)
            return;

        PopulateCollapsedRibbonGroupMenu(menu, group);
    }

    private static void PopulateCollapsedRibbonGroupMenu(ContextMenu menu, FrameworkElement group)
    {
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

    private static void SynchronizeCollapsedRibbonTopLevelMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is ButtonBase sourceButton)
                item.IsEnabled = sourceButton.IsEnabled;
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

        if (TryGetCachedActiveRibbonPanel(tabItem, out var cachedPanel))
            return cachedPanel;

        if (string.Equals(tabItem.Header?.ToString(), "Home", StringComparison.Ordinal) &&
            HomeRibbonPanel is not null)
        {
            return CacheActiveRibbonPanel(tabItem, HomeRibbonPanel);
        }

        var contentRoot = GetRibbonTabContentRoot(tabItem);
        var activePanel = EnumerateVisualDescendants(contentRoot)
            .Concat(EnumerateLogicalDescendants(contentRoot))
            .OfType<StackPanel>()
            .Distinct()
            .Where(panel => FindVisualAncestor<Button>(panel) is not { } button ||
                            !RibbonMetadata.IsCollapsedGroupButton(button))
            .OrderByDescending(panel => panel.Children.OfType<DependencyObject>().Count(RibbonMetadata.IsRibbonGroup))
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<DependencyObject>().Any(RibbonMetadata.IsRibbonGroup));
        return CacheActiveRibbonPanel(tabItem, activePanel);
    }

    private bool TryGetCachedActiveRibbonPanel(TabItem tabItem, out StackPanel? activePanel)
    {
        if (_ribbonAdaptiveActivePanelCacheByTab.TryGetValue(tabItem, out var cachedPanel) &&
            cachedPanel.IsVisible &&
            ReferenceEquals(FindVisualAncestor<TabItem>(cachedPanel), tabItem))
        {
            activePanel = cachedPanel;
            return true;
        }

        _ribbonAdaptiveActivePanelCacheByTab.Remove(tabItem);
        activePanel = null;
        return false;
    }

    private StackPanel? CacheActiveRibbonPanel(TabItem tabItem, StackPanel? activePanel)
    {
        if (activePanel is null)
        {
            _ribbonAdaptiveActivePanelCacheByTab.Remove(tabItem);
            return null;
        }

        _ribbonAdaptiveActivePanelCacheByTab[tabItem] = activePanel;
        return activePanel;
    }

    private static DependencyObject GetRibbonTabContentRoot(TabItem tabItem) =>
        tabItem.Content as DependencyObject ?? tabItem;

    private enum RibbonCompactLevel
    {
        Full,
        SmallWithLabels,
        IconOnly
    }

    private enum RibbonFallbackWork
    {
        None,
        CompactOnly,
        NormalizeSurface
    }

    private sealed class RibbonCompactGroupSnapshot(
        FrameworkElement group,
        IReadOnlyList<TextBlock> commandLabels,
        IReadOnlyList<RibbonCompactButtonSnapshot> buttons)
    {
        public FrameworkElement Group { get; } = group;
        public IReadOnlyList<TextBlock> CommandLabels { get; } = commandLabels;
        public IReadOnlyList<RibbonCompactButtonSnapshot> Buttons { get; } = buttons;
    }

    private sealed class RibbonCompactButtonSnapshot(
        ButtonBase button,
        bool isCheckOrRadioButton,
        FrameworkElement? content,
        bool hasContentLayout,
        RibbonCommandContentLayout contentLayout,
        bool isLargeButton,
        bool hasCompactWidths,
        double fullWidth,
        double compactWidth,
        IReadOnlyList<TextBlock> labels,
        IReadOnlyList<StackPanel> horizontalStacks,
        Grid? smallGrid,
        ColumnDefinition? smallSpacerColumn,
        StackPanel? largeStack,
        Border? largeIconSlot,
        FrameworkElement? largeIconChild,
        TextBlock? largeLabelBlock)
    {
        public ButtonBase Button { get; } = button;
        public bool IsCheckOrRadioButton { get; } = isCheckOrRadioButton;
        public FrameworkElement? Content { get; } = content;
        public bool HasContentLayout { get; } = hasContentLayout;
        public RibbonCommandContentLayout ContentLayout { get; } = contentLayout;
        public bool IsLargeButton { get; } = isLargeButton;
        public bool HasCompactWidths { get; } = hasCompactWidths;
        public double FullWidth { get; } = fullWidth;
        public double CompactWidth { get; } = compactWidth;
        public IReadOnlyList<TextBlock> Labels { get; } = labels;
        public IReadOnlyList<StackPanel> HorizontalStacks { get; } = horizontalStacks;
        public Grid? SmallGrid { get; } = smallGrid;
        public ColumnDefinition? SmallSpacerColumn { get; } = smallSpacerColumn;
        public StackPanel? LargeStack { get; } = largeStack;
        public Border? LargeIconSlot { get; } = largeIconSlot;
        public FrameworkElement? LargeIconChild { get; } = largeIconChild;
        public TextBlock? LargeLabelBlock { get; } = largeLabelBlock;
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

    private static RibbonCompactGroupSnapshot CaptureRibbonCompactGroupSnapshot(FrameworkElement group)
    {
        var elements = EnumerateSelfVisualAndLogicalDescendants(group)
            .OfType<FrameworkElement>()
            .ToList();
        var commandLabels = elements
            .OfType<TextBlock>()
            .Where(RibbonMetadata.IsCommandLabel)
            .ToList();
        var buttons = elements
            .OfType<ButtonBase>()
            .Select(CaptureRibbonCompactButtonSnapshot)
            .ToList();

        return new RibbonCompactGroupSnapshot(group, commandLabels, buttons);
    }

    private static RibbonCompactButtonSnapshot CaptureRibbonCompactButtonSnapshot(ButtonBase button)
    {
        var descendants = EnumerateSelfVisualAndLogicalDescendants(button)
            .Concat(button.Content is DependencyObject contentRoot
                ? EnumerateSelfVisualAndLogicalDescendants(contentRoot)
                : [])
            .Distinct()
            .OfType<FrameworkElement>()
            .ToList();
        var content = button.Content as FrameworkElement;
        var contentLayout = RibbonCommandContentLayout.None;
        var hasContentLayout = content is not null &&
            RibbonMetadata.TryGetCommandContentLayout(content, out contentLayout);
        var isLargeButton = hasContentLayout && contentLayout == RibbonCommandContentLayout.Large;
        var hasCompactWidths = RibbonMetadata.TryGetCompactWidths(button, out var fullWidth, out var compactWidth);
        var labels = descendants
            .OfType<TextBlock>()
            .Where(IsRibbonButtonLabel)
            .ToList();
        var horizontalStacks = descendants
            .OfType<StackPanel>()
            .Where(stack => stack.Orientation == Orientation.Horizontal)
            .ToList();
        var smallGrid = hasContentLayout && contentLayout == RibbonCommandContentLayout.Small
            ? content as Grid
            : null;
        var smallSpacerColumn = GetRibbonSmallButtonSpacerColumn(smallGrid);
        var largeStack = isLargeButton ? content as StackPanel : null;
        var largeIconSlot = largeStack?.Children
            .OfType<Border>()
            .FirstOrDefault(RibbonMetadata.IsCommandIcon);
        var largeLabelBlock = largeStack?.Children
            .OfType<TextBlock>()
            .FirstOrDefault(RibbonMetadata.IsCommandLabel);
        var largeIconChild = largeIconSlot?.Child as FrameworkElement;

        return new RibbonCompactButtonSnapshot(
            button,
            button is CheckBox or RadioButton,
            content,
            hasContentLayout,
            contentLayout,
            isLargeButton,
            hasCompactWidths,
            fullWidth,
            compactWidth,
            labels,
            horizontalStacks,
            smallGrid,
            smallSpacerColumn,
            largeStack,
            largeIconSlot,
            largeIconChild,
            largeLabelBlock);
    }

    private static IEnumerable<DependencyObject> EnumerateSelfVisualAndLogicalDescendants(DependencyObject root) =>
        [root, .. EnumerateVisualDescendants(root), .. EnumerateLogicalDescendants(root)];

    private static ColumnDefinition? GetRibbonSmallButtonSpacerColumn(Grid? contentGrid)
    {
        if (contentGrid is null)
            return null;

        var spacerColumn = contentGrid.ColumnDefinitions
            .Cast<ColumnDefinition>()
            .FirstOrDefault(RibbonMetadata.IsCommandSpacer);
        if (spacerColumn is null && contentGrid.ColumnDefinitions.Count >= 2)
            spacerColumn = contentGrid.ColumnDefinitions[1];

        return spacerColumn;
    }

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level) =>
        ApplyRibbonGroupCompactSnapshot(CaptureRibbonCompactGroupSnapshot(group), level);

    private static void ApplyRibbonGroupCompactSnapshot(RibbonCompactGroupSnapshot snapshot, RibbonCompactLevel level)
    {
        foreach (var label in snapshot.CommandLabels)
            label.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;

        foreach (var buttonSnapshot in snapshot.Buttons)
        {
            if (buttonSnapshot.HasCompactWidths)
            {
                buttonSnapshot.Button.Width = level switch
                {
                    RibbonCompactLevel.Full => buttonSnapshot.FullWidth,
                    RibbonCompactLevel.SmallWithLabels => buttonSnapshot.IsLargeButton ? double.NaN : buttonSnapshot.FullWidth,
                    _ => buttonSnapshot.CompactWidth
                };
            }

            ApplyRibbonButtonCompactSnapshot(buttonSnapshot, level);
        }
    }

    private static void SetRibbonButtonCompact(ButtonBase button, RibbonCompactLevel level) =>
        ApplyRibbonButtonCompactSnapshot(CaptureRibbonCompactButtonSnapshot(button), level);

    private static void ApplyRibbonButtonCompactSnapshot(RibbonCompactButtonSnapshot snapshot, RibbonCompactLevel level)
    {
        if (snapshot.IsCheckOrRadioButton)
        {
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            if (snapshot.Content is not null)
                snapshot.Content.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            return;
        }

        foreach (var label in snapshot.Labels)
            label.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;

        var isSmallOrMedium = snapshot.ContentLayout is RibbonCommandContentLayout.Small or RibbonCommandContentLayout.Medium;
        if (snapshot.HasContentLayout &&
            snapshot.ContentLayout == RibbonCommandContentLayout.Small &&
            snapshot.SmallGrid is not null)
        {
            ApplySmallButtonCompactLayout(snapshot, level);
        }

        if (!isSmallOrMedium)
        {
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;

            if (snapshot.Content is not null)
                snapshot.Content.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            foreach (var stack in snapshot.HorizontalStacks)
                stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        }

        if (snapshot.HasContentLayout &&
            snapshot.ContentLayout == RibbonCommandContentLayout.Large &&
            snapshot.LargeStack is not null)
        {
            ApplyLargeButtonCompactLayout(snapshot, level);
        }
    }

    private static void ApplySmallButtonCompactLayout(
        Grid contentGrid,
        ButtonBase button,
        RibbonCompactLevel level) =>
        ApplySmallButtonCompactLayout(
            new RibbonCompactButtonSnapshot(
                button,
                button is CheckBox or RadioButton,
                contentGrid,
                hasContentLayout: true,
                RibbonCommandContentLayout.Small,
                isLargeButton: false,
                hasCompactWidths: false,
                fullWidth: 0,
                compactWidth: 0,
                [],
                [],
                contentGrid,
                GetRibbonSmallButtonSpacerColumn(contentGrid),
                null,
                null,
                null,
                null),
            level);

    private static void ApplySmallButtonCompactLayout(
        RibbonCompactButtonSnapshot snapshot,
        RibbonCompactLevel level)
    {
        if (snapshot.SmallSpacerColumn is not null)
        {
            snapshot.SmallSpacerColumn.Width = level == RibbonCompactLevel.IconOnly
                ? new GridLength(0)
                : new GridLength(5);
        }

        if (level == RibbonCompactLevel.IconOnly)
        {
            snapshot.SmallGrid!.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            snapshot.SmallGrid!.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
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

        ApplyLargeButtonCompactLayout(
            new RibbonCompactButtonSnapshot(
                button,
                button is CheckBox or RadioButton,
                contentStack,
                hasContentLayout: true,
                RibbonCommandContentLayout.Large,
                isLargeButton: true,
                hasCompactWidths: false,
                fullWidth: 0,
                compactWidth: 0,
                [],
                [],
                null,
                null,
                contentStack,
                iconSlot,
                iconSlot?.Child as FrameworkElement,
                labelBlock),
            level);
    }

    private static void ApplyLargeButtonCompactLayout(
        RibbonCompactButtonSnapshot snapshot, RibbonCompactLevel level)
    {
        if (snapshot.LargeStack is null ||
            snapshot.LargeIconSlot is null ||
            snapshot.LargeLabelBlock is null)
        {
            return;
        }

        if (level == RibbonCompactLevel.Full)
        {
            snapshot.LargeStack.Orientation = Orientation.Vertical;
            snapshot.LargeStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            snapshot.Button.Height = 76;
            snapshot.LargeIconSlot.Width = 34;
            snapshot.LargeIconSlot.Height = 34;
            snapshot.LargeIconSlot.Margin = new Thickness(0, 0, 0, 2);
            if (snapshot.LargeIconChild is not null)
            {
                snapshot.LargeIconChild.Width = 32;
                snapshot.LargeIconChild.Height = 32;
            }
            snapshot.LargeLabelBlock.TextWrapping = TextWrapping.Wrap;
            snapshot.LargeLabelBlock.MaxWidth = 96;
            snapshot.LargeLabelBlock.TextTrimming = TextTrimming.None;
            snapshot.LargeLabelBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            snapshot.LargeLabelBlock.TextAlignment = TextAlignment.Center;
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            snapshot.LargeStack.Orientation = Orientation.Horizontal;
            snapshot.LargeStack.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            snapshot.Button.Height = 48;
            snapshot.LargeIconSlot.Width = 24;
            snapshot.LargeIconSlot.Height = 24;
            snapshot.LargeIconSlot.Margin = new Thickness(0, 0, 5, 0);
            if (snapshot.LargeIconChild is not null)
            {
                snapshot.LargeIconChild.Width = 24;
                snapshot.LargeIconChild.Height = 24;
            }
            snapshot.LargeLabelBlock.TextWrapping = TextWrapping.NoWrap;
            snapshot.LargeLabelBlock.MaxWidth = 90;
            snapshot.LargeLabelBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            snapshot.LargeLabelBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            snapshot.LargeLabelBlock.TextAlignment = TextAlignment.Left;
            snapshot.Button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
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
