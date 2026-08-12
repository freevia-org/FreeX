using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Services;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void InvalidateNavigationCaches()
    {
        _navigationCacheRevision++;
        _statusBarStatsCache.Clear();
        _sparklineValueCache.Clear();
    }

    private void RefreshKeyLockIndicators()
    {
        var plan = KeyLockIndicatorPlanner.Build(
            System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock),
            System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.NumLock));

        SetVisibilityIfChanged(StatusCapsLockText, ToVisibility(plan.CapsLockVisible));
        SetVisibilityIfChanged(StatusNumLockText, ToVisibility(plan.NumLockVisible));
    }

    private void RefreshStatusBar()
    {
        RefreshKeyLockIndicators();

        var sheet = _workbook.GetSheet(_currentSheetId);
        var selectedRange = SheetGrid.SelectedRange;
        var selectedRanges = SheetGrid.SelectedRanges;
        WorkbookSelectionStats? stats = sheet is null
            ? null
            : selectedRanges is { Count: > 0 }
                ? _statusBarStatsCache.GetOrCalculate(sheet, selectedRanges, _navigationCacheRevision)
                : selectedRange is { } range
                    ? _statusBarStatsCache.GetOrCalculate(sheet, range, _navigationCacheRevision)
                    : null;

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            selectedRange,
            stats,
            IsFileOperationProgressVisible(),
            zoomPercent: 0,
            WpfResourceKeyTextResolver.StatusBarTextProvider,
            sheet is null ? null : GetEffectiveViewState(sheet).ViewMode,
            isManualCalculationMode: _workbook.CalculationMode == WorkbookCalculationMode.Manual,
            hasPendingRecalculation: _workbook.HasPendingManualRecalculation);
        ApplyStatusBarRefreshPlan(plan);
    }

    private void ApplyStatusBarRefreshPlan(StatusBarRefreshPlan plan)
    {
        switch (plan.Action)
        {
            case StatusBarRefreshAction.HideReadouts:
                SetVisibilityIfChanged(StatusReadyText, Visibility.Collapsed);
                SetVisibilityIfChanged(StatusStatsPanel, Visibility.Collapsed);
                return;
            case StatusBarRefreshAction.Ready:
                ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
                    plan.ViewMode,
                    plan.ZoomPercent,
                    plan.ReadyText));
                return;
            case StatusBarRefreshAction.Stats:
                ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetStats(
                    plan.ViewMode,
                    plan.ZoomPercent,
                    plan.Stats));
                return;
            default:
                ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
                    plan.ViewMode,
                    plan.ZoomPercent));
                return;
        }
    }

    private StatusBarViewMode GetCurrentStatusBarViewMode()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return sheet is null
            ? StatusBarViewMode.Normal
            : WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(GetEffectiveViewState(sheet).ViewMode);
    }

    private bool IsFileOperationProgressVisible() =>
        StatusSaveProgressPanel.Visibility == Visibility.Visible;

    private void ApplyStatusBarDisplayState(Free.Shared.AppServices.StatusBarViewModel state)
    {
        if (_lastStatusBarDisplayState == state && IsStatusBarDisplayStateApplied(state))
            return;

        var rendererPlan = BuildStatusBarRendererPlan(state);
        ApplyStatusBarRendererPlan(state, rendererPlan);
        _lastStatusBarDisplayState = state;
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private bool IsStatusBarDisplayStateApplied(Free.Shared.AppServices.StatusBarViewModel state)
    {
        return IsStatusBarRendererPlanApplied(BuildStatusBarRendererPlan(state));
    }

    private StatusBarRendererPlan BuildStatusBarRendererPlan(Free.Shared.AppServices.StatusBarViewModel state) =>
        FreeXStatusBarRendererPlanner.BuildRendererPlan(
            state,
            GetStatusBarOptionVisibility(),
            hasPageNumberText: !string.IsNullOrEmpty(StatusPageNumberText.Text),
            fallbackAutomationText: UiText.Get("StatusBar_CustomizeStatusBar"));

    private void ApplyStatusBarInteractiveDisplayState()
    {
        var state = _lastStatusBarDisplayState ??
            _statusBarDisplayStateCache.GetReady(
                GetCurrentStatusBarViewMode(),
                zoomPercent: 0);
        ApplyStatusBarInteractiveDisplayState(BuildStatusBarRendererPlan(state));
    }

    private void ApplyStatusBarInteractiveDisplayState(StatusBarRendererPlan rendererPlan)
    {
        SetVisibilityIfChanged(
            StatusViewShortcutControls,
            ToVisibility(rendererPlan.IsElementVisible(StatusBarPresentationElement.ViewShortcuts)));
        SetVisibilityIfChanged(
            StatusZoomText,
            ToVisibility(rendererPlan.IsElementVisible(StatusBarPresentationElement.ZoomText)));
        SetVisibilityIfChanged(
            StatusZoomSliderControls,
            ToVisibility(rendererPlan.IsElementVisible(StatusBarPresentationElement.ZoomSlider)));
        SetVisibilityIfChanged(
            StatusZoomControls,
            ToVisibility(rendererPlan.IsElementVisible(StatusBarPresentationElement.ZoomControls)));
        SetVisibilityIfChanged(
            StatusInteractiveControls,
            ToVisibility(rendererPlan.IsElementVisible(StatusBarPresentationElement.InteractiveControls)));
    }

    private void ApplyStatusBarRendererPlan(
        Free.Shared.AppServices.StatusBarViewModel state,
        StatusBarRendererPlan rendererPlan)
    {
        foreach (var entry in rendererPlan.VisibilityElements)
            SetVisibilityIfChanged(GetStatusBarElement(entry.Element), ToVisibility(entry.IsVisible));

        SetTextIfChanged(StatusReadyText, rendererPlan.ReadyText);
        foreach (var readout in rendererPlan.ReadoutElements)
        {
            SetStatusStatisticTextIfChanged(
                GetStatusBarReadoutTextBlock(readout.Kind),
                readout.Text,
                UiText.Get(readout.AutomationFallbackResourceKey));
        }

        UpdateStatusStatsPanelAutomation(state, rendererPlan.StatsPanelAutomationText);
    }

    private bool IsStatusBarRendererPlanApplied(StatusBarRendererPlan rendererPlan)
    {
        foreach (var entry in rendererPlan.VisibilityElements)
        {
            if (GetStatusBarElement(entry.Element).Visibility != ToVisibility(entry.IsVisible))
                return false;
        }

        if (StatusReadyText.Text != rendererPlan.ReadyText)
            return false;

        foreach (var readout in rendererPlan.ReadoutElements)
        {
            if (GetStatusBarReadoutTextBlock(readout.Kind).Text != readout.Text)
                return false;
        }

        return true;
    }

    private UIElement GetStatusBarElement(StatusBarPresentationElement element) =>
        element switch
        {
            StatusBarPresentationElement.ReadyText => StatusReadyText,
            StatusBarPresentationElement.PageNumberText => StatusPageNumberText,
            StatusBarPresentationElement.StatsPanel => StatusStatsPanel,
            StatusBarPresentationElement.Average => StatusAvgText,
            StatusBarPresentationElement.Count => StatusCountText,
            StatusBarPresentationElement.NumericalCount => StatusNumericalCountText,
            StatusBarPresentationElement.Sum => StatusSumText,
            StatusBarPresentationElement.Minimum => StatusMinText,
            StatusBarPresentationElement.Maximum => StatusMaxText,
            StatusBarPresentationElement.ViewShortcuts => StatusViewShortcutControls,
            StatusBarPresentationElement.ZoomText => StatusZoomText,
            StatusBarPresentationElement.ZoomSlider => StatusZoomSliderControls,
            StatusBarPresentationElement.ZoomControls => StatusZoomControls,
            StatusBarPresentationElement.InteractiveControls => StatusInteractiveControls,
            _ => StatusReadyText
        };

    private TextBlock GetStatusBarReadoutTextBlock(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => StatusAvgText,
            StatusBarReadoutKind.Count => StatusCountText,
            StatusBarReadoutKind.NumericalCount => StatusNumericalCountText,
            StatusBarReadoutKind.Sum => StatusSumText,
            StatusBarReadoutKind.Minimum => StatusMinText,
            StatusBarReadoutKind.Maximum => StatusMaxText,
            _ => StatusCountText
        };

    private StatusBarOptionVisibility GetStatusBarOptionVisibility() =>
        StatusBarOptionVisibilityStore.ToVisibility(_options);

    // Tracks the runtime-built status-bar customize toggle items by their persisted-option Tag so the menu's
    // live checked state can be refreshed on open without relying on hand-authored x:Name fields.
    private readonly Dictionary<string, MenuItem> _statusBarCustomizeMenuItems = new(StringComparer.Ordinal);

    private void RegisterStatusBarCustomizeMenuItem(string optionTag, MenuItem menuItem)
    {
        _statusBarCustomizeMenuItems[optionTag] = menuItem;
    }

    private void StatusBarCustomizeMenu_Opened(object sender, RoutedEventArgs e)
    {
        foreach (var (optionTag, menuItem) in _statusBarCustomizeMenuItems)
            menuItem.IsChecked = GetStatusBarCustomizeOption(optionTag);
    }

    private bool GetStatusBarCustomizeOption(string optionTag) =>
        GetStatusBarOptionVisibility().IsVisible(optionTag);

    private void StatusBarCustomizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string option)
            return;

        var result = StatusBarOptionUpdateWorkflow.ApplyToRuntimeSession(
            _optionsRuntimeSession,
            option,
            menuItem.IsChecked);
        _options = _optionsRuntimeSession.LiveOptions;
        if (!result.IsRecognized)
            return;

        if (!result.IsPersisted)
        {
            ShowOwnedMessage(
                result.PersistenceError ?? UiText.Get("StatusBar_CustomizationSaveFailed"),
                UiText.Get("StatusBar_CustomizeStatusBar"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _lastStatusBarDisplayState = null;
        _lastStatusBarAutomationState = null;
        RefreshStatusBar();
    }

    private static void SetVisibilityIfChanged(UIElement element, Visibility visibility)
    {
        if (element.Visibility != visibility)
            element.Visibility = visibility;
    }

    private static void SetTextIfChanged(TextBlock textBlock, string text)
    {
        if (textBlock.Text != text)
            textBlock.Text = text;
    }

    private static void SetStatusStatisticTextIfChanged(TextBlock textBlock, string text, string fallbackAutomationName)
    {
        SetTextIfChanged(textBlock, text);

        var automationName = string.IsNullOrWhiteSpace(text)
            ? fallbackAutomationName
            : text;
        var previousAutomationName = AutomationProperties.GetName(textBlock);
        if (!string.Equals(previousAutomationName, automationName, StringComparison.Ordinal))
        {
            AutomationProperties.SetName(textBlock, automationName);
            NotifyStatusStatisticAutomationChanged(textBlock, previousAutomationName, automationName);
        }

        if (!string.Equals(AutomationProperties.GetHelpText(textBlock), automationName, StringComparison.Ordinal))
            AutomationProperties.SetHelpText(textBlock, automationName);
    }

    private void UpdateStatusStatsPanelAutomation(
        Free.Shared.AppServices.StatusBarViewModel state,
        string automationName)
    {
        if (_lastStatusBarAutomationName is { } cachedName &&
            _lastStatusBarAutomationState == state &&
            string.Equals(cachedName, automationName, StringComparison.Ordinal))
        {
            var currentName = AutomationProperties.GetName(StatusStatsPanel);
            if (!string.Equals(currentName, cachedName, StringComparison.Ordinal))
            {
                AutomationProperties.SetName(StatusStatsPanel, cachedName);
                NotifyStatusStatsPanelAutomationChanged(currentName, cachedName);
            }

            if (!string.Equals(AutomationProperties.GetHelpText(StatusStatsPanel), cachedName, StringComparison.Ordinal))
                AutomationProperties.SetHelpText(StatusStatsPanel, cachedName);
            return;
        }

        var previousAutomationName = AutomationProperties.GetName(StatusStatsPanel);
        if (!string.Equals(previousAutomationName, automationName, StringComparison.Ordinal))
        {
            AutomationProperties.SetName(StatusStatsPanel, automationName);
            NotifyStatusStatsPanelAutomationChanged(previousAutomationName, automationName);
        }

        if (!string.Equals(AutomationProperties.GetHelpText(StatusStatsPanel), automationName, StringComparison.Ordinal))
            AutomationProperties.SetHelpText(StatusStatsPanel, automationName);

        _lastStatusBarAutomationState = state;
        _lastStatusBarAutomationName = automationName;
    }

    private void NotifyStatusStatsPanelAutomationChanged(string previousAutomationName, string automationName)
    {
        if (!StatusStatsPanel.IsLoaded)
            return;

        try
        {
            var peer = UIElementAutomationPeer.FromElement(StatusStatsPanel) ??
                       UIElementAutomationPeer.CreatePeerForElement(StatusStatsPanel);
            peer?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty,
                previousAutomationName,
                automationName);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void NotifyStatusStatisticAutomationChanged(
        TextBlock textBlock,
        string previousAutomationName,
        string automationName)
    {
        if (!textBlock.IsLoaded)
            return;

        try
        {
            var peer = UIElementAutomationPeer.FromElement(textBlock) ??
                       UIElementAutomationPeer.CreatePeerForElement(textBlock);
            peer?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty,
                previousAutomationName,
                automationName);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private (uint start, uint end) GetSelectedColRange(uint col) =>
        GridResizePreviewPlanner.GetSelectedColumnResizeRange(SheetGrid.SelectedRange, col);

    private (uint start, uint end) GetColumnResizeRange(Sheet sheet, uint col) =>
        GridResizePreviewPlanner.GetColumnResizeRange(sheet, SheetGrid.SelectedRange, col);

    private (uint start, uint end) GetSelectedRowRange(uint row) =>
        GridResizePreviewPlanner.GetSelectedRowResizeRange(SheetGrid.SelectedRange, row);

    private (uint start, uint end) GetRowResizeRange(Sheet sheet, uint row) =>
        GridResizePreviewPlanner.GetRowResizeRange(sheet, SheetGrid.SelectedRange, row);

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        CancelPendingViewportResizeRefresh();
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        if (sheet.IsProtected && !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.FormatColumns))
            return;

        var (startCol, endCol) = GetColumnResizeRange(sheet, col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
        GridResizePreviewPlanner.ApplyColumnResizePreview(sheet, startCol, endCol, newWidthPx);
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { SheetId: var sheetId } snap && sheetId == sheet.Id
            ? (snap.StartIndex, snap.EndIndex)
            : GetColumnResizeRange(sheet, col);
        var restoredPreview = RestoreColumnResizePreview(sheet);
        if (!TryExecuteWorksheetLayout(
                () => _session.SetColumnsWidthPixels(startCol, endCol, newWidthPx),
                "Column Width"))
        {
            if (restoredPreview)
                UpdateViewport();
            return;
        }
        UpdateViewport();
    }

    private void OnColumnAutoFitRequested(uint col)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var (startCol, endCol) = GetColumnResizeRange(sheet, col);
        if (!TryExecuteWorksheetLayout(
                () => _session.AutoFitColumns(startCol, endCol),
                "Auto Column Width"))
            return;

        UpdateViewport();
    }

    private void OnRowResizing(uint row, double newHeightPx)
    {
        CancelPendingViewportResizeRefresh();
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        if (sheet.IsProtected && !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.FormatRows))
            return;

        var (startRow, endRow) = GetRowResizeRange(sheet, row);
        CaptureRowResizeSnapshot(sheet, startRow, endRow);
        GridResizePreviewPlanner.ApplyRowResizePreview(sheet, startRow, endRow, newHeightPx);
        UpdateViewport();
    }

    private void OnRowAutoFitRequested(uint row)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var (startRow, endRow) = GetRowResizeRange(sheet, row);
        if (!TryExecuteWorksheetLayout(
                () => _session.AutoFitRows(startRow, endRow),
                "Auto Row Height"))
            return;

        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = _rowResizeSnapshot is { SheetId: var sheetId } snap && sheetId == sheet.Id
            ? (snap.StartIndex, snap.EndIndex)
            : GetRowResizeRange(sheet, row);
        var restoredPreview = RestoreRowResizePreview(sheet);
        if (!TryExecuteWorksheetLayout(
                () => _session.SetRowsHeightPixels(startRow, endRow, newHeightPx),
                "Row Height"))
        {
            if (restoredPreview)
                UpdateViewport();
            return;
        }
        UpdateViewport();
    }

    private void OnPageMarginsChanged(WorksheetPageMargins margins)
    {
        if (!TryExecuteGroupedSheetCommand(
                PageLayoutRibbonActionPlanner.PageMarginsCommandLabel,
                sheetId => PageLayoutRibbonCommandPlanner.BuildMarginsCommand(sheetId, margins)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void CaptureColumnResizeSnapshot(Sheet sheet, uint startCol, uint endCol)
    {
        if (GridResizePreviewPlanner.SnapshotMatches(
                _columnResizeSnapshot,
                sheet,
                GridResizeAxis.Column,
                startCol,
                endCol))
        {
            return;
        }

        _columnResizeSnapshot = GridResizePreviewPlanner.CaptureColumnSnapshot(sheet, startCol, endCol);
    }

    private void OnResizeCanceled()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var restored = false;
        if (sheet is not null)
        {
            restored |= RestoreColumnResizePreview(sheet);
            restored |= RestoreRowResizePreview(sheet);
        }
        else
        {
            _columnResizeSnapshot = null;
            _rowResizeSnapshot = null;
        }

        if (restored)
            UpdateViewport();
    }

    private void CaptureRowResizeSnapshot(Sheet sheet, uint startRow, uint endRow)
    {
        if (GridResizePreviewPlanner.SnapshotMatches(
                _rowResizeSnapshot,
                sheet,
                GridResizeAxis.Row,
                startRow,
                endRow))
        {
            return;
        }

        _rowResizeSnapshot = GridResizePreviewPlanner.CaptureRowSnapshot(sheet, startRow, endRow);
    }

    private bool RestoreColumnResizePreview(Sheet sheet)
    {
        var restored = GridResizePreviewPlanner.RestoreColumnResizePreview(sheet, _columnResizeSnapshot);
        _columnResizeSnapshot = null;
        return restored;
    }

    private bool RestoreRowResizePreview(Sheet sheet)
    {
        var restored = GridResizePreviewPlanner.RestoreRowResizePreview(sheet, _rowResizeSnapshot);
        _rowResizeSnapshot = null;
        return restored;
    }

}
