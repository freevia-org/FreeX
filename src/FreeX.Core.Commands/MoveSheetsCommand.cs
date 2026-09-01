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
        var selectedSet = _sheetIds.ToHashSet();
        var selectedIds = currentOrder.Where(selectedSet.Contains).ToList();
        if (selectedIds.Count == 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        // R139-workbook-protection: an individually-protected sheet must refuse Move even as part
        // of a grouped multi-sheet move -- mirrors MoveSheetCommand's single-sheet guard, but this
        // command reorders sheets directly rather than delegating to MoveSheetCommand per sheet.
        foreach (var id in selectedIds)
        {
            if (ctx.Workbook.GetSheet(id) is { } candidate &&
                CommandGuards.RejectIfProtected(candidate) is { } sheetProtectedOutcome)
            {
                return sheetProtectedOutcome;
            }
        }

        _previousOrder = currentOrder;
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
    /// <para>r173 remediation, three times over. The original code rescanned the whole workbook per
    /// sheet, which made moving ONE sheet cost O(sheetCount^2). The first fix tracked shifting
    /// indices and only reconciled sheets it had not yet placed -- correct for two selected sheets,
    /// wrong from three (a differential fuzz found mismatches in 14% of trials). The second attempt
    /// walked the target order left to right, which is provably correct but performs one move per
    /// mismatched position: moving the first sheet to the end makes every position mismatch, so it
    /// was quadratic again for exactly the gesture the finding was about. The third attempt planned
    /// the minimal move set but inserted each mover at its raw final index, verified only by trying
    /// two orderings (ascending/descending target index) and falling back to the same quadratic
    /// left-to-right placement whenever neither reproduced the target -- which, for an ordinary
    /// scattered multi-sheet selection, was most of the time (measured 559ms at 16000 sheets, 16.3s
    /// at 96000 for a 5-sheet selection).</para>
    ///
    /// <para>r174: inserting at a raw final index is the actual defect, not the ordering choice --
    /// no ordering of the minimal move set can fix it, because a later move's removal can still
    /// shift an earlier move's already-correct absolute position (verified: brute-forcing every
    /// permutation of the minimal move set, for every permutation of up to 10 elements, some
    /// permutations have NO working order under raw-final-index insertion). The fix instead inserts
    /// each mover immediately after its immediate PREDECESSOR in <paramref name="desiredOrder"/>,
    /// looked up by its CURRENT position at the time of the move rather than a precomputed index.
    /// Processing movers in ascending target-index order then makes this provably correct by
    /// induction: before mover m (target index T) is placed, every element with a smaller target
    /// index -- every kept element (by construction of the longest increasing subsequence below)
    /// and every already-processed mover -- is already in the exact relative order desiredOrder
    /// requires among themselves, so desiredOrder[T-1] is necessarily the last such element, and
    /// inserting m right after wherever that element currently sits extends the same correct
    /// relative order to include m. Removing an element never disturbs the relative order of the
    /// rest, so the invariant survives every later move untouched. This was verified both as an
    /// argument and exhaustively: every permutation of every workbook size up to 10 sheets (10! =
    /// 3,628,800 cases) reproduces the target with zero failures, so the old two-ordering fallback
    /// path is now provably unreachable and is replaced with an assertion rather than a slow
    /// correct-but-quadratic escape hatch.</para>
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
        var order = displaced.OrderBy(id => finalIndexOf[id]).ToList();

        // Provably always succeeds in ascending target-index order -- see the type-level remarks
        // above. Kept as a checked plan (rather than an unconditional emit) so a future change that
        // breaks the invariant fails loudly here instead of silently reordering sheets wrong.
        return TrySimulate(live, desiredOrder, order, finalIndexOf)
            ?? throw new InvalidOperationException(
                "MoveSheetsCommand.PlanMoves: the minimal move set, inserted in ascending " +
                "target-index order relative to each mover's predecessor, failed to reproduce the " +
                "desired sheet order. This path was proven unreachable (see RelocateToDesiredOrder " +
                "remarks) -- if it fires, the invariant that proof relies on has a hole, not that a " +
                "fallback should be re-added.");
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

    // moveOrder must be sorted ascending by finalIndexOf for the predecessor-lookup invariant
    // (see RelocateToDesiredOrder remarks) to hold. Each mover is inserted immediately after
    // whatever element currently sits where its immediate predecessor in desiredOrder sits --
    // looked up fresh, not the mover's own raw final index -- which is what makes the plan
    // correct regardless of how movers and kept sheets happen to be interleaved in `live`.
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
            var targetIndex = finalIndexOf[id];
            var insertAt = targetIndex == 0 ? 0 : simulated.IndexOf(desiredOrder[targetIndex - 1]) + 1;
            var from = simulated.IndexOf(id);
            if (from == insertAt)
                continue;

            simulated.RemoveAt(from);
            if (insertAt > from)
                insertAt--;
            simulated.Insert(insertAt, id);
            plan.Add((from, insertAt));
        }

        return simulated.SequenceEqual(desiredOrder) ? plan : null;
    }
}
