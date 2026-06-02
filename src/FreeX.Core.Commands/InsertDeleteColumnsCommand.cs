using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank columns before <paramref name="beforeCol"/>.</summary>
public sealed class InsertColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _beforeCol;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Snapshot, Cell? Original)>? _movedSnapshot;
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

        foreach (var (addr, _, _) in _movedSnapshot)
            sheet.ClearCell(addr);

        foreach (var (addr, _, original) in _movedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col + _count), original!);

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

        foreach (var (addr, _, _) in _movedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row, addr.Col + _count));

        foreach (var (addr, snapshot, _) in _movedSnapshot)
            sheet.SetCell(addr, snapshot.Clone());

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
        ReleaseOriginalCells(_movedSnapshot);
    }

    private (uint MaxOccupied, List<(CellAddress Addr, Cell Snapshot, Cell? Original)> Moved) CaptureMovedCells(Sheet sheet)
    {
        var moved = new List<(CellAddress Addr, Cell Snapshot, Cell? Original)>(sheet.CellCount);
        uint maxOccupied = 0;

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col < _beforeCol)
                continue;

            if (col > maxOccupied)
                maxOccupied = col;

            moved.Add((new CellAddress(sheet.Id, row, col), cell.Clone(), cell));
        }

        return (maxOccupied, moved);
    }

    private static void ReleaseOriginalCells(List<(CellAddress Addr, Cell Snapshot, Cell? Original)> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            cells[i] = (cell.Addr, cell.Snapshot, null);
        }
    }
}

/// <summary>Deletes <paramref name="count"/> columns starting at <paramref name="startCol"/>.</summary>
public sealed class DeleteColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _deletedSnapshot;
    private List<(CellAddress Addr, Cell Snapshot, Cell? Original)>? _shiftedSnapshot;
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

        foreach (var (addr, _) in _deletedSnapshot) sheet.ClearCell(addr);

        foreach (var (addr, _, _) in _shiftedSnapshot)
            sheet.ClearCell(addr);
        foreach (var (addr, _, original) in _shiftedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col - _count), original!);

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

        foreach (var (addr, _, _) in _shiftedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row, addr.Col - _count));

        foreach (var (addr, snapshot, _) in _shiftedSnapshot)
            sheet.SetCell(addr, snapshot.Clone());

        foreach (var (addr, cell) in _deletedSnapshot)
            sheet.SetCell(addr, cell.Clone());

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
        ReleaseOriginalCells(_shiftedSnapshot);
    }

    private (List<(CellAddress Addr, Cell Cell)> Deleted, List<(CellAddress Addr, Cell Snapshot, Cell? Original)> Shifted)
        CaptureDeletedAndShiftedCells(Sheet sheet, uint endCol)
    {
        var deleted = new List<(CellAddress Addr, Cell Cell)>();
        var shifted = new List<(CellAddress Addr, Cell Snapshot, Cell? Original)>(sheet.CellCount);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col > endCol)
            {
                shifted.Add((new CellAddress(sheet.Id, row, col), cell.Clone(), cell));
            }
            else if (col >= _startCol)
            {
                deleted.Add((new CellAddress(sheet.Id, row, col), cell.Clone()));
            }
        }

        return (deleted, shifted);
    }

    private static void ReleaseOriginalCells(List<(CellAddress Addr, Cell Snapshot, Cell? Original)> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            cells[i] = (cell.Addr, cell.Snapshot, null);
        }
    }
}
