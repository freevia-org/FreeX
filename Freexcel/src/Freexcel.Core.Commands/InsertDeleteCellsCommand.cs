using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum InsertCellsShiftDirection
{
    Right,
    Down
}

public enum DeleteCellsShiftDirection
{
    Left,
    Up
}

public sealed class InsertCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly InsertCellsShiftDirection _direction;
    private List<(CellAddress Address, Cell Cell)>? _snapshot;

    public string Label => "Insert Cells";

    public InsertCellsCommand(SheetId sheetId, GridRange range, InsertCellsShiftDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Insert range must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = CaptureCells(sheet);
        if (_direction == InsertCellsShiftDirection.Right)
            InsertShiftRight(sheet);
        else
            InsertShiftDown(sheet);

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        RestoreCells(ctx.GetSheet(_sheetId), _snapshot);
    }

    private void InsertShiftRight(Sheet sheet)
    {
        var width = _range.ColCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Row >= _range.Start.Row &&
                           item.Address.Row <= _range.End.Row &&
                           item.Address.Col >= _range.Start.Col)
            .OrderByDescending(item => item.Address.Col)
            .ToList();

        foreach (var (address, _) in moved)
            sheet.ClearCell(address);

        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col + width), cell.Clone());

        ClearRange(sheet, _range);
    }

    private void InsertShiftDown(Sheet sheet)
    {
        var height = _range.RowCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Col >= _range.Start.Col &&
                           item.Address.Col <= _range.End.Col &&
                           item.Address.Row >= _range.Start.Row)
            .OrderByDescending(item => item.Address.Row)
            .ToList();

        foreach (var (address, _) in moved)
            sheet.ClearCell(address);

        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row + height, address.Col), cell.Clone());

        ClearRange(sheet, _range);
    }

    internal static List<(CellAddress Address, Cell Cell)> CaptureCells(Sheet sheet) =>
        sheet.EnumerateCells().Select(item => (item.Address, item.Cell.Clone())).ToList();

    internal static void RestoreCells(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> snapshot)
    {
        foreach (var (address, _) in sheet.EnumerateCells().ToList())
            sheet.ClearCell(address);

        foreach (var (address, cell) in snapshot)
            sheet.SetCell(address, cell.Clone());
    }

    internal static void ClearRange(Sheet sheet, GridRange range)
    {
        foreach (var address in range.AllCells())
            sheet.ClearCell(address);
    }
}

public sealed class DeleteCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly DeleteCellsShiftDirection _direction;
    private List<(CellAddress Address, Cell Cell)>? _snapshot;

    public string Label => "Delete Cells";

    public DeleteCellsCommand(SheetId sheetId, GridRange range, DeleteCellsShiftDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Delete range must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = InsertCellsCommand.CaptureCells(sheet);
        if (_direction == DeleteCellsShiftDirection.Left)
            DeleteShiftLeft(sheet);
        else
            DeleteShiftUp(sheet);

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        InsertCellsCommand.RestoreCells(ctx.GetSheet(_sheetId), _snapshot);
    }

    private void DeleteShiftLeft(Sheet sheet)
    {
        var width = _range.ColCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Row >= _range.Start.Row &&
                           item.Address.Row <= _range.End.Row &&
                           item.Address.Col > _range.End.Col)
            .OrderBy(item => item.Address.Col)
            .ToList();

        foreach (var address in _range.AllCells())
            sheet.ClearCell(address);
        foreach (var (address, _) in moved)
            sheet.ClearCell(address);
        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col - width), cell.Clone());
    }

    private void DeleteShiftUp(Sheet sheet)
    {
        var height = _range.RowCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Col >= _range.Start.Col &&
                           item.Address.Col <= _range.End.Col &&
                           item.Address.Row > _range.End.Row)
            .OrderBy(item => item.Address.Row)
            .ToList();

        foreach (var address in _range.AllCells())
            sheet.ClearCell(address);
        foreach (var (address, _) in moved)
            sheet.ClearCell(address);
        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row - height, address.Col), cell.Clone());
    }
}
