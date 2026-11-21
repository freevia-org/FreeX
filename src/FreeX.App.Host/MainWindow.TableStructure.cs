using System.Windows;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// Handles the table-scoped Insert/Delete entries (Table Rows Above/Below, Table Columns to the
    /// Left/Right, Delete Table Rows/Columns) shared by the cell context menu. Returns false for any
    /// other action so the caller keeps routing it.
    /// </summary>
    private bool TryExecuteTableStructureContextMenuAction(WorksheetContextMenuAction action, CellAddress address)
    {
        if (!WorksheetContextMenuTableActions.TryGetTableStructureAction(action, out var tableAction))
            return false;

        ExecuteTableStructureAction(tableAction, address);
        return true;
    }

    // Home > Cells > Insert / Delete dropdown entries (selection-driven, like the context menu).
    private void TableInsertRowsAboveBtn_Click(object sender, RoutedEventArgs e) =>
        ExecuteTableStructureAction(TableStructureAction.InsertRowsAbove);

    private void TableInsertColumnsLeftBtn_Click(object sender, RoutedEventArgs e) =>
        ExecuteTableStructureAction(TableStructureAction.InsertColumnsLeft);

    private void TableDeleteRowsBtn_Click(object sender, RoutedEventArgs e) =>
        ExecuteTableStructureAction(TableStructureAction.DeleteRows);

    private void TableDeleteColumnsBtn_Click(object sender, RoutedEventArgs e) =>
        ExecuteTableStructureAction(TableStructureAction.DeleteColumns);

    private void ExecuteTableStructureAction(TableStructureAction action) =>
        ExecuteTableStructureAction(action, new CellAddress(_currentSheetId, 1, 1));

    private void ExecuteTableStructureAction(TableStructureAction action, CellAddress fallbackAddress)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selection = SheetGrid.SelectedRange ?? new GridRange(fallbackAddress, fallbackAddress);
        var plan = TableStructureCommandPlanner.Plan(sheet, selection, action);
        if (!plan.Succeeded)
        {
            _messageService.ShowWarning(
                UiText.Get(plan.Failure == TableStructureFailure.NotInTable
                    ? "TableLoc_ShiftNotInTable"
                    : "TableLoc_ShiftNeedsBodyCell"),
                plan.Label);
            return;
        }

        if (!TryExecuteCommand(plan.Command!, plan.Label))
            return;

        RefreshTableContextualTab();
        UpdateViewport();
    }
}
