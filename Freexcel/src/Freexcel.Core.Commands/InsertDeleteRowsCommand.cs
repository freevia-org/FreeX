using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank rows before <paramref name="beforeRow"/>.</summary>
public sealed class InsertRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _beforeRow;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _movedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var maxOccupied = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _beforeRow)
            .Select(p => p.Address.Row)
            .DefaultIfEmpty(0u)
            .Max();
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxRow)
            return new CommandOutcome(false,
                ErrorMessage: $"Cannot insert {_count} row(s): data would be pushed past the last row ({Model.CellAddress.MaxRow}).");

        _movedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _beforeRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _movedSnapshot.OrderByDescending(p => p.Addr.Row))
            sheet.ClearCell(addr);

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row + _count, addr.Col), cell.Clone());

        var hiddenToShift = sheet.HiddenRows.Where(r => r >= _beforeRow).ToList();
        foreach (var r in hiddenToShift) sheet.HiddenRows.Remove(r);
        foreach (var r in hiddenToShift) sheet.HiddenRows.Add(r + _count);

        var filterHiddenToShift = sheet.FilterHiddenRows.Where(r => r >= _beforeRow).ToList();
        foreach (var r in filterHiddenToShift) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in filterHiddenToShift) sheet.FilterHiddenRows.Add(r + _count);

        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        RowColumnShiftHelpers.ShiftIndexesUp(sheet.RowHeights, _beforeRow, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Comments, _beforeRow, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.ThreadedComments, _beforeRow, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.Hyperlinks, _beforeRow, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        RowColumnShiftHelpers.ShiftCommentRowsUp(sheet.HyperlinkMetadata, _beforeRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
        RowColumnShiftHelpers.ShiftRuleRowsUp(sheet, _beforeRow, _count);
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        RowColumnShiftHelpers.ShiftNamedRangeRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        _printAreaSnapshot = sheet.PrintArea;
        RowColumnShiftHelpers.ShiftPrintAreaRowsUp(sheet, _beforeRow, _count);
        _rowPageBreakSnapshot = sheet.RowPageBreaks.ToList();
        RowColumnShiftHelpers.ShiftSortedSetUp(sheet.RowPageBreaks, _beforeRow, _count);
        _chartSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(sheet);
        RowColumnShiftHelpers.ShiftChartRowsUp(sheet, _sheetId, _beforeRow, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var shiftedMerges = sheet.MergedRegions.Select(m =>
        {
            if (m.Start.Row >= _beforeRow)
                return new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row + _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   + _count, m.End.Col));
            if (m.End.Row >= _beforeRow)
                return new GridRange(
                    m.Start,
                    new CellAddress(m.End.Sheet, m.End.Row + _count, m.End.Col));
            return m;
        }).ToList();
        sheet.ReplaceMergedRegions(shiftedMerges);

        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _movedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row + _count, addr.Col));

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        var shifted = sheet.HiddenRows.Where(r => r >= _beforeRow + _count).ToList();
        foreach (var r in shifted) sheet.HiddenRows.Remove(r);
        foreach (var r in shifted) sheet.HiddenRows.Add(r - _count);

        var filterShifted = sheet.FilterHiddenRows.Where(r => r >= _beforeRow + _count).ToList();
        foreach (var r in filterShifted) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in filterShifted) sheet.FilterHiddenRows.Add(r - _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreRuleRanges(_dataValidationSnapshot, _conditionalFormatSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        RowColumnShiftHelpers.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(sheet, _chartSnapshot);
    }
}

internal sealed record NamedRangeSnapshot(GridRange Range, NamedRangeMetadata Metadata);
