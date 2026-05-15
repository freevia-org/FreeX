using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Pastes complete cell payloads, including values/formulas and formatting.
/// </summary>
public sealed class PasteCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _cells;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _cells.Count == 1 ? "Paste Cell" : $"Paste {_cells.Count} Cells";

    public PasteCellsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, Cell Cell)> cells)
    {
        _sheetId = sheetId;
        _cells = cells;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _cells)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>(_cells.Count);

        foreach (var (addr, cell) in _cells)
        {
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));
            sheet.SetCell(addr, cell.Clone());
            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(addr);
            else
                sheet.SetCell(addr, oldCell.Clone());
        }
    }
}
