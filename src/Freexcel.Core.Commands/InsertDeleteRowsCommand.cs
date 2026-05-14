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

        _mergeSnapshot = sheet.MergedRegions.ToList();
        for (int i = 0; i < sheet.MergedRegions.Count; i++)
        {
            var m = sheet.MergedRegions[i];
            if (m.Start.Row >= _beforeRow)
            {
                sheet.MergedRegions[i] = new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row + _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   + _count, m.End.Col));
            }
        }

        _formulaSnapshot.Clear();
        RewriteAllFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _movedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row + _count, addr.Col));

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        var shifted = sheet.HiddenRows.Where(r => r >= _beforeRow + _count).ToList();
        foreach (var r in shifted) sheet.HiddenRows.Remove(r);
        foreach (var r in shifted) sheet.HiddenRows.Add(r - _count);

        if (_mergeSnapshot is not null)
        {
            sheet.MergedRegions.Clear();
            sheet.MergedRegions.AddRange(_mergeSnapshot);
        }
    }

    internal static void RewriteAllFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (addr, cell) in sheet.EnumerateCells())
            {
                if (cell.FormulaText is null) continue;
                var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, op, sheet.Name);
                if (rewritten is null) continue;
                snapshot[addr] = cell.FormulaText;
                cell.FormulaText = rewritten;
            }
        }
    }

    internal static void RestoreFormulas(
        Workbook workbook, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var (addr, original) in snapshot)
        {
            var s = workbook.GetSheet(addr.Sheet);
            var cell = s?.GetCell(addr.Row, addr.Col);
            if (cell is not null)
                cell.FormulaText = original;
        }
        snapshot.Clear();
    }
}

/// <summary>Deletes <paramref name="count"/> rows starting at <paramref name="startRow"/>.</summary>
public sealed class DeleteRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _deletedSnapshot;
    private List<(CellAddress Addr, Cell Cell)>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
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
        foreach (var r in inRangeHidden) sheet.HiddenRows.Remove(r);
        foreach (var r in belowHidden) { sheet.HiddenRows.Remove(r); sheet.HiddenRows.Add(r - _count); }

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.MergedRegions.RemoveAll(m => m.Start.Row <= endRow && m.End.Row >= _startRow);
        for (int i = 0; i < sheet.MergedRegions.Count; i++)
        {
            var m = sheet.MergedRegions[i];
            if (m.Start.Row > endRow)
            {
                sheet.MergedRegions[i] = new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row - _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   - _count, m.End.Col));
            }
        }

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
        {
            sheet.MergedRegions.Clear();
            sheet.MergedRegions.AddRange(_mergeSnapshot);
        }
    }
}
