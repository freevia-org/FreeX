using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Deletes <paramref name="count"/> rows starting at <paramref name="startRow"/>.</summary>
public sealed class DeleteRowsCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: _deletedSnapshot/_shiftedSnapshot below each retain a
    // CellStateSnapshot (value, formula text, cached AST, style, array-mode metadata) for EVERY
    // occupied cell in the deleted row band PLUS every occupied cell shifted up below it -- plus
    // several companion per-cell dictionary snapshots (comments, hyperlinks, rich text, phonetic
    // guides, DV/CF rule ranges) -- the richest undo snapshot shape in the codebase (see the
    // defect's own description). Estimated from the two snapshots' combined count, known only once
    // Apply has captured them (this command's affected-cell count depends on how many cells are
    // actually occupied, not just _count rows). Both are null before Apply runs, in which case
    // CommandBus never actually queries this (EstimateBytes is only called after Apply pushes the
    // command).
    private const int BytesPerCell = 400;

    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _count;
    private List<CellStateSnapshot>? _deletedSnapshot;
    private List<CellStateSnapshot>? _shiftedSnapshot;
    // R96-commands-undo-affected-cells-1: mutated by both Apply (post-shift addresses) and Revert
    // (original, pre-delete addresses of both the shifted-back and the restored-deleted formula
    // cells) so CommandBus.Undo can report the CURRENT set of formula cells needing dependency-graph
    // re-registration instead of the frozen forward payload -- see
    // RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress.
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private List<GridRange>? _mergeSnapshot;
    private List<KeyValuePair<uint, double>>? _rowHeightSnapshot;
    private List<uint>? _hiddenRowsSnapshot;
    private List<uint>? _filterHiddenRowsSnapshot;
    private List<uint>? _valueFilterHiddenRowsSnapshot;
    private Dictionary<uint, HashSet<uint>>? _columnFilterOwnedRowsSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentAuthorsSnapshot;
    private List<CellAddress>? _shownCommentsSnapshot;
    private List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedCommentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _hyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadataSnapshot;
    private List<RowColumnShiftHelpers.HyperlinkOtherSheetChange>? _otherSheetHyperlinkBookmarkSnapshot;
    // R106-io-hyperlink-range-shift: see Sheet.RangeHyperlinks -- whole-column/row and oversized-
    // bounded hyperlink refs shift/delete independently of the CellAddress-keyed dictionaries above.
    private List<KeyValuePair<string, GridRange>>? _rangeHyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<KeyValuePair<CellAddress, CellPhoneticGuide>>? _phoneticGuideSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    // R102: see RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsDown -- every SeriesIndex-keyed
    // per-series/per-point collection on a Switch-Row/Column chart whose plotted series span this
    // delete overlaps must be captured here (undo) since the remap mutates them in place / drops rows.
    private List<RowColumnShiftHelpers.ChartSeriesFormattingWorkbookSnapshot>? _chartSeriesFormattingSnapshot;
    // R86-commands-insert-move-refadjust-5-1: see RowColumnShiftHelpers.ShiftChartPositionRowsDown —
    // tracked separately from _chartSnapshot above, which only tracks DataRange.
    private List<RowColumnShiftHelpers.ChartPositionSnapshot>? _chartPositionSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    // R92-commands-undo-structural-format-5-1: see RebandTablesAfterRowDelete.
    private List<(CellAddress Address, Cell? OldCell)>? _tableRebandSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Delete {_count} Row(s)";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes =>
        (int)Math.Min(((long)(_deletedSnapshot?.Count ?? 0) + (_shiftedSnapshot?.Count ?? 0)) * BytesPerCell, int.MaxValue);

    public DeleteRowsCommand(SheetId sheetId, uint startRow, uint count = 1)
    {
        _sheetId  = sheetId;
        _startRow = startRow;
        _count    = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.DeleteRows) is { } protectedOutcome)
            return protectedOutcome;

        uint endRow = _startRow + _count - 1;

        // R47-meta-1: mirror InsertRowsCommand's CSE-array/dynamic-spill split guard
        // (InsertDeleteRowsCommand.cs) on the delete side. Deleting whole rows removes exactly the
        // band [_startRow, endRow] across every column while every row below shifts up in lockstep —
        // an array/spill whose extent straddles that band (some members inside the deleted band,
        // some outside it) would have part of it deleted and part of it shifted, desyncing the
        // array's anchor from its still-computed member rows with no error shown. An array entirely
        // inside the deleted band is removed as one atomic unit (fine); an array entirely outside the
        // band just rides the uniform shift (also fine).
        var deletedRowsShiftRegion = new CellShiftRegion(_startRow, endRow, 1u, CellAddress.MaxCol);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, deletedRowsShiftRegion)) is { } deleteSplitsArrayRejection)
            return deleteSplitsArrayRejection;

        // R110-formula-structuredref-rowcoldelete-ref: computed over the live (not-yet-mutated)
        // sheet.StructuredTables before anything below touches it, so every DeleteRowsOp built for
        // a FormulaRewriter pass in this method can carry the same "these tables are fully gone"
        // list and convert any remaining Table[...] reference to them into #REF! instead of leaving
        // it to dangle as #NAME? -- mirrors DeleteSheetOp's DeletedTableNames handling.
        var deletedTableNames = RowColumnShiftHelpers.FindStructuredTablesRemovedByRowDelete(sheet, _startRow, _count);

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        var (deletedSnapshot, shiftedSnapshot) = CaptureDeletedAndShiftedCells(sheet, endRow);
        _deletedSnapshot = deletedSnapshot;
        _shiftedSnapshot = shiftedSnapshot;

        foreach (var snapshot in deletedSnapshot)
            sheet.ClearCell(snapshot.Row, snapshot.Col);

        MoveCellsForDelete(sheet, shiftedSnapshot, _count);

        _hiddenRowsSnapshot = RowColumnShiftHelpers.CaptureSet(sheet.HiddenRows);
        RowColumnShiftHelpers.DeleteSetRangeAndShiftDown(sheet.HiddenRows, _startRow, _count);

        _filterHiddenRowsSnapshot = RowColumnShiftHelpers.CaptureSet(sheet.FilterHiddenRows);
        RowColumnShiftHelpers.DeleteSetRangeAndShiftDown(sheet.FilterHiddenRows, _startRow, _count);

        // G2: sheet.ValueFilterHiddenRows must shift/delete in lockstep with FilterHiddenRows — it
        // records which of those rows the value-filter mechanism (sheet.ActiveValueFilterColumns)
        // currently owns, and FilterCommand.RecomputeHiddenRows relies on it to decide which rows it
        // may safely un-hide on the next recompute.
        _valueFilterHiddenRowsSnapshot = RowColumnShiftHelpers.CaptureSet(sheet.ValueFilterHiddenRows);
        RowColumnShiftHelpers.DeleteSetRangeAndShiftDown(sheet.ValueFilterHiddenRows, _startRow, _count);

        // R13-meta-1: sheet.ColumnFilterOwnedRows' HashSet row VALUES must delete/shift the same way
        // as FilterHiddenRows/ValueFilterHiddenRows above, or a column's condition/color/Top-Bottom/
        // Average filter keeps pointing at a stale row index and orphans a permanently-hidden row.
        _columnFilterOwnedRowsSnapshot = RowColumnShiftHelpers.CaptureRowSetDictionary(sheet.ColumnFilterOwnedRows);
        RowColumnShiftHelpers.DeleteRowSetDictionaryRangeAndShiftDown(sheet.ColumnFilterOwnedRows, _startRow, _count);

        _rowHeightSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RowHeights);
        // R86-commands-insert-move-refadjust-5-1: must run BEFORE sheet.RowHeights is re-keyed below
        // — the deleted band's own heights are needed to measure the removed band's pixel height, and
        // they are gone from the live dictionary once ShiftIndexesDown below has run.
        _chartPositionSnapshot = RowColumnShiftHelpers.CaptureChartPositions(sheet);
        RowColumnShiftHelpers.ShiftChartPositionRowsDown(sheet, _startRow, _count, sheet.RowHeights, sheet.DefaultRowHeight);
        RowColumnShiftHelpers.ShiftIndexesDown(sheet.RowHeights, _startRow, _count);

        _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.Comments, _startRow, _count);
        // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
        // author + pinned/"Show Comment" state) and must shift/delete in lockstep with it, or a
        // note's author/pinned box goes stale (or survives at a deleted address) after the delete.
        _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.CommentAuthors, _startRow, _count);
        _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
        RowColumnShiftHelpers.ShiftCommentSetRowsDown(sheet.ShownComments, _startRow, _count);
        _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.ThreadedComments, _startRow, _count);
        _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.Hyperlinks, _startRow, _count);
        _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.HyperlinkMetadata, _startRow, _count);
        _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
            ctx.Workbook, sheet, new DeleteRowsOp(sheet.Name, _startRow, _count), sheet.Name);
        _rangeHyperlinkSnapshot = RowColumnShiftHelpers.CaptureRangeHyperlinks(sheet);
        RowColumnShiftHelpers.ShiftRangeHyperlinksRowsDown(sheet, _startRow, _count);
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.RichTextRuns, _startRow, _count);
        // R78-selfreg-twin-sweep-2: sheet.CellPhoneticGuides must shift/delete in lockstep with its
        // RichTextRuns companion, or a deleted row's phonetic guide survives orphaned while a
        // surviving row's guide is left behind at its stale pre-delete address.
        _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.CellPhoneticGuides, _startRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleRowsDown(sheet, _startRow, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaRowsDown(sheet, _startRow, _count);
        _rowPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.RowPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetDown(sheet.RowPageBreaks, _startRow, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        // R102: must run BEFORE ShiftChartRowsDown below -- it needs each chart's PRE-delete
        // DataRange to tell whether the deleted band overlaps a Switch-Row/Column chart's plotted
        // series span (see RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsDown).
        _chartSeriesFormattingSnapshot = RowColumnShiftHelpers.CaptureChartSeriesFormatting(ctx.Workbook);
        RowColumnShiftHelpers.ShiftChartSeriesFormattingRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        RowColumnShiftHelpers.ShiftChartRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count, deletedTableNames));
        RowColumnShiftHelpers.ShiftAddressBearingRowsDown(ctx.Workbook, sheet, _addressStateSnapshot, _startRow, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var adjustedMerges = new List<GridRange>();
        foreach (var m in sheet.MergedRegions)
        {
            if (m.End.Row < _startRow)
            {
                adjustedMerges.Add(m); // entirely above
            }
            else if (m.Start.Row > endRow)
            {
                // entirely below — shift up
                adjustedMerges.Add(new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row - _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   - _count, m.End.Col)));
            }
            else
            {
                // overlapping — shrink
                uint newStart = m.Start.Row < _startRow ? m.Start.Row : _startRow;
                uint newEnd   = m.End.Row   > endRow    ? m.End.Row - _count
                              : _startRow > 1           ? _startRow - 1 : 0;
                // Drop only when the row range is entirely consumed (no surviving rows) OR
                // when the result is a true single cell (1 row tall AND 1 column wide).
                // A region that becomes 1 row tall but still spans multiple columns is a valid
                // horizontal merge in Excel and must be kept.
                bool rowRangeGone = newEnd == 0 || newEnd < newStart;
                bool singleRow = newEnd == newStart;
                bool multiCol = m.Start.Col != m.End.Col;

                if (!rowRangeGone && (singleRow ? multiCol : true))
                {
                    adjustedMerges.Add(new GridRange(
                        new CellAddress(m.Start.Sheet, newStart, m.Start.Col),
                        new CellAddress(m.End.Sheet,   newEnd,   m.End.Col)));
                }
                // if row range gone, or shrinks to single cell (1×1), drop it
            }
        }
        sheet.ReplaceMergedRegions(adjustedMerges);

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count, deletedTableNames), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count, deletedTableNames), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count, deletedTableNames), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        // R92-commands-undo-structural-format-5-1: mirror InsertRowsCommand's RebandTable call
        // (R90-io-table-style-banding-5-3) on the delete side. MoveCellsForDelete above relocates
        // every shifted cell (and its baked-in StyleId/fill) intact to its new row -- it never
        // repaints banding -- so a row-banded structured table's alternating stripe fill is left at
        // its PRE-delete parity for every row below the deleted band, inverted relative to each
        // row's new position. Real Excel's table banding is purely positional and reflows
        // immediately after any row delete just like it does after an insert.
        _tableRebandSnapshot = RebandTablesAfterRowDelete(ctx.Workbook, sheet);

        // R103-commands-dependency-deleted-band-1: mirror DeleteCellsCommand's Apply-side fix
        // (InsertDeleteCellsCommand.cs, `_range.AllCells()`) for the band that this delete
        // PERMANENTLY removes. deletedSnapshot holds every cell that lived inside [_startRow, endRow]
        // (already ClearCell'd above) -- unlike shiftedSnapshot, none of these addresses are ever fed
        // into RelocatedFormulaCellsPendingDependencyRefresh/VacatedAddressesForShiftedFormulaCells
        // (both shifted-only) or _formulaSnapshot (populated by RewriteAllFormulas, which scans the
        // sheet AFTER the deleted band was cleared, so it can never see a formula cell that lived
        // there). Without this, a formula cell inside the deleted band that is never re-occupied by a
        // relocated survivor (e.g. it was the last formula in its column) leaves its stale
        // DependencyGraph precedent/dependent entries in place forever, since
        // WorkbookCellEditService.UpdateFormulaDependencies only ever visits AffectedCells.
        _affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, shiftedSnapshot, _count, _formulaSnapshot)
                .Concat(VacatedAddressesForShiftedFormulaCells(_sheetId, shiftedSnapshot))
                .Concat(deletedSnapshot.Select(s => s.ToAddress(_sheetId))),
            _formulaSnapshot);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // R98-commands-dependency-vacated-1: mirror InsertRowsCommand's
    // VacatedAddressesForRelocatedFormulaCells fix (InsertDeleteRowsCommand.cs) on the delete-rows
    // side. MoveCellsForDelete above physically relocates every shiftedSnapshot formula cell from its
    // captured (Row, Col) to (Row - count, Col), always leaving the OLD (pre-delete) address blank
    // afterward -- Delete only ever shifts rows UP, so nothing below endRow can move down into the
    // vacated slot. Neither RelocatedFormulaCellsPendingDependencyRefresh (new-address only) nor
    // _formulaSnapshot (also new-address only) ever surfaced this OLD address in AffectedCells, so
    // WorkbookCellEditService.UpdateFormulaDependencies (driven purely off AffectedCells) never
    // purged the stale dependency-graph entry left behind there.
    private static IEnumerable<CellAddress> VacatedAddressesForShiftedFormulaCells(
        SheetId sheetId, IEnumerable<CellStateSnapshot> shiftedSnapshot)
    {
        foreach (var snapshot in shiftedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return new CellAddress(sheetId, snapshot.Row, snapshot.Col);
        }
    }

    // R69-calc-dependency-insert-6-1: mirror InsertRowsCommand's
    // RelocatedFormulaCellsPendingDependencyRefresh fix (InsertDeleteRowsCommand.cs) on the
    // delete-rows side. A relocated formula cell whose text needs no rewrite (its cell references
    // are unaffected by the row shift, e.g. a volatile 0-arg function or a formula referencing a row
    // outside the shifted band) is never added to _formulaSnapshot by RewriteAllFormulas, so it would
    // otherwise be absent from AffectedCells and the dependency graph would never re-register it at
    // its new, shifted-up address -- orphaning it so an edit to its precedent never triggers a
    // recalc of the (stale) relocated cell.
    private static IEnumerable<CellAddress> RelocatedFormulaCellsPendingDependencyRefresh(
        SheetId sheetId,
        List<CellStateSnapshot> shiftedSnapshot,
        uint count,
        Dictionary<CellAddress, string> formulaSnapshot)
    {
        foreach (var snapshot in shiftedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            var newAddr = new CellAddress(sheetId, snapshot.Row - count, snapshot.Col);
            if (!formulaSnapshot.ContainsKey(newAddr))
                yield return newAddr;
        }
    }

    // R92-commands-undo-structural-format-5-1: re-flows every row-banded/column-banded structured
    // table's stripe fill after the physical row shift above, matching InsertRowsCommand's
    // FillGrownCalculatedColumnsForInsertedRows -> RebandTable call (R90-io-table-style-banding-5-3)
    // for the delete side. Only a table whose OWN rows were removed by this delete (Start.Row
    // unchanged, End.Row pulled up) has its internal row parity disturbed -- a table that shifted up
    // as a whole (its Start.Row moved too, because the deleted band sat entirely above it) or one
    // entirely unaffected keeps its pre-existing internal offsets and needs no repaint.
    private List<(CellAddress Address, Cell? OldCell)> RebandTablesAfterRowDelete(Workbook workbook, Sheet sheet)
    {
        var captured = new List<(CellAddress Address, Cell? OldCell)>();
        if (_addressStateSnapshot is null)
            return captured;

        foreach (var resizedTable in sheet.StructuredTables)
        {
            var previousTable = _addressStateSnapshot.StructuredTables.FirstOrDefault(t => t.Id == resizedTable.Id);
            if (previousTable is null ||
                previousTable.Range.Start.Row != resizedTable.Range.Start.Row ||
                resizedTable.Range.End.Row >= previousTable.Range.End.Row)
                continue;

            var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(resizedTable);
            if (lastDataRow >= firstDataRow)
            {
                // Capture the pre-reband state of every data-body cell before RebandTable below
                // repaints its stripe fill onto them. MoveCellsForDelete above relocates only
                // previously-OCCUPIED cells (already captured/restored via _shiftedSnapshot) -- a
                // cell that was blank both before and after the shift has no other undo coverage,
                // so without this a blank cell RebandTable materializes purely to hold a repainted
                // stripe (R90's no-materialize rule still lets it create a cell when the computed
                // style is a genuine visible change) would survive undo as a permanent leftover.
                for (var row = firstDataRow; row <= lastDataRow; row++)
                {
                    for (var col = resizedTable.Range.Start.Col; col <= resizedTable.Range.End.Col; col++)
                    {
                        var address = new CellAddress(sheet.Id, row, col);
                        captured.Add((address, sheet.GetCell(address)?.Clone()));
                    }
                }
            }

            StructuredTableStyleService.RebandTable(workbook, sheet, resizedTable);
        }

        return captured;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R92-commands-undo-structural-format-5-1: undo the reband repaint FIRST -- it was the very
        // last effect Apply performed. A cell RebandTable materialized purely to hold a repainted
        // stripe has no other undo coverage (see RebandTablesAfterRowDelete); a cell that already
        // held content is also captured here but is harmlessly re-overwritten (with the same value)
        // by the general shifted/deleted-cell restore below.
        if (_tableRebandSnapshot is not null)
        {
            foreach (var (address, oldCell) in _tableRebandSnapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell);
            }
        }

        // R96-commands-undo-affected-cells-1: RestoreFormulas below clears _formulaSnapshot as its
        // last step, so capture its keys (the post-delete addresses of every stationary-or-shifted
        // formula cell whose text was rewritten by Apply) now, before that happens -- needed to
        // recompute _affectedCells at the end of this method.
        var formulaSnapshotAddressesBeforeRestore = _formulaSnapshot.Keys.ToList();

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(ctx.Workbook, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        // R20-array-dynamic-spill-1: mirror MoveCellsForDelete's spill-relocation fix for undo —
        // capture any live spill rooted at the shifted-down address before clearing it back.
        var shiftedSpillPayloads = new RangeValue?[_shiftedSnapshot.Count];
        for (var i = 0; i < _shiftedSnapshot.Count; i++)
        {
            var s = _shiftedSnapshot[i];
            shiftedSpillPayloads[i] = sheet.CaptureSpillForRelocate(new CellAddress(sheet.Id, s.Row - _count, s.Col));
        }

        foreach (var snapshot in _shiftedSnapshot)
            sheet.ClearCell(snapshot.Row - _count, snapshot.Col);

        for (var i = 0; i < _shiftedSnapshot.Count; i++)
        {
            var snapshot = _shiftedSnapshot[i];
            var addr = snapshot.ToAddress(sheet.Id);
            sheet.SetCell(addr, snapshot.ToCell());
            if (shiftedSpillPayloads[i] is { } payload)
                sheet.SetSpillRange(addr, payload);
        }

        foreach (var snapshot in _deletedSnapshot)
            sheet.SetCell(snapshot.ToAddress(sheet.Id), snapshot.ToCell());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.HiddenRows, _hiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.FilterHiddenRows, _filterHiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.ValueFilterHiddenRows, _valueFilterHiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnFilterOwnedRows, _columnFilterOwnedRowsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(ctx.Workbook, _otherSheetHyperlinkBookmarkSnapshot);
        RowColumnShiftHelpers.RestoreRangeHyperlinks(sheet, _rangeHyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CellPhoneticGuides, _phoneticGuideSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesFormatting(ctx.Workbook, _chartSeriesFormattingSnapshot);
        RowColumnShiftHelpers.RestoreChartPositions(_chartPositionSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);

        // R96-commands-undo-affected-cells-1: recompute AffectedCells to reflect where every
        // formula cell ACTUALLY ended up after this Revert -- the shifted-back cells' original
        // (pre-delete) address AND the restored-deleted cells' original address (the latter never
        // appeared in Apply's own AffectedCells at all, since forward Apply only needs to report
        // the shifted set -- the deleted cells didn't exist yet). CommandBus.Undo reads this live
        // property instead of the frozen forward payload.
        // R98-commands-dependency-vacated-1: symmetric to the Apply-side fix above -- Revert
        // physically moves each shiftedSnapshot formula cell back from its current (post-delete,
        // shifted-up) address (Row - _count, Col) to its restored, pre-delete address (Row, Col),
        // always leaving (Row - _count, Col) blank afterward (undo of a Delete only ever shifts rows
        // DOWN, so nothing above can move up into it). That vacated address was never included in
        // AffectedCells either, leaving the identical stale dependency-graph entry behind after Undo.
        _affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_shiftedSnapshot, _sheetId)
                .Concat(RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_deletedSnapshot, _sheetId))
                .Concat(_tableRebandSnapshot?.Select(f => f.Address) ?? [])
                .Concat(formulaSnapshotAddressesBeforeRestore)
                .Concat(VacatedAddressesAfterRevertForShiftedCells(_sheetId, _shiftedSnapshot, _count)),
            []);
    }

    private static IEnumerable<CellAddress> VacatedAddressesAfterRevertForShiftedCells(
        SheetId sheetId, IEnumerable<CellStateSnapshot> shiftedSnapshot, uint count)
    {
        foreach (var snapshot in shiftedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return new CellAddress(sheetId, snapshot.Row - count, snapshot.Col);
        }
    }

    private (List<CellStateSnapshot> Deleted, List<CellStateSnapshot> Shifted)
        CaptureDeletedAndShiftedCells(Sheet sheet, uint endRow)
    {
        if (_startRow <= FullSnapshotCapacityThreshold)
            return CaptureDeletedAndShiftedCellsWithFullCapacity(sheet, endRow);

        var (deletedCount, shiftedCount) = CountDeletedAndShiftedCells(sheet, endRow);
        var deleted = new List<CellStateSnapshot>(deletedCount);
        var shifted = new List<CellStateSnapshot>(shiftedCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row > endRow)
            {
                var addr = new CellAddress(sheet.Id, row, col);
                shifted.Add(CellStateSnapshot.Capture(addr, cell));
            }
            else if (row >= _startRow)
            {
                var addr = new CellAddress(sheet.Id, row, col);
                deleted.Add(CellStateSnapshot.Capture(addr, cell));
            }
        }

        return (deleted, shifted);
    }

    private (List<CellStateSnapshot> Deleted, List<CellStateSnapshot> Shifted)
        CaptureDeletedAndShiftedCellsWithFullCapacity(Sheet sheet, uint endRow)
    {
        var deleted = new List<CellStateSnapshot>();
        var shifted = new List<CellStateSnapshot>(sheet.CellCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row > endRow)
            {
                shifted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
            else if (row >= _startRow)
            {
                deleted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
        }

        return (deleted, shifted);
    }

    private (int Deleted, int Shifted) CountDeletedAndShiftedCells(Sheet sheet, uint endRow)
    {
        var deleted = 0;
        var shifted = 0;

        foreach (var ((row, _), _) in sheet.GetOccupiedCellMap())
        {
            if (row > endRow)
            {
                shifted++;
            }
            else if (row >= _startRow)
            {
                deleted++;
            }
        }

        return (deleted, shifted);
    }

    private static void MoveCellsForDelete(
        Sheet sheet,
        IReadOnlyList<CellStateSnapshot> shiftedCells,
        uint count)
    {
        if (shiftedCells.Count == 0)
            return;

        var originals = ArrayPool<Cell>.Shared.Rent(shiftedCells.Count);
        // R20-array-dynamic-spill-1: capture any live spill rooted at each shifted cell BEFORE it is
        // cleared/moved, so a relocated dynamic-array anchor (e.g. =SEQUENCE with no cell references,
        // whose formula text never changes on a row shift) keeps spilling at its new address instead
        // of silently collapsing to a stale scalar.
        var spillPayloads = new RangeValue?[shiftedCells.Count];
        try
        {
            for (var i = 0; i < shiftedCells.Count; i++)
            {
                originals[i] = sheet.GetCell(shiftedCells[i].Row, shiftedCells[i].Col)!;
                spillPayloads[i] = sheet.CaptureSpillForRelocate(
                    new CellAddress(sheet.Id, shiftedCells[i].Row, shiftedCells[i].Col));
            }

            for (var i = 0; i < shiftedCells.Count; i++)
                sheet.ClearCell(shiftedCells[i].Row, shiftedCells[i].Col);

            for (var i = 0; i < shiftedCells.Count; i++)
            {
                var snapshot = shiftedCells[i];
                var newAddr = new CellAddress(sheet.Id, snapshot.Row - count, snapshot.Col);
                sheet.SetCell(newAddr, originals[i]);
                if (spillPayloads[i] is { } payload)
                    sheet.SetSpillRange(newAddr, payload);
            }
        }
        finally
        {
            Array.Clear(originals, 0, shiftedCells.Count);
            ArrayPool<Cell>.Shared.Return(originals);
        }
    }
}
