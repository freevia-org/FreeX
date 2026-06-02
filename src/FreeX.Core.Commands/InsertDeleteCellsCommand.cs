using System.Buffers;
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
            var capture = CaptureCellsForMove(sheet, CellShiftRegion.Rightward(_range));
            if (capture.MaxCol > 0 && capture.MaxCol + width > CellAddress.MaxCol)
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last column ({CellAddress.MaxCol}).");
            _snapshot = capture.Snapshot;
            InsertShiftRight(sheet, capture.Cells);
        }
        else
        {
            uint height = _range.RowCount;
            var capture = CaptureCellsForMove(sheet, CellShiftRegion.Downward(_range));
            if (capture.MaxRow > 0 && capture.MaxRow + height > CellAddress.MaxRow)
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last row ({CellAddress.MaxRow}).");
            _snapshot = capture.Snapshot;
            InsertShiftDown(sheet, capture.Cells);
        }

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;
    }

    private void InsertShiftRight(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = RentOriginalCells(sheet, captured);
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col + width), originalCells[i]);
            }
        }
        finally
        {
            ReturnOriginalCells(originalCells);
        }
    }

    private void InsertShiftDown(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var height = _range.RowCount;
        var originalCells = RentOriginalCells(sheet, captured);
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                sheet.SetCell(new CellAddress(address.Sheet, address.Row + height, address.Col), originalCells[i]);
            }
        }
        finally
        {
            ReturnOriginalCells(originalCells);
        }
    }

    internal static CellShiftSnapshot CaptureCells(Sheet sheet, CellShiftRegion region)
        => CaptureCellsForMove(sheet, region).Snapshot;

    internal static CellShiftCapture CaptureCellsForDelete(Sheet sheet, CellShiftRegion region)
        => CaptureCellsForMove(sheet, region);

    private static CellShiftCapture CaptureCellsForMove(Sheet sheet, CellShiftRegion region)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var snapshotCells = new List<(CellAddress Address, Cell Cell)>(
            CountCellsInRegion(occupiedCells, region));
        uint maxRow = 0;
        uint maxCol = 0;

        foreach (var ((row, col), cell) in occupiedCells)
        {
            if (!region.Contains(row, col))
                continue;

            if (row > maxRow)
                maxRow = row;
            if (col > maxCol)
                maxCol = col;

            var address = new CellAddress(sheet.Id, row, col);
            snapshotCells.Add((address, cell.Clone()));
        }

        return new CellShiftCapture(
            new CellShiftSnapshot(region, snapshotCells),
            snapshotCells,
            maxRow,
            maxCol);
    }

    private static int CountCellsInRegion(IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells, CellShiftRegion region)
    {
        var count = 0;
        foreach (var ((row, col), _) in occupiedCells)
        {
            if (region.Contains(row, col))
                count++;
        }

        return count;
    }

    internal static Cell[] RentOriginalCells(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        if (captured.Count == 0)
            return Array.Empty<Cell>();

        var originalCells = ArrayPool<Cell>.Shared.Rent(captured.Count);
        for (var i = 0; i < captured.Count; i++)
            originalCells[i] = sheet.GetCell(captured[i].Address)!;
        return originalCells;
    }

    internal static void ReturnOriginalCells(Cell[] originalCells)
    {
        if (originalCells.Length != 0)
            ArrayPool<Cell>.Shared.Return(originalCells, clearArray: true);
    }

    internal static void ClearRange(Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                sheet.ClearCell(row, col);
        }
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
            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, CellShiftRegion.Rightward(_range));
            _snapshot = capture.Snapshot;
            DeleteShiftLeft(sheet, capture.Cells);
        }
        else
        {
            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, CellShiftRegion.Downward(_range));
            _snapshot = capture.Snapshot;
            DeleteShiftUp(sheet, capture.Cells);
        }

        return new CommandOutcome(true, AffectedCells: _range.AllCells().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;
    }

    private void DeleteShiftLeft(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = InsertCellsCommand.RentOriginalCells(sheet, captured);
        try
        {
            InsertCellsCommand.ClearRange(sheet, _range);
            foreach (var (address, _) in captured)
            {
                if (address.Col > _range.End.Col)
                    sheet.ClearCell(address);
            }

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                if (address.Col > _range.End.Col)
                    sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col - width), originalCells[i]);
            }
        }
        finally
        {
            InsertCellsCommand.ReturnOriginalCells(originalCells);
        }
    }

    private void DeleteShiftUp(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var height = _range.RowCount;
        var originalCells = InsertCellsCommand.RentOriginalCells(sheet, captured);
        try
        {
            InsertCellsCommand.ClearRange(sheet, _range);
            foreach (var (address, _) in captured)
            {
                if (address.Row > _range.End.Row)
                    sheet.ClearCell(address);
            }

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                if (address.Row > _range.End.Row)
                    sheet.SetCell(new CellAddress(address.Sheet, address.Row - height, address.Col), originalCells[i]);
            }
        }
        finally
        {
            InsertCellsCommand.ReturnOriginalCells(originalCells);
        }
    }
}

internal readonly record struct CellShiftRegion(uint StartRow, uint EndRow, uint StartCol, uint EndCol)
{
    public static CellShiftRegion Rightward(GridRange range) =>
        new(range.Start.Row, range.End.Row, range.Start.Col, CellAddress.MaxCol);

    public static CellShiftRegion Downward(GridRange range) =>
        new(range.Start.Row, CellAddress.MaxRow, range.Start.Col, range.End.Col);

    public bool Contains(CellAddress address) =>
        Contains(address.Row, address.Col);

    public bool Contains(uint row, uint col) =>
        row >= StartRow &&
        row <= EndRow &&
        col >= StartCol &&
        col <= EndCol;
}

internal sealed class CellShiftCapture(
    CellShiftSnapshot snapshot,
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
    uint maxRow,
    uint maxCol)
{
    public CellShiftSnapshot Snapshot { get; } = snapshot;
    public IReadOnlyList<(CellAddress Address, Cell Cell)> Cells { get; } = cells;
    public uint MaxRow { get; } = maxRow;
    public uint MaxCol { get; } = maxCol;
}

internal sealed class CellShiftSnapshot(
    CellShiftRegion region,
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells)
{
    public void Restore(Sheet sheet)
    {
        var current = ArrayPool<CellAddress>.Shared.Rent(Math.Max(cells.Count, 16));
        var count = 0;
        try
        {
            foreach (var ((row, col), _) in sheet.GetOccupiedCellMap())
            {
                if (!region.Contains(row, col))
                    continue;

                if (count == current.Length)
                {
                    var expanded = ArrayPool<CellAddress>.Shared.Rent(current.Length * 2);
                    current.AsSpan(0, count).CopyTo(expanded);
                    ArrayPool<CellAddress>.Shared.Return(current);
                    current = expanded;
                }

                current[count++] = new CellAddress(sheet.Id, row, col);
            }

            for (var i = 0; i < count; i++)
                sheet.ClearCell(current[i]);

            foreach (var (address, cell) in cells)
                sheet.SetCell(address, cell);
        }
        finally
        {
            ArrayPool<CellAddress>.Shared.Return(current);
        }
    }
}
