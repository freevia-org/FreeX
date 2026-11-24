using FreeX.App.Presentation.TableUI;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Handles the table-scoped Insert/Delete entries and Convert to Range shared by the cell
    /// context menu. Returns false for other actions so the caller keeps routing them.
    /// </summary>
    private bool TryExecuteTableStructureContextMenuAction(WorksheetContextMenuAction action)
    {
        if (action == WorksheetContextMenuAction.ConvertTableToRange)
        {
            ConvertActiveTableToRange();
            return true;
        }

        if (!WorksheetContextMenuTableActions.TryGetTableStructureAction(action, out var tableAction))
            return false;

        ExecuteTableStructureAction(tableAction);
        return true;
    }

    private void ExecuteTableStructureAction(TableStructureAction action)
    {
        var plan = TableStructureCommandPlanner.Plan(_session.ActiveSheet, _session.SelectedRange, action);
        if (!plan.Succeeded)
        {
            RefreshShell(UiText.Get(plan.Failure == TableStructureFailure.NotInTable
                ? "TableLoc_ShiftNotInTable"
                : "TableLoc_ShiftNeedsBodyCell"));
            return;
        }

        var result = _session.ExecuteReviewCommand(plan.Command!);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? plan.Label);
            return;
        }

        RefreshTableContextualTab();
        RefreshShell(plan.Label);
    }
}
