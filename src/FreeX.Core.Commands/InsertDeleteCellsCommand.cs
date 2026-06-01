using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
    private CellShiftSnapshot? _snapshot;

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
        if (!Enum.IsDefined(_direction))
            return new CommandOutcome(false, "Insert shift direction is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (_direction == InsertCellsShiftDirection.Right)
        {
            uint width = _range.ColCount;
            var maxOccupied = sheet.EnumerateCells()
                .Where(p => p.Address.Row >= _range.Start.Row && p.Address.Row <= _range.End.Row && p.Address.Col >= _range.Start.Col)
                .Select(p => p.Address.Col)
                .DefaultIfEmpty(0u).Max();
            if (maxOccupied > 0 && maxOccupied + width > CellAddress.MaxCol)
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last column ({CellAddress.MaxCol}).");
            _snapshot = CaptureCells(sheet, CellShiftRegion.Rightward(_range));
            InsertShiftRight(sheet);
        }
        else
        {
            uint height = _range.RowCount;
            var maxOccupied = sheet.EnumerateCells()
                .Where(p => p.Address.Col >= _range.Start.Col && p.Address.Col <= _range.End.Col && p.Address.Row >= _range.Start.Row)
                .Select(p => p.Address.Row)
                .DefaultIfEmpty(0u).Max();
            if (maxOccupied > 0 && maxOccupied + height > CellAddress.MaxRow)
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last row ({CellAddress.MaxRow}).");
            _snapshot = CaptureCells(sheet, CellShiftRegion.Downward(_range));
            InsertShiftDown(sheet);
        }

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        _snapshot.Restore(ctx.GetSheet(_sheetId));
    }

    private void InsertShiftRight(Sheet sheet)
    {
        var width = _range.ColCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Row >= _range.Start.Row &&
                           item.Address.Row <= _range.End.Row &&
                           item.Address.Col >= _range.Start.Col)
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
            .ToList();

        foreach (var (address, _) in moved)
            sheet.ClearCell(address);

        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row + height, address.Col), cell.Clone());

        ClearRange(sheet, _range);
    }

    internal static CellShiftSnapshot CaptureCells(Sheet sheet, CellShiftRegion region)
    {
        var cells = new List<(CellAddress Address, Cell Cell)>();
        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            if (region.Contains(address))
                cells.Add((address, cell.Clone()));
        }

        return new CellShiftSnapshot(region, cells);
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
    private CellShiftSnapshot? _snapshot;

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
        if (!Enum.IsDefined(_direction))
            return new CommandOutcome(false, "Delete shift direction is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (_direction == DeleteCellsShiftDirection.Left)
        {
            _snapshot = InsertCellsCommand.CaptureCells(sheet, CellShiftRegion.Rightward(_range));
            DeleteShiftLeft(sheet);
        }
        else
        {
            _snapshot = InsertCellsCommand.CaptureCells(sheet, CellShiftRegion.Downward(_range));
            DeleteShiftUp(sheet);
        }

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        _snapshot.Restore(ctx.GetSheet(_sheetId));
    }

    private void DeleteShiftLeft(Sheet sheet)
    {
        var width = _range.ColCount;
        var moved = sheet.EnumerateCells()
            .Where(item => item.Address.Row >= _range.Start.Row &&
                           item.Address.Row <= _range.End.Row &&
                           item.Address.Col > _range.End.Col)
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
            .ToList();

        foreach (var address in _range.AllCells())
            sheet.ClearCell(address);
        foreach (var (address, _) in moved)
            sheet.ClearCell(address);
        foreach (var (address, cell) in moved)
            sheet.SetCell(new CellAddress(address.Sheet, address.Row - height, address.Col), cell.Clone());
    }
}

internal readonly record struct CellShiftRegion(uint StartRow, uint EndRow, uint StartCol, uint EndCol)
{
    public static CellShiftRegion Rightward(GridRange range) =>
        new(range.Start.Row, range.End.Row, range.Start.Col, CellAddress.MaxCol);

    public static CellShiftRegion Downward(GridRange range) =>
        new(range.Start.Row, CellAddress.MaxRow, range.Start.Col, range.End.Col);

    public bool Contains(CellAddress address) =>
        address.Row >= StartRow &&
        address.Row <= EndRow &&
        address.Col >= StartCol &&
        address.Col <= EndCol;
}

internal sealed class CellShiftSnapshot(
    CellShiftRegion region,
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells)
{
    public void Restore(Sheet sheet)
    {
        var current = new List<CellAddress>();
        foreach (var (address, _) in sheet.EnumerateCells())
        {
            if (region.Contains(address))
                current.Add(address);
        }

        foreach (var address in current)
            sheet.ClearCell(address);

        foreach (var (address, cell) in cells)
            sheet.SetCell(address, cell.Clone());
    }
}
