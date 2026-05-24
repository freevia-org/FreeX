using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ClearContentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Clear Contents";

    public ClearContentsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var cells = _range.AllCells().ToList();
        if (cells.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return new CommandOutcome(false, "The sheet is protected.");

        _snapshot = [];
        var affected = new List<CellAddress>();
        foreach (var address in cells)
        {
            var oldCell = sheet.GetCell(address)?.Clone();
            _snapshot.Add((address, oldCell));

            var cleared = Cell.FromValue(BlankValue.Instance);
            if (oldCell is not null)
                cleared.StyleId = oldCell.StyleId;
            sheet.SetCell(address, cleared);
            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, oldCell.Clone());
        }
    }
}
