using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ImportSheetCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures a (Cell?, StyleId?) pair for every
    // cell the import overwrites -- the same shape MoveRangeCommand/CopyRangeCommand use 400
    // bytes/cell for. Importing a large external range should count proportionally, not the flat
    // 200-byte default.
    private const int BytesPerCell = 400;

    private readonly SheetId _targetSheetId;
    private readonly CellAddress _destination;
    private readonly IReadOnlyList<(uint RowOffset, uint ColOffset, Cell Cell)> _sourceCells;
    private readonly uint _sourceRowCount;
    private readonly uint _sourceColCount;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Import Data";

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? _sourceCells.Count) * BytesPerCell, int.MaxValue);

    public ImportSheetCommand(SheetId targetSheetId, CellAddress destination, Sheet sourceSheet)
    {
        _targetSheetId = targetSheetId;
        _destination = destination;
        var usedRange = sourceSheet.GetUsedRange();
        if (usedRange is null)
        {
            _sourceRowCount = 0;
            _sourceColCount = 0;
            _sourceCells = [];
            return;
        }

        _sourceRowCount = usedRange.Value.RowCount;
        _sourceColCount = usedRange.Value.ColCount;
        _sourceCells = sourceSheet.EnumerateCells()
            .Select(c => (
                RowOffset: c.Address.Row - usedRange.Value.Start.Row,
                ColOffset: c.Address.Col - usedRange.Value.Start.Col,
                Cell: c.Cell.Clone()))
            .ToList();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_destination.Sheet != _targetSheetId)
            return new CommandOutcome(false, "Import destination must be on the target sheet.");
        if (_sourceCells.Count > 0 &&
            !WorksheetBounds.TryGetRectangleEnd(_destination, _sourceRowCount, _sourceColCount, out _))
        {
            return new CommandOutcome(false, "Import destination range is outside the worksheet bounds.");
        }

        var targetSheet = ctx.GetSheet(_targetSheetId);
        var targetCells = BuildTargetCells();

        foreach (var (address, _) in targetCells)
        {
            if (!CommandGuards.CanEditCell(ctx.Workbook, targetSheet, address))
                return CommandGuards.RejectSheetProtected();
        }

        _snapshot = [];
        var affected = new List<CellAddress>(targetCells.Count);
        foreach (var (address, cell) in targetCells)
        {
            var oldCell = targetSheet.GetCell(address)?.Clone();
            var oldStyleOnly = targetSheet.GetStyleOnly(address.Row, address.Col);
            _snapshot.Add((address, oldCell, oldStyleOnly));

            var newCell = cell.Clone();
            if (oldCell is not null)
                newCell.StyleId = oldCell.StyleId;
            else if (oldStyleOnly.HasValue)
                newCell.StyleId = oldStyleOnly.Value;
            targetSheet.SetCell(address, newCell);
            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var targetSheet = ctx.GetSheet(_targetSheetId);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                targetSheet.ClearCell(address);
                RestoreStyleOnly(targetSheet, address, oldStyleOnly);
            }
            else
            {
                targetSheet.SetCell(address, oldCell.Clone());
            }
        }
    }

    private List<(CellAddress Address, Cell Cell)> BuildTargetCells()
    {
        var result = new List<(CellAddress Address, Cell Cell)>(_sourceCells.Count);
        foreach (var (rowOffset, colOffset, cell) in _sourceCells)
        {
            if (!WorksheetBounds.TryOffset(_destination, _targetSheetId, rowOffset, colOffset, out var address))
                throw new InvalidOperationException("Import destination range is outside the worksheet bounds.");

            result.Add((
                address,
                cell));
        }

        return result;
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}
