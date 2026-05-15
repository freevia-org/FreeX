using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ConsolidateCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<GridRange> _sourceRanges;
    private readonly CellAddress _destination;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Consolidate";

    public ConsolidateCommand(IReadOnlyList<GridRange> sourceRanges, CellAddress destination)
    {
        _sourceRanges = sourceRanges;
        _destination = destination;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRanges.Count == 0)
            return new CommandOutcome(false, "At least one source range is required.");

        var rowCount = _sourceRanges[0].RowCount;
        var colCount = _sourceRanges[0].ColCount;
        if (_sourceRanges.Any(r => r.RowCount != rowCount || r.ColCount != colCount))
            return new CommandOutcome(false, "Consolidate source ranges must be the same size.");

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        if (destinationSheet.IsProtected)
        {
            for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                for (uint colOffset = 0; colOffset < colCount; colOffset++)
                {
                    var address = new CellAddress(_destination.Sheet, _destination.Row + rowOffset, _destination.Col + colOffset);
                    if (!CommandGuards.CanEditCell(ctx.Workbook, destinationSheet, address))
                        return new CommandOutcome(false, "The sheet is protected.");
                }
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();

        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                var total = 0.0;
                foreach (var range in _sourceRanges)
                {
                    var sourceSheet = ctx.GetSheet(range.Start.Sheet);
                    if (sourceSheet.GetValue(range.Start.Row + rowOffset, range.Start.Col + colOffset) is NumberValue number)
                        total += number.Value;
                }

                var destinationAddress = new CellAddress(
                    _destination.Sheet,
                    _destination.Row + rowOffset,
                    _destination.Col + colOffset);
                _snapshot.Add((destinationAddress, destinationSheet.GetCell(destinationAddress)?.Clone()));

                var newCell = Cell.FromValue(new NumberValue(total));
                if (destinationSheet.GetCell(destinationAddress) is { } oldCell)
                    newCell.StyleId = oldCell.StyleId;
                destinationSheet.SetCell(destinationAddress, newCell);
                affected.Add(destinationAddress);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        foreach (var (address, oldCell) in _snapshot)
        {
            if (oldCell is null)
                destinationSheet.ClearCell(address);
            else
                destinationSheet.SetCell(address, oldCell.Clone());
        }
    }
}
