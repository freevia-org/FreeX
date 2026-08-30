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
    /// Reorders the workbook's sheets to <paramref name="desiredOrder"/>.
    ///
    /// <para>r173 remediation, twice over. The original code rescanned the whole workbook per sheet,
    /// which made moving ONE sheet cost O(sheetCount^2). The first fix tracked shifting indices and
    /// only reconciled sheets it had not yet placed -- correct for two selected sheets, wrong from
    /// three (a differential fuzz found mismatches in 14% of trials). The second attempt walked the
    /// target order left to right, which is provably correct but performs one move per mismatched
    /// position: moving the first sheet to the end makes every position mismatch, so it was
    /// quadratic again for exactly the gesture the finding was about.</para>
    ///
    /// <para>So this PLANS the moves and verifies the plan by simulation before touching the
    /// workbook. It first tries moving only the sheets that actually changed position, in each of
    /// the two orderings that can be right, and checks the simulated result equals the target. Only
    /// if neither ordering reproduces the target does it fall back to the left-to-right placement,
    /// which always works. Correctness does not depend on reasoning about which sheets a shift can
    /// disturb -- the plan is checked against the answer first.</para>
    /// </summary>
    private static void RelocateToDesiredOrder(Workbook workbook, IReadOnlyList<SheetId> desiredOrder)
    {
        var live = workbook.Sheets.Select(sheet => sheet.Id).ToList();
        if (live.Count != desiredOrder.Count || live.SequenceEqual(desiredOrder))
            return;

        foreach (var (from, to) in PlanMoves(live, desiredOrder))
            workbook.MoveSheet(from, to);
    }

    // The sheets whose position differs between the two orders are the only ones that need moving;
    // everything else falls into place because a move is a remove-then-insert.
    internal static List<(int From, int To)> PlanMoves(
        List<SheetId> live,
        IReadOnlyList<SheetId> desiredOrder)
    {
        var finalIndexOf = new Dictionary<SheetId, int>(desiredOrder.Count);
        for (var i = 0; i < desiredOrder.Count; i++)
            finalIndexOf[desiredOrder[i]] = i;

        var displaced = MinimalMoveSet(live, finalIndexOf);

        foreach (var ascending in new[] { true, false })
        {
            var order = displaced.OrderBy(id => ascending ? finalIndexOf[id] : -finalIndexOf[id]).ToList();
            if (TrySimulate(live, desiredOrder, order, finalIndexOf) is { } plan)
                return plan;
        }

        // Fallback: place each position left to right. Always correct -- positions before i are
        // final, because a move from j > i to i shifts only the range [i, j) rightward.
        return LeftToRightPlan(live, desiredOrder);
    }

    // The sheets that must move are the complement of the longest INCREASING subsequence of their
    // target positions: everything on that subsequence is already in the right relative order and
    // can stay put while the rest are lifted out and reinserted around it. Using the displaced-set
    // (every sheet whose index changes) instead would be correct but wasteful -- moving the first
    // sheet to the end changes every index, while the minimal answer is one move, which is exactly
    // the gesture the complexity finding was about.
    private static List<SheetId> MinimalMoveSet(
        List<SheetId> live,
        Dictionary<SheetId, int> finalIndexOf)
    {
        var targets = new int[live.Count];
        for (var i = 0; i < live.Count; i++)
            targets[i] = finalIndexOf[live[i]];

        // Standard patience-sorting LIS, remembering predecessors so the subsequence can be walked
        // back out rather than just measured.
        var tails = new List<int>();
        var tailIndex = new List<int>();
        var previous = new int[live.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            previous[i] = -1;
            var lo = 0;
            var hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (tails[mid] < targets[i])
                    lo = mid + 1;
                else
                    hi = mid;
            }

            if (lo > 0)
                previous[i] = tailIndex[lo - 1];

            if (lo == tails.Count)
            {
                tails.Add(targets[i]);
                tailIndex.Add(i);
            }
            else
            {
                tails[lo] = targets[i];
                tailIndex[lo] = i;
            }
        }

        var keep = new HashSet<int>();
        for (var i = tailIndex.Count == 0 ? -1 : tailIndex[^1]; i >= 0; i = previous[i])
            keep.Add(i);

        var movers = new List<SheetId>();
        for (var i = 0; i < live.Count; i++)
            if (!keep.Contains(i))
                movers.Add(live[i]);

        return movers;
    }

    private static List<(int From, int To)>? TrySimulate(
        List<SheetId> live,
        IReadOnlyList<SheetId> desiredOrder,
        List<SheetId> moveOrder,
        Dictionary<SheetId, int> finalIndexOf)
    {
        var simulated = new List<SheetId>(live);
        var plan = new List<(int From, int To)>(moveOrder.Count);
        foreach (var id in moveOrder)
        {
            var from = simulated.IndexOf(id);
            var to = finalIndexOf[id];
            if (from == to)
                continue;

            simulated.RemoveAt(from);
            simulated.Insert(to, id);
            plan.Add((from, to));
        }

        return simulated.SequenceEqual(desiredOrder) ? plan : null;
    }

    private static List<(int From, int To)> LeftToRightPlan(
        List<SheetId> live,
        IReadOnlyList<SheetId> desiredOrder)
    {
        var simulated = new List<SheetId>(live);
        var plan = new List<(int From, int To)>();
        for (var i = 0; i < desiredOrder.Count; i++)
        {
            if (simulated[i].Equals(desiredOrder[i]))
                continue;

            var from = simulated.IndexOf(desiredOrder[i], i);
            simulated.RemoveAt(from);
            simulated.Insert(i, desiredOrder[i]);
            plan.Add((from, i));
        }

        return plan;
    }

}
