using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Applies the same cell edits to the same row/column addresses on multiple grouped sheets.
/// </summary>
public sealed class GroupedEditCellsCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly SheetId _sourceSheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _sourceEdits;
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Edit Grouped Sheets";

    public GroupedEditCellsCommand(
        IReadOnlyCollection<SheetId> sheetIds,
        SheetId sourceSheetId,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> sourceEdits)
    {
        _sheetIds = sheetIds.Distinct().ToList();
        _sourceSheetId = sourceSheetId;
        _sourceEdits = sourceEdits;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sheetIds.Count == 0 || _sourceEdits.Count == 0)
            return new CommandOutcome(true, AffectedCells: []);

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            foreach (var (sourceAddress, _) in _sourceEdits)
            {
                var address = RemapAddress(sourceAddress, sheetId);
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            foreach (var (sourceAddress, sourceCell) in _sourceEdits)
            {
                var address = RemapAddress(sourceAddress, sheetId);
                var oldCell = sheet.GetCell(address)?.Clone();
                _snapshot.Add((sheetId, address, oldCell));

                var appliedCell = sourceCell.Clone();
                if (oldCell is not null)
                    appliedCell.StyleId = oldCell.StyleId;

                sheet.SetCell(address, appliedCell);
                affected.Add(address);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        foreach (var (sheetId, address, oldCell) in _snapshot)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (oldCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, oldCell.Clone());
        }
    }

    private CellAddress RemapAddress(CellAddress address, SheetId targetSheetId)
    {
        var sourceAddress = address.Sheet == _sourceSheetId
            ? address
            : new CellAddress(_sourceSheetId, address.Row, address.Col);
        return new CellAddress(targetSheetId, sourceAddress.Row, sourceAddress.Col);
    }
}
