using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank rows before <paramref name="beforeRow"/>.</summary>
public sealed class InsertRowsCommand : IWorkbookCommand
{
    private const uint FullSnapshotCapacityThreshold = 32;
    private readonly SheetId _sheetId;
    private readonly uint _beforeRow;
    private readonly uint _count;
    private List<CellStateSnapshot>? _movedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private List<KeyValuePair<uint, double>>? _rowHeightSnapshot;
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
    private List<uint>? _rowPageBreakSnapshot;
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];

    public string Label => $"Insert {_count} Row(s)";

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

        var (maxOccupied, movedSnapshot) = CaptureMovedCells(sheet);
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxRow)
            return new CommandOutcome(false,
                ErrorMessage: CommandGuards.CannotInsertRowsPastLastRow(_count));

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
        // R13-meta-1: sheet.ColumnFilterOwnedRows' HashSet row VALUES must shift the same way, or a
        // column's condition/color/Top-Bottom/Average filter forgets which row it actually owns and
        // orphans a permanently-hidden row the next time that column's filter is cleared/recomputed.
        RowColumnShiftHelpers.ShiftRowSetDictionaryUpFrom(sheet.ColumnFilterOwnedRows, _beforeRow, _count);

        _rowHeightSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RowHeights);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.RowHeights, _beforeRow, _count);

        _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Comments, _beforeRow, _count);
        // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
        // author + pinned/"Show Comment" state) and must shift in lockstep with it, or a note's
        // author/pinned box goes stale at the note's old address after the insert.
        _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.CommentAuthors, _beforeRow, _count);
        _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
        RowColumnShiftHelpers.ShiftCommentSetRowsUp(sheet.ShownComments, _beforeRow, _count);
        _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.ThreadedComments, _beforeRow, _count);
        _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Hyperlinks, _beforeRow, _count);
        _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.HyperlinkMetadata, _beforeRow, _count);
        _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
            ctx.Workbook, sheet, new InsertRowsOp(sheet.Name, _beforeRow, _count), sheet.Name);
        _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.RichTextRuns, _beforeRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleRowsUp(sheet, _beforeRow, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        _printAreaSnapshot = sheet.PrintAreas.ToList();
        RowColumnShiftHelpers.ShiftPrintAreaRowsUp(sheet, _beforeRow, _count);
        _rowPageBreakSnapshot = RowColumnShiftHelpers.CaptureSortedSet(sheet.RowPageBreaks);
        RowColumnShiftHelpers.ShiftSortedSetUp(sheet.RowPageBreaks, _beforeRow, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        RowColumnShiftHelpers.ShiftChartRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count));
        RowColumnShiftHelpers.ShiftAddressBearingRowsUp(ctx.Workbook, sheet, _addressStateSnapshot, _beforeRow, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var shiftedMerges = new List<GridRange>(_mergeSnapshot.Count);
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

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count), _formulaSnapshot);
        _namedFormulaSnapshot.Clear();
        _scopedNamedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteRuleFormulas(sheet, new InsertRowsOp(sheet.Name, _beforeRow, _count), _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                Enumerable.Empty<CellAddress>(),
                _formulaSnapshot));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);

        foreach (var snapshot in _movedSnapshot)
            sheet.ClearCell(snapshot.Row + _count, snapshot.Col);

        foreach (var snapshot in _movedSnapshot)
            sheet.SetCell(snapshot.ToAddress(sheet.Id), snapshot.ToCell());

        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.HiddenRows, _beforeRow + _count, _count);
        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.FilterHiddenRows, _beforeRow + _count, _count);
        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.ValueFilterHiddenRows, _beforeRow + _count, _count);
        // R13-meta-1: undo the ColumnFilterOwnedRows shift in lockstep with the sibling sets above.
        RowColumnShiftHelpers.ShiftRowSetDictionaryDownFrom(sheet.ColumnFilterOwnedRows, _beforeRow + _count, _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
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
        RowColumnShiftHelpers.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
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
        try
        {
            for (var i = 0; i < movedCells.Count; i++)
                originals[i] = sheet.GetCell(movedCells[i].Row, movedCells[i].Col)!;

            for (var i = 0; i < movedCells.Count; i++)
                sheet.ClearCell(movedCells[i].Row, movedCells[i].Col);

            for (var i = 0; i < movedCells.Count; i++)
            {
                var snapshot = movedCells[i];
                sheet.SetCell(new CellAddress(sheet.Id, snapshot.Row + count, snapshot.Col), originals[i]);
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
