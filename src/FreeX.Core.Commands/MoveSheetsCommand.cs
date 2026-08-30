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

        RelocateToDesiredOrder(ctx.Workbook, desiredOrder);
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _previousOrder is null)
            return;

        // Undo simply restores the order Apply recorded before it ran.
        RelocateToDesiredOrder(ctx.Workbook, _previousOrder);
        _applied = false;
    }

    /// <summary>
    /// Reorders the workbook's sheets to <paramref name="desiredOrder"/> using the fewest moves the
    /// simple placement rule needs, without rescanning the whole workbook per sheet.
    ///
    /// <para>r173 remediation. The first version of this tracked each selected sheet's shifting
    /// index and only fixed up entries it had not yet processed, on the assumption that a sheet
    /// already placed could never be crossed by a later move. That holds for two selected sheets
    /// and fails from three: a scope auditor's differential fuzz (20,000 randomised move/duplicate
    /// trials against an independent oracle) found mismatches in 14% of cases, every one of them
    /// with three or more selected sheets -- moving {A,C,D} before B yielded A,C,B,D instead of
    /// A,C,D,B. That is a correctness regression in exchange for a performance fix, which is a bad
    /// trade: the code it replaced was slow but never wrong.</para>
    ///
    /// <para>This walks the target order left to right and, whenever position i does not already
    /// hold the sheet it should, moves that sheet there. Positions before i are final and cannot be
    /// disturbed, because a move from j &gt; i to i shifts only the range [i, j) rightward. The
    /// bookkeeping mirrors <c>Workbook.MoveSheet</c>'s remove-then-insert on a local copy rather
    /// than reasoning about which entries "should" have moved, so it cannot drift from it. Work is
    /// proportional to the total displacement, so moving one sheet a short distance stays cheap --
    /// which is what the original finding was about.</para>
    /// </summary>
    private static void RelocateToDesiredOrder(Workbook workbook, IReadOnlyList<SheetId> desiredOrder)
    {
        var live = workbook.Sheets.Select(sheet => sheet.Id).ToList();
        if (live.Count != desiredOrder.Count)
            return;

        var positionOf = new Dictionary<SheetId, int>(live.Count);
        for (var i = 0; i < live.Count; i++)
            positionOf[live[i]] = i;

        for (var i = 0; i < desiredOrder.Count; i++)
        {
            var wanted = desiredOrder[i];
            if (live[i].Equals(wanted))
                continue;

            var from = positionOf[wanted];
            workbook.MoveSheet(from, i);

            // Mirror the same remove-then-insert locally, and reindex only the range it disturbed.
            live.RemoveAt(from);
            live.Insert(i, wanted);
            for (var j = i; j <= from; j++)
                positionOf[live[j]] = j;
        }
    }


}
