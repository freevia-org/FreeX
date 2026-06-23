using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Services;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private sealed record ColumnResizeSnapshot(
        SheetId SheetId,
        uint StartCol,
        uint EndCol,
        Dictionary<uint, double> OriginalWidths,
        HashSet<uint> OriginalHiddenCols);

    private sealed record RowResizeSnapshot(
        SheetId SheetId,
        uint StartRow,
        uint EndRow,
        Dictionary<uint, double> OriginalHeights,
        HashSet<uint> OriginalHiddenRows);

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
                zoomPercent: 0,
                UiText.Get("MainWindow_Text_Ready")));
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
                zoomPercent: 0,
                UiText.Get("MainWindow_Text_Ready"));
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
        new(
            CellMode: _options.StatusBarShowCellMode,
            EndMode: _options.StatusBarShowEndMode,
            SelectionMode: _options.StatusBarShowSelectionMode,
            PageNumber: _options.StatusBarShowPageNumber,
            Average: _options.StatusBarShowAverage,
            Count: _options.StatusBarShowCount,
            NumericalCount: _options.StatusBarShowNumericalCount,
            Minimum: _options.StatusBarShowMinimum,
            Maximum: _options.StatusBarShowMaximum,
            Sum: _options.StatusBarShowSum,
            ViewShortcuts: _options.StatusBarShowViewShortcuts,
            Zoom: _options.StatusBarShowZoom,
            ZoomSlider: _options.StatusBarShowZoomSlider);

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
        switch (option)
        {
            case StatusBarOptionTags.CellMode:
                _options.StatusBarShowCellMode = isChecked;
                break;
            case StatusBarOptionTags.EndMode:
                _options.StatusBarShowEndMode = isChecked;
                break;
            case StatusBarOptionTags.SelectionMode:
                _options.StatusBarShowSelectionMode = isChecked;
                break;
            case StatusBarOptionTags.PageNumber:
                _options.StatusBarShowPageNumber = isChecked;
                break;
            case StatusBarOptionTags.Average:
                _options.StatusBarShowAverage = isChecked;
                break;
            case StatusBarOptionTags.Count:
                _options.StatusBarShowCount = isChecked;
                break;
            case StatusBarOptionTags.NumericalCount:
                _options.StatusBarShowNumericalCount = isChecked;
                break;
            case StatusBarOptionTags.Minimum:
                _options.StatusBarShowMinimum = isChecked;
                break;
            case StatusBarOptionTags.Maximum:
                _options.StatusBarShowMaximum = isChecked;
                break;
            case StatusBarOptionTags.Sum:
                _options.StatusBarShowSum = isChecked;
                break;
            case StatusBarOptionTags.ViewShortcuts:
                _options.StatusBarShowViewShortcuts = isChecked;
                break;
            case StatusBarOptionTags.Zoom:
                _options.StatusBarShowZoom = isChecked;
                break;
            case StatusBarOptionTags.ZoomSlider:
                _options.StatusBarShowZoomSlider = isChecked;
                break;
            default:
                return;
        }

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

    private (uint start, uint end) GetSelectedColRange(uint col)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && col >= sel.Value.Start.Col && col <= sel.Value.End.Col
            && sel.Value.Start.Col != sel.Value.End.Col)
            return (sel.Value.Start.Col, sel.Value.End.Col);
        return (col, col);
    }

    private (uint start, uint end) GetColumnResizeRange(Sheet sheet, uint col) =>
        sheet.HiddenCols.Contains(col)
            ? GetContiguousHiddenColumnRange(sheet, col)
            : GetSelectedColRange(col);

    private (uint start, uint end) GetSelectedRowRange(uint row)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && row >= sel.Value.Start.Row && row <= sel.Value.End.Row
            && sel.Value.Start.Row != sel.Value.End.Row)
            return (sel.Value.Start.Row, sel.Value.End.Row);
        return (row, row);
    }

    private (uint start, uint end) GetRowResizeRange(Sheet sheet, uint row) =>
        sheet.HiddenRows.Contains(row)
            ? GetContiguousHiddenRowRange(sheet, row)
            : GetSelectedRowRange(row);

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        CancelPendingViewportResizeRefresh();
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        if (sheet.IsProtected && !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.FormatColumns))
            return;

        var (startCol, endCol) = GetColumnResizeRange(sheet, col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
        ApplyColumnResizePreview(
            sheet,
            startCol,
            endCol,
            ColumnWidthPixelMapper.PixelsToColumnWidth(newWidthPx));
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { } snap && snap.SheetId == sheet.Id
            ? (snap.StartCol, snap.EndCol)
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
        ApplyRowResizePreview(sheet, startRow, endRow, newHeightPx);
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
        var (startRow, endRow) = _rowResizeSnapshot is { } snap && snap.SheetId == sheet.Id
            ? (snap.StartRow, snap.EndRow)
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
        if (_columnResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartCol == startCol && existing.EndCol == endCol)
            return;

        _columnResizeSnapshot = new ColumnResizeSnapshot(
            sheet.Id,
            startCol,
            endCol,
            CaptureDimensionSnapshot(sheet.ColumnWidths, startCol, endCol),
            CaptureIndexSnapshot(sheet.HiddenCols, startCol, endCol));
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
        if (_rowResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartRow == startRow && existing.EndRow == endRow)
            return;

        _rowResizeSnapshot = new RowResizeSnapshot(
            sheet.Id,
            startRow,
            endRow,
            CaptureDimensionSnapshot(sheet.RowHeights, startRow, endRow),
            CaptureIndexSnapshot(sheet.HiddenRows, startRow, endRow));
    }

    private static Dictionary<uint, double> CaptureDimensionSnapshot(
        Dictionary<uint, double> dimensions,
        uint start,
        uint end)
    {
        var snapshot = new Dictionary<uint, double>();
        for (var index = start; index <= end; index++)
        {
            if (dimensions.TryGetValue(index, out var size))
                snapshot[index] = size;
        }

        return snapshot;
    }

    private static HashSet<uint> CaptureIndexSnapshot(HashSet<uint> indexes, uint start, uint end)
    {
        var snapshot = new HashSet<uint>();
        for (var index = start; index <= end; index++)
        {
            if (indexes.Contains(index))
                snapshot.Add(index);
        }

        return snapshot;
    }

    private static void RestoreDimensionSnapshot(
        Dictionary<uint, double> dimensions,
        uint start,
        uint end,
        IReadOnlyDictionary<uint, double> snapshot)
    {
        for (var index = start; index <= end; index++)
            dimensions.Remove(index);

        foreach (var (index, size) in snapshot)
            dimensions[index] = size;
    }

    private static void RestoreIndexSnapshot(HashSet<uint> indexes, uint start, uint end, IReadOnlySet<uint> snapshot)
    {
        for (var index = start; index <= end; index++)
            indexes.Remove(index);

        foreach (var index in snapshot)
            indexes.Add(index);
    }

    private static void ApplyDimensionResizePreview(
        Dictionary<uint, double> dimensions,
        HashSet<uint> hiddenIndexes,
        uint start,
        uint end,
        double size)
    {
        for (var index = start; index <= end; index++)
        {
            if (size == 0)
            {
                dimensions.Remove(index);
                hiddenIndexes.Add(index);
            }
            else
            {
                dimensions[index] = size;
                hiddenIndexes.Remove(index);
            }
        }
    }

    private static void ApplyColumnResizePreview(Sheet sheet, uint startCol, uint endCol, double width) =>
        ApplyDimensionResizePreview(sheet.ColumnWidths, sheet.HiddenCols, startCol, endCol, width);

    private static void ApplyRowResizePreview(Sheet sheet, uint startRow, uint endRow, double height) =>
        ApplyDimensionResizePreview(sheet.RowHeights, sheet.HiddenRows, startRow, endRow, height);

    private bool RestoreColumnResizePreview(Sheet sheet)
    {
        if (_columnResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id)
        {
            _columnResizeSnapshot = null;
            return false;
        }

        RestoreDimensionSnapshot(sheet.ColumnWidths, snapshot.StartCol, snapshot.EndCol, snapshot.OriginalWidths);
        RestoreIndexSnapshot(sheet.HiddenCols, snapshot.StartCol, snapshot.EndCol, snapshot.OriginalHiddenCols);
        _columnResizeSnapshot = null;
        return true;
    }

    private bool RestoreRowResizePreview(Sheet sheet)
    {
        if (_rowResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id)
        {
            _rowResizeSnapshot = null;
            return false;
        }

        RestoreDimensionSnapshot(sheet.RowHeights, snapshot.StartRow, snapshot.EndRow, snapshot.OriginalHeights);
        RestoreIndexSnapshot(sheet.HiddenRows, snapshot.StartRow, snapshot.EndRow, snapshot.OriginalHiddenRows);
        _rowResizeSnapshot = null;
        return true;
    }

    private static (uint start, uint end) GetContiguousHiddenColumnRange(Sheet sheet, uint col)
    {
        var startCol = col;
        while (startCol > 1 && sheet.HiddenCols.Contains(startCol - 1))
            startCol--;

        var endCol = col;
        while (endCol < CellAddress.MaxCol && sheet.HiddenCols.Contains(endCol + 1))
            endCol++;

        return (startCol, endCol);
    }

    private static (uint start, uint end) GetContiguousHiddenRowRange(Sheet sheet, uint row)
    {
        var startRow = row;
        while (startRow > 1 && sheet.HiddenRows.Contains(startRow - 1))
            startRow--;

        var endRow = row;
        while (endRow < CellAddress.MaxRow && sheet.HiddenRows.Contains(endRow + 1))
            endRow++;

        return (startRow, endRow);
    }

}
