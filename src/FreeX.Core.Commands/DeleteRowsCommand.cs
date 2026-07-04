using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Deletes <paramref name="count"/> rows starting at <paramref name="startRow"/>.</summary>
public sealed class DeleteRowsCommand : IWorkbookCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _count;
    private List<CellStateSnapshot>? _deletedSnapshot;
    private List<CellStateSnapshot>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private List<KeyValuePair<uint, double>>? _rowHeightSnapshot;
    private List<uint>? _hiddenRowsSnapshot;
    private List<uint>? _filterHiddenRowsSnapshot;
    private List<uint>? _valueFilterHiddenRowsSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentAuthorsSnapshot;
    private List<CellAddress>? _shownCommentsSnapshot;
    private List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedCommentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _hyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadataSnapshot;
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<GridRange>? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimSnapshot?>? _chartVerbatimSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Delete {_count} Row(s)";

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

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        (_deletedSnapshot, _shiftedSnapshot) = CaptureDeletedAndShiftedCells(sheet, endRow);

        foreach (var snapshot in _deletedSnapshot)
            sheet.ClearCell(snapshot.Row, snapshot.Col);

        MoveCellsForDelete(sheet, _shiftedSnapshot, _count);

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

        _rowHeightSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RowHeights);
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
        RowColumnShiftHelpers.ShiftHyperlinkBookmarks(sheet, new DeleteRowsOp(sheet.Name, _startRow, _count), sheet.Name);
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentRowsDown(sheet.RichTextRuns, _startRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleRowsDown(sheet, _startRow, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaRowsDown(sheet, _startRow, _count);
        _rowPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.RowPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetDown(sheet.RowPageBreaks, _startRow, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(sheet);
        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(sheet);
        RowColumnShiftHelpers.ShiftChartRowsDown(sheet, _sheetId, _startRow, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(sheet, new DeleteRowsOp(sheet.Name, _startRow, _count), sheet.Name);
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
            ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(sheet, new DeleteRowsOp(sheet.Name, _startRow, _count), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                Enumerable.Empty<CellAddress>(),
                _formulaSnapshot));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        foreach (var snapshot in _shiftedSnapshot)
            sheet.ClearCell(snapshot.Row - _count, snapshot.Col);

        foreach (var snapshot in _shiftedSnapshot)
            sheet.SetCell(snapshot.ToAddress(sheet.Id), snapshot.ToCell());

        foreach (var snapshot in _deletedSnapshot)
            sheet.SetCell(snapshot.ToAddress(sheet.Id), snapshot.ToCell());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.HiddenRows, _hiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.FilterHiddenRows, _filterHiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.ValueFilterHiddenRows, _valueFilterHiddenRowsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        sheet.SetPrintAreas(_printAreaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(sheet, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(sheet, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
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
        try
        {
            for (var i = 0; i < shiftedCells.Count; i++)
                originals[i] = sheet.GetCell(shiftedCells[i].Row, shiftedCells[i].Col)!;

            for (var i = 0; i < shiftedCells.Count; i++)
                sheet.ClearCell(shiftedCells[i].Row, shiftedCells[i].Col);

            for (var i = 0; i < shiftedCells.Count; i++)
            {
                var snapshot = shiftedCells[i];
                sheet.SetCell(new CellAddress(sheet.Id, snapshot.Row - count, snapshot.Col), originals[i]);
            }
        }
        finally
        {
            Array.Clear(originals, 0, shiftedCells.Count);
            ArrayPool<Cell>.Shared.Return(originals);
        }
    }
}
