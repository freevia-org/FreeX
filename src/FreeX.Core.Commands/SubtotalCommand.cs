using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SubtotalCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _groupByColumnOffset;
    private readonly IReadOnlyList<uint> _subtotalColumnOffsets;
    private readonly int _functionNumber;
    private readonly bool _pageBreakBetweenGroups;
    private readonly bool _summaryBelowData;
    private readonly List<IWorkbookCommand> _appliedCommands = [];
    private List<uint>? _previousRowPageBreaks;
    // subtotal-formula-prefix-false-positive-deletion: snapshot of sheet.SubtotalRows taken before
    // this command mutates anything, so Revert can wholesale-restore the exact pre-Apply set once
    // every sub-command (InsertRowsCommand/EditCellsCommand) has already reverted the sheet back to
    // its pre-Apply row layout -- mirrors _previousRowPageBreaks immediately above.
    private List<uint>? _previousSubtotalRows;
    private bool? _previousOutlineSummaryBelow;
    private bool _outlineSummaryBelowChanged;

    public string Label => "Subtotal";

    /// <inheritdoc/>
    /// <remarks>
    /// R125-commands-undo-byte-budget: Subtotal inserts one row (via InsertRowsCommand) and
    /// writes one aggregate formula (via EditCellsCommand) per detected group, so for a large
    /// range with many groups _appliedCommands can hold dozens/hundreds of sub-commands, each
    /// already retaining its own undo snapshot. Sum their estimates instead of the flat 200-byte
    /// default so a big Subtotal actually counts against CommandBus's byte budget.
    /// </remarks>
    public int EstimatedBytes
    {
        get
        {
            long bytes = 0;
            foreach (var command in _appliedCommands)
                bytes += command is IEstimatesMemory mem ? mem.EstimatedBytes : 200;
            return (int)Math.Min(bytes, int.MaxValue);
        }
    }

    public SubtotalCommand(
        SheetId sheetId,
        GridRange range,
        uint groupByColumnOffset,
        uint subtotalColumnOffset,
        int functionNumber = 9,
        bool pageBreakBetweenGroups = false,
        bool summaryBelowData = true)
        : this(
            sheetId,
            range,
            groupByColumnOffset,
            [subtotalColumnOffset],
            functionNumber,
            pageBreakBetweenGroups,
            summaryBelowData)
    {
    }

    public SubtotalCommand(
        SheetId sheetId,
        GridRange range,
        uint groupByColumnOffset,
        IReadOnlyList<uint> subtotalColumnOffsets,
        int functionNumber = 9,
        bool pageBreakBetweenGroups = false,
        bool summaryBelowData = true)
    {
        _sheetId = sheetId;
        _range = range;
        _groupByColumnOffset = groupByColumnOffset;
        _subtotalColumnOffsets = subtotalColumnOffsets;
        _functionNumber = functionNumber;
        _pageBreakBetweenGroups = pageBreakBetweenGroups;
        _summaryBelowData = summaryBelowData;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        // Subtotals require both inserting rows and writing formula cells. Check both
        // permissions atomically before mutating anything, so failures produce accurate
        // messages rather than a generic "Could not insert subtotal row" from a sub-command.
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.InsertRows) is { } insertOutcome)
            return insertOutcome;
        // Formula cells are written into newly-inserted rows; newly-inserted rows default
        // to Locked=true, so cell editing also requires the sheet to be unprotected.
        if (CommandGuards.RejectIfProtected(sheet) is { } editOutcome)
            return editOutcome;
        if (SelectionRangeService.IsWholeColumnSelection(_range) ||
            SelectionRangeService.IsWholeRowSelection(_range))
        {
            return new CommandOutcome(
                false,
                "Subtotal requires a bounded data range; select the occupied table range instead of whole rows or columns.");
        }

        if (_range.RowCount < 2)
            return new CommandOutcome(false, "Subtotal requires a header row and at least one data row.");
        if (_groupByColumnOffset >= _range.ColCount ||
            _subtotalColumnOffsets.Count == 0 ||
            _subtotalColumnOffsets.Any(offset => offset >= _range.ColCount))
            return new CommandOutcome(false, "Subtotal columns must be inside the selected range.");

        _appliedCommands.Clear();
        _previousRowPageBreaks = sheet.RowPageBreaks.ToList();
        _previousSubtotalRows = sheet.SubtotalRows.ToList();

        // Data > Subtotal's "Summary below data" checkbox must drive Sheet.OutlineSummaryBelow,
        // the same setting Data > Outline > Settings writes and Group/Ungroup/Collapse read (see
        // GroupRowsCommand's summaryBelow lookups and the saved outlinePr/summaryBelow attribute).
        // Otherwise the physical row layout this command just built (subtotal rows above or below
        // their detail blocks, per _summaryBelowData) disagrees with the sheet-wide direction flag:
        // Collapse Group would anchor to the wrong row, and the saved file's outlinePr direction
        // would contradict the actual layout when reopened in Excel.
        _previousOutlineSummaryBelow = sheet.OutlineSummaryBelow;
        _outlineSummaryBelowChanged = true;
        sheet.OutlineSummaryBelow = _summaryBelowData;

        var plan = SubtotalPlanBuilder.Build(
            sheet,
            _range,
            _groupByColumnOffset,
            _pageBreakBetweenGroups,
            _summaryBelowData);
        var affected = new List<CellAddress>();

        return ApplyPlan(ctx, sheet, plan, affected);
    }

    public void Revert(ICommandContext ctx)
    {
        for (int i = _appliedCommands.Count - 1; i >= 0; i--)
            _appliedCommands[i].Revert(ctx);
        _appliedCommands.Clear();
        if (_previousRowPageBreaks is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            sheet.RowPageBreaks.Clear();
            foreach (var rowBreak in _previousRowPageBreaks)
                sheet.RowPageBreaks.Add(rowBreak);
            _previousRowPageBreaks = null;
        }
        if (_previousSubtotalRows is not null)
        {
            // Every InsertRowsCommand.Revert call above has already removed the exact rows this
            // pass inserted and shifted the sheet's row numbering back to its pre-Apply state, so a
            // wholesale replace here is correct regardless of how many times sheet.SubtotalRows'
            // entries shifted mid-Apply (each group insertion shifts every already-inserted subtotal
            // row above it -- see ApplyGroupOutline's remarks).
            var sheet = ctx.GetSheet(_sheetId);
            sheet.SubtotalRows.Clear();
            foreach (var row in _previousSubtotalRows)
                sheet.SubtotalRows.Add(row);
            _previousSubtotalRows = null;
        }
        if (_outlineSummaryBelowChanged)
        {
            ctx.GetSheet(_sheetId).OutlineSummaryBelow = _previousOutlineSummaryBelow;
            _outlineSummaryBelowChanged = false;
        }
    }

    private bool ApplyInsertAndEdit(
        ICommandContext ctx,
        SubtotalInsertionPlan subtotalRow,
        List<CellAddress> affected)
    {
        var insert = new InsertRowsCommand(_sheetId, subtotalRow.InsertRow);
        var insertOutcome = insert.Apply(ctx);
        if (!insertOutcome.Success)
            return false;
        _appliedCommands.Add(insert);

        // subtotal-formula-prefix-false-positive-deletion: mark the row this command JUST inserted
        // as real, authored subtotal-row state -- not something later re-derived by guessing from
        // formula text. This must happen immediately after each insert (not once at the end), so a
        // subsequent group's insert (which shifts every already-marked row below it) shifts this
        // marker forward in lockstep via InsertRowsCommand's own SubtotalRows shift (see
        // ApplyGroupOutline's remarks on why insertions applied later shift earlier subtotal rows).
        ctx.GetSheet(_sheetId).SubtotalRows.Add(subtotalRow.InsertRow);

        var labelAddress = new CellAddress(_sheetId, subtotalRow.InsertRow, _range.Start.Col + _groupByColumnOffset);
        var edits = new List<(CellAddress Address, Cell Cell)>
        {
            (labelAddress, Cell.FromValue(new TextValue(subtotalRow.Label)))
        };
        foreach (var subtotalColumnOffset in _subtotalColumnOffsets)
        {
            // The group-by column already got its "<Label> Total" text cell above; if it is
            // also checked as a subtotal column (Excel allows this), skip it here rather than
            // emitting a second edit for the same address. A duplicate address in `edits` would
            // make EditCellsCommand snapshot the label text (just written) as the "old" value for
            // undo instead of the true pre-Apply value, permanently corrupting that cell on Revert.
            if (subtotalColumnOffset == _groupByColumnOffset)
                continue;

            var formulaAddress = new CellAddress(_sheetId, subtotalRow.InsertRow, _range.Start.Col + subtotalColumnOffset);
            var formula = SubtotalPlanBuilder.BuildSubtotalFormula(
                _functionNumber,
                formulaAddress.Col,
                subtotalRow.FormulaStartRow,
                subtotalRow.FormulaEndRow);
            edits.Add((formulaAddress, Cell.FromFormula(formula)));
        }

        var edit = new EditCellsCommand(_sheetId, edits);
        var editOutcome = edit.Apply(ctx);
        if (!editOutcome.Success)
            return false;

        _appliedCommands.Add(edit);
        affected.Add(labelAddress);
        affected.AddRange(edits.Skip(1).Select(editItem => editItem.Address));
        return true;
    }

    private CommandOutcome ApplyPlan(
        ICommandContext ctx,
        Sheet sheet,
        SubtotalPlan plan,
        List<CellAddress> affected)
    {
        if (!ApplyInsertions(ctx, plan.GroupRows, affected))
        {
            Revert(ctx);
            return CommandGuards.RejectCouldNotInsertSubtotalRow();
        }

        if (_summaryBelowData)
            AddPlannedPageBreaks(sheet, plan);

        if (!ApplyInsertAndEdit(ctx, plan.GrandTotalRow, affected))
        {
            Revert(ctx);
            return CommandGuards.RejectCouldNotInsertSubtotalRow();
        }

        if (!_summaryBelowData)
            AddPlannedPageBreaks(sheet, plan);

        ApplyGroupOutline(sheet, plan);

        return new CommandOutcome(true, AffectedCells: affected);
    }

    /// <summary>
    /// Builds the row outline the same way Excel does for Data &gt; Subtotal: each group's detail
    /// rows get the deepest outline level (so the outline pane's 1/2/3 buttons can collapse to
    /// just the subtotal/grand-total rows), while the grand-total row stays at level 0. This
    /// mirrors <see cref="GroupRowsCommand"/>'s ownership of <see cref="Sheet.RowOutlineLevels"/>,
    /// including its use of <see cref="OutlineGroupingService.GetGroupedOutlineLevel"/> to nest
    /// levels rather than clobbering them.
    /// </summary>
    /// <remarks>
    /// <see cref="SubtotalInsertionPlan.FormulaStartRow"/>/<see cref="SubtotalInsertionPlan.FormulaEndRow"/>
    /// are computed once, up front, against the pre-insertion row numbering (see
    /// <see cref="SubtotalPlanBuilder"/>) — every insertion applied after the first one shifts rows
    /// that were already written, including earlier subtotal rows and their formulas (that's why,
    /// e.g., the last group's SUBTOTAL formula ends up referencing different row numbers than the
    /// plan originally computed). So rather than re-deriving the cumulative shift here, this scans
    /// the sheet's own final state — via the same <see cref="SubtotalRowFinder"/> used by
    /// <see cref="RemoveSubtotalRowsCommand"/> — to find exactly which rows in the final range are
    /// subtotal/grand-total rows.
    /// </remarks>
    /// <remarks>
    /// For nested subtotals (Data &gt; Subtotal run a second time, with "Replace current
    /// subtotals" unchecked, over a range that still contains a prior pass's subtotal rows) this
    /// must build a multi-level outline, not flatten everything to level 1: the prior pass's
    /// group-total rows (still present, still SUBTOTAL-formula rows, so still in
    /// <paramref name="sheet"/>'s <see cref="Sheet.RowOutlineLevels"/> at whatever level that pass
    /// left them) become intermediate levels, and only the innermost, ungrouped detail rows sit at
    /// the deepest level. That's exactly the nesting rule <see cref="GroupRowsCommand"/> already
    /// uses for manual Group Rows: the next level is one more than the deepest existing level
    /// already present in the range (<see cref="OutlineGroupingService.GetGroupedOutlineLevel"/>
    /// with <c>preserveExistingHierarchy: true</c>). The grand-total row is excluded from that
    /// scan and pinned at level 0 (Excel never nests the grand total itself), and every other
    /// pre-existing subtotal/grand-total row this pass finds becomes an intermediate level — one
    /// less than the new detail level — rather than being left at its old, now-too-shallow level.
    /// </remarks>
    private void ApplyGroupOutline(Sheet sheet, SubtotalPlan plan)
    {
        var insertedRowCount = (uint)plan.GroupRows.Count + 1;
        var finalRange = new GridRange(
            _range.Start,
            new CellAddress(_range.Start.Sheet, _range.End.Row + insertedRowCount, _range.End.Col));
        var totalRows = new HashSet<uint>(SubtotalRowFinder.Find(sheet, _sheetId, finalRange));

        // The grand-total row is whichever total row this pass itself just inserted/rewrote via
        // ApplyInsertAndEdit(plan.GrandTotalRow, ...); its final position shifted along with every
        // other row already written, but SubtotalRowFinder's rescan reports final positions, so the
        // simplest correct way to identify it post-shift is: it's the total row that is NOT one of
        // this pass's own group-total rows AND sits outside every group span, i.e. the one total row
        // that remains after removing rows immediately following each detected group. Rather than
        // re-deriving that mapping, use the plan-relative fact that summaryBelowData puts the grand
        // total strictly after the last group-total row in the final range, and summaryAboveData puts
        // it strictly before the first group-total row — both are simply the total row furthest from
        // the detail rows on the "outside" of the whole block.
        var grandTotalRow = _summaryBelowData
            ? finalRange.End.Row
            : finalRange.Start.Row + 1;

        // The deepest level any prior pass left in this range (grand total excluded — it always
        // stays at 0 and must never inflate the nesting depth). A first pass has no prior levels
        // here, so this is 0 and every non-total row lands at level 1, matching the single-level
        // behavior the existing tests pin down.
        var deepestExistingLevel = 0;
        for (var row = finalRange.Start.Row + 1; row <= finalRange.End.Row; row++)
        {
            if (row == grandTotalRow)
                continue;
            if (sheet.RowOutlineLevels.TryGetValue(row, out var existingLevel) && existingLevel > deepestExistingLevel)
                deepestExistingLevel = existingLevel;
        }

        var isNestedPass = deepestExistingLevel > 0;
        var detailLevel = OutlineGroupingService.GetGroupedOutlineLevel(deepestExistingLevel, 1, preserveExistingHierarchy: true);
        var intermediateLevel = Math.Max(1, detailLevel - 1);

        for (var row = finalRange.Start.Row + 1; row <= finalRange.End.Row; row++)
        {
            if (row == grandTotalRow)
            {
                sheet.RowOutlineLevels[row] = 0;
            }
            else if (totalRows.Contains(row))
            {
                // A first pass keeps its own group-total rows at level 0, exactly like Excel's
                // single-level Data > Subtotal (and the existing regression tests pin this down).
                // Only once a prior pass has already built an outline here (isNestedPass) does a
                // subtotal row need to move off level 0: it sits one level shallower than the
                // freshly-marked detail rows, so it can still be collapsed to on its own via the
                // outline pane before collapsing all the way to the grand total.
                sheet.RowOutlineLevels[row] = isNestedPass ? intermediateLevel : 0;
            }
            else
            {
                sheet.RowOutlineLevels[row] = detailLevel;
            }
        }
    }

    private bool ApplyInsertions(
        ICommandContext ctx,
        IReadOnlyList<SubtotalInsertionPlan> subtotalRows,
        List<CellAddress> affected)
    {
        foreach (var subtotalRow in subtotalRows)
        {
            if (!ApplyInsertAndEdit(ctx, subtotalRow, affected))
                return false;
        }

        return true;
    }

    private static void AddPlannedPageBreaks(Sheet sheet, SubtotalPlan plan)
    {
        foreach (var rowBreak in plan.PageBreakRows)
            sheet.RowPageBreaks.Add(rowBreak);
    }
}

