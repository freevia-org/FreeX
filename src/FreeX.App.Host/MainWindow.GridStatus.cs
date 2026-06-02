using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private sealed record ColumnResizeSnapshot(SheetId SheetId, uint StartCol, uint EndCol);
    private sealed record RowResizeSnapshot(SheetId SheetId, uint StartRow, uint EndRow);

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

        SetVisibilityIfChanged(StatusReadyText, state.ReadyVisibility);
        SetVisibilityIfChanged(StatusStatsPanel, state.StatsVisibility);
        SetTextIfChanged(StatusReadyText, state.ReadyText);
        SetTextIfChanged(StatusAvgText, state.AverageText);
        SetTextIfChanged(StatusCountText, state.CountText);
        SetTextIfChanged(StatusNumericalCountText, state.NumericalCountText);
        SetTextIfChanged(StatusSumText, state.SumText);
        SetTextIfChanged(StatusMinText, state.MinText);
        SetTextIfChanged(StatusMaxText, state.MaxText);
        _lastStatusBarDisplayState = state;
    }

    private bool IsStatusBarDisplayStateApplied(StatusBarDisplayState state) =>
        StatusReadyText.Visibility == state.ReadyVisibility &&
        StatusStatsPanel.Visibility == state.StatsVisibility &&
        StatusReadyText.Text == state.ReadyText &&
        StatusAvgText.Text == state.AverageText &&
        StatusCountText.Text == state.CountText &&
        StatusNumericalCountText.Text == state.NumericalCountText &&
        StatusSumText.Text == state.SumText &&
        StatusMinText.Text == state.MinText &&
        StatusMaxText.Text == state.MaxText;

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

    private (uint start, uint end) GetSelectedColRange(uint col)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && col >= sel.Value.Start.Col && col <= sel.Value.End.Col
            && sel.Value.Start.Col != sel.Value.End.Col)
            return (sel.Value.Start.Col, sel.Value.End.Col);
        return (col, col);
    }

    private (uint start, uint end) GetSelectedRowRange(uint row)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && row >= sel.Value.Start.Row && row <= sel.Value.End.Row
            && sel.Value.Start.Row != sel.Value.End.Row)
            return (sel.Value.Start.Row, sel.Value.End.Row);
        return (row, row);
    }

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        CancelPendingViewportResizeRefresh();
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = GetSelectedColRange(col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { } snap && snap.SheetId == sheet.Id
            ? (snap.StartCol, snap.EndCol)
            : GetSelectedColRange(col);
        _columnResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, startCol, endCol, newWidthPx / 8.0)))
            return;
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
        var (startRow, endRow) = GetSelectedRowRange(row);
        CaptureRowResizeSnapshot(sheet, startRow, endRow);
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
            : GetSelectedRowRange(row);
        _rowResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, startRow, endRow, newHeightPx)))
            return;
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

        _columnResizeSnapshot = new ColumnResizeSnapshot(sheet.Id, startCol, endCol);
    }

    private void OnResizeCanceled()
    {
        _columnResizeSnapshot = null;
        _rowResizeSnapshot = null;
    }

    private void CaptureRowResizeSnapshot(Sheet sheet, uint startRow, uint endRow)
    {
        if (_rowResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartRow == startRow && existing.EndRow == endRow)
            return;

        _rowResizeSnapshot = new RowResizeSnapshot(sheet.Id, startRow, endRow);
    }

}
