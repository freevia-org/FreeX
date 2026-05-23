using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private sealed record ColumnResizeSnapshot(SheetId SheetId, uint StartCol, uint EndCol, Dictionary<uint, (bool Had, double Width)> Widths);
    private sealed record RowResizeSnapshot(SheetId SheetId, uint StartRow, uint EndRow, Dictionary<uint, (bool Had, double Height)> Heights);

    private void RefreshStatusBar()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = "Ready";
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var stats = StatusBarCalculator.Calculate(sheet, range);

        if (stats.Count == 0)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = StatusBarCalculator.GetReadyStatusText(sheet, range.Start);
            return;
        }

        StatusReadyText.Visibility  = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Visible;
        StatusAvgText.Text   = stats.Average.HasValue ? $"Average: {StatusBarCalculator.FormatNumber(stats.Average.Value)}" : "";
        StatusCountText.Text = $"Count: {stats.Count}";
        StatusSumText.Text   = $"Sum: {StatusBarCalculator.FormatNumber(stats.Sum)}";
        StatusMinText.Text   = stats.Min.HasValue ? $"Min: {StatusBarCalculator.FormatNumber(stats.Min.Value)}" : "";
        StatusMaxText.Text   = stats.Max.HasValue ? $"Max: {StatusBarCalculator.FormatNumber(stats.Max.Value)}" : "";
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
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = GetSelectedColRange(col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
        for (uint c = startCol; c <= endCol; c++)
            sheet.ColumnWidths[c] = newWidthPx / 8.0;
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { } snap
            ? (snap.StartCol, snap.EndCol)
            : GetSelectedColRange(col);
        RestoreColumnResizeSnapshot(sheet);
        _columnResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, startCol, endCol, newWidthPx / 8.0)))
            return;
        UpdateViewport();
    }

    private void OnRowResizing(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = GetSelectedRowRange(row);
        CaptureRowResizeSnapshot(sheet, startRow, endRow);
        for (uint r = startRow; r <= endRow; r++)
            sheet.RowHeights[r] = newHeightPx;
        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = _rowResizeSnapshot is { } snap
            ? (snap.StartRow, snap.EndRow)
            : GetSelectedRowRange(row);
        RestoreRowResizeSnapshot(sheet);
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

        var widths = new Dictionary<uint, (bool, double)>();
        for (uint c = startCol; c <= endCol; c++)
        {
            var had = sheet.ColumnWidths.TryGetValue(c, out var w);
            widths[c] = (had, w);
        }
        _columnResizeSnapshot = new ColumnResizeSnapshot(sheet.Id, startCol, endCol, widths);
    }

    private void RestoreColumnResizeSnapshot(Sheet sheet)
    {
        if (_columnResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (c, (had, w)) in snapshot.Widths)
        {
            if (had) sheet.ColumnWidths[c] = w;
            else sheet.ColumnWidths.Remove(c);
        }
    }

    private void CaptureRowResizeSnapshot(Sheet sheet, uint startRow, uint endRow)
    {
        if (_rowResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartRow == startRow && existing.EndRow == endRow)
            return;

        var heights = new Dictionary<uint, (bool, double)>();
        for (uint r = startRow; r <= endRow; r++)
        {
            var had = sheet.RowHeights.TryGetValue(r, out var h);
            heights[r] = (had, h);
        }
        _rowResizeSnapshot = new RowResizeSnapshot(sheet.Id, startRow, endRow, heights);
    }

    private void RestoreRowResizeSnapshot(Sheet sheet)
    {
        if (_rowResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (r, (had, h)) in snapshot.Heights)
        {
            if (had) sheet.RowHeights[r] = h;
            else sheet.RowHeights.Remove(r);
        }
    }

}
