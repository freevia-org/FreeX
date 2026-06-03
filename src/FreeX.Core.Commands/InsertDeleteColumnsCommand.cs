using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank columns before <paramref name="beforeCol"/>.</summary>
public sealed class InsertColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _beforeCol;
    private readonly uint _count;
    private List<CellStateSnapshot>? _movedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _columnWidthSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

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

        var (maxOccupied, movedSnapshot) = CaptureMovedCells(sheet);
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxCol)
            return new CommandOutcome(false,
                ErrorMessage: $"Cannot insert {_count} column(s): data would be pushed past the last column ({Model.CellAddress.MaxCol}).");

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        _movedSnapshot = movedSnapshot;

        MoveCellsForInsert(sheet, _movedSnapshot, _count);

        RowColumnShiftHelpers.ShiftSetUpFrom(sheet.HiddenCols, _beforeCol, _count);

        _columnWidthSnapshot = new Dictionary<uint, double>(sheet.ColumnWidths);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.ColumnWidths, _beforeCol, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.Comments, _beforeCol, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.ThreadedComments, _beforeCol, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.Hyperlinks, _beforeCol, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentColumnsUp(sheet.HyperlinkMetadata, _beforeCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleColumnsUp(sheet, _beforeCol, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        _printAreaSnapshot = sheet.PrintArea;
        RowColumnShiftHelpers.ShiftPrintAreaColumnsUp(sheet, _beforeCol, _count);
        _columnPageBreakSnapshot = sheet.ColumnPageBreaks.ToList();
        RowColumnShiftHelpers.ShiftSortedSetUp(sheet.ColumnPageBreaks, _beforeCol, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(sheet);
        RowColumnShiftHelpers.ShiftChartColumnsUp(sheet, _sheetId, _beforeCol, _count);
        RowColumnShiftHelpers.ShiftAddressBearingColumnsUp(ctx.Workbook, sheet, _addressStateSnapshot, _beforeCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.ReplaceMergedRegions(RowColumnShiftHelpers.InsertColumnsIntoMergedRegions(
            sheet.MergedRegions,
            _beforeCol,
            _count));

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var snapshot in _movedSnapshot)
            sheet.ClearCell(new CellAddress(snapshot.Address.Sheet, snapshot.Address.Row, snapshot.Address.Col + _count));

        foreach (var snapshot in _movedSnapshot)
            sheet.SetCell(snapshot.Address, snapshot.ToCell());

        RowColumnShiftHelpers.ShiftSetDownFrom(sheet.HiddenCols, _beforeCol + _count, _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreRuleRanges(_dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(sheet, _chartSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
    }

    private (uint MaxOccupied, List<CellStateSnapshot> Moved) CaptureMovedCells(Sheet sheet)
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
                originals[i] = sheet.GetCell(movedCells[i].Address)!;

            for (var i = 0; i < movedCells.Count; i++)
                sheet.ClearCell(movedCells[i].Address);

            for (var i = 0; i < movedCells.Count; i++)
            {
                var addr = movedCells[i].Address;
                sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col + count), originals[i]);
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
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _count;
    private List<CellStateSnapshot>? _deletedSnapshot;
    private List<CellStateSnapshot>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _columnWidthSnapshot;
    private HashSet<uint>? _hiddenColsSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private AddressBearingStateSnapshot? _addressStateSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

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

        _addressStateSnapshot = RowColumnShiftHelpers.CaptureAddressBearingState(ctx.Workbook, sheet);

        (_deletedSnapshot, _shiftedSnapshot) = CaptureDeletedAndShiftedCells(sheet, endCol);

        foreach (var snapshot in _deletedSnapshot)
            sheet.ClearCell(snapshot.Address);

        MoveCellsForDelete(sheet, _shiftedSnapshot, _count);

        _hiddenColsSnapshot = [.. sheet.HiddenCols];
        RowColumnShiftHelpers.DeleteSetRangeAndShiftDown(sheet.HiddenCols, _startCol, _count);

        _columnWidthSnapshot = new Dictionary<uint, double>(sheet.ColumnWidths);
        RowColumnShiftHelpers.ShiftIndexesDown(sheet.ColumnWidths, _startCol, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.Comments, _startCol, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.ThreadedComments, _startCol, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.Hyperlinks, _startCol, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentColumnsDown(sheet.HyperlinkMetadata, _startCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleColumnsDown(sheet, _startCol, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        _printAreaSnapshot = sheet.PrintArea;
        RowColumnShiftHelpers.ShiftPrintAreaColumnsDown(sheet, _startCol, _count);
        _columnPageBreakSnapshot = sheet.ColumnPageBreaks.ToList();
        RowColumnShiftHelpers.ShiftSortedSetDown(sheet.ColumnPageBreaks, _startCol, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(sheet);
        RowColumnShiftHelpers.ShiftChartColumnsDown(sheet, _sheetId, _startCol, _count);
        RowColumnShiftHelpers.ShiftAddressBearingColumnsDown(ctx.Workbook, sheet, _addressStateSnapshot, _startCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.ReplaceMergedRegions(RowColumnShiftHelpers.DeleteColumnsFromMergedRegions(
            sheet.MergedRegions,
            _startCol,
            _count));

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var snapshot in _shiftedSnapshot)
            sheet.ClearCell(new CellAddress(snapshot.Address.Sheet, snapshot.Address.Row, snapshot.Address.Col - _count));

        foreach (var snapshot in _shiftedSnapshot)
            sheet.SetCell(snapshot.Address, snapshot.ToCell());

        foreach (var snapshot in _deletedSnapshot)
            sheet.SetCell(snapshot.Address, snapshot.ToCell());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        RowColumnShiftHelpers.RestoreSet(sheet.HiddenCols, _hiddenColsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        RowColumnShiftHelpers.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(sheet, _chartSnapshot);
        RowColumnShiftHelpers.RestoreAddressBearingState(ctx.Workbook, sheet, _addressStateSnapshot);
    }

    private (List<CellStateSnapshot> Deleted, List<CellStateSnapshot> Shifted)
        CaptureDeletedAndShiftedCells(Sheet sheet, uint endCol)
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
                originals[i] = sheet.GetCell(shiftedCells[i].Address)!;

            for (var i = 0; i < shiftedCells.Count; i++)
                sheet.ClearCell(shiftedCells[i].Address);

            for (var i = 0; i < shiftedCells.Count; i++)
            {
                var addr = shiftedCells[i].Address;
                sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col - count), originals[i]);
            }
        }
        finally
        {
            Array.Clear(originals, 0, shiftedCells.Count);
            ArrayPool<Cell>.Shared.Return(originals);
        }
    }
}
