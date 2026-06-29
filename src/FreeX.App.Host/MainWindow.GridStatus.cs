using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using FreeX.App.Presentation.GridInteraction;
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

    private void RefreshStatusBar()
    {
        var viewMode = GetCurrentStatusBarViewMode();
        if (IsFileOperationProgressVisible())
        {
            SetVisibilityIfChanged(StatusReadyText, Visibility.Collapsed);
            SetVisibilityIfChanged(StatusStatsPanel, Visibility.Collapsed);
            return;
        }

        if (SheetGrid.SelectedRange is not { } range)
        {
            ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
                viewMode,
                zoomPercent: 0));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        viewMode = WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(sheet.ViewMode);

        var stats = _statusBarStatsCache.GetOrCalculate(sheet, range, _navigationCacheRevision);

        if (stats.Count == 0)
        {
            ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
                viewMode,
                zoomPercent: 0,
                StatusBarCalculator.GetReadyStatusText(sheet, range.Start)));
            return;
        }

        ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetStats(
            viewMode,
            zoomPercent: 0,
            StatusBarCalculator.ToShared(stats)));
    }

    private StatusBarViewMode GetCurrentStatusBarViewMode()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return sheet is null
            ? StatusBarViewMode.Normal
            : WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(sheet.ViewMode);
    }

    private bool IsFileOperationProgressVisible() =>
        StatusSaveProgressPanel.Visibility == Visibility.Visible;

    private void ApplyStatusBarDisplayState(Free.Shared.AppServices.StatusBarViewModel state)
    {
        if (_lastStatusBarDisplayState == state && IsStatusBarDisplayStateApplied(state))
            return;

        var plan = BuildStatusBarPresentationPlan(state);
        var visibility = plan.Visibility;
        SetVisibilityIfChanged(StatusReadyText, ToVisibility(visibility.ReadyTextVisible));
        SetVisibilityIfChanged(StatusPageNumberText, ToVisibility(visibility.PageNumberVisible));
        SetVisibilityIfChanged(StatusStatsPanel, ToVisibility(visibility.StatsPanelVisible));
        SetVisibilityIfChanged(StatusAvgText, ToVisibility(visibility.AverageVisible));
        SetVisibilityIfChanged(StatusCountText, ToVisibility(visibility.CountVisible));
        SetVisibilityIfChanged(StatusNumericalCountText, ToVisibility(visibility.NumericalCountVisible));
        SetVisibilityIfChanged(StatusSumText, ToVisibility(visibility.SumVisible));
        SetVisibilityIfChanged(StatusMinText, ToVisibility(visibility.MinimumVisible));
        SetVisibilityIfChanged(StatusMaxText, ToVisibility(visibility.MaximumVisible));
        SetTextIfChanged(StatusReadyText, plan.ReadyText);
        SetStatusStatisticTextIfChanged(StatusAvgText, plan.AverageText, UiText.Get("StatusBar_Average"));
        SetStatusStatisticTextIfChanged(StatusCountText, plan.CountText, UiText.Get("StatusBar_Count"));
        SetStatusStatisticTextIfChanged(StatusNumericalCountText, plan.NumericalCountText, UiText.Get("StatusBar_NumericalCount"));
        SetStatusStatisticTextIfChanged(StatusSumText, plan.SumText, UiText.Get("StatusBar_Sum"));
        SetStatusStatisticTextIfChanged(StatusMinText, plan.MinimumText, UiText.Get("StatusBar_Minimum"));
        SetStatusStatisticTextIfChanged(StatusMaxText, plan.MaximumText, UiText.Get("StatusBar_Maximum"));
        UpdateStatusStatsPanelAutomation(state, plan.AutomationText);
        ApplyStatusBarInteractiveDisplayState(visibility);
        _lastStatusBarDisplayState = state;
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private bool IsStatusBarDisplayStateApplied(Free.Shared.AppServices.StatusBarViewModel state)
    {
        var plan = BuildStatusBarPresentationPlan(state);
        var visibility = plan.Visibility;
        return
            StatusReadyText.Visibility == ToVisibility(visibility.ReadyTextVisible) &&
            StatusPageNumberText.Visibility == ToVisibility(visibility.PageNumberVisible) &&
            StatusStatsPanel.Visibility == ToVisibility(visibility.StatsPanelVisible) &&
            StatusAvgText.Visibility == ToVisibility(visibility.AverageVisible) &&
            StatusCountText.Visibility == ToVisibility(visibility.CountVisible) &&
            StatusNumericalCountText.Visibility == ToVisibility(visibility.NumericalCountVisible) &&
            StatusSumText.Visibility == ToVisibility(visibility.SumVisible) &&
            StatusMinText.Visibility == ToVisibility(visibility.MinimumVisible) &&
            StatusMaxText.Visibility == ToVisibility(visibility.MaximumVisible) &&
            StatusViewShortcutControls.Visibility == ToVisibility(visibility.ViewShortcutsVisible) &&
            StatusZoomText.Visibility == ToVisibility(visibility.ZoomVisible) &&
            StatusZoomSliderControls.Visibility == ToVisibility(visibility.ZoomSliderVisible) &&
            StatusZoomControls.Visibility == ToVisibility(visibility.ZoomControlsVisible) &&
            StatusInteractiveControls.Visibility == ToVisibility(visibility.InteractiveControlsVisible) &&
            StatusReadyText.Text == plan.ReadyText &&
            StatusAvgText.Text == plan.AverageText &&
            StatusCountText.Text == plan.CountText &&
            StatusNumericalCountText.Text == plan.NumericalCountText &&
            StatusSumText.Text == plan.SumText &&
            StatusMinText.Text == plan.MinimumText &&
            StatusMaxText.Text == plan.MaximumText;
    }

    private StatusBarPresentationPlan BuildStatusBarPresentationPlan(Free.Shared.AppServices.StatusBarViewModel state) =>
        StatusBarPresentationPlanner.Build(
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
        ApplyStatusBarInteractiveDisplayState(BuildStatusBarPresentationPlan(state).Visibility);
    }

    private void ApplyStatusBarInteractiveDisplayState(StatusBarVisibilityPlan visibility)
    {
        SetVisibilityIfChanged(StatusViewShortcutControls, ToVisibility(visibility.ViewShortcutsVisible));
        SetVisibilityIfChanged(StatusZoomText, ToVisibility(visibility.ZoomVisible));
        SetVisibilityIfChanged(StatusZoomSliderControls, ToVisibility(visibility.ZoomSliderVisible));
        SetVisibilityIfChanged(StatusZoomControls, ToVisibility(visibility.ZoomControlsVisible));
        SetVisibilityIfChanged(StatusInteractiveControls, ToVisibility(visibility.InteractiveControlsVisible));
    }

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

        var isChecked = menuItem.IsChecked;
        if (!StatusBarOptionVisibilityStore.TrySetOption(_options, option, isChecked))
            return;

        if (!_options.Save())
        {
            ShowOwnedMessage(
                _options.LastPersistenceError ?? "Failed to save status bar customization.",
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
        if (!TryExecuteGroupedSheetCommand(
                "Column Width",
                sheetId => new SetColumnWidthCommand(
                    sheetId,
                    startCol,
                    endCol,
                    ColumnWidthPixelMapper.PixelsToColumnWidth(newWidthPx))))
        {
            if (restoredPreview)
                UpdateViewport();
            return;
        }
        UpdateViewport();
    }

    private void OnColumnAutoFitRequested(uint col)
    {
        var (startCol, endCol) = GetSelectedColRange(col);
        var range = new GridRange(
            new CellAddress(_currentSheetId, 1, startCol),
            new CellAddress(_currentSheetId, CellAddress.MaxRow, endCol));

        if (!TryExecuteGroupedSheetCommand("Auto Column Width", sheetId => CreateAutoFitColumnWidthCommand(sheetId, range)))
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
        var (startRow, endRow) = GetSelectedRowRange(row);
        var range = new GridRange(
            new CellAddress(_currentSheetId, startRow, 1),
            new CellAddress(_currentSheetId, endRow, CellAddress.MaxCol));

        if (!TryExecuteGroupedSheetCommand("Auto Row Height", sheetId => CreateAutoFitRowHeightCommand(sheetId, range)))
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
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, startRow, endRow, newHeightPx)))
        {
            if (restoredPreview)
                UpdateViewport();
            return;
        }
        UpdateViewport();
    }

    private void OnPageMarginsChanged(WorksheetPageMargins margins)
    {
        if (!TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, margins)))
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
