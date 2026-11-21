using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>Excel's table-scoped Insert/Delete entries (context menu Insert/Delete, Home Insert/Delete).</summary>
public enum TableStructureAction
{
    InsertRowsAbove,
    InsertRowsBelow,
    InsertColumnsLeft,
    InsertColumnsRight,
    DeleteRows,
    DeleteColumns,
}

public enum TableStructureFailure
{
    None,

    /// <summary>The selection is not inside a structured table.</summary>
    NotInTable,

    /// <summary>The action needs data-body cells (header or totals row selected), or no room to insert there.</summary>
    NeedsBodyCell,
}

public sealed record TableStructurePlan(IWorkbookCommand? Command, TableStructureFailure Failure, string Label)
{
    public bool Succeeded => Command is not null;
}

/// <summary>
/// UI-free planner that turns a selection plus an Excel "Table Rows Above / Table Columns to the Left /
/// Delete Table Rows ..." request into the table-scoped Core command, so both hosts behave identically.
/// Row actions count the data-body rows the selection touches; column actions count the table columns it
/// touches. Rows above/below follow Excel: Above is unavailable on the header row, Below on the totals row.
/// </summary>
public static class TableStructureCommandPlanner
{
    public static bool IsInsertAction(TableStructureAction action) =>
        action is TableStructureAction.InsertRowsAbove
            or TableStructureAction.InsertRowsBelow
            or TableStructureAction.InsertColumnsLeft
            or TableStructureAction.InsertColumnsRight;

    public static string GetLabel(TableStructureAction action) => action switch
    {
        TableStructureAction.InsertRowsAbove or TableStructureAction.InsertRowsBelow => "Insert Table Rows",
        TableStructureAction.InsertColumnsLeft or TableStructureAction.InsertColumnsRight => "Insert Table Columns",
        TableStructureAction.DeleteRows => "Delete Table Rows",
        _ => "Delete Table Columns",
    };

    public static TableStructurePlan Plan(Sheet? sheet, GridRange selection, TableStructureAction action)
    {
        var label = GetLabel(action);
        if (sheet is null ||
            !TableDesignCommandPlanner.TryGetActiveStructuredTable(sheet, selection.Start, out var table))
        {
            return new TableStructurePlan(null, TableStructureFailure.NotInTable, label);
        }

        return action switch
        {
            TableStructureAction.InsertColumnsLeft or TableStructureAction.InsertColumnsRight or TableStructureAction.DeleteColumns =>
                PlanColumns(sheet.Id, table, selection, action, label),
            _ => PlanRows(sheet.Id, table, selection, action, label),
        };
    }

    private static TableStructurePlan PlanRows(
        SheetId sheetId, StructuredTableModel table, GridRange selection, TableStructureAction action, string label)
    {
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var firstData = table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var lastData = table.TotalsRowShown && table.Range.End.Row > table.Range.Start.Row
            ? table.Range.End.Row - 1
            : table.Range.End.Row;
        lastData = Math.Max(firstData, lastData);
        var intersectionStart = Math.Max(selection.Start.Row, firstData);
        var intersectionEnd = Math.Min(selection.End.Row, lastData);
        var touchesBody = intersectionStart <= intersectionEnd;
        var count = touchesBody ? (uint)(intersectionEnd - intersectionStart + 1) : 1u;

        IWorkbookCommand? command = null;
        switch (action)
        {
            case TableStructureAction.InsertRowsAbove:
                if (touchesBody)
                    command = new InsertTableRowsCommand(sheetId, table.Id, (int)(intersectionStart - firstData), count);
                break;
            case TableStructureAction.InsertRowsBelow:
                if (touchesBody)
                    command = new InsertTableRowsCommand(sheetId, table.Id, (int)(intersectionEnd - firstData) + 1, count);
                else if (selection.End.Row < firstData)
                    command = new InsertTableRowsCommand(sheetId, table.Id, 0, count);
                break;
            case TableStructureAction.DeleteRows:
                if (touchesBody)
                    command = new DeleteTableRowsCommand(sheetId, table.Id, (int)(intersectionStart - firstData), count);
                break;
        }

        return command is null
            ? new TableStructurePlan(null, TableStructureFailure.NeedsBodyCell, label)
            : new TableStructurePlan(command, TableStructureFailure.None, label);
    }

    private static TableStructurePlan PlanColumns(
        SheetId sheetId, StructuredTableModel table, GridRange selection, TableStructureAction action, string label)
    {
        var intersectionStart = Math.Max(selection.Start.Col, table.Range.Start.Col);
        var intersectionEnd = Math.Min(selection.End.Col, table.Range.End.Col);
        if (intersectionStart > intersectionEnd)
            return new TableStructurePlan(null, TableStructureFailure.NeedsBodyCell, label);

        var count = intersectionEnd - intersectionStart + 1;
        var firstIndex = (int)(intersectionStart - table.Range.Start.Col);
        IWorkbookCommand command = action switch
        {
            TableStructureAction.InsertColumnsLeft => new InsertTableColumnsCommand(sheetId, table.Id, firstIndex, count),
            TableStructureAction.InsertColumnsRight => new InsertTableColumnsCommand(sheetId, table.Id, firstIndex + (int)count, count),
            _ => new DeleteTableColumnsCommand(sheetId, table.Id, firstIndex, count),
        };
        return new TableStructurePlan(command, TableStructureFailure.None, label);
    }
}
