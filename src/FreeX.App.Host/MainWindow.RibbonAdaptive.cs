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

        var selectedTabHeader = GetRibbonAdaptiveTabIdentity(activePanel);
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
            ResetRibbonAdaptiveLayoutPlanCache(cacheKey);
            _ribbonCorrectedStateCache.Clear();
            _ribbonMeasuredOverflowCache.Clear();
        }

        UpdateRibbonResizeThresholdCache(cacheKey, adaptiveGroups, fixedChromeWidth, selectedTabHeader);
        if (_ribbonAdaptiveStateDiffInvalidated)
            _ribbonMeasuredOverflowCache.Clear();

        var layout = GetCachedRibbonAdaptiveLayout(
            cacheKey,
            availableWidth,
            adaptiveGroups,
            fixedChromeWidth,
            selectedTabHeader);
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

        var appliedStateKey = CreateRibbonAppliedStateKey(availableWidth, plannedStates);
        if (!force &&
            !_ribbonAdaptiveStateDiffInvalidated &&
            _lastRibbonAdaptiveAppliedStateKey == appliedStateKey)
        {
            _ribbonAppliedStateSkipCount++;
            return;
        }

        ApplyRibbonAdaptiveStates(
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            _ribbonAdaptiveStateDiffInvalidated ? null : _lastRibbonAdaptiveAppliedStates,
            availableWidth);
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
        appliedStateKey = CreateRibbonAppliedStateKey(availableWidth, plannedStates);
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
        ResetRibbonAdaptiveLayoutPlanCache(null);
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
            _ribbonAdaptiveLayoutPlanComputeCount,
            _ribbonAdaptiveLayoutPlanCacheHitCount,
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
        _ribbonAdaptiveLayoutPlanComputeCount = 0;
        _ribbonAdaptiveLayoutPlanCacheHitCount = 0;
        _ribbonMeasuredOverflowMeasurementCount = 0;
        _ribbonCorrectedStateCacheHitCount = 0;
        _ribbonAppliedStateSkipCount = 0;

        if (resetSelectedStaticNormalization &&
            RibbonTabs?.SelectedItem is TabItem selectedTab)
        {
            _normalizedRibbonStaticTabs.Remove(selectedTab);
        }
    }

    private RibbonAdaptiveLayoutResult GetCachedRibbonAdaptiveLayout(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        double fixedChromeWidth,
        string? selectedTabHeader)
    {
        if (!string.Equals(_ribbonAdaptiveLayoutPlanCacheKey, measurementCacheKey, StringComparison.Ordinal))
            ResetRibbonAdaptiveLayoutPlanCache(measurementCacheKey);

        var planCacheKey = CreateRibbonAdaptiveLayoutPlanCacheEntryKey(availableWidth, fixedChromeWidth, selectedTabHeader);
        if (_ribbonAdaptiveLayoutPlanCache.TryGetValue(planCacheKey, out var cachedLayout))
        {
            _ribbonAdaptiveLayoutPlanCacheHitCount++;
            return cachedLayout;
        }

        _ribbonAdaptiveLayoutPlanComputeCount++;
        var layout = RibbonAdaptiveLayoutEngine.Plan(availableWidth, adaptiveGroups, fixedChromeWidth, selectedTabHeader);
        var cached = new RibbonAdaptiveLayoutResult(
            layout.States.ToArray(),
            layout.PlannedWidth,
            layout.RequiresMeasuredCorrection);
        _ribbonAdaptiveLayoutPlanCache[planCacheKey] = cached;
        return cached;
    }

    private void ResetRibbonAdaptiveLayoutPlanCache(string? measurementCacheKey)
    {
        _ribbonAdaptiveLayoutPlanCacheKey = measurementCacheKey;
        _ribbonAdaptiveLayoutPlanCache.Clear();
    }

    private static RibbonAdaptiveLayoutPlanCacheEntryKey CreateRibbonAdaptiveLayoutPlanCacheEntryKey(
        double availableWidth,
        double fixedChromeWidth,
        string? selectedTabHeader) =>
        new(
            RoundRibbonWidthToTenths(availableWidth),
            RoundRibbonWidthToTenths(fixedChromeWidth),
            selectedTabHeader ?? "",
            GetCollapsedRibbonFootprintMode(availableWidth));

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

    private static string GetRibbonAdaptiveTabIdentity(DependencyObject element)
    {
        if (FindVisualAncestor<TabItem>(element) is not { } tab)
            return "";

        if (RibbonMetadata.TryGetCatalogId(tab, out var catalogId))
            return catalogId;

        return tab.Header?.ToString() ?? "";
    }

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
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null, availableWidth);
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
        }

        while (RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, plannedStates) &&
               RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(plannedStates, preserveFirstGroup: false, runtimeVisibilityProtectedGroupIndexes))
        {
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null, availableWidth);
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
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
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null, availableWidth);
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
            if (!RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, plannedStates))
                continue;

            plannedStates[index] = currentState;
            ApplyRibbonAdaptiveStates(groupSnapshots, collapsedButtons, plannedStates, previousStates: null, availableWidth);
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
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

    private static RibbonAppliedStateKey CreateRibbonAppliedStateKey(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        return new RibbonAppliedStateKey(
            RoundRibbonWidthToTenths(availableWidth),
            GetCollapsedRibbonFootprintMode(availableWidth),
            CreateRibbonStateSignature(states));
    }

    private static RibbonCorrectionCacheKey CreateRibbonCorrectionCacheKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        new(
            measurementCacheKey,
            RoundRibbonWidthToTenths(availableWidth),
            CreateRibbonStateSignature(states));

    private static RibbonMeasuredOverflowCacheKey CreateRibbonMeasuredOverflowCacheKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        return new RibbonMeasuredOverflowCacheKey(
            measurementCacheKey,
            RoundRibbonWidthToTenths(availableWidth),
            GetCollapsedRibbonFootprintMode(availableWidth),
            CreateRibbonStateSignature(states));
    }

    private static void ApplyRibbonAdaptiveStates(
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveGroupState>? previousStates,
        double availableWidth = 0) =>
        RibbonAdaptiveStateApplicator.ApplyStates(
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            previousStates,
            availableWidth);

    private void SetCollapsedRibbonButtonFootprintIfNeeded(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprintMode = GetCollapsedRibbonFootprintMode(availableWidth);
        if (_lastRibbonCollapsedFootprintMode == footprintMode)
            return;

        RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
        _lastRibbonCollapsedFootprintMode = footprintMode;
    }

    private static RibbonCollapsedGroupFootprintMode GetCollapsedRibbonFootprintMode(double availableWidth)
    {
        if (availableWidth <= 760)
            return RibbonCollapsedGroupFootprintMode.Captionless;

        return availableWidth <= 920
            ? RibbonCollapsedGroupFootprintMode.Compact
            : RibbonCollapsedGroupFootprintMode.Normal;
    }

    private static int RoundRibbonWidthToTenths(double width) =>
        (int)Math.Round(Math.Max(0, width) * 10, MidpointRounding.ToEven);

    private static RibbonStateSignature CreateRibbonStateSignature(IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        ulong low = 0;
        ulong high = 0;
        var count = states.Count;
        var packedCount = Math.Min(count, 64);
        for (var index = 0; index < packedCount; index++)
        {
            var value = ((ulong)states[index]) & 0x3UL;
            if (index < 32)
                low |= value << (index * 2);
            else
                high |= value << ((index - 32) * 2);
        }

        var overflow = count > 64 ? CreateRibbonStateOverflowSignature(states) : null;
        return new RibbonStateSignature(count, low, high, overflow);
    }

    private static string CreateRibbonStateOverflowSignature(IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        var builder = new System.Text.StringBuilder(states.Count - 64);
        for (var index = 64; index < states.Count; index++)
            builder.Append((char)('0' + (int)states[index]));

        return builder.ToString();
    }

    private static RibbonAdaptiveGroup MeasureRibbonAdaptiveGroup(RibbonCompactGroupSnapshot snapshot, Button collapsedButton)
    {
        var name = GetRibbonGroupName(snapshot.Group);
        var catalogId = GetRibbonGroupCatalogId(snapshot.Group);
        var fullWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.Full);
        var smallWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.SmallWithLabels);
        var iconWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.IconOnly);
        collapsedButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var collapsedWidth = Math.Max(48, collapsedButton.DesiredSize.Width);
        RibbonAdaptiveStateApplicator.ApplyGroup(snapshot, RibbonCompactLevel.Full);

        return new RibbonAdaptiveGroup(name, fullWidth, smallWidth, iconWidth, collapsedWidth, catalogId);
    }

    private static double MeasureRibbonGroupWidth(RibbonCompactGroupSnapshot snapshot, RibbonCompactLevel level)
    {
        RibbonAdaptiveStateApplicator.ApplyGroup(snapshot, level);
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
        var tabName = GetRibbonAdaptiveTabIdentity(activePanel);
        return string.Join(
            "|",
            tabName,
            groups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", groups.Select(group => $"{GetRibbonGroupName(group)}:{GetRibbonGroupCatalogId(group)}:{group.GetHashCode():X}")));
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

    private static string? GetRibbonGroupCatalogId(FrameworkElement group) =>
        RibbonMetadata.TryGetCatalogId(group, out var catalogId) ? catalogId : null;

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

    internal enum RibbonCompactLevel
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

    private readonly record struct RibbonAdaptiveLayoutPlanCacheEntryKey(
        int AvailableWidthTenths,
        int FixedChromeWidthTenths,
        string SelectedTabHeader,
        RibbonCollapsedGroupFootprintMode FootprintMode);

    private readonly record struct RibbonStateSignature(
        int Count,
        ulong Low,
        ulong High,
        string? Overflow);

    private readonly record struct RibbonAppliedStateKey(
        int AvailableWidthTenths,
        RibbonCollapsedGroupFootprintMode FootprintMode,
        RibbonStateSignature States);

    private readonly record struct RibbonCorrectionCacheKey(
        string MeasurementCacheKey,
        int AvailableWidthTenths,
        RibbonStateSignature States);

    private readonly record struct RibbonMeasuredOverflowCacheKey(
        string MeasurementCacheKey,
        int AvailableWidthTenths,
        RibbonCollapsedGroupFootprintMode FootprintMode,
        RibbonStateSignature States);

    internal sealed class RibbonCompactGroupSnapshot(
        FrameworkElement group,
        IReadOnlyList<TextBlock> commandLabels,
        IReadOnlyList<RibbonCompactButtonSnapshot> buttons)
    {
        public FrameworkElement Group { get; } = group;
        public IReadOnlyList<TextBlock> CommandLabels { get; } = commandLabels;
        public IReadOnlyList<RibbonCompactButtonSnapshot> Buttons { get; } = buttons;
    }

    internal sealed class RibbonCompactButtonSnapshot(
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
        private readonly FrameworkElement _chevron = CreateRibbonChevronGlyph(8, 8, Brushes.Black, pointsUp: false);

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
        var smallSpacerColumn = RibbonAdaptiveStateApplicator.GetSmallButtonSpacerColumn(smallGrid);
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

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level) =>
        RibbonAdaptiveStateApplicator.ApplyGroup(CaptureRibbonCompactGroupSnapshot(group), level);

    private static void SetRibbonButtonCompact(ButtonBase button, RibbonCompactLevel level) =>
        RibbonAdaptiveStateApplicator.ApplyButton(CaptureRibbonCompactButtonSnapshot(button), level);

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
