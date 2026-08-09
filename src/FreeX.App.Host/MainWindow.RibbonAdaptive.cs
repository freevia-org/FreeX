using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private RibbonCompactUpdateResult UpdateRibbonCompactMode(bool force = false)
    {
        if (RibbonTabs is null)
            return RibbonCompactUpdateResult.Noop;

        var activePanel = GetActiveRibbonPanel();
        if (activePanel is null)
            return RibbonCompactUpdateResult.Noop;

        var groups = GetCachedRibbonAdaptiveGroups(activePanel);
        if (groups.Count == 0)
            return RibbonCompactUpdateResult.Noop;

        var controlCacheKey = _ribbonAdaptiveControlCacheKey ??
            CreateRibbonAdaptiveMeasurementCacheKey(activePanel, groups);
        var collapsedButtons = GetCachedRibbonCollapsedGroupButtons(activePanel, groups, controlCacheKey);
        var groupSnapshots = GetCachedRibbonCompactGroupSnapshots(groups, controlCacheKey);
        var availableWidth = GetRibbonAvailableWidth(activePanel);
        if (availableWidth <= 0)
            return RibbonCompactUpdateResult.Noop;

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
            fixedChromeWidth = RibbonAdaptiveWpfSurface.MeasureFixedChromeWidth(activePanel) + 24;
            _ribbonAdaptiveGroupMeasurementCount += groupSnapshots.Count;
            adaptiveGroups = groupSnapshots
                .Select((snapshot, index) => MeasureRibbonAdaptiveGroup(snapshot, collapsedButtons[index]))
                .ToList();
            _ribbonAdaptiveMeasurementCacheKey = cacheKey;
            _ribbonAdaptiveGroupCache = adaptiveGroups;
            _ribbonAdaptiveGroupProfileKeyCache = RibbonAdaptiveWpfSurface.CreateGroupProfileKeys(adaptiveGroups);
            _ribbonAdaptiveFixedChromeWidthCache = fixedChromeWidth;
            ResetRibbonAdaptiveLayoutPlanCache(cacheKey);
            _ribbonCorrectedStateCache.Clear();
            _ribbonMeasuredOverflowCache.Clear();
        }

        var groupProfileKeys = _ribbonAdaptiveGroupProfileKeyCache ??=
            RibbonAdaptiveWpfSurface.CreateGroupProfileKeys(adaptiveGroups);
        UpdateRibbonResizeThresholdCache(cacheKey, adaptiveGroups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
        if (_ribbonAdaptiveStateDiffInvalidated)
            _ribbonMeasuredOverflowCache.Clear();

        var layout = GetCachedRibbonAdaptiveLayout(
            cacheKey,
            availableWidth,
            adaptiveGroups,
            groupProfileKeys,
            fixedChromeWidth,
            selectedTabHeader);
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStatesSource = layout.States;

        var correctionCacheKey = CreateRibbonCorrectionCacheKey(cacheKey, availableWidth, plannedStatesSource);
        var hasCachedCorrection = _ribbonCorrectedStateCache.TryGetValue(correctionCacheKey, out var correctedStates);
        if (hasCachedCorrection)
            _ribbonCorrectedStateCacheHitCount++;
        var cachedCorrectionNeedsExpansion = false;
        if (hasCachedCorrection && correctedStates is not null)
        {
            plannedStatesSource = correctedStates;
            cachedCorrectionNeedsExpansion = RibbonAdaptiveWpfSurface.StatesAreMoreCollapsedThan(plannedStatesSource, layout.States);
        }

        var appliedStateKey = CreateRibbonAppliedStateKey(availableWidth, plannedStatesSource);
        if (!_ribbonAdaptiveStateDiffInvalidated &&
            _lastRibbonAdaptiveAppliedStateKey == appliedStateKey)
        {
            _ribbonAppliedStateSkipCount++;
            return RibbonCompactUpdateResult.SkippedAppliedState;
        }

        var changedGroupCount = ApplyRibbonAdaptiveStates(
            groupSnapshots,
            collapsedButtons,
            plannedStatesSource,
            _ribbonAdaptiveStateDiffInvalidated ? null : _lastRibbonAdaptiveAppliedStates,
            availableWidth);
        var visualStateChanged = changedGroupCount > 0;
        visualStateChanged |= SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        var shouldApplyMeasuredCorrection = layout.RequiresMeasuredCorrection;
        var dataPrimaryCorrection = shouldApplyMeasuredCorrection
            ? RibbonCollapsedGroupCatalogPlanner.PlanDataPrimaryCorrection(
                adaptiveGroups,
                plannedStatesSource,
                availableWidth,
                selectedTabHeader)
            : null;
        var needsMeasuredPrimaryCorrection = dataPrimaryCorrection is not null;
        var requiresMeasuredCorrection = cachedCorrectionNeedsExpansion ||
            shouldApplyMeasuredCorrection &&
            (needsMeasuredPrimaryCorrection ||
             !hasCachedCorrection ||
             RibbonRowOverflowsMeasuredCached(activePanel, cacheKey, availableWidth, plannedStatesSource));
        var measuredCorrectionApplied = false;
        IReadOnlyList<RibbonAdaptiveGroupState> appliedStates = plannedStatesSource;
        if (requiresMeasuredCorrection)
        {
            var plannedStates = plannedStatesSource.ToArray();
            var overflowProtection = RibbonCollapsedGroupCatalogPlanner.PlanMeasuredOverflowProtection(
                adaptiveGroups,
                groupProfileKeys,
                availableWidth,
                selectedTabHeader);
            measuredCorrectionApplied |= ApplyRibbonMeasuredPrimaryFallback(
                activePanel,
                groupSnapshots,
                collapsedButtons,
                plannedStates,
                adaptiveGroups,
                overflowProtection.RuntimeVisibilityProtectedGroupIndexes,
                cacheKey,
                availableWidth,
                dataPrimaryCorrection);
            measuredCorrectionApplied |= ApplyRibbonMeasuredOverflowFallback(
                activePanel,
                groupSnapshots,
                collapsedButtons,
                plannedStates,
                adaptiveGroups,
                overflowProtection,
                cacheKey,
                availableWidth);
            measuredCorrectionApplied |= ApplyRibbonMeasuredExpansionFallback(activePanel, groupSnapshots, collapsedButtons, plannedStates, groupProfileKeys, cacheKey, availableWidth, selectedTabHeader);
            if (RibbonAdaptiveWpfSurface.MeasureOverflows(activePanel, availableWidth))
            {
                _ribbonMeasuredOverflowCache.Clear();
                measuredCorrectionApplied |= ApplyRibbonMeasuredOverflowFallback(
                    activePanel,
                    groupSnapshots,
                    collapsedButtons,
                    plannedStates,
                    adaptiveGroups,
                    overflowProtection,
                    cacheKey,
                    availableWidth);
            }

            measuredCorrectionApplied |= RibbonAdaptiveWpfFallback.ApplyFallbackUntilFits(
                plannedStates,
                preserveFirstGroup: false,
                protectedGroupIndexes: null,
                _ => RibbonAdaptiveWpfSurface.MeasureOverflows(activePanel, availableWidth),
                (RibbonAdaptiveGroupState[] states, bool preserveFirstGroup, IReadOnlySet<int>? protectedIndexes, out int changedIndex, out RibbonAdaptiveGroupState previousState) =>
                    RibbonAdaptiveLayoutEngine.TryFallbackOneMoreGroup(
                        states,
                        preserveFirstGroup,
                        protectedIndexes,
                        out changedIndex,
                        out previousState),
                (index, state, previousState) => ApplyRibbonAdaptiveStateAt(
                    groupSnapshots,
                    collapsedButtons,
                    index,
                    state,
                    previousState,
                    availableWidth) > 0);

            appliedStates = plannedStates;
        }

        visualStateChanged |= SetCollapsedRibbonButtonFootprintIfNeeded(collapsedButtons, availableWidth);
        appliedStateKey = CreateRibbonAppliedStateKey(availableWidth, appliedStates);
        if (!hasCachedCorrection || requiresMeasuredCorrection)
            _ribbonCorrectedStateCache[correctionCacheKey] = appliedStates;
        _lastRibbonAdaptiveAppliedStateKey = appliedStateKey;
        _lastRibbonAdaptiveAppliedStates = appliedStates;
        _ribbonAdaptiveStateDiffInvalidated = false;

        var compacted = appliedStates.Any(state => state != RibbonAdaptiveGroupState.Full);
        _ribbonCompact = compacted;
        if (measuredCorrectionApplied)
            return RibbonCompactUpdateResult.MeasuredCorrectionApplied;

        return visualStateChanged
            ? RibbonCompactUpdateResult.AppliedVisualChange
            : RibbonCompactUpdateResult.Noop;
    }

    private bool ApplyRibbonMeasuredPrimaryFallback(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        IReadOnlySet<int> protectedGroupIndexes,
        string measurementCacheKey,
        double availableWidth,
        RibbonAdaptiveRuntimeStateOverride? primaryCorrection)
    {
        if (primaryCorrection is not { } correction)
            return false;

        var appliedCorrection = false;
        var previousState = plannedStates[correction.Index];
        plannedStates[correction.Index] = correction.State;
        appliedCorrection |= ApplyRibbonAdaptiveStateAt(
            groupSnapshots,
            collapsedButtons,
            correction.Index,
            correction.State,
            previousState,
            availableWidth) > 0;

        appliedCorrection |= RibbonAdaptiveWpfFallback.ApplyFallbackUntilFits(
            plannedStates,
            correction.Index == 0,
            protectedGroupIndexes,
            states => RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, states),
            (RibbonAdaptiveGroupState[] states, bool preserveFirstGroup, IReadOnlySet<int>? protectedIndexes, out int changedIndex, out RibbonAdaptiveGroupState fallbackPreviousState) =>
                RibbonAdaptiveLayoutEngine.TryFallbackOneMoreGroup(
                    states,
                    adaptiveGroups,
                    preserveFirstGroup,
                    protectedIndexes,
                    availableWidth,
                    out changedIndex,
                    out fallbackPreviousState),
            (index, state, fallbackPreviousState) => ApplyRibbonAdaptiveStateAt(
                groupSnapshots,
                collapsedButtons,
                index,
                state,
                fallbackPreviousState,
                availableWidth) > 0);

        return appliedCorrection;
    }

    private IReadOnlyList<FrameworkElement> GetCachedRibbonAdaptiveGroups(StackPanel activePanel)
    {
        if (ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel) &&
            _ribbonAdaptiveGroupControlCache is not null)
        {
            _ribbonAdaptiveScrollViewerCache ??= GetOrCacheRibbonActivePanelScrollViewer(activePanel);
            return _ribbonAdaptiveGroupControlCache;
        }

        var groups = RibbonAdaptiveWpfSurface.GetAdaptiveGroups(activePanel);
        _ribbonAdaptiveControlCachePanel = activePanel;
        _ribbonAdaptiveControlCacheTab = RibbonTabs?.SelectedItem as TabItem;
        _ribbonAdaptiveScrollViewerCache = GetOrCacheRibbonActivePanelScrollViewer(activePanel);
        _ribbonAdaptiveGroupControlCache = groups;
        _ribbonAdaptiveControlCacheKey = null;
        _ribbonAdaptiveCollapsedButtonCache = null;
        _ribbonCompactSnapshotCacheKey = null;
        _ribbonCompactGroupSnapshotCache = null;
        _ribbonAdaptiveGroupProfileKeyCache = null;
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
        _ribbonAdaptiveControlCacheTab = RibbonTabs?.SelectedItem as TabItem;
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
        _ribbonAdaptiveGroupProfileKeyCache = null;
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
            _ribbonAdaptiveStateApplyCount,
            _ribbonAdaptiveStateChangedGroupCount,
            _ribbonCollapsedFootprintApplyCount,
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
        _ribbonAdaptiveStateApplyCount = 0;
        _ribbonAdaptiveStateChangedGroupCount = 0;
        _ribbonCollapsedFootprintApplyCount = 0;

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
        IReadOnlyList<string> groupProfileKeys,
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
        var layout = RibbonAdaptiveLayoutEngine.Plan(availableWidth, adaptiveGroups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
        var layoutStates = RibbonCollapsedGroupCatalogPlanner.NormalizeDataSurfaceStates(
            adaptiveGroups,
            layout.States,
            availableWidth,
            selectedTabHeader);
        var cached = new RibbonAdaptiveLayoutResult(
            layoutStates,
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
        new(RibbonAdaptiveWpfSurface.CreateLayoutPlanKey(
            availableWidth,
            fixedChromeWidth,
            selectedTabHeader,
            GetCollapsedRibbonFootprintMode(availableWidth)));

    private double GetRibbonAvailableWidth(StackPanel activePanel)
    {
        var ribbonScrollViewer = ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel)
            ? _ribbonAdaptiveScrollViewerCache
            : null;
        ribbonScrollViewer ??= GetOrCacheRibbonActivePanelScrollViewer(activePanel);
        _ribbonAdaptiveScrollViewerCache = ribbonScrollViewer;
        return RibbonAdaptiveWpfSurface.ResolveAvailableWidth(
            activePanel,
            ribbonScrollViewer,
            RibbonTabs.ActualWidth);
    }

    private string GetRibbonAdaptiveTabIdentity(DependencyObject element)
    {
        if (element is StackPanel activePanel &&
            TryGetSelectedRibbonActivePanelCache(activePanel, out var cachedTab, out _))
        {
            return GetRibbonAdaptiveTabIdentity(cachedTab);
        }

        if (FindVisualAncestor<TabItem>(element) is not { } tab)
            return "";

        return GetRibbonAdaptiveTabIdentity(tab);
    }

    private static string GetRibbonAdaptiveTabIdentity(TabItem tab)
    {
        if (RibbonMetadata.TryGetCatalogId(tab, out var catalogId))
            return catalogId;

        return tab.Header?.ToString() ?? "";
    }

    private bool ApplyRibbonMeasuredOverflowFallback(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        RibbonMeasuredOverflowProtectionPlan overflowProtection,
        string measurementCacheKey,
        double availableWidth)
    {
        var appliedCorrection = ApplyMeasuredFallbackPass(
            activePanel,
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            adaptiveGroups,
            measurementCacheKey,
            availableWidth,
            overflowProtection.PreserveFirstGroupDuringInitialFallback,
            overflowProtection.InitialFallbackProtectedGroupIndexes);

        appliedCorrection |= ApplyMeasuredFallbackPass(
            activePanel,
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            adaptiveGroups,
            measurementCacheKey,
            availableWidth,
            preserveFirstGroup: false,
            protectedGroupIndexes: overflowProtection.RelaxedFallbackProtectedGroupIndexes);

        return appliedCorrection;
    }

    private bool ApplyMeasuredFallbackPass(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        string measurementCacheKey,
        double availableWidth,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes) =>
        RibbonAdaptiveWpfFallback.ApplyFallbackUntilFits(
            plannedStates,
            preserveFirstGroup,
            protectedGroupIndexes,
            states => RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, states),
            (RibbonAdaptiveGroupState[] states, bool preserveFirst, IReadOnlySet<int>? protectedIndexes, out int changedIndex, out RibbonAdaptiveGroupState previousState) =>
                RibbonAdaptiveLayoutEngine.TryFallbackOneMoreGroup(
                    states,
                    adaptiveGroups,
                    preserveFirst,
                    protectedIndexes,
                    availableWidth,
                    out changedIndex,
                    out previousState),
            (index, state, previousState) => ApplyRibbonAdaptiveStateAt(
                groupSnapshots,
                collapsedButtons,
                index,
                state,
                previousState,
                availableWidth) > 0);

    private bool ApplyRibbonMeasuredExpansionFallback(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        IReadOnlyList<string> groupProfileKeys,
        string measurementCacheKey,
        double availableWidth,
        string? selectedTabHeader)
    {
        var appliedCorrection = false;
        appliedCorrection |= ApplyRibbonMeasuredExpansionPass(
            activePanel,
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            measurementCacheKey,
            availableWidth,
            RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader));
        appliedCorrection |= ApplyRibbonMeasuredExpansionPass(
            activePanel,
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            measurementCacheKey,
            availableWidth,
            RibbonAdaptivePriorityPlanner.GetSpaceFillingExpandableGroupIndexes(groupProfileKeys, availableWidth, selectedTabHeader));

        return appliedCorrection;
    }

    private bool ApplyRibbonMeasuredExpansionPass(
        StackPanel activePanel,
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        RibbonAdaptiveGroupState[] plannedStates,
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<int> expandableIndexes)
        => RibbonAdaptiveWpfFallback.ApplyExpansionPass(
            plannedStates,
            expandableIndexes,
            states => RibbonRowOverflowsMeasuredCached(activePanel, measurementCacheKey, availableWidth, states),
            (index, state, previousState) => ApplyRibbonAdaptiveStateAt(
                groupSnapshots,
                collapsedButtons,
                index,
                state,
                previousState,
                availableWidth) > 0);

    private bool RibbonRowOverflowsMeasuredCached(
        StackPanel activePanel,
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
    {
        var overflowCacheKey = CreateRibbonMeasuredOverflowCacheKey(measurementCacheKey, availableWidth, states);
        if (_ribbonMeasuredOverflowCache.TryGetValue(overflowCacheKey, out var overflows))
            return overflows;

        overflows = RibbonAdaptiveWpfSurface.MeasureOverflows(activePanel, availableWidth);
        _ribbonMeasuredOverflowMeasurementCount++;
        _ribbonMeasuredOverflowCache[overflowCacheKey] = overflows;
        return overflows;
    }

    private static RibbonAppliedStateKey CreateRibbonAppliedStateKey(
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
        => new(RibbonAdaptiveWpfSurface.CreateAppliedStateKey(
            GetCollapsedRibbonFootprintMode(availableWidth),
            UsesWideIconOnlyLabelMode(availableWidth),
            states));

    private static RibbonCorrectionCacheKey CreateRibbonCorrectionCacheKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states) =>
        new(RibbonAdaptiveWpfSurface.CreateCorrectionKey(
            measurementCacheKey,
            availableWidth,
            states));

    private static RibbonMeasuredOverflowCacheKey CreateRibbonMeasuredOverflowCacheKey(
        string measurementCacheKey,
        double availableWidth,
        IReadOnlyList<RibbonAdaptiveGroupState> states)
        => new(RibbonAdaptiveWpfSurface.CreateMeasuredOverflowKey(
            measurementCacheKey,
            availableWidth,
            GetCollapsedRibbonFootprintMode(availableWidth),
            states));

    private int ApplyRibbonAdaptiveStates(
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        IReadOnlyList<RibbonAdaptiveGroupState> plannedStates,
        IReadOnlyList<RibbonAdaptiveGroupState>? previousStates,
        double availableWidth = 0)
    {
        var changedGroupCount = RibbonAdaptiveStateApplicator.ApplyStates(
            groupSnapshots,
            collapsedButtons,
            plannedStates,
            previousStates,
            availableWidth);
        _ribbonAdaptiveStateApplyCount++;
        _ribbonAdaptiveStateChangedGroupCount += changedGroupCount;
        return changedGroupCount;
    }

    private int ApplyRibbonAdaptiveStateAt(
        IReadOnlyList<RibbonCompactGroupSnapshot> groupSnapshots,
        IReadOnlyList<Button> collapsedButtons,
        int index,
        RibbonAdaptiveGroupState plannedState,
        RibbonAdaptiveGroupState previousState,
        double availableWidth = 0)
    {
        var changedGroupCount = RibbonAdaptiveStateApplicator.ApplyStateAt(
            groupSnapshots,
            collapsedButtons,
            index,
            plannedState,
            previousState,
            availableWidth);
        _ribbonAdaptiveStateApplyCount++;
        _ribbonAdaptiveStateChangedGroupCount += changedGroupCount;
        return changedGroupCount;
    }

    private bool SetCollapsedRibbonButtonFootprintIfNeeded(IReadOnlyList<Button> collapsedButtons, double availableWidth)
    {
        var footprintMode = GetCollapsedRibbonFootprintMode(availableWidth);
        if (_lastRibbonCollapsedFootprintMode == footprintMode)
            return false;

        RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(collapsedButtons, availableWidth);
        _lastRibbonCollapsedFootprintMode = footprintMode;
        _ribbonCollapsedFootprintApplyCount++;
        return true;
    }

    private static RibbonCollapsedGroupFootprintMode GetCollapsedRibbonFootprintMode(double availableWidth)
        => RibbonCollapsedGroupBreakpoints.GetFootprintMode(availableWidth);

    private static bool UsesWideIconOnlyLabelMode(double availableWidth) =>
        availableWidth > 820;

    private static RibbonAdaptiveGroup MeasureRibbonAdaptiveGroup(RibbonCompactGroupSnapshot snapshot, Button collapsedButton)
    {
        var name = GetRibbonGroupName(snapshot.Group);
        var catalogId = GetRibbonGroupCatalogId(snapshot.Group);
        var fullWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.Full);
        var smallWidth = MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.SmallWithLabels);
        var iconWidth = RibbonCollapsedGroupCatalogPlanner.ShouldUseFullLayoutForIconOnlyGroup(
            catalogId,
            availableWidth: double.PositiveInfinity)
            ? fullWidth
            : RibbonCollapsedGroupCatalogPlanner.ShouldUseSmallWithLabelsForIconOnlyGroup(catalogId)
            ? smallWidth
            : MeasureRibbonGroupWidth(snapshot, RibbonCompactLevel.IconOnly);
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

    private string CreateRibbonAdaptiveMeasurementCacheKey(StackPanel activePanel, IReadOnlyList<FrameworkElement> groups)
        => RibbonAdaptiveWpfSurface.CreateMeasurementCacheKey(
            GetRibbonAdaptiveTabIdentity(activePanel),
            groups,
            GetRibbonGroupName,
            GetRibbonGroupCatalogId);

    private void UpdateRibbonResizeThresholdCache(
        string cacheKey,
        IReadOnlyList<RibbonAdaptiveGroup> adaptiveGroups,
        IReadOnlyList<string> groupProfileKeys,
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
        _ribbonResizeThresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(adaptiveGroups, groupProfileKeys, fixedChromeWidth, selectedTabHeader);
    }

    private static List<Button> EnsureRibbonCollapsedGroupButtons(StackPanel panel, IReadOnlyList<FrameworkElement> groups)
        => RibbonCollapsedGroupOverflow.ReconcileButtons(
            panel,
            groups,
            GetRibbonGroupName,
            (group, keyTips) => CreateRibbonCollapsedGroupButton(group, keyTips));

    private static bool IsRibbonCollapsedGroupButton(FrameworkElement element) =>
        RibbonMetadata.IsCollapsedGroupButton(element);

    private static Button CreateRibbonCollapsedGroupButton(FrameworkElement group, ISet<string>? usedKeyTips = null)
    {
        var groupName = GetRibbonGroupName(group);
        var presentation = RibbonCollapsedGroupCatalogPlanner.PlanPresentation(
            GetRibbonGroupCatalogId(group),
            groupName);
        var displayName = presentation.ResolveDisplayName(UiText.Get);
        var iconKey = presentation.IconKey;
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(iconKey);
        var (slotBackground, slotBorder, glyphBrush) = GetRibbonIconAccentBrushes(icon.Accent);
        var label = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 60,
            LineHeight = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
        var iconSlot = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(3),
            Background = slotBackground,
            BorderBrush = slotBorder,
            BorderThickness = slotBorder is null ? new Thickness(0) : new Thickness(1),
            Child = RibbonIconFactory.CreateCommandIcon(iconKey, icon, 28, glyphBrush),
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        RibbonMetadata.SetRole(iconSlot, RibbonMetadataRole.CommandIcon);

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
                    iconSlot,
                    label
                }
            }
        };
        RibbonMetadata.SetRole(button, RibbonMetadataRole.CollapsedGroupButton);

        button.SetResourceReference(StyleProperty, "RibbonTallButton");
        RibbonTooltip.SetTitle(button, groupName);
        RibbonTooltip.SetDescription(button, UiText.Format("MainWindow_RibbonCollapsedGroupTooltipFormat", groupName));
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
        => RibbonCollapsedGroupOverflow.EnsureChevronAdorner(
            button,
            () => CreateRibbonChevronGlyph(8, 8, Brushes.Black, pointsUp: false));

    private static ContextMenu CreateLazyCollapsedRibbonGroupMenu(FrameworkElement group)
        => RibbonCollapsedGroupOverflow.CreateLazyMenu(
            group,
            GetRibbonGroupName,
            RibbonMenuItemCloner.CloneRibbonMenuItem,
            RibbonMenuItemCloner.SynchronizeClonedMenuItems,
            item => FocusCollapsedRibbonMenuPlacementTarget(item));

    private static void EnsureCollapsedRibbonGroupMenuItems(ContextMenu menu)
        => RibbonCollapsedGroupOverflow.EnsureMenuItems(
            menu,
            GetRibbonGroupName,
            RibbonMenuItemCloner.CloneRibbonMenuItem,
            RibbonMenuItemCloner.SynchronizeClonedMenuItems,
            item => FocusCollapsedRibbonMenuPlacementTarget(item));

    private static void FocusCollapsedRibbonMenuPlacementTarget(MenuItem item)
        => RibbonCollapsedGroupOverflow.FocusPlacementTarget(item);

    private static string GetRibbonGroupName(FrameworkElement group)
    {
        if (RibbonMetadata.TryGetGroupName(group, out var groupName))
            return groupName;

        return UiText.Get("MainWindow_RibbonCollapsedGroupFallbackName");
    }

    private static string? GetRibbonGroupCatalogId(FrameworkElement group) =>
        RibbonMetadata.TryGetCatalogId(group, out var catalogId) ? catalogId : null;

    private static string CreateGroupKeyTip(string groupName, ISet<string>? usedKeyTips = null)
        => Free.Shared.Ribbon.RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip(
            groupName,
            usedKeyTips);

    private StackPanel? GetActiveRibbonPanel()
    {
        if (RibbonTabs.SelectedItem is not TabItem tabItem)
            return null;

        if (TryGetCachedActiveRibbonPanel(tabItem, out var cachedPanel))
            return cachedPanel;

        if (RibbonMetadata.TryGetCatalogId(tabItem, out var catalogId) &&
            string.Equals(catalogId, "HomeTab", StringComparison.Ordinal) &&
            HomeRibbonPanel is not null)
        {
            return CacheActiveRibbonPanel(tabItem, HomeRibbonPanel);
        }

        var contentRoot = GetRibbonTabContentRoot(tabItem);
        var activePanel = RibbonAdaptiveWpfSurface.FindLegacyAdaptivePanel(contentRoot);
        return CacheActiveRibbonPanel(tabItem, activePanel);
    }

    private bool TryGetCachedActiveRibbonPanel(TabItem tabItem, out StackPanel? activePanel)
    {
        if (_ribbonAdaptiveActivePanelCacheByTab.TryGetValue(tabItem, out var cached) &&
            cached.Panel.IsVisible)
        {
            activePanel = cached.Panel;
            _ribbonAdaptiveControlCacheTab = tabItem;
            if (ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel))
                _ribbonAdaptiveScrollViewerCache ??= cached.ScrollViewer;
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

        var scrollViewer = FindVisualAncestor<ScrollViewer>(activePanel);
        _ribbonAdaptiveActivePanelCacheByTab[tabItem] = new RibbonActivePanelCacheEntry(activePanel, scrollViewer);
        _ribbonAdaptiveControlCacheTab = tabItem;
        if (ReferenceEquals(_ribbonAdaptiveControlCachePanel, activePanel))
            _ribbonAdaptiveScrollViewerCache = scrollViewer;
        return activePanel;
    }

    private ScrollViewer? GetOrCacheRibbonActivePanelScrollViewer(StackPanel activePanel)
    {
        if (TryGetSelectedRibbonActivePanelCache(activePanel, out var selectedTab, out var cached) &&
            cached.ScrollViewer is { } cachedScrollViewer)
        {
            return cachedScrollViewer;
        }

        var scrollViewer = FindVisualAncestor<ScrollViewer>(activePanel);
        if (scrollViewer is not null &&
            TryGetSelectedRibbonActivePanelCache(activePanel, out selectedTab, out cached))
        {
            _ribbonAdaptiveActivePanelCacheByTab[selectedTab] = cached with { ScrollViewer = scrollViewer };
        }

        return scrollViewer;
    }

    private bool TryGetSelectedRibbonActivePanelCache(
        StackPanel activePanel,
        out TabItem selectedTab,
        out RibbonActivePanelCacheEntry cached)
    {
        if (RibbonTabs?.SelectedItem is TabItem tabItem &&
            _ribbonAdaptiveActivePanelCacheByTab.TryGetValue(tabItem, out var entry) &&
            ReferenceEquals(entry.Panel, activePanel))
        {
            selectedTab = tabItem;
            cached = entry;
            return true;
        }

        selectedTab = null!;
        cached = null!;
        return false;
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

    private enum RibbonCompactUpdateResult
    {
        Noop,
        SkippedAppliedState,
        AppliedVisualChange,
        MeasuredCorrectionApplied
    }

    private readonly record struct RibbonAdaptiveLayoutPlanCacheEntryKey(
        RibbonAdaptiveWpfLayoutPlanKey SharedKey);

    private sealed record RibbonActivePanelCacheEntry(StackPanel Panel, ScrollViewer? ScrollViewer);

    private readonly record struct RibbonAppliedStateKey(
        RibbonAdaptiveWpfAppliedStateKey SharedKey);

    private readonly record struct RibbonCorrectionCacheKey(
        RibbonAdaptiveWpfCorrectionKey SharedKey);

    private readonly record struct RibbonMeasuredOverflowCacheKey(
        RibbonAdaptiveWpfMeasuredOverflowKey SharedKey);

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
        bool hasDropdownChevron,
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
        public bool HasDropdownChevron { get; } = hasDropdownChevron;
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
        var hasDropdownChevron = descendants.Any(RibbonMetadata.IsDropdownChevron);
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
        var largeIconSlot = largeStack is not null ? FindLargeCommandIconSlot(largeStack) : null;
        var largeLabelBlock = largeStack is not null ? FindLargeCommandLabelBlock(largeStack) : null;
        var largeIconChild = largeIconSlot?.Child as FrameworkElement;

        return new RibbonCompactButtonSnapshot(
            button,
            button is CheckBox or RadioButton,
            content,
            hasContentLayout,
            contentLayout,
            isLargeButton,
            hasDropdownChevron,
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

    private static Border? FindLargeCommandIconSlot(StackPanel largeStack)
        => RibbonAdaptiveWpfSurface.FindDirectCommandIconSlot(largeStack);

    private static TextBlock? FindLargeCommandLabelBlock(StackPanel largeStack)
        => RibbonAdaptiveWpfSurface.FindDirectCommandLabel(largeStack);

    private static IEnumerable<DependencyObject> EnumerateSelfVisualAndLogicalDescendants(DependencyObject root) =>
        RibbonAdaptiveWpfSurface.EnumerateSelfVisualAndLogicalDescendants(root);

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level) =>
        RibbonAdaptiveStateApplicator.ApplyGroup(CaptureRibbonCompactGroupSnapshot(group), level);

    private static void SetRibbonButtonCompact(ButtonBase button, RibbonCompactLevel level) =>
        RibbonAdaptiveStateApplicator.ApplyButton(CaptureRibbonCompactButtonSnapshot(button), level);

    private static bool IsRibbonButtonLabel(TextBlock textBlock)
        => RibbonAdaptiveWpfSurface.IsRibbonButtonLabel(textBlock);

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
        => RibbonAdaptiveWpfSurface.FindVisualAncestor<T>(element);
}
