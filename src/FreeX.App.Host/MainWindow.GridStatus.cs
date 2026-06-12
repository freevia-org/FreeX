using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
        if (SheetGrid.SelectedRange is not { } range)
        {
            ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(UiText.Get("MainWindow_Text_Ready")));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var stats = _statusBarStatsCache.GetOrCalculate(sheet, range, _navigationCacheRevision);

        if (stats.Count == 0)
        {
            ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
                StatusBarCalculator.GetReadyStatusText(sheet, range.Start)));
            return;
        }

        ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetStats(stats));
    }

    private void ApplyStatusBarDisplayState(StatusBarDisplayState state)
    {
        if (_lastStatusBarDisplayState == state && IsStatusBarDisplayStateApplied(state))
            return;

        SetVisibilityIfChanged(StatusReadyText, _options.StatusBarShowCellMode ? state.ReadyVisibility : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusPageNumberText, _options.StatusBarShowPageNumber && !string.IsNullOrEmpty(StatusPageNumberText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusStatsPanel, HasVisibleStatusBarStatistic() ? state.StatsVisibility : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusAvgText, _options.StatusBarShowAverage ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusCountText, _options.StatusBarShowCount ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusNumericalCountText, _options.StatusBarShowNumericalCount ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusSumText, _options.StatusBarShowSum ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusMinText, _options.StatusBarShowMinimum ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusMaxText, _options.StatusBarShowMaximum ? Visibility.Visible : Visibility.Collapsed);
        SetTextIfChanged(StatusReadyText, state.ReadyText);
        SetStatusStatisticTextIfChanged(StatusAvgText, state.AverageText, UiText.Get("StatusBar_Average"));
        SetStatusStatisticTextIfChanged(StatusCountText, state.CountText, UiText.Get("StatusBar_Count"));
        SetStatusStatisticTextIfChanged(StatusNumericalCountText, state.NumericalCountText, UiText.Get("StatusBar_NumericalCount"));
        SetStatusStatisticTextIfChanged(StatusSumText, state.SumText, UiText.Get("StatusBar_Sum"));
        SetStatusStatisticTextIfChanged(StatusMinText, state.MinText, UiText.Get("StatusBar_Minimum"));
        SetStatusStatisticTextIfChanged(StatusMaxText, state.MaxText, UiText.Get("StatusBar_Maximum"));
        UpdateStatusStatsPanelAutomation(state);
        ApplyStatusBarInteractiveDisplayState();
        _lastStatusBarDisplayState = state;
    }

    private bool IsStatusBarDisplayStateApplied(StatusBarDisplayState state) =>
        StatusReadyText.Visibility == (_options.StatusBarShowCellMode ? state.ReadyVisibility : Visibility.Collapsed) &&
        StatusPageNumberText.Visibility == (_options.StatusBarShowPageNumber && !string.IsNullOrEmpty(StatusPageNumberText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed) &&
        StatusStatsPanel.Visibility == (HasVisibleStatusBarStatistic() ? state.StatsVisibility : Visibility.Collapsed) &&
        StatusAvgText.Visibility == (_options.StatusBarShowAverage ? Visibility.Visible : Visibility.Collapsed) &&
        StatusCountText.Visibility == (_options.StatusBarShowCount ? Visibility.Visible : Visibility.Collapsed) &&
        StatusNumericalCountText.Visibility == (_options.StatusBarShowNumericalCount ? Visibility.Visible : Visibility.Collapsed) &&
        StatusSumText.Visibility == (_options.StatusBarShowSum ? Visibility.Visible : Visibility.Collapsed) &&
        StatusMinText.Visibility == (_options.StatusBarShowMinimum ? Visibility.Visible : Visibility.Collapsed) &&
        StatusMaxText.Visibility == (_options.StatusBarShowMaximum ? Visibility.Visible : Visibility.Collapsed) &&
        StatusViewShortcutControls.Visibility == (_options.StatusBarShowViewShortcuts ? Visibility.Visible : Visibility.Collapsed) &&
        StatusZoomText.Visibility == (_options.StatusBarShowZoom ? Visibility.Visible : Visibility.Collapsed) &&
        StatusZoomSliderControls.Visibility == (_options.StatusBarShowZoomSlider ? Visibility.Visible : Visibility.Collapsed) &&
        StatusReadyText.Text == state.ReadyText &&
        StatusAvgText.Text == state.AverageText &&
        StatusCountText.Text == state.CountText &&
        StatusNumericalCountText.Text == state.NumericalCountText &&
        StatusSumText.Text == state.SumText &&
        StatusMinText.Text == state.MinText &&
        StatusMaxText.Text == state.MaxText;

    private bool HasVisibleStatusBarStatistic() =>
        _options.StatusBarShowAverage ||
        _options.StatusBarShowCount ||
        _options.StatusBarShowNumericalCount ||
        _options.StatusBarShowSum ||
        _options.StatusBarShowMinimum ||
        _options.StatusBarShowMaximum;

    private void ApplyStatusBarInteractiveDisplayState()
    {
        SetVisibilityIfChanged(StatusViewShortcutControls, _options.StatusBarShowViewShortcuts ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusZoomText, _options.StatusBarShowZoom ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusZoomSliderControls, _options.StatusBarShowZoomSlider ? Visibility.Visible : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusZoomControls, _options.StatusBarShowZoom || _options.StatusBarShowZoomSlider
            ? Visibility.Visible
            : Visibility.Collapsed);
        SetVisibilityIfChanged(StatusInteractiveControls, _options.StatusBarShowViewShortcuts ||
            _options.StatusBarShowZoom ||
            _options.StatusBarShowZoomSlider
                ? Visibility.Visible
                : Visibility.Collapsed);
    }

    private void StatusBarCustomizeMenu_Opened(object sender, RoutedEventArgs e)
    {
        StatusBarCellModeMenuItem.IsChecked = _options.StatusBarShowCellMode;
        StatusBarEndModeMenuItem.IsChecked = _options.StatusBarShowEndMode;
        StatusBarSelectionModeMenuItem.IsChecked = _options.StatusBarShowSelectionMode;
        StatusBarPageNumberMenuItem.IsChecked = _options.StatusBarShowPageNumber;
        StatusBarAverageMenuItem.IsChecked = _options.StatusBarShowAverage;
        StatusBarCountMenuItem.IsChecked = _options.StatusBarShowCount;
        StatusBarNumericalCountMenuItem.IsChecked = _options.StatusBarShowNumericalCount;
        StatusBarMinimumMenuItem.IsChecked = _options.StatusBarShowMinimum;
        StatusBarMaximumMenuItem.IsChecked = _options.StatusBarShowMaximum;
        StatusBarSumMenuItem.IsChecked = _options.StatusBarShowSum;
        StatusBarViewShortcutsMenuItem.IsChecked = _options.StatusBarShowViewShortcuts;
        StatusBarZoomMenuItem.IsChecked = _options.StatusBarShowZoom;
        StatusBarZoomSliderMenuItem.IsChecked = _options.StatusBarShowZoomSlider;
    }

    private void StatusBarCustomizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string option)
            return;

        var isChecked = menuItem.IsChecked;
        switch (option)
        {
            case "CellMode":
                _options.StatusBarShowCellMode = isChecked;
                break;
            case "EndMode":
                _options.StatusBarShowEndMode = isChecked;
                break;
            case "SelectionMode":
                _options.StatusBarShowSelectionMode = isChecked;
                break;
            case "PageNumber":
                _options.StatusBarShowPageNumber = isChecked;
                break;
            case "Average":
                _options.StatusBarShowAverage = isChecked;
                break;
            case "Count":
                _options.StatusBarShowCount = isChecked;
                break;
            case "NumericalCount":
                _options.StatusBarShowNumericalCount = isChecked;
                break;
            case "Minimum":
                _options.StatusBarShowMinimum = isChecked;
                break;
            case "Maximum":
                _options.StatusBarShowMaximum = isChecked;
                break;
            case "Sum":
                _options.StatusBarShowSum = isChecked;
                break;
            case "ViewShortcuts":
                _options.StatusBarShowViewShortcuts = isChecked;
                break;
            case "Zoom":
                _options.StatusBarShowZoom = isChecked;
                break;
            case "ZoomSlider":
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

    private void UpdateStatusStatsPanelAutomation(StatusBarDisplayState state)
    {
        var visibleStatTexts = new[]
            {
                _options.StatusBarShowAverage ? state.AverageText : string.Empty,
                _options.StatusBarShowCount ? state.CountText : string.Empty,
                _options.StatusBarShowNumericalCount ? state.NumericalCountText : string.Empty,
                _options.StatusBarShowSum ? state.SumText : string.Empty,
                _options.StatusBarShowMinimum ? state.MinText : string.Empty,
                _options.StatusBarShowMaximum ? state.MaxText : string.Empty
            }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var automationName = visibleStatTexts.Length == 0
            ? UiText.Get("StatusBar_CustomizeStatusBar")
            : string.Join("; ", visibleStatTexts);
        var previousAutomationName = AutomationProperties.GetName(StatusStatsPanel);
        if (!string.Equals(previousAutomationName, automationName, StringComparison.Ordinal))
        {
            AutomationProperties.SetName(StatusStatsPanel, automationName);
            NotifyStatusStatsPanelAutomationChanged(previousAutomationName, automationName);
        }

        if (!string.Equals(AutomationProperties.GetHelpText(StatusStatsPanel), automationName, StringComparison.Ordinal))
            AutomationProperties.SetHelpText(StatusStatsPanel, automationName);
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
