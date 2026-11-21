using FreeX.App.Presentation.TableUI;

namespace FreeX.App.Services.Ribbon;

/// <summary>Maps the cell context menu's table-scoped Insert/Delete entries onto the shared table planner.</summary>
public static class WorksheetContextMenuTableActions
{
    public static bool TryGetTableStructureAction(
        WorksheetContextMenuAction action,
        out TableStructureAction tableAction)
    {
        switch (action)
        {
            case WorksheetContextMenuAction.InsertTableRowAbove:
                tableAction = TableStructureAction.InsertRowsAbove;
                return true;
            case WorksheetContextMenuAction.InsertTableRowBelow:
                tableAction = TableStructureAction.InsertRowsBelow;
                return true;
            case WorksheetContextMenuAction.InsertTableColumnLeft:
                tableAction = TableStructureAction.InsertColumnsLeft;
                return true;
            case WorksheetContextMenuAction.InsertTableColumnRight:
                tableAction = TableStructureAction.InsertColumnsRight;
                return true;
            case WorksheetContextMenuAction.DeleteTableRows:
                tableAction = TableStructureAction.DeleteRows;
                return true;
            case WorksheetContextMenuAction.DeleteTableColumns:
                tableAction = TableStructureAction.DeleteColumns;
                return true;
            default:
                tableAction = default;
                return false;
        }
    }
}
