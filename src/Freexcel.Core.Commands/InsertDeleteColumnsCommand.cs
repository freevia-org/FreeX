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

        _mergeSnapshot = sheet.MergedRegions.ToList();
        for (int i = 0; i < sheet.MergedRegions.Count; i++)
        {
            var m = sheet.MergedRegions[i];
            if (m.Start.Col >= _beforeCol)
            {
                sheet.MergedRegions[i] = new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row, m.Start.Col + _count),
                    new CellAddress(m.End.Sheet,   m.End.Row,   m.End.Col   + _count));
            }
        }

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
        {
            sheet.MergedRegions.Clear();
            sheet.MergedRegions.AddRange(_mergeSnapshot);
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
    private List<(CellAddress Addr, Cell Cell)>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
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
        foreach (var c in inRangeHidden) sheet.HiddenCols.Remove(c);
        foreach (var c in aboveHidden) { sheet.HiddenCols.Remove(c); sheet.HiddenCols.Add(c - _count); }

        _mergeSnapshot = sheet.MergedRegions.ToList();
        sheet.MergedRegions.RemoveAll(m => m.Start.Col <= endCol && m.End.Col >= _startCol);
        for (int i = 0; i < sheet.MergedRegions.Count; i++)
        {
            var m = sheet.MergedRegions[i];
            if (m.Start.Col > endCol)
            {
                sheet.MergedRegions[i] = new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row, m.Start.Col - _count),
                    new CellAddress(m.End.Sheet,   m.End.Row,   m.End.Col   - _count));
            }
        }

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
        {
            sheet.MergedRegions.Clear();
            sheet.MergedRegions.AddRange(_mergeSnapshot);
        }
    }
}
