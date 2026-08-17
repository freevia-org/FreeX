using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank rows before <paramref name="beforeRow"/>.</summary>
public sealed class InsertRowsCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: mirrors DeleteRowsCommand's rationale -- _movedSnapshot
    // below retains a CellStateSnapshot for every occupied cell shifted down by the insert, plus
    // several companion per-cell dictionary snapshots. Estimated from _movedSnapshot.Count, known
    // only once Apply has captured it.
    private const int BytesPerCell = 400;

    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _beforeRow;
    private readonly uint _count;
    private List<CellStateSnapshot>? _movedSnapshot;
    // R96-commands-undo-affected-cells-1: mutated by both Apply (post-shift addresses) and Revert
    // (original, pre-shift addresses) so CommandBus.Undo can report the CURRENT set of relocated
    // formula cells instead of the frozen forward payload -- see
    // RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress.
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private RowColumnMutationSnapshot? _mutationSnapshot;
    private List<KeyValuePair<uint, double>>? _rowHeightSnapshot;
    // R136-io-worksheet-props-col-row-default-style-shift: sheet.RowStyles is keyed by the same
    // absolute row index as RowHeights and must shift the same way, or a whole-row default style
    // (e.g. a customFormat row's "s" style) is left painting the wrong row after an insert --
    // ViewportService reads this dictionary directly with no other re-key path.
    private List<KeyValuePair<uint, StyleId>>? _rowStyleSnapshot;
    // R106-io-hyperlink-range-shift: whole-column/row and oversized-bounded hyperlink refs live
    // outside the CellAddress-keyed Hyperlinks/HyperlinkMetadata dictionaries above (see
    // Sheet.RangeHyperlinks) and must be shifted independently.
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    // R102: see RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsUp -- every SeriesIndex-keyed
    // per-series/per-point collection on a Switch-Row/Column chart whose plotted series span this
    // insert falls strictly inside must be captured here (undo) since the remap mutates them in place.
    private List<RowColumnShiftHelpers.ChartSeriesFormattingWorkbookSnapshot>? _chartSeriesFormattingSnapshot;
    // R86-commands-insert-move-refadjust-5-1: a chart's own drawing position (Left/Top) is never
    // cell-anchored (see RowColumnShiftHelpers.ShiftChartPositionRowsUp), so it must be captured and
    // shifted separately from _chartSnapshot above, which only tracks DataRange.
    private List<RowColumnShiftHelpers.ChartPositionSnapshot>? _chartPositionSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private List<(CellAddress Address, Cell? OldCell)>? _tableCalculatedColumnFillSnapshot;

    public string Label => $"Insert {_count} Row(s)";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => _movedSnapshot is null
        ? 0
        : (int)Math.Min((long)_movedSnapshot.Count * BytesPerCell, int.MaxValue);

    public InsertRowsCommand(SheetId sheetId, uint beforeRow, uint count = 1)
    {
        _sheetId   = sheetId;
        _beforeRow = beforeRow;
        _count     = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.InsertRows) is { } protectedOutcome)
            return protectedOutcome;

        // R46-commands-insert-delete-shift-2-1: whole-row insert shifts every occupied cell at or
        // below _beforeRow down by _count, which silently splits a legacy CSE array or a live
        // dynamic-array spill whose row extent straddles the insert point (e.g. inserting a row
        // through the middle of a SEQUENCE spill) — the array's anchor moves/stays independent of its
        // still-computed member rows, desyncing the array from the sheet's shifted row numbering with
        // no error shown. Mirror the identical guard the band-scoped Insert/Delete Cells command
        // already applies: treat every row at or below the insert point as the "shifted" region (the
        // whole width of the sheet, since a row insert affects every column) and reject if that region
        // would carry only *some* members of an array along while leaving the rest behind.
        var shiftRegion = new CellShiftRegion(_beforeRow, Model.CellAddress.MaxRow, 1u, Model.CellAddress.MaxCol);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, shiftRegion)) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var (maxOccupied, movedSnapshot) = CaptureMovedCells(sheet);
        // R111-commands-insert-overflow-metadata-1: maxOccupied above only sees rows holding an
        // actual Cell object. A row past the last data row can still carry row-level state that
        // lives entirely outside the cell dictionary -- a style-only formatting band (whole-row
        // header select with no value), a RowHeights override, a hidden-row flag, or an outline
        // level -- and that state shifts (and overflows) exactly like a real cell would. Fold in
        // the highest such row, but only if it actually falls within the shifted region (>=
        // _beforeRow): HighestFormattedOrOccupiedRow is sheet-wide, and a formatted row ABOVE the
        // insert point never moves, so it must not count toward the overflow check.
        var highestFormattedRow = RowColumnShiftHelpers.HighestFormattedOrOccupiedRow(sheet);
        var maxOccupiedOrFormatted = Math.Max(
            maxOccupied,
            highestFormattedRow >= _beforeRow ? highestFormattedRow : 0);
        if (maxOccupiedOrFormatted > 0 && maxOccupiedOrFormatted + _count > Model.CellAddress.MaxRow)
            return new CommandOutcome(false,
                ErrorMessage: CommandGuards.CannotInsertRowsPastLastRow(_count));

        _mutationSnapshot = RowColumnMutationSnapshot.Capture(ctx.Workbook, sheet);
        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        _movedSnapshot = movedSnapshot;

        MoveCellsForInsert(sheet, _movedSnapshot, _count);

        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.HiddenRows, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.FilterHiddenRows, _beforeRow, _count);
        // G2: sheet.ValueFilterHiddenRows must shift in lockstep with FilterHiddenRows — it records
        // exactly which of those rows the value-filter mechanism (sheet.ActiveValueFilterColumns)
        // currently owns, and FilterCommand.RecomputeHiddenRows uses it to decide which rows it may
        // safely un-hide. Left unshifted, it would go stale the moment rows move.
        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.ValueFilterHiddenRows, _beforeRow, _count);
        // subtotal-formula-prefix-false-positive-deletion: sheet.SubtotalRows tracks (as real state,
        // not a formula-text guess) which rows SubtotalCommand itself created. Any row insert must
        // shift those markers the same way HiddenRows/FilterHiddenRows shift, so a subtotal row's
        // tracked position stays correct even when the insert came from something unrelated to
        // Subtotal (e.g. the user manually inserting a row above an existing subtotal block).
        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.SubtotalRows, _beforeRow, _count);
        // R13-meta-1: sheet.ColumnFilterOwnedRows' HashSet row VALUES must shift the same way, or a
        // column's condition/color/Top-Bottom/Average filter forgets which row it actually owns and
        // orphans a permanently-hidden row the next time that column's filter is cleared/recomputed.
        RowColumnShiftHelpers.ShiftRowSetDictionaryUpFrom(sheet.ColumnFilterOwnedRows, _beforeRow, _count);

        _rowHeightSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RowHeights);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.RowHeights, _beforeRow, _count);

        // R136-io-worksheet-props-col-row-default-style-shift: sheet.RowStyles is the same
        // absolute-row key space as RowHeights above -- re-key it in lockstep or the whole-row
        // default style painted by ViewportService lands on whatever row ends up at its stale
        // pre-insert index.
        _rowStyleSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RowStyles);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.RowStyles, _beforeRow, _count);

        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Comments, _beforeRow, _count);
        // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
        // author + pinned/"Show Comment" state) and must shift in lockstep with it, or a note's
        // author/pinned box goes stale at the note's old address after the insert.
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.CommentAuthors, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftCommentSetRowsUp(sheet.ShownComments, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.ThreadedComments, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Hyperlinks, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.HyperlinkMetadata, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftRangeHyperlinksRowsUp(sheet, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.RichTextRuns, _beforeRow, _count);
        // R78-selfreg-twin-sweep-2: sheet.CellPhoneticGuides is RichTextRuns' address-keyed
        // companion (furigana annotations for the same cell) and must shift in lockstep with it,
        // or an inserted row leaves a phonetic guide keyed to the cell's stale pre-insert address
        // while the rich text it decorates moves on to the new address.
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.CellPhoneticGuides, _beforeRow, _count);

        RowColumnShiftHelpers.ShiftRuleRowsUp(sheet, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftNamedRangeRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaRowsUp(sheet, _beforeRow, _count);
        _rowPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.RowPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetUp(sheet.RowPageBreaks, _beforeRow, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        // R102: must run BEFORE ShiftChartRowsUp below -- it needs each chart's PRE-insert DataRange
        // to tell whether _beforeRow falls strictly inside a Switch-Row/Column chart's plotted series
        // span (see RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsUp -- every SeriesIndex-keyed
        // collection on such a chart must move in lockstep with the inserted row(s), the row-axis twin
        // of ShiftChartSeriesFormattingColumnsUp above).
        _chartSeriesFormattingSnapshot = RowColumnShiftHelpers.CaptureChartSeriesFormatting(ctx.Workbook);
        RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftChartRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        // R86-commands-insert-move-refadjust-5-1: see ShiftChartPositionRowsUp — rows before
        // _beforeRow are untouched by this insert, so it is safe to read sheet.RowHeights here
        // regardless of whether the RowHeights re-key above has already run.
        _chartPositionSnapshot = RowColumnShiftHelpers.CaptureChartPositions(sheet);
        RowColumnShiftHelpers.ShiftChartPositionRowsUp(sheet, _beforeRow, _count);
        RowColumnShiftHelpers.ShiftAddressBearingRowsUp(ctx.Workbook, sheet, _addressStateSnapshot, _beforeRow, _count);

        // R92-render-cellstyle-inheritance-5-2: Excel's Insert Sheet Rows default ("Insert
        // Options") inherits the format of the row above into the newly-vacated band. Must run
        // after the ShiftAddressBearingRowsUp call above, which rebuilds the whole style-only
        // store from the pre-insert snapshot and would otherwise wipe these new entries.
        RowColumnShiftHelpers.InheritVacatedRowFormatFromAbove(sheet, _beforeRow, _count);

        var shiftedMerges = new List<GridRange>(sheet.MergedRegions.Count);
        foreach (var m in sheet.MergedRegions)
        {
            GridRange shifted;
            if (m.Start.Row >= _beforeRow)
                shifted = new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row + _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   + _count, m.End.Col));
            else if (m.End.Row >= _beforeRow)
                shifted = new GridRange(
                    m.Start,
                    new CellAddress(m.End.Sheet, m.End.Row + _count, m.End.Col));
            else
                shifted = m;

            // R16-large-workbook-perf-1: a merged region whose entire shifted position falls past
            // the last row runs off the sheet and is dropped, mirroring Excel; one whose bottom
            // edge merely overshoots is clamped back to the last row instead of left out-of-bounds.
            if (shifted.Start.Row > Model.CellAddress.MaxRow)
                continue;
            if (shifted.End.Row > Model.CellAddress.MaxRow)
                shifted = new GridRange(
                    shifted.Start,
                    new CellAddress(shifted.End.Sheet, Model.CellAddress.MaxRow, shifted.End.Col));

            shiftedMerges.Add(shifted);
        }
        sheet.ReplaceMergedRegions(shiftedMerges);

        _mutationSnapshot.RewriteReferences(
            ctx.Workbook,
            sheet,
            new InsertRowsOp(sheet.Name, _beforeRow, _count));

        // R26-table-structured-ref-deep-2: the address-bearing shift above already grows a
        // structured table's Range (via ShiftStructuredTables) when the insert point falls inside
        // it, but that is a pure range/columns reconciliation with no calculated-column fill.
        // Mirror ResizeStructuredTableCommand.FillGrownCalculatedColumns here so a row inserted
        // inside a table's body gets its calculated column(s) auto-filled the way Excel does,
        // instead of being left blank. This MUST run after RewriteAllFormulas above: that pass
        // bumps every existing formula's cell-reference rows >= _beforeRow by _count regardless of
        // which cell holds them, so a same-row reference we write here (already targeting its final
        // post-insert row) would be incorrectly re-shifted again if written any earlier.
        _tableCalculatedColumnFillSnapshot = FillGrownCalculatedColumnsForInsertedRows(ctx.Workbook, sheet);

        _affectedCells = _mutationSnapshot.BuildAffectedCells(
            RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, movedSnapshot, _count, _mutationSnapshot.FormulaTexts)
                .Concat(_tableCalculatedColumnFillSnapshot.Select(f => f.Address))
                .Concat(VacatedAddressesForRelocatedFormulaCells(_sheetId, movedSnapshot)));
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // R98-commands-dependency-vacated-1: every snapshot in movedSnapshot with a formula physically
    // relocates from its captured (Row, Col) to (Row + count, Col) by MoveCellsForInsert above --
    // regardless of whether RewriteAllFormulas needed to touch its formula TEXT. The OLD address is
    // therefore always left blank afterward (Insert only ever shifts rows DOWN, so nothing below the
    // insert point can move up into it), yet neither RelocatedFormulaCellsPendingDependencyRefresh
    // (new-address only) nor the shared formula snapshot (also new-address only, since RewriteAllFormulas runs
    // AFTER the physical move) ever surfaced it in AffectedCells. WorkbookCellEditService's
    // UpdateFormulaDependencies (and MainWindow.Editing's mirror) drives RecalcEngine purely off
    // AffectedCells: for each affected address it either re-registers dependencies or, if the cell
    // there is blank, calls ClearFormulaDependencies -- so the OLD address's dependency-graph entry
    // was never purged, leaving a phantom precedent/dependent edge keyed at an address that no
    // longer holds any formula. Surfacing it here lets UpdateFormulaDependencies's existing
    // `cell?.FormulaText is null -> ClearFormulaDependencies` branch reclaim it with no other
    // pipeline changes. (If some other relocated cell or a calculated-column fill happens to have
    // repopulated this same address by the time AffectedCells is consumed, BuildAffectedCellsForFormulaRewrite's
    // de-dup plus UpdateFormulaDependencies reading LIVE cell state means this is still handled
    // correctly -- it just re-registers whatever formula actually ended up there instead of clearing.)
    private static IEnumerable<CellAddress> VacatedAddressesForRelocatedFormulaCells(
        SheetId sheetId, IEnumerable<CellStateSnapshot> movedSnapshot)
    {
        foreach (var snapshot in movedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return new CellAddress(sheetId, snapshot.Row, snapshot.Col);
        }
    }

    // R26-table-structured-ref-deep-2: fills each structured table's calculated-column formula into
    // the row(s) this insert newly brought into the table's data body -- matching Excel's real
    // behavior of always extending a calculated column into a row inserted inside the table (whether
    // the row lands at the bottom or in the middle via Insert Row). Only rows exactly in
    // [_beforeRow, _beforeRow + _count - 1] are new/blank here -- everything at or above them was
    // already relocated intact (with formulas already rewritten) by MoveCellsForInsert /
    // RewriteAllFormulas above, so only that inserted window (clipped to the table's post-shift data
    // body, which excludes the header and any shown totals row) is touched. Mirrors
    // ResizeStructuredTableCommand.FillGrownCalculatedColumns's formula-anchoring convention: each
    // row's formula is row-shifted from the table's first data-body row, never written verbatim.
    private List<(CellAddress Address, Cell? OldCell)> FillGrownCalculatedColumnsForInsertedRows(Workbook workbook, Sheet sheet)
    {
        var filled = new List<(CellAddress Address, Cell? OldCell)>();
        if (_addressStateSnapshot is null)
            return filled;

        var lastInsertedRow = _beforeRow + _count - 1;

        foreach (var resizedTable in sheet.StructuredTables)
        {
            var previousTable = _addressStateSnapshot.StructuredTables.FirstOrDefault(t => t.Id == resizedTable.Id);
            // A table only gains newly-inserted rows in its body when the insert point fell
            // strictly inside it (Start.Row unchanged, End.Row pushed down by the insert) -- one
            // that shifted down as a whole (its Start.Row moved too, because the insert landed at
            // or above its header) or that sits entirely above/below the insert point is untouched.
            if (previousTable is null ||
                previousTable.Range.Start.Row != resizedTable.Range.Start.Row ||
                resizedTable.Range.End.Row <= previousTable.Range.End.Row)
                continue;

            var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(resizedTable);
            var fillStartRow = Math.Max(_beforeRow, firstDataRow);
            var fillEndRow = Math.Min(lastInsertedRow, lastDataRow);
            if (fillEndRow < fillStartRow)
                continue;

            var calculatedColumns = new HashSet<uint>();
            for (var columnIndex = 0; columnIndex < resizedTable.Columns.Count; columnIndex++)
            {
                var formula = resizedTable.Columns[columnIndex].CalculatedColumnFormula;
                if (string.IsNullOrWhiteSpace(formula))
                    continue;

                var col = resizedTable.Range.Start.Col + (uint)columnIndex;
                calculatedColumns.Add(col);
                for (var row = fillStartRow; row <= fillEndRow; row++)
                {
                    var address = new CellAddress(sheet.Id, row, col);
                    filled.Add((address, sheet.GetCell(address)?.Clone()));
                    var shiftedFormula = StructuredTableEditEffects.ShiftFormulaRows(formula, firstDataRow, row, sheet.Name);
                    var filledCell = Cell.FromFormula(shiftedFormula);
                    // R92-render-cellstyle-inheritance-5-2: this row was just given a row-above-
                    // inherited style-only entry by InheritVacatedRowFormatFromAbove above (this cell
                    // is brand new -- inserted rows never carry a pre-existing style-only entry of
                    // their own) -- sheet.SetCell below unconditionally clears it (Sheet.cs
                    // ClearStyleOnly side-effect), so without this the calculated-column auto-fill
                    // would silently discard the inherited row format the instant it fills the cell.
                    if (sheet.GetStyleOnly(row, col) is { } inheritedStyle)
                        filledCell.StyleId = inheritedStyle;
                    sheet.SetCell(address, filledCell);
                }
            }

            // R94-commands-undo-structural-format-reband-1: capture the pre-reband state of the
            // table's FULL data body (every row GetDataBodyRowBounds returns, not just the
            // newly-inserted window) before RebandTable below paints its stripe fill -- mirroring
            // DeleteRowsCommand.RebandTablesAfterRowDelete and
            // InsertDeleteColumnsCommand.RebandTablesAfterColumnInsert, which both already snapshot
            // the whole body for the exact same reason. RebandTable/ApplyTableStyle always repaints
            // every data-body cell with forceFill:true (MergeStyleOntoCell's keepExistingFill is
            // unconditionally false under forceFill), so it can overwrite an explicit FillColor on a
            // table row far from the insertion point (e.g. a user-highlighted cell above _beforeRow,
            // which _movedSnapshot never captures since that only covers rows >= _beforeRow). Without
            // this wider capture, such a row's formatting loss had no undo coverage at all -- Ctrl+Z
            // never restored it. Calculated-column cells strictly inside the inserted window
            // ([fillStartRow, fillEndRow]) are still excluded there since their pre-fill state
            // (always null, captured above) already fully covers whatever reband does to the same
            // address -- capturing them twice would let this second, later entry's stale "after
            // fill, before reband" cell state win on Revert instead of the true original null. Rows
            // outside that window (including calculated-column cells there) are captured normally.
            for (var row = firstDataRow; row <= lastDataRow; row++)
            {
                for (var col = resizedTable.Range.Start.Col; col <= resizedTable.Range.End.Col; col++)
                {
                    if (row >= fillStartRow && row <= fillEndRow && calculatedColumns.Contains(col))
                        continue;
                    var address = new CellAddress(sheet.Id, row, col);
                    filled.Add((address, sheet.GetCell(address)?.Clone()));
                }
            }

            // Real Excel's table banding is purely positional and reflows immediately after any
            // row insert; StructuredTableStyleService's load-time bake otherwise leaves the new
            // row unstriped and every row below it out of parity. Recompute now that the row
            // shift + calculated-column fill above are both done.
            StructuredTableStyleService.RebandTable(workbook, sheet, resizedTable);
        }

        return filled;
    }

    // R24-volatile-recalc-deep-3: a relocated formula cell whose text needs no rewrite (e.g. a
    // volatile 0-arg function like NOW()/RAND() with no cell references) is never added to
    // the shared formula snapshot by RewriteAllFormulas, so it would otherwise be absent from AffectedCells and
    // RecalcEngine would never re-register its dependencies/volatile tracking at its new address
    // (leaving a stale entry at the old, now-blank address and none at the new one). Surface such
    // cells as primary affected cells so the post-command pipeline still registers them.
    private static IEnumerable<CellAddress> RelocatedFormulaCellsPendingDependencyRefresh(
        SheetId sheetId,
        List<CellStateSnapshot> movedSnapshot,
        uint count,
        Dictionary<CellAddress, string> formulaSnapshot)
    {
        foreach (var snapshot in movedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            var newAddr = new CellAddress(sheetId, snapshot.Row + count, snapshot.Col);
            if (!formulaSnapshot.ContainsKey(newAddr))
                yield return newAddr;
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R26-table-structured-ref-deep-2 / R94-commands-undo-structural-format-reband-1: undo the
        // calculated-column auto-fill and the (now table-body-wide) pre-reband snapshot FIRST -- they
        // were the very last effects Apply performed (after the physical row move below), so they
        // must be the first undone. Addresses inside [_beforeRow, _beforeRow + _count - 1] are about
        // to be repopulated by the moved-cell restore below (moved cells always restore back to their
        // pre-shift address, which for the lowest shifted rows lands exactly in this window) --
        // undoing the fill afterward would instead clobber that just-restored data. Addresses outside
        // that window (both above _beforeRow, which the moved-cell restore never touches at all, and
        // below the window, which the moved-cell clear/restore pair below still fully re-derives at
        // the same address) are unaffected by running this restore first: the moved-cell step below
        // either doesn't touch that address (leaving this restore's correct value standing) or is the
        // last write to it regardless of ordering (its own clear-then-restore pair is self-contained).
        if (_tableCalculatedColumnFillSnapshot is not null)
        {
            foreach (var (address, oldCell) in _tableCalculatedColumnFillSnapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell);
            }
        }

        // R96-commands-undo-affected-cells-1: RestoreFormulas below clears the shared formula snapshot as its
        // last step, so capture its keys (the post-shift addresses of every stationary-or-moved
        // formula cell whose text was rewritten by Apply) now, before that happens -- needed to
        // recompute _affectedCells at the end of this method.
        if (_mutationSnapshot is null) return;
        var formulaSnapshotAddressesBeforeRestore = _mutationSnapshot.RestoreRewrittenFormulas(ctx.Workbook);

        // R20-array-dynamic-spill-1: mirror MoveCellsForInsert's spill-relocation fix for undo —
        // capture any live spill rooted at the shifted-up address before clearing it back.
        var movedSpillPayloads = new RangeValue?[_movedSnapshot.Count];
        for (var i = 0; i < _movedSnapshot.Count; i++)
        {
            var s = _movedSnapshot[i];
            movedSpillPayloads[i] = sheet.CaptureSpillForRelocate(new CellAddress(sheet.Id, s.Row + _count, s.Col));
        }

        foreach (var snapshot in _movedSnapshot)
            sheet.ClearCell(snapshot.Row + _count, snapshot.Col);

        for (var i = 0; i < _movedSnapshot.Count; i++)
        {
            var snapshot = _movedSnapshot[i];
            var addr = snapshot.ToAddress(sheet.Id);
            sheet.SetCell(addr, snapshot.ToCell());
            if (movedSpillPayloads[i] is { } payload)
                sheet.SetSpillRange(addr, payload);
        }

        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.HiddenRows, _beforeRow + _count, _count);
        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.FilterHiddenRows, _beforeRow + _count, _count);
        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.ValueFilterHiddenRows, _beforeRow + _count, _count);
        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.SubtotalRows, _beforeRow + _count, _count);
        // R13-meta-1: undo the ColumnFilterOwnedRows shift in lockstep with the sibling sets above.
        RowColumnShiftHelpers.ShiftRowSetDictionaryDownFrom(sheet.ColumnFilterOwnedRows, _beforeRow + _count, _count);

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RowStyles, _rowStyleSnapshot);
        _mutationSnapshot.RestoreCommonState(ctx.Workbook, sheet, restoreRulesInPlace: true);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesFormatting(ctx.Workbook, _chartSeriesFormattingSnapshot);
        RowColumnShiftHelpers.RestoreChartPositions(_chartPositionSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);

        // R96-commands-undo-affected-cells-1: recompute AffectedCells to reflect where every
        // relocated formula cell ACTUALLY ended up after this Revert -- its original, pre-shift
        // address (mirroring Apply's own AffectedCells, which reports the post-shift address for
        // the forward direction). CommandBus.Undo reads this live property instead of the frozen
        // forward payload.
        // R98-commands-dependency-vacated-1: symmetric to the Apply-side fix above -- Revert
        // physically moves each formula cell in _movedSnapshot back from its current (post-shift)
        // address (Row + _count, Col) to its restored, pre-shift address (Row, Col), always leaving
        // the post-shift address blank afterward (undo of an Insert only ever shifts rows UP, so
        // nothing above the insert point can move down into it). That vacated post-shift address was
        // never included in AffectedCells either, leaving the identical stale dependency-graph entry
        // behind after an Undo.
        _affectedCells = _mutationSnapshot.BuildAffectedCells(
            RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_movedSnapshot, _sheetId)
                .Concat(_tableCalculatedColumnFillSnapshot?.Select(f => f.Address) ?? [])
                .Concat(formulaSnapshotAddressesBeforeRestore)
                .Concat(VacatedAddressesAfterRevert(_sheetId, _movedSnapshot, _count)),
            includeRewrittenFormulaAddresses: false);
    }

    private static IEnumerable<CellAddress> VacatedAddressesAfterRevert(
        SheetId sheetId, IEnumerable<CellStateSnapshot> movedSnapshot, uint count)
    {
        foreach (var snapshot in movedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return new CellAddress(sheetId, snapshot.Row + count, snapshot.Col);
        }
    }

    private (uint MaxOccupied, List<CellStateSnapshot> Moved) CaptureMovedCells(Sheet sheet)
    {
        if (_beforeRow <= FullSnapshotCapacityThreshold)
            return CaptureMovedCellsWithFullCapacity(sheet);

        var movedCount = CountMovedCells(sheet, out var maxOccupied);
        var moved = new List<CellStateSnapshot>(movedCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row < _beforeRow)
                continue;

            moved.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
        }

        return (maxOccupied, moved);
    }

    private (uint MaxOccupied, List<CellStateSnapshot> Moved) CaptureMovedCellsWithFullCapacity(Sheet sheet)
    {
        var moved = new List<CellStateSnapshot>(sheet.CellCount);
        uint maxOccupied = 0;

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row < _beforeRow)
                continue;

            if (row > maxOccupied)
                maxOccupied = row;

            moved.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
        }

        return (maxOccupied, moved);
    }

    private int CountMovedCells(Sheet sheet, out uint maxOccupied)
    {
        var movedCount = 0;
        maxOccupied = 0;

        foreach (var ((row, _), _) in sheet.GetOccupiedCellMap())
        {
            if (row < _beforeRow)
                continue;

            movedCount++;
            if (row > maxOccupied)
                maxOccupied = row;
        }

        return movedCount;
    }

    private static void MoveCellsForInsert(
        Sheet sheet,
        IReadOnlyList<CellStateSnapshot> movedCells,
        uint count)
    {
        if (movedCells.Count == 0)
            return;

        var originals = ArrayPool<Cell>.Shared.Rent(movedCells.Count);
        // R20-array-dynamic-spill-1: capture any live spill rooted at each moved cell BEFORE it is
        // cleared/moved, so a relocated dynamic-array anchor (e.g. =SEQUENCE with no cell references,
        // whose formula text never changes on a row shift) keeps spilling at its new address instead
        // of silently collapsing to a stale scalar.
        var spillPayloads = new RangeValue?[movedCells.Count];
        try
        {
            for (var i = 0; i < movedCells.Count; i++)
            {
                originals[i] = sheet.GetCell(movedCells[i].Row, movedCells[i].Col)!;
                spillPayloads[i] = sheet.CaptureSpillForRelocate(
                    new CellAddress(sheet.Id, movedCells[i].Row, movedCells[i].Col));
            }

            for (var i = 0; i < movedCells.Count; i++)
                sheet.ClearCell(movedCells[i].Row, movedCells[i].Col);

            for (var i = 0; i < movedCells.Count; i++)
            {
                var snapshot = movedCells[i];
                var newAddr = new CellAddress(sheet.Id, snapshot.Row + count, snapshot.Col);
                sheet.SetCell(newAddr, originals[i]);
                if (spillPayloads[i] is { } payload)
                    sheet.SetSpillRange(newAddr, payload);
            }
        }
        finally
        {
            Array.Clear(originals, 0, movedCells.Count);
            ArrayPool<Cell>.Shared.Return(originals);
        }
    }
}

internal sealed record NamedRangeSnapshot(GridRange Range, NamedRangeMetadata Metadata);
