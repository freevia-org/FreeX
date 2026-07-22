using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank columns before <paramref name="beforeCol"/>.</summary>
public sealed class InsertColumnsCommand : IWorkbookCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _beforeCol;
    private readonly uint _count;
    private List<CellStateSnapshot>? _movedSnapshot;
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
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private List<RowColumnShiftHelpers.ChartSeriesColumnMappingsWorkbookSnapshot>? _chartSeriesColumnMappingsSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Insert {_count} Column(s)";

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
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxCol)
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
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.RichTextRuns, _beforeCol, _count);

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
        RowColumnShiftHelpers.ShiftChartColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.ShiftChartSeriesColumnMappingsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count));
        RowColumnShiftHelpers.ShiftAddressBearingColumnsUp(ctx.Workbook, sheet, _addressStateSnapshot, _beforeCol, _count);

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
        RowColumnShiftHelpers.RewriteRuleFormulas(sheet, new InsertColsOp(sheet.Name, _beforeCol, _count), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, movedSnapshot, _count, _formulaSnapshot),
                _formulaSnapshot));
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

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

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
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        RowColumnShiftHelpers.RestoreRuleRangesInPlace(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesColumnMappings(ctx.Workbook, _chartSeriesColumnMappingsSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
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
public sealed class DeleteColumnsCommand : IWorkbookCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _count;
    private List<CellStateSnapshot>? _deletedSnapshot;
    private List<CellStateSnapshot>? _shiftedSnapshot;
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
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private List<RowColumnShiftHelpers.ChartSeriesColumnMappingsWorkbookSnapshot>? _chartSeriesColumnMappingsSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Delete {_count} Column(s)";

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
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.RichTextRuns, _startCol, _count);

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
        RowColumnShiftHelpers.ShiftChartColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.ShiftChartSeriesColumnMappingsDown(ctx.Workbook, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count));
        RowColumnShiftHelpers.ShiftAddressBearingColumnsDown(ctx.Workbook, sheet, _addressStateSnapshot, _startCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.ReplaceMergedRegions(RowColumnShiftHelpers.DeleteColumnsFromMergedRegions(
            sheet.MergedRegions,
            _startCol,
            _count));

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(sheet, new DeleteColsOp(sheet.Name, _startCol, _count), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                RelocatedFormulaCellsPendingDependencyRefresh(_sheetId, shiftedSnapshot, _count, _formulaSnapshot),
                _formulaSnapshot));
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

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

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
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartSeriesColumnMappings(ctx.Workbook, _chartSeriesColumnMappingsSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
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
