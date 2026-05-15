using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ImportSheetCommand : IWorkbookCommand
{
    private readonly SheetId _targetSheetId;
    private readonly CellAddress _destination;
    private readonly IReadOnlyList<(uint RowOffset, uint ColOffset, Cell Cell)> _sourceCells;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Import Data";

    public ImportSheetCommand(SheetId targetSheetId, CellAddress destination, Sheet sourceSheet)
    {
        _targetSheetId = targetSheetId;
        _destination = destination;
        var usedRange = sourceSheet.GetUsedRange();
        if (usedRange is null)
        {
            _sourceCells = [];
            return;
        }

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

        var targetSheet = ctx.GetSheet(_targetSheetId);
        var targetCells = BuildTargetCells();

        foreach (var (address, _) in targetCells)
        {
            if (!CommandGuards.CanEditCell(ctx.Workbook, targetSheet, address))
                return new CommandOutcome(false, "The sheet is protected.");
        }

        _snapshot = [];
        var affected = new List<CellAddress>(targetCells.Count);
        foreach (var (address, cell) in targetCells)
        {
            var oldCell = targetSheet.GetCell(address)?.Clone();
            _snapshot.Add((address, oldCell));

            var newCell = cell.Clone();
            if (oldCell is not null)
                newCell.StyleId = oldCell.StyleId;
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
        foreach (var (address, oldCell) in _snapshot)
        {
            if (oldCell is null)
                targetSheet.ClearCell(address);
            else
                targetSheet.SetCell(address, oldCell.Clone());
        }
    }

    private List<(CellAddress Address, Cell Cell)> BuildTargetCells()
    {
        var result = new List<(CellAddress Address, Cell Cell)>(_sourceCells.Count);
        foreach (var (rowOffset, colOffset, cell) in _sourceCells)
        {
            result.Add((
                new CellAddress(_targetSheetId, _destination.Row + rowOffset, _destination.Col + colOffset),
                cell));
        }

        return result;
    }
}
