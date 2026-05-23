using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Deletes <paramref name="count"/> rows starting at <paramref name="startRow"/>.</summary>
public sealed class DeleteRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _deletedSnapshot;
    private List<(CellAddress Addr, Cell Cell)>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    private HashSet<uint>? _hiddenRowsSnapshot;
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        uint endRow = _startRow + _count - 1;

        _deletedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _startRow && p.Address.Row <= endRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();
        _shiftedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row > endRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _deletedSnapshot)
            sheet.ClearCell(addr);

        foreach (var (addr, _) in _shiftedSnapshot.OrderBy(p => p.Addr.Row))
            sheet.ClearCell(addr);
        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row - _count, addr.Col), cell.Clone());

        var inRangeHidden = sheet.HiddenRows.Where(r => r >= _startRow && r <= endRow).ToList();
        var belowHidden   = sheet.HiddenRows.Where(r => r > endRow).ToList();
        _hiddenRowsSnapshot = [.. sheet.HiddenRows];
        foreach (var r in inRangeHidden) sheet.HiddenRows.Remove(r);
        foreach (var r in belowHidden) { sheet.HiddenRows.Remove(r); sheet.HiddenRows.Add(r - _count); }

        var inRangeFilterHidden = sheet.FilterHiddenRows.Where(r => r >= _startRow && r <= endRow).ToList();
        var belowFilterHidden = sheet.FilterHiddenRows.Where(r => r > endRow).ToList();
        _filterHiddenRowsSnapshot = [.. sheet.FilterHiddenRows];
        foreach (var r in inRangeFilterHidden) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in belowFilterHidden) { sheet.FilterHiddenRows.Remove(r); sheet.FilterHiddenRows.Add(r - _count); }

        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        InsertRowsCommand.ShiftIndexesDown(sheet.RowHeights, _startRow, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.Comments, _startRow, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.ThreadedComments, _startRow, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.Hyperlinks, _startRow, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.HyperlinkMetadata, _startRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = InsertRowsCommand.CaptureRuleRanges(sheet);
        InsertRowsCommand.ShiftRuleRowsDown(sheet, _startRow, _count);
        _namedRangeSnapshot = InsertRowsCommand.CaptureNamedRanges(ctx.Workbook);
        InsertRowsCommand.ShiftNamedRangeRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        _printAreaSnapshot = sheet.PrintArea;
        InsertRowsCommand.ShiftPrintAreaRowsDown(sheet, _startRow, _count);
        _rowPageBreakSnapshot = sheet.RowPageBreaks.ToList();
        InsertRowsCommand.ShiftSortedSetDown(sheet.RowPageBreaks, _startRow, _count);
        _chartSnapshot = InsertRowsCommand.CaptureChartDataRanges(sheet);
        InsertRowsCommand.ShiftChartRowsDown(sheet, _sheetId, _startRow, _count);

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
                if (newEnd > 0 && newEnd >= newStart)
                {
                    adjustedMerges.Add(new GridRange(
                        new CellAddress(m.Start.Sheet, newStart, m.Start.Col),
                        new CellAddress(m.End.Sheet,   newEnd,   m.End.Col)));
                }
                // if newEnd < newStart the merge was entirely deleted — drop it
            }
        }
        sheet.ReplaceMergedRegions(adjustedMerges);

        _formulaSnapshot.Clear();
        InsertRowsCommand.RewriteAllFormulas(
            ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        InsertRowsCommand.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _shiftedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row - _count, addr.Col));

        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        foreach (var (addr, cell) in _deletedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        InsertRowsCommand.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        InsertRowsCommand.RestoreSet(sheet.HiddenRows, _hiddenRowsSnapshot);
        InsertRowsCommand.RestoreSet(sheet.FilterHiddenRows, _filterHiddenRowsSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Comments, _commentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        InsertRowsCommand.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        InsertRowsCommand.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        InsertRowsCommand.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        InsertRowsCommand.RestoreChartDataRanges(sheet, _chartSnapshot);
    }
}
