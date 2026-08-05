using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookWorksheetStructureOperation
{
    InsertCellsShiftRight,
    InsertCellsShiftDown,
    InsertRows,
    InsertColumns,
    DeleteCellsShiftLeft,
    DeleteCellsShiftUp,
    DeleteRows,
    DeleteColumns,
}

/// <summary>
/// Portable outcome for a worksheet structure edit. Renderers use the range and viewport deltas
/// only to update native selection chrome, scrollbars, and other transient visuals.
/// </summary>
public sealed record WorkbookWorksheetStructureResult(
    WorkbookCellEditResult EditResult,
    WorkbookWorksheetStructureOperation Operation,
    GridRange TargetRange)
{
    public bool Success => EditResult.Success;

    public string? ErrorMessage => EditResult.ErrorMessage;

    public bool IsNoOp => EditResult.IsNoOp;

    public bool InvalidatesFormulaTraceArrows => Operation is
        WorkbookWorksheetStructureOperation.InsertRows or
        WorkbookWorksheetStructureOperation.InsertColumns or
        WorkbookWorksheetStructureOperation.DeleteRows or
        WorkbookWorksheetStructureOperation.DeleteColumns;

    public int ViewportRowDelta => Operation switch
    {
        WorkbookWorksheetStructureOperation.InsertRows => checked((int)TargetRange.RowCount),
        WorkbookWorksheetStructureOperation.DeleteRows => -checked((int)TargetRange.RowCount),
        _ => 0,
    };

    public int ViewportColumnDelta => Operation switch
    {
        WorkbookWorksheetStructureOperation.InsertColumns => checked((int)TargetRange.ColCount),
        WorkbookWorksheetStructureOperation.DeleteColumns => -checked((int)TargetRange.ColCount),
        _ => 0,
    };

    public string CommandTitle => GetCommandTitle(Operation);

    public static string GetCommandTitle(WorkbookWorksheetStructureOperation operation) => operation switch
    {
        WorkbookWorksheetStructureOperation.InsertCellsShiftRight or
        WorkbookWorksheetStructureOperation.InsertCellsShiftDown => "Insert Cells",
        WorkbookWorksheetStructureOperation.InsertRows => "Insert Row",
        WorkbookWorksheetStructureOperation.InsertColumns => "Insert Column",
        WorkbookWorksheetStructureOperation.DeleteCellsShiftLeft or
        WorkbookWorksheetStructureOperation.DeleteCellsShiftUp => "Delete Cells",
        WorkbookWorksheetStructureOperation.DeleteRows => "Delete Row",
        _ => "Delete Column",
    };
}
