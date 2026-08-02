using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank columns before <paramref name="beforeCol"/>.</summary>
public sealed class InsertColumnsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _beforeCol;
    private readonly uint _count;
    private List<CellStateSnapshot>? _movedSnapshot;
    // R96-commands-undo-affected-cells-1: mutated by both Apply (post-shift addresses) and Revert
    // (original, pre-shift addresses) so CommandBus.Undo can report the CURRENT set of relocated
    // formula cells instead of the frozen forward payload -- see
    // RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress.
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private List<GridRange>? _mergeSnapshot;
    private List<KeyValuePair<uint, double>>? _columnWidthSnapshot;
    private List<KeyValuePair<uint, IReadOnlyList<string>>>? _activeValueFilterColumnsSnapshot;
    private List<KeyValuePair<uint, HashSet<uint>>>? _columnFilterOwnedRowsSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentAuthorsSnapshot;
    private List<CellAddress>? _shownCommentsSnapshot;
    private List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedCommentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _hyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadataSnapshot;
    private List<RowColumnShiftHelpers.HyperlinkOtherSheetChange>? _otherSheetHyperlinkBookmarkSnapshot;
    // R106-io-hyperlink-range-shift: see Sheet.RangeHyperlinks -- whole-column/row and oversized-
    // bounded hyperlink refs shift independently of the CellAddress-keyed dictionaries above.
    private List<KeyValuePair<string, GridRange>>? _rangeHyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<KeyValuePair<CellAddress, CellPhoneticGuide>>? _phoneticGuideSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private List<RowColumnShiftHelpers.ChartSeriesColumnMappingsWorkbookSnapshot>? _chartSeriesColumnMappingsSnapshot;
    // R102: see RowColumnShiftHelpers.ShiftChartSeriesFormattingColumnsUp — every SeriesIndex-keyed
    // per-series/per-point chart override (SeriesFormats, PointFillColors, trendline/error-bar
    // series index, legend entries, etc.), tracked separately from the two snapshots above (which
    // only cover DataRange and SeriesColumnMappings).
    private List<RowColumnShiftHelpers.ChartSeriesFormattingWorkbookSnapshot>? _chartSeriesFormattingSnapshot;
    // R86-commands-insert-move-refadjust-5-1: see RowColumnShiftHelpers.ShiftChartPositionColumnsUp/
    // Down — tracked separately from _chartSnapshot above, which only tracks DataRange.
    private List<RowColumnShiftHelpers.ChartPositionSnapshot>? _chartPositionSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    // R92-commands-undo-structural-format-5-2: see RebandTablesAfterColumnInsert.
    private List<(CellAddress Address, Cell? OldCell)>? _tableRebandSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Insert {_count} Column(s)";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public InsertColumnsCommand(SheetId sheetId, uint beforeCol, uint count = 1)
    {
        _sheetId   = sheetId;
        _beforeCol = beforeCol;
        _count     = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.InsertColumns) is { } protectedOutcome)
            return protectedOutcome;

        // R47-sibling-guard-asymmetry-sweep-2: mirror InsertRowsCommand's CSE-array/dynamic-spill
        // split guard (InsertDeleteRowsCommand.cs) on the column axis. Inserting columns shifts every
        // occupied cell at or after _beforeCol right by _count across every row — an array/spill
        // whose column extent straddles the insert point would have some members shift right while
        // others (to the left of it) stay put, splitting it with no error.
        var insertColumnsShiftRegion = new CellShiftRegion(1u, CellAddress.MaxRow, _beforeCol, CellAddress.MaxCol);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, insertColumnsShiftRegion)) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var (maxOccupied, movedSnapshot) = CaptureMovedCells(sheet);
        // R111-commands-insert-overflow-metadata-1: mirror InsertRowsCommand's identical fix --
        // maxOccupied above only sees columns holding an actual Cell object, missing a style-only
        // formatting band, ColumnWidths override, hidden-column flag, or outline level at/after the
        // insert point. Fold in the highest such column, but only when it actually falls within the
        // shifted region (>= _beforeCol); HighestFormattedOrOccupiedColumn is sheet-wide, and a
        // formatted column BEFORE the insert point never moves.
        var highestFormattedCol = RowColumnShiftHelpers.HighestFormattedOrOccupiedColumn(sheet);
        var maxOccupiedOrFormatted = Math.Max(
            maxOccupied,
            highestFormattedCol >= _beforeCol ? highestFormattedCol : 0);
        if (maxOccupiedOrFormatted > 0 && maxOccupiedOrFormatted + _count > Model.CellAddress.MaxCol)
            return new CommandOutcome(false,
                ErrorMessage: CommandGuards.CannotInsertColumnsPastLastColumn(_count));

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        _movedSnapshot = movedSnapshot;

        MoveCellsForInsert(sheet, _movedSnapshot, _count);

        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.HiddenCols, _beforeCol, _count);

        _columnWidthSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ColumnWidths);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.ColumnWidths, _beforeCol, _count);

        // G1: sheet.ActiveValueFilterColumns is keyed by absolute column index exactly like
        // ColumnWidths, so it must shift the same way or a filtered column's criteria silently
        // apply to whatever column ends up at its old (now stale) index.
        _activeValueFilterColumnsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ActiveValueFilterColumns);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.ActiveValueFilterColumns, _beforeCol, _count);

        // R13-meta-2: sheet.ColumnFilterOwnedRows is the identically column-keyed row-ownership map
        // added alongside ActiveValueFilterColumns and must shift the same way, or a filter column's
        // owned-hidden-row bookkeeping is mis-attributed to whatever column ends up at its old index.
        _columnFilterOwnedRowsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ColumnFilterOwnedRows);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.ColumnFilterOwnedRows, _beforeCol, _count);

        _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.Comments, _beforeCol, _count);
        // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
        // author + pinned/"Show Comment" state) and must shift in lockstep with it, or a note's
        // author/pinned box goes stale at the note's old address after the insert.
        _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.CommentAuthors, _beforeCol, _count);
        _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
        RowColumnShiftHelpers.ShiftCommentSetColumnsUp(sheet.ShownComments, _beforeCol, _count);
        _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.ThreadedComments, _beforeCol, _count);
        _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.Hyperlinks, _beforeCol, _count);
        _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.HyperlinkMetadata, _beforeCol, _count);
        _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
            ctx.Workbook, sheet, new InsertColsOp(sheet.Name, _beforeCol, _count), sheet.Name);
        _rangeHyperlinkSnapshot = RowColumnShiftHelpers.CaptureRangeHyperlinks(sheet);
        RowColumnShiftHelpers.ShiftRangeHyperlinksColumnsUp(sheet, _beforeCol, _count);
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.RichTextRuns, _beforeCol, _count);
        // R78-selfreg-twin-sweep-2: sheet.CellPhoneticGuides must shift in lockstep with its
        // RichTextRuns companion, or an inserted column leaves a phonetic guide keyed to the
        // cell's stale pre-insert address while the rich text it decorates moves to the new one.
        _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.CellPhoneticGuides, _beforeCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleColumnsUp(sheet, _beforeCol, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaColumnsUp(sheet, _beforeCol, _count);
        _columnPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.ColumnPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetUp(sheet.ColumnPageBreaks, _beforeCol, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        _chartSeriesColumnMappingsSnapshot = RowColumnShiftHelpers.CaptureChartSeriesColumnMappings(ctx.Workbook);
        _chartSeriesFormattingSnapshot = RowColumnShiftHelpers.CaptureChartSeriesFormatting(ctx.Workbook);
        // R102: must run BEFORE ShiftChartColumnsUp below — it needs each chart's PRE-insert
        // DataRange to tell whether _beforeCol lands strictly inside the plotted data-column span.
        RowColumnShiftHelpers.ShiftChartSeriesFormattingColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.ShiftChartColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.ShiftChartSeriesColumnMappingsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count));
        // R86-commands-insert-move-refadjust-5-1: see ShiftChartPositionColumnsUp — columns before
        // _beforeCol are untouched by this insert, so it is safe to read sheet.ColumnWidths here
        // regardless of whether the ColumnWidths re-key above has already run.
        _chartPositionSnapshot = RowColumnShiftHelpers.CaptureChartPositions(sheet);
        RowColumnShiftHelpers.ShiftChartPositionColumnsUp(sheet, _beforeCol, _count);
        RowColumnShiftHelpers.ShiftAddressBearingColumnsUp(ctx.Workbook, sheet, _addressStateSnapshot, _beforeCol, _count);

        // R92-render-cellstyle-inheritance-5-2: Excel's Insert Sheet Columns default ("Insert
        // Options") inherits the format of the column to the left into the newly-vacated band.
        // Must run after the ShiftAddressBearingColumnsUp call above, which rebuilds the whole
        // style-only store from the pre-insert snapshot and would otherwise wipe these new entries.
        RowColumnShiftHelpers.InheritVacatedColumnFormatFromLeft(sheet, _beforeCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.ReplaceMergedRegions(RowColumnShiftHelpers.InsertColumnsIntoMergedRegions(
            sheet.MergedRegions,
            _beforeCol,
            _count));

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        // R92-commands-undo-structural-format-5-2: mirror InsertRowsCommand's RebandTable call
        // (R90-io-table-style-banding-5-3) on the column axis. MoveCellsForInsert above relocates
        // every shifted cell (and its baked-in StyleId/fill) intact to its new column -- it never
        // repaints banding -- so a column-banded structured table's alternating stripe fill is left
        // at its PRE-insert column position for every column after the insert point. Excel's table
        // banding is purely positional on both axes and reflows immediately after any structural
        // edit.
        _tableRebandSnapshot = RebandTablesAfterColumnInsert(ctx.Workbook, sheet);

        _affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, movedSnapshot, _count, _formulaSnapshot)
                .Concat(VacatedAddressesForRelocatedFormulaCells(_sheetId, movedSnapshot)),
            _formulaSnapshot);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // R98-commands-dependency-vacated-1: mirror InsertRowsCommand's
    // VacatedAddressesForRelocatedFormulaCells fix (InsertDeleteRowsCommand.cs) on the
    // insert-columns axis. MoveCellsForInsert above physically relocates every movedSnapshot formula
    // cell from its captured (Row, Col) to (Row, Col + count), always leaving the OLD (pre-shift)
    // address blank afterward -- Insert only ever shifts columns RIGHT, so nothing to the left of
    // _beforeCol can move right into the vacated slot. Neither
    // RelocatedFormulaCellsPendingDependencyRefresh (new-address only) nor _formulaSnapshot
    // (also new-address only) ever surfaced this OLD address in AffectedCells, so
    // WorkbookCellEditService.UpdateFormulaDependencies (driven purely off AffectedCells) never
    // purged the stale dependency-graph entry left behind there.
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

    // R92-commands-undo-structural-format-5-2: re-flows every column-banded structured table's
    // stripe fill after the physical column shift above, matching InsertRowsCommand's row-axis
    // RebandTable call (R90) and DeleteColumnsCommand's RebandTablesAfterColumnDelete (below) for
    // axis symmetry. Only a table whose insert point fell strictly inside it (Start.Col unchanged,
    // End.Col pushed right) has its internal column parity disturbed -- a table that shifted right
    // as a whole (its Start.Col moved too, because the insert landed at or before its first column)
    // or one entirely unaffected keeps its pre-existing internal offsets and needs no repaint.
    private List<(CellAddress Address, Cell? OldCell)> RebandTablesAfterColumnInsert(Workbook workbook, Sheet sheet)
    {
        var captured = new List<(CellAddress Address, Cell? OldCell)>();
        if (_addressStateSnapshot is null)
            return captured;

        foreach (var resizedTable in sheet.StructuredTables)
        {
            var previousTable = _addressStateSnapshot.StructuredTables.FirstOrDefault(t => t.Id == resizedTable.Id);
            if (previousTable is null ||
                previousTable.Range.Start.Col != resizedTable.Range.Start.Col ||
                resizedTable.Range.End.Col <= previousTable.Range.End.Col)
                continue;

            var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(resizedTable);
            if (lastDataRow >= firstDataRow)
            {
                // Capture the pre-reband state of every data-body cell before RebandTable below
                // repaints its stripe fill onto them. MoveCellsForInsert above only relocates
                // previously-OCCUPIED cells (already captured/restored via _movedSnapshot) -- every
                // cell in the newly-inserted column band is brand new (no prior existence anywhere),
                // so without this a cell RebandTable materializes purely to hold a repainted stripe
                // would have no undo coverage at all and would survive undo as a permanent leftover.
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

    // R69-calc-dependency-insert-6-1: mirror InsertRowsCommand's
    // RelocatedFormulaCellsPendingDependencyRefresh fix (InsertDeleteRowsCommand.cs) on the
    // insert-columns side. A relocated formula cell whose text needs no rewrite (its cell
    // references are unaffected by the column shift, e.g. a volatile 0-arg function or a formula
    // referencing a column outside the shifted band) is never added to _formulaSnapshot by
    // RewriteAllFormulas, so it would otherwise be absent from AffectedCells and the dependency
    // graph would never re-register it at its new, shifted-right address -- orphaning it so an edit
    // to its precedent never triggers a recalc of the (stale) relocated cell.
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

            var newAddr = new CellAddress(sheetId, snapshot.Row, snapshot.Col + count);
            if (!formulaSnapshot.ContainsKey(newAddr))
                yield return newAddr;
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R92-commands-undo-structural-format-5-2: undo the reband repaint FIRST -- it was the very
        // last effect Apply performed (after the physical column move below). Every address here
        // falls strictly inside the newly-inserted column band, which the moved-cell restore below
        // is about to repopulate with the original pre-insert content (moved cells always restore
        // back to their pre-shift address, i.e. this same band's neighbors) -- undoing the fill
        // afterward would instead clobber that just-restored data. A cell that already held content
        // is also captured here but is harmlessly re-overwritten by the general restore.
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
        // last step, so capture its keys (the post-shift addresses of every stationary-or-moved
        // formula cell whose text was rewritten by Apply) now, before that happens -- needed to
        // recompute _affectedCells at the end of this method.
        var formulaSnapshotAddressesBeforeRestore = _formulaSnapshot.Keys.ToList();

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(ctx.Workbook, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        // R20-array-dynamic-spill-1: mirror MoveCellsForInsert's spill-relocation fix for undo —
        // capture any live spill rooted at the shifted-right address before clearing it back.
        var movedSpillPayloads = new RangeValue?[_movedSnapshot.Count];
        for (var i = 0; i < _movedSnapshot.Count; i++)
        {
            var s = _movedSnapshot[i];
            movedSpillPayloads[i] = sheet.CaptureSpillForRelocate(new CellAddress(sheet.Id, s.Row, s.Col + _count));
        }

        foreach (var snapshot in _movedSnapshot)
            sheet.ClearCell(snapshot.Row, snapshot.Col + _count);

        for (var i = 0; i < _movedSnapshot.Count; i++)
        {
            var snapshot = _movedSnapshot[i];
            var addr = snapshot.ToAddress(sheet.Id);
            sheet.SetCell(addr, snapshot.ToCell());
            if (movedSpillPayloads[i] is { } payload)
                sheet.SetSpillRange(addr, payload);
        }

        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.HiddenCols, _beforeCol + _count, _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ActiveValueFilterColumns, _activeValueFilterColumnsSnapshot);
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
        RowColumnShiftHelpers.RestoreRuleRangesInPlace(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesColumnMappings(ctx.Workbook, _chartSeriesColumnMappingsSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesFormatting(ctx.Workbook, _chartSeriesFormattingSnapshot);
        RowColumnShiftHelpers.RestoreChartPositions(_chartPositionSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);

        // R96-commands-undo-affected-cells-1: recompute AffectedCells to reflect where every
        // relocated formula cell ACTUALLY ended up after this Revert -- its original, pre-shift
        // address (mirroring Apply's own AffectedCells, which reports the post-shift address for
        // the forward direction). CommandBus.Undo reads this live property instead of the frozen
        // forward payload.
        // R98-commands-dependency-vacated-1: symmetric to the Apply-side fix above -- Revert
        // physically moves each movedSnapshot formula cell back from its current (post-shift)
        // address (Row, Col + _count) to its restored, pre-shift address (Row, Col), always leaving
        // (Row, Col + _count) blank afterward (undo of an Insert only ever shifts columns LEFT, so
        // nothing to the right can move left into it). That vacated address was never included in
        // AffectedCells either, leaving the identical stale dependency-graph entry behind after Undo.
        _affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_movedSnapshot, _sheetId)
                .Concat(_tableRebandSnapshot?.Select(f => f.Address) ?? [])
                .Concat(formulaSnapshotAddressesBeforeRestore)
                .Concat(VacatedAddressesAfterRevert(_sheetId, _movedSnapshot, _count)),
            []);
    }

    private static IEnumerable<CellAddress> VacatedAddressesAfterRevert(
        SheetId sheetId, IEnumerable<CellStateSnapshot> movedSnapshot, uint count)
    {
        foreach (var snapshot in movedSnapshot)
        {
            if (snapshot.FormulaText is null)
                continue;

            yield return new CellAddress(sheetId, snapshot.Row, snapshot.Col + count);
        }
    }

    private (uint MaxOccupied, List<CellStateSnapshot> Moved) CaptureMovedCells(Sheet sheet)
    {
        if (_beforeCol <= FullSnapshotCapacityThreshold)
            return CaptureMovedCellsWithFullCapacity(sheet);

        var movedCount = CountMovedCells(sheet, out var maxOccupied);
        var moved = new List<CellStateSnapshot>(movedCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col < _beforeCol)
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
            if (col < _beforeCol)
                continue;

            if (col > maxOccupied)
                maxOccupied = col;

            moved.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
        }

        return (maxOccupied, moved);
    }

    private int CountMovedCells(Sheet sheet, out uint maxOccupied)
    {
        var movedCount = 0;
        maxOccupied = 0;

        foreach (var ((_, col), _) in sheet.GetOccupiedCellMap())
        {
            if (col < _beforeCol)
                continue;

            movedCount++;
            if (col > maxOccupied)
                maxOccupied = col;
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
        // whose formula text never changes on a column shift) keeps spilling at its new address
        // instead of silently collapsing to a stale scalar.
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
                var newAddr = new CellAddress(sheet.Id, snapshot.Row, snapshot.Col + count);
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

/// <summary>Deletes <paramref name="count"/> columns starting at <paramref name="startCol"/>.</summary>
public sealed class DeleteColumnsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
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
    private List<KeyValuePair<uint, double>>? _columnWidthSnapshot;
    private List<KeyValuePair<uint, IReadOnlyList<string>>>? _activeValueFilterColumnsSnapshot;
    private List<KeyValuePair<uint, HashSet<uint>>>? _columnFilterOwnedRowsSnapshot;
    private List<uint>? _hiddenColsSnapshot;
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
    private List<uint>? _columnPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private List<RowColumnShiftHelpers.ChartSeriesColumnMappingsWorkbookSnapshot>? _chartSeriesColumnMappingsSnapshot;
    // R102: see RowColumnShiftHelpers.ShiftChartSeriesFormattingColumnsDown — every SeriesIndex-keyed
    // per-series/per-point chart override (SeriesFormats, PointFillColors, trendline/error-bar
    // series index, legend entries, etc.), tracked separately from the two snapshots above (which
    // only cover DataRange and SeriesColumnMappings).
    private List<RowColumnShiftHelpers.ChartSeriesFormattingWorkbookSnapshot>? _chartSeriesFormattingSnapshot;
    // R86-commands-insert-move-refadjust-5-1: see RowColumnShiftHelpers.ShiftChartPositionColumnsUp/
    // Down — tracked separately from _chartSnapshot above, which only tracks DataRange.
    private List<RowColumnShiftHelpers.ChartPositionSnapshot>? _chartPositionSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    // R92-commands-undo-structural-format-5-2: see RebandTablesAfterColumnDelete.
    private List<(CellAddress Address, Cell? OldCell)>? _tableRebandSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Delete {_count} Column(s)";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public DeleteColumnsCommand(SheetId sheetId, uint startCol, uint count = 1)
    {
        _sheetId  = sheetId;
        _startCol = startCol;
        _count    = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.DeleteColumns) is { } protectedOutcome)
            return protectedOutcome;

        uint endCol = _startCol + _count - 1;

        // R47-sibling-guard-asymmetry-sweep-2: mirror the delete-rows guard for columns. Deleting
        // whole columns removes exactly the band [_startCol, endCol] across every row while every
        // column to the right shifts left in lockstep — an array/spill straddling that band would
        // have part of it deleted and part of it shifted, splitting it with no error. An array
        // entirely inside the deleted band is removed as one atomic unit (fine); an array entirely
        // outside the band just rides the uniform shift (also fine).
        var deletedColumnsShiftRegion = new CellShiftRegion(1u, CellAddress.MaxRow, _startCol, endCol);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, deletedColumnsShiftRegion)) is { } deleteSplitsArrayRejection)
            return deleteSplitsArrayRejection;

        // R110-formula-structuredref-rowcoldelete-ref: mirror DeleteRowsCommand.Apply's
        // deletedTableNames capture (see FindStructuredTablesRemovedByRowDelete for the full
        // rationale) so every DeleteColsOp built below for a FormulaRewriter pass can convert any
        // remaining Table[...] reference to a fully-consumed table into #REF! instead of #NAME?.
        var deletedTableNames = RowColumnShiftHelpers.FindStructuredTablesRemovedByColumnDelete(sheet, _startCol, _count);
        // R115-formula-structuredref-coldelete-survivingtable-ref: counterpart for a table that
        // SURVIVES this delete but loses some of its columns (see
        // FindStructuredTableColumnsRemovedByColumnDelete for the full rationale) -- feeds
        // DeleteColsOp.DeletedColumnNamesByTable so the same FormulaRewriter passes convert a
        // remaining Table[DeletedColumn] reference into #REF! instead of leaving the stale column
        // name to fail as #NAME? once StructuredReferenceResolver can no longer find it.
        var deletedColumnNamesByTable = RowColumnShiftHelpers.FindStructuredTableColumnsRemovedByColumnDelete(sheet, _startCol, _count);

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        var (deletedSnapshot, shiftedSnapshot) = CaptureDeletedAndShiftedCells(sheet, endCol);
        _deletedSnapshot = deletedSnapshot;
        _shiftedSnapshot = shiftedSnapshot;

        foreach (var snapshot in deletedSnapshot)
            sheet.ClearCell(snapshot.Row, snapshot.Col);

        MoveCellsForDelete(sheet, shiftedSnapshot, _count);

        _hiddenColsSnapshot = RowColumnShiftHelpers.CaptureSet(sheet.HiddenCols);
        RowColumnShiftHelpers.DeleteSetRangeAndShiftDown(sheet.HiddenCols, _startCol, _count);

        _columnWidthSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ColumnWidths);
        // R86-commands-insert-move-refadjust-5-1: must run BEFORE sheet.ColumnWidths is re-keyed
        // below — the deleted band's own widths are needed to measure the removed band's pixel
        // width, and they are gone from the live dictionary once ShiftIndexesDown below has run.
        _chartPositionSnapshot = RowColumnShiftHelpers.CaptureChartPositions(sheet);
        RowColumnShiftHelpers.ShiftChartPositionColumnsDown(sheet, _startCol, _count, sheet.ColumnWidths, sheet.DefaultColumnWidth);
        RowColumnShiftHelpers.ShiftIndexesDown(sheet.ColumnWidths, _startCol, _count);

        // G1: same key-space as ColumnWidths — must shift/drop entries the same way, or a filter
        // column that was deleted (or lies after the deletion point) leaves a stale/misaligned key.
        _activeValueFilterColumnsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ActiveValueFilterColumns);
        RowColumnShiftHelpers.ShiftIndexesDown(sheet.ActiveValueFilterColumns, _startCol, _count);

        // R13-meta-2: same key-space as ActiveValueFilterColumns — must shift/drop entries the same
        // way, or a filter column's owned-hidden-row bookkeeping goes stale/misaligned after delete.
        _columnFilterOwnedRowsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ColumnFilterOwnedRows);
        RowColumnShiftHelpers.ShiftIndexesDown(sheet.ColumnFilterOwnedRows, _startCol, _count);

        _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.Comments, _startCol, _count);
        // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
        // author + pinned/"Show Comment" state) and must shift/delete in lockstep with it, or a
        // note's author/pinned box goes stale (or survives at a deleted address) after the delete.
        _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.CommentAuthors, _startCol, _count);
        _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
        RowColumnShiftHelpers.ShiftCommentSetColumnsDown(sheet.ShownComments, _startCol, _count);
        _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.ThreadedComments, _startCol, _count);
        _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.Hyperlinks, _startCol, _count);
        _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.HyperlinkMetadata, _startCol, _count);
        _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
            ctx.Workbook, sheet, new DeleteColsOp(sheet.Name, _startCol, _count), sheet.Name);
        _rangeHyperlinkSnapshot = RowColumnShiftHelpers.CaptureRangeHyperlinks(sheet);
        RowColumnShiftHelpers.ShiftRangeHyperlinksColumnsDown(sheet, _startCol, _count);
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.RichTextRuns, _startCol, _count);
        // R78-selfreg-twin-sweep-2: sheet.CellPhoneticGuides must shift/delete in lockstep with its
        // RichTextRuns companion, or a deleted column's phonetic guide survives orphaned while a
        // surviving column's guide is left behind at its stale pre-delete address.
        _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.CellPhoneticGuides, _startCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleColumnsDown(sheet, _startCol, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaColumnsDown(sheet, _startCol, _count);
        _columnPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.ColumnPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetDown(sheet.ColumnPageBreaks, _startCol, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        _chartSeriesColumnMappingsSnapshot = RowColumnShiftHelpers.CaptureChartSeriesColumnMappings(ctx.Workbook);
        _chartSeriesFormattingSnapshot = RowColumnShiftHelpers.CaptureChartSeriesFormatting(ctx.Workbook);
        // R102: must run BEFORE ShiftChartColumnsDown below — it needs each chart's PRE-delete
        // DataRange to tell which plotted series positions the deleted band actually removed.
        RowColumnShiftHelpers.ShiftChartSeriesFormattingColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.ShiftChartColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.ShiftChartSeriesColumnMappingsDown(ctx.Workbook, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count, deletedTableNames, deletedColumnNamesByTable));
        RowColumnShiftHelpers.ShiftAddressBearingColumnsDown(ctx.Workbook, sheet, _addressStateSnapshot, _startCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.ReplaceMergedRegions(RowColumnShiftHelpers.DeleteColumnsFromMergedRegions(
            sheet.MergedRegions,
            _startCol,
            _count));

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count, deletedTableNames, deletedColumnNamesByTable), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count, deletedTableNames, deletedColumnNamesByTable), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count, deletedTableNames, deletedColumnNamesByTable), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        // R92-commands-undo-structural-format-5-2: mirror DeleteRowsCommand's
        // RebandTablesAfterRowDelete (R92-commands-undo-structural-format-5-1) on the column axis.
        // MoveCellsForDelete above relocates every shifted cell (and its baked-in StyleId/fill)
        // intact to its new column -- it never repaints banding -- so a column-banded structured
        // table's alternating stripe fill is left at its PRE-delete column position for every
        // column after the deleted band. Excel's table banding is purely positional on both axes
        // and reflows immediately after any structural edit.
        _tableRebandSnapshot = RebandTablesAfterColumnDelete(ctx.Workbook, sheet);

        // R103-commands-dependency-deleted-band-1: mirror DeleteRowsCommand's Apply-side fix (and
        // DeleteCellsCommand's `_range.AllCells()` fix in InsertDeleteCellsCommand.cs) for the band
        // this delete PERMANENTLY removes. deletedSnapshot holds every cell that lived inside
        // [_startCol, endCol] (already ClearCell'd above) -- neither
        // RelocatedFormulaCellsPendingDependencyRefresh/VacatedAddressesForShiftedFormulaCells
        // (shifted-survivors only) nor _formulaSnapshot (populated by RewriteAllFormulas, which scans
        // the sheet AFTER the deleted band was cleared) ever surfaces these addresses. Without this a
        // formula cell inside the deleted band that is never re-occupied by a relocated survivor
        // leaves its stale DependencyGraph precedent/dependent entries in place forever.
        _affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, shiftedSnapshot, _count, _formulaSnapshot)
                .Concat(VacatedAddressesForShiftedFormulaCells(_sheetId, shiftedSnapshot))
                .Concat(deletedSnapshot.Select(s => s.ToAddress(_sheetId))),
            _formulaSnapshot);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // R98-commands-dependency-vacated-1: mirror DeleteRowsCommand's
    // VacatedAddressesForShiftedFormulaCells fix on the delete-columns axis. MoveCellsForDelete above
    // physically relocates every shiftedSnapshot formula cell from its captured (Row, Col) to
    // (Row, Col - count), always leaving the OLD (pre-delete) address blank afterward -- Delete only
    // ever shifts columns LEFT, so nothing to the right of endCol can move left into the vacated
    // slot. Neither RelocatedFormulaCellsPendingDependencyRefresh (new-address only) nor
    // _formulaSnapshot (also new-address only) ever surfaced this OLD address in AffectedCells.
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

    // R92-commands-undo-structural-format-5-2: re-flows every column-banded structured table's
    // stripe fill after the physical column shift above, matching InsertColumnsCommand's
    // RebandTablesAfterColumnInsert (above) and DeleteRowsCommand's row-axis RebandTablesAfterRowDelete
    // (R92-commands-undo-structural-format-5-1) for axis symmetry. Only a table whose OWN columns
    // were removed by this delete (Start.Col unchanged, End.Col pulled left) has its internal column
    // parity disturbed -- a table that shifted left as a whole (its Start.Col moved too, because the
    // deleted band sat entirely before it) or one entirely unaffected keeps its pre-existing internal
    // offsets and needs no repaint.
    private List<(CellAddress Address, Cell? OldCell)> RebandTablesAfterColumnDelete(Workbook workbook, Sheet sheet)
    {
        var captured = new List<(CellAddress Address, Cell? OldCell)>();
        if (_addressStateSnapshot is null)
            return captured;

        foreach (var resizedTable in sheet.StructuredTables)
        {
            var previousTable = _addressStateSnapshot.StructuredTables.FirstOrDefault(t => t.Id == resizedTable.Id);
            if (previousTable is null ||
                previousTable.Range.Start.Col != resizedTable.Range.Start.Col ||
                resizedTable.Range.End.Col >= previousTable.Range.End.Col)
                continue;

            var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(resizedTable);
            if (lastDataRow >= firstDataRow)
            {
                // Capture the pre-reband state of every data-body cell before RebandTable below
                // repaints its stripe fill onto them. MoveCellsForDelete above relocates only
                // previously-OCCUPIED cells (already captured/restored via _shiftedSnapshot) -- a
                // cell that was blank both before and after the shift has no other undo coverage,
                // so without this a blank cell RebandTable materializes purely to hold a repainted
                // stripe would survive undo as a permanent leftover.
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

    // R69-calc-dependency-insert-6-1: mirror InsertRowsCommand's
    // RelocatedFormulaCellsPendingDependencyRefresh fix (InsertDeleteRowsCommand.cs) on the
    // delete-columns side. A relocated formula cell whose text needs no rewrite (its cell
    // references are unaffected by the column shift, e.g. a volatile 0-arg function or a formula
    // referencing a column outside the shifted band) is never added to _formulaSnapshot by
    // RewriteAllFormulas, so it would otherwise be absent from AffectedCells and the dependency
    // graph would never re-register it at its new, shifted-left address -- orphaning it so an edit
    // to its precedent never triggers a recalc of the (stale) relocated cell.
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

            var newAddr = new CellAddress(sheetId, snapshot.Row, snapshot.Col - count);
            if (!formulaSnapshot.ContainsKey(newAddr))
                yield return newAddr;
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R92-commands-undo-structural-format-5-2: undo the reband repaint FIRST -- it was the very
        // last effect Apply performed. A cell RebandTable materialized purely to hold a repainted
        // stripe has no other undo coverage (see RebandTablesAfterColumnDelete); a cell that already
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
        // capture any live spill rooted at the shifted-left address before clearing it back.
        var shiftedSpillPayloads = new RangeValue?[_shiftedSnapshot.Count];
        for (var i = 0; i < _shiftedSnapshot.Count; i++)
        {
            var s = _shiftedSnapshot[i];
            shiftedSpillPayloads[i] = sheet.CaptureSpillForRelocate(new CellAddress(sheet.Id, s.Row, s.Col - _count));
        }

        foreach (var snapshot in _shiftedSnapshot)
            sheet.ClearCell(snapshot.Row, snapshot.Col - _count);

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

        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ActiveValueFilterColumns, _activeValueFilterColumnsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnFilterOwnedRows, _columnFilterOwnedRowsSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.HiddenCols, _hiddenColsSnapshot);
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
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesColumnMappings(ctx.Workbook, _chartSeriesColumnMappingsSnapshot);
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
        // shifted-left) address (Row, Col - _count) to its restored, pre-delete address (Row, Col),
        // always leaving (Row, Col - _count) blank afterward (undo of a Delete only ever shifts
        // columns RIGHT, so nothing to the left can move right into it). That vacated address was
        // never included in AffectedCells either, leaving the identical stale dependency-graph entry
        // behind after Undo.
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

            yield return new CellAddress(sheetId, snapshot.Row, snapshot.Col - count);
        }
    }

    private (List<CellStateSnapshot> Deleted, List<CellStateSnapshot> Shifted)
        CaptureDeletedAndShiftedCells(Sheet sheet, uint endCol)
    {
        if (_startCol <= FullSnapshotCapacityThreshold)
            return CaptureDeletedAndShiftedCellsWithFullCapacity(sheet, endCol);

        var (deletedCount, shiftedCount) = CountDeletedAndShiftedCells(sheet, endCol);
        var deleted = new List<CellStateSnapshot>(deletedCount);
        var shifted = new List<CellStateSnapshot>(shiftedCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col > endCol)
            {
                shifted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
            else if (col >= _startCol)
            {
                deleted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
        }

        return (deleted, shifted);
    }

    private (List<CellStateSnapshot> Deleted, List<CellStateSnapshot> Shifted)
        CaptureDeletedAndShiftedCellsWithFullCapacity(Sheet sheet, uint endCol)
    {
        var deleted = new List<CellStateSnapshot>();
        var shifted = new List<CellStateSnapshot>(sheet.CellCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col > endCol)
            {
                shifted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
            else if (col >= _startCol)
            {
                deleted.Add(CellStateSnapshot.Capture(new CellAddress(sheet.Id, row, col), cell));
            }
        }

        return (deleted, shifted);
    }

    private (int Deleted, int Shifted) CountDeletedAndShiftedCells(Sheet sheet, uint endCol)
    {
        var deleted = 0;
        var shifted = 0;

        foreach (var ((_, col), _) in sheet.GetOccupiedCellMap())
        {
            if (col > endCol)
            {
                shifted++;
            }
            else if (col >= _startCol)
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
        // whose formula text never changes on a column shift) keeps spilling at its new address
        // instead of silently collapsing to a stale scalar.
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
                var newAddr = new CellAddress(sheet.Id, snapshot.Row, snapshot.Col - count);
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
