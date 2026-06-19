using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.Ribbon.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Hide / Unhide rows and columns (Excel parity: Ctrl+9 / Ctrl+Shift+9 / Ctrl+0 / Ctrl+Shift+0,
    // Home ▸ Cells ▸ Format ▸ Hide & Unhide, and the row/column header right-click menus). The
    // selection→span math and the undoable mutation are fully portable: span extraction lives in
    // FreeX.Core.Commands.SelectionRangeService and the edit runs through the shared
    // Set{Rows,Columns}HiddenCommand, executed via WorkbookSession.ExecuteReviewCommand (undo/redo).

    /// <summary>
    /// Matches the Windows host's hide/unhide grid shortcuts and runs the corresponding command:
    /// Ctrl+9 hides rows, Ctrl+Shift+9 unhides rows, Ctrl+0 hides columns, Ctrl+Shift+0 unhides
    /// columns. Uses the Control modifier specifically (not Meta) so Cmd+0 "Zoom 100%" is untouched.
    /// </summary>
    private bool TryHandleRowColumnVisibilityShortcut(KeyEventArgs e)
    {
        if (_formulaBox.IsFocused)
            return false;

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var otherModifiers = (e.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift)) != 0;
        if (!control || otherModifiers)
            return false;

        if (e.Key is Key.D9 or Key.NumPad9)
        {
            if (shift)
                UnhideSelectedRows();
            else
                HideSelectedRows();
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.D0 or Key.NumPad0)
        {
            if (shift)
                UnhideSelectedColumns();
            else
                HideSelectedColumns();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void HideSelectedRows() => SetSelectedRowsHidden(hidden: true);

    private void UnhideSelectedRows() => SetSelectedRowsHidden(hidden: false);

    private void HideSelectedColumns() => SetSelectedColumnsHidden(hidden: true);

    private void UnhideSelectedColumns() => SetSelectedColumnsHidden(hidden: false);

    private void SetSelectedRowsHidden(bool hidden)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        var result = _session.ExecuteReviewCommand(
            new SetRowsHiddenCommand(_session.ActiveSheet.Id, startRow, endRow, hidden));
        if (result.Success)
            RefreshShell(hidden
                ? UiText.Format("RowColumn_RowsHidden", endRow - startRow + 1)
                : UiText.Get("RowColumn_RowsUnhidden"));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get(hidden ? "RowColumn_HideRowsFailed" : "RowColumn_UnhideRowsFailed"));
    }

    private void SetSelectedColumnsHidden(bool hidden)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        var result = _session.ExecuteReviewCommand(
            new SetColumnsHiddenCommand(_session.ActiveSheet.Id, startCol, endCol, hidden));
        if (result.Success)
            RefreshShell(hidden
                ? UiText.Format("RowColumn_ColumnsHidden", endCol - startCol + 1)
                : UiText.Get("RowColumn_ColumnsUnhidden"));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get(hidden ? "RowColumn_HideColumnsFailed" : "RowColumn_UnhideColumnsFailed"));
    }

    /// <summary>
    /// Selects an entire row (every column) so subsequent row commands (hide/clear/etc.) target it.
    /// Extends the existing selection when <paramref name="extend"/> is set (Shift-click on a header).
    /// </summary>
    private void SelectEntireRow(uint row, bool extend = false)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var anchorRow = extend ? _session.ActiveCell.Row : row;
        var sheet = _session.ActiveSheet.Id;
        var range = SelectionRangeService.GetWholeRows(
            new GridRange(new CellAddress(sheet, anchorRow, 1), new CellAddress(sheet, row, 1)));
        _session.SelectRange(range);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Selects an entire column (every row) so subsequent column commands target it. Extends the
    /// existing selection when <paramref name="extend"/> is set (Shift-click on a header).
    /// </summary>
    private void SelectEntireColumn(uint col, bool extend = false)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var anchorCol = extend ? _session.ActiveCell.Col : col;
        var sheet = _session.ActiveSheet.Id;
        var range = SelectionRangeService.GetWholeColumns(
            new GridRange(new CellAddress(sheet, 1, anchorCol), new CellAddress(sheet, 1, col)));
        _session.SelectRange(range);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Opens the row-header context menu (Hide/Unhide Rows, Row Height, Insert/Delete, etc.), built
    /// from the shared neutral <see cref="WorksheetContextMenuPlanner"/> RowSelection plan — the same
    /// plan the Windows host renders.
    /// </summary>
    private void OpenRowHeaderContextMenu(Control anchor)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.RowSelection);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, DispatchWorksheetContextMenuCommand).Open(anchor);
    }

    /// <summary>
    /// Opens the column-header context menu (Hide/Unhide Columns, Column Width, Insert/Delete, etc.),
    /// built from the shared neutral ColumnSelection plan.
    /// </summary>
    private void OpenColumnHeaderContextMenu(Control anchor)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.ColumnSelection);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, DispatchWorksheetContextMenuCommand).Open(anchor);
    }
}
