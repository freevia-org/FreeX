using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank columns before <paramref name="beforeCol"/>.</summary>
public sealed class InsertColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _beforeCol;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _movedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _columnWidthSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, GridRange>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var maxOccupied = sheet.EnumerateCells()
            .Where(p => p.Address.Col >= _beforeCol)
            .Select(p => p.Address.Col)
            .DefaultIfEmpty(0u)
            .Max();
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxCol)
            return new CommandOutcome(false,
                ErrorMessage: $"Cannot insert {_count} column(s): data would be pushed past the last column ({Model.CellAddress.MaxCol}).");

        _movedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Col >= _beforeCol)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _movedSnapshot.OrderByDescending(p => p.Addr.Col))
            sheet.ClearCell(addr);

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col + _count), cell.Clone());

        var hiddenToShift = sheet.HiddenCols.Where(c => c >= _beforeCol).ToList();
        foreach (var c in hiddenToShift) sheet.HiddenCols.Remove(c);
        foreach (var c in hiddenToShift) sheet.HiddenCols.Add(c + _count);

        _columnWidthSnapshot = new Dictionary<uint, double>(sheet.ColumnWidths);
        InsertRowsCommand.ShiftIndexesUp(sheet.ColumnWidths, _beforeCol, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        InsertRowsCommand.ShiftCommentColumnsUp(sheet.Comments, _beforeCol, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        InsertRowsCommand.ShiftCommentColumnsUp(sheet.ThreadedComments, _beforeCol, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        InsertRowsCommand.ShiftCommentColumnsUp(sheet.Hyperlinks, _beforeCol, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        InsertRowsCommand.ShiftCommentColumnsUp(sheet.HyperlinkMetadata, _beforeCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = InsertRowsCommand.CaptureRuleRanges(sheet);
        InsertRowsCommand.ShiftRuleColumnsUp(sheet, _beforeCol, _count);
        _namedRangeSnapshot = InsertRowsCommand.CaptureNamedRanges(ctx.Workbook);
        InsertRowsCommand.ShiftNamedRangeColumnsUp(ctx.Workbook, _sheetId, _beforeCol, _count);
        _printAreaSnapshot = sheet.PrintArea;
        InsertRowsCommand.ShiftPrintAreaColumnsUp(sheet, _beforeCol, _count);
        _columnPageBreakSnapshot = sheet.ColumnPageBreaks.ToList();
        InsertRowsCommand.ShiftSortedSetUp(sheet.ColumnPageBreaks, _beforeCol, _count);
        _chartSnapshot = InsertRowsCommand.CaptureChartDataRanges(sheet);
        InsertRowsCommand.ShiftChartColumnsUp(sheet, _sheetId, _beforeCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var shiftedMerges = sheet.MergedRegions.Select(m =>
        {
            if (m.Start.Col >= _beforeCol)
                return new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row, m.Start.Col + _count),
                    new CellAddress(m.End.Sheet,   m.End.Row,   m.End.Col   + _count));
            if (m.End.Col >= _beforeCol)
                return new GridRange(
                    m.Start,
                    new CellAddress(m.End.Sheet, m.End.Row, m.End.Col + _count));
            return m;
        }).ToList();
        sheet.ReplaceMergedRegions(shiftedMerges);

        _formulaSnapshot.Clear();
        InsertRowsCommand.RewriteAllFormulas(
            ctx.Workbook, new InsertColsOp(sheet.Name, _beforeCol, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        InsertRowsCommand.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _movedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row, addr.Col + _count));

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        var shifted = sheet.HiddenCols.Where(c => c >= _beforeCol + _count).ToList();
        foreach (var c in shifted) sheet.HiddenCols.Remove(c);
        foreach (var c in shifted) sheet.HiddenCols.Add(c - _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        InsertRowsCommand.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Comments, _commentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        InsertRowsCommand.RestoreRuleRanges(_dataValidationSnapshot, _conditionalFormatSnapshot);
        InsertRowsCommand.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        InsertRowsCommand.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        InsertRowsCommand.RestoreChartDataRanges(sheet, _chartSnapshot);
    }
}

/// <summary>Deletes <paramref name="count"/> columns starting at <paramref name="startCol"/>.</summary>
public sealed class DeleteColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _deletedSnapshot;
    private List<(CellAddress Addr, Cell Cell)>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _columnWidthSnapshot;
    private HashSet<uint>? _hiddenColsSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, GridRange>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _columnPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        uint endCol = _startCol + _count - 1;

        _deletedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Col >= _startCol && p.Address.Col <= endCol)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();
        _shiftedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Col > endCol)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _deletedSnapshot) sheet.ClearCell(addr);

        foreach (var (addr, _) in _shiftedSnapshot.OrderBy(p => p.Addr.Col))
            sheet.ClearCell(addr);
        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row, addr.Col - _count), cell.Clone());

        var inRangeHidden = sheet.HiddenCols.Where(c => c >= _startCol && c <= endCol).ToList();
        var aboveHidden   = sheet.HiddenCols.Where(c => c > endCol).ToList();
        _hiddenColsSnapshot = [.. sheet.HiddenCols];
        foreach (var c in inRangeHidden) sheet.HiddenCols.Remove(c);
        foreach (var c in aboveHidden) { sheet.HiddenCols.Remove(c); sheet.HiddenCols.Add(c - _count); }

        _columnWidthSnapshot = new Dictionary<uint, double>(sheet.ColumnWidths);
        InsertRowsCommand.ShiftIndexesDown(sheet.ColumnWidths, _startCol, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        InsertRowsCommand.ShiftCommentColumnsDown(sheet.Comments, _startCol, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        InsertRowsCommand.ShiftCommentColumnsDown(sheet.ThreadedComments, _startCol, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        InsertRowsCommand.ShiftCommentColumnsDown(sheet.Hyperlinks, _startCol, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        InsertRowsCommand.ShiftCommentColumnsDown(sheet.HyperlinkMetadata, _startCol, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = InsertRowsCommand.CaptureRuleRanges(sheet);
        InsertRowsCommand.ShiftRuleColumnsDown(sheet, _startCol, _count);
        _namedRangeSnapshot = InsertRowsCommand.CaptureNamedRanges(ctx.Workbook);
        InsertRowsCommand.ShiftNamedRangeColumnsDown(ctx.Workbook, _sheetId, _startCol, _count);
        _printAreaSnapshot = sheet.PrintArea;
        InsertRowsCommand.ShiftPrintAreaColumnsDown(sheet, _startCol, _count);
        _columnPageBreakSnapshot = sheet.ColumnPageBreaks.ToList();
        InsertRowsCommand.ShiftSortedSetDown(sheet.ColumnPageBreaks, _startCol, _count);
        _chartSnapshot = InsertRowsCommand.CaptureChartDataRanges(sheet);
        InsertRowsCommand.ShiftChartColumnsDown(sheet, _sheetId, _startCol, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var adjustedMerges = new List<GridRange>();
        foreach (var m in sheet.MergedRegions)
        {
            if (m.End.Col < _startCol)
            {
                adjustedMerges.Add(m); // entirely to the left
            }
            else if (m.Start.Col > endCol)
            {
                // entirely to the right — shift left
                adjustedMerges.Add(new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row, m.Start.Col - _count),
                    new CellAddress(m.End.Sheet,   m.End.Row,   m.End.Col   - _count)));
            }
            else
            {
                // overlapping — shrink
                uint newStart = m.Start.Col < _startCol ? m.Start.Col : _startCol;
                uint newEnd   = m.End.Col   > endCol    ? m.End.Col - _count
                              : _startCol > 1           ? _startCol - 1 : 0;
                if (newEnd > 0 && newEnd >= newStart)
                {
                    adjustedMerges.Add(new GridRange(
                        new CellAddress(m.Start.Sheet, m.Start.Row, newStart),
                        new CellAddress(m.End.Sheet,   m.End.Row,   newEnd)));
                }
                // if newEnd < newStart the merge was entirely deleted — drop it
            }
        }
        sheet.ReplaceMergedRegions(adjustedMerges);

        _formulaSnapshot.Clear();
        InsertRowsCommand.RewriteAllFormulas(
            ctx.Workbook, new DeleteColsOp(sheet.Name, _startCol, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        InsertRowsCommand.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _shiftedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row, addr.Col - _count));

        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        foreach (var (addr, cell) in _deletedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        InsertRowsCommand.RestoreDictionary(sheet.ColumnWidths, _columnWidthSnapshot);
        InsertRowsCommand.RestoreSet(sheet.HiddenCols, _hiddenColsSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Comments, _commentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        InsertRowsCommand.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        InsertRowsCommand.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        InsertRowsCommand.RestoreSortedSet(sheet.ColumnPageBreaks, _columnPageBreakSnapshot);
        InsertRowsCommand.RestoreChartDataRanges(sheet, _chartSnapshot);
    }
}