public sealed class RemoveSubtotalRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly List<DeleteRowsCommand> _deletes = [];
    private Dictionary<uint, int>? _clearedRowOutlineLevels;
    private List<uint>? _clearedGroupHiddenRows;

    public string Label => "Remove Subtotals";

    public RemoveSubtotalRowsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _deletes.Clear();
        _clearedRowOutlineLevels = null;
        _clearedGroupHiddenRows = null;
        var rows = SubtotalRowFinder.Find(sheet, _sheetId, _range);
        foreach (var row in rows.OrderByDescending(r => r))
        {
            var delete = new DeleteRowsCommand(_sheetId, row);
            var outcome = delete.Apply(ctx);
            if (!outcome.Success)
                return outcome;

            _deletes.Add(delete);
        }

        if (_deletes.Count > 0)
            ClearDetailRowOutline(sheet, (uint)_deletes.Count);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        // Reverse of Apply: the outline entries were cleared at post-delete row
        // indexes, so they must be restored before the deletes shift rows back down.
        if (_clearedRowOutlineLevels is not null && _clearedGroupHiddenRows is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            foreach (var (row, level) in _clearedRowOutlineLevels)
                sheet.RowOutlineLevels[row] = level;
            foreach (var row in _clearedGroupHiddenRows)
                sheet.GroupHiddenRows.Add(row);
            _clearedRowOutlineLevels = null;
            _clearedGroupHiddenRows = null;
        }

        for (var i = _deletes.Count - 1; i >= 0; i--)
            _deletes[i].Revert(ctx);
        _deletes.Clear();
    }

    // Excel's Remove All Subtotals also clears the outline that Data > Subtotal built
    // for the detail rows; leaving it in place would strand the survivors at a group
    // level with no subtotal rows to collapse to.
    private void ClearDetailRowOutline(Sheet sheet, uint removedRowCount)
    {
        // The deletes shifted everything below each removed subtotal row up, so the
        // subtotaled block now ends removedRowCount rows earlier. Only clear up to
        // that boundary: rows shifted up from below the original range belong to
        // whatever outline hierarchy they were part of and must keep it.
        var detailRowCount = _range.RowCount - removedRowCount;
        _clearedRowOutlineLevels = [];
        _clearedGroupHiddenRows = [];
        for (uint offset = 0; offset < detailRowCount; offset++)
        {
            var row = _range.Start.Row + offset;
            if (sheet.RowOutlineLevels.Remove(row, out var level))
                _clearedRowOutlineLevels[row] = level;
            if (sheet.GroupHiddenRows.Remove(row))
                _clearedGroupHiddenRows.Add(row);
        }
    }
}
