using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Moves one or more worksheets before a target workbook position.</summary>
// R107: reordering sheets can change which sheets fall inside a 3-D span reference (e.g.
// =SUM(Sheet1:Sheet3!A1)) purely by the position change, not by editing any cell of its own, so
// Apply reports no AffectedCells. The marker makes WorkbookCellEditService force a full recalc
// for forward execution and Undo/Redo alike.
public sealed class MoveSheetsCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly int _insertBeforeIndex;
    private List<SheetId>? _previousOrder;
    private bool _applied;

    public string Label => _sheetIds.Count == 1 ? "Move Sheet" : "Move Sheets";

    public MoveSheetsCommand(IReadOnlyList<SheetId> sheetIds, int insertBeforeIndex)
    {
        _sheetIds = sheetIds;
        _insertBeforeIndex = insertBeforeIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (_insertBeforeIndex < 0 || _insertBeforeIndex > ctx.Workbook.Sheets.Count)
            return new CommandOutcome(false, "Sheet index is out of range.");

        var currentOrder = ctx.Workbook.Sheets.Select(sheet => sheet.Id).ToList();
        var selectedIds = currentOrder.Where(_sheetIds.Contains).ToList();
        if (selectedIds.Count == 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        // R139-workbook-protection: an individually-protected sheet must refuse Move even as part
        // of a grouped multi-sheet move -- mirrors MoveSheetCommand's single-sheet guard, but this
        // command reorders sheets directly rather than delegating to MoveSheetCommand per sheet.
        foreach (var id in selectedIds)
        {
            if (ctx.Workbook.Sheets.FirstOrDefault(sheet => sheet.Id == id) is { } candidate &&
                CommandGuards.RejectIfProtected(candidate) is { } sheetProtectedOutcome)
            {
                return sheetProtectedOutcome;
            }
        }

        _previousOrder = currentOrder;
        var selectedSet = selectedIds.ToHashSet();
        var remaining = currentOrder.Where(id => !selectedSet.Contains(id)).ToList();
        var selectedBeforeTarget = currentOrder
            .Take(Math.Min(_insertBeforeIndex, currentOrder.Count))
            .Count(selectedSet.Contains);
        var targetIndex = Math.Clamp(_insertBeforeIndex - selectedBeforeTarget, 0, remaining.Count);
        var desiredOrder = remaining.ToList();
        desiredOrder.InsertRange(targetIndex, selectedIds);
        if (currentOrder.SequenceEqual(desiredOrder))
            return new CommandOutcome(true, IsNoOp: true);

        ReorderSheets(ctx.Workbook, desiredOrder);
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _previousOrder is null)
            return;

        ReorderSheets(ctx.Workbook, _previousOrder);
        _applied = false;
    }

    private static void ReorderSheets(Workbook workbook, IReadOnlyList<SheetId> desiredOrder)
    {
        for (var targetIndex = 0; targetIndex < desiredOrder.Count; targetIndex++)
        {
            var currentIndex = FindSheetIndex(workbook, desiredOrder[targetIndex]);
            if (currentIndex < 0 || currentIndex == targetIndex)
                continue;

            workbook.MoveSheet(currentIndex, targetIndex);
        }
    }

    private static int FindSheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
            if (workbook.Sheets[index].Id == sheetId)
                return index;

        return -1;
    }
}
