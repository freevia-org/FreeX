using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Applies a style diff to the same row/column range across multiple grouped sheets.
/// </summary>
public sealed class GroupedApplyStyleCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly GridRange _sourceRange;
    private readonly StyleDiff _diff;
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Apply Style to Grouped Sheets";

    public GroupedApplyStyleCommand(
        IReadOnlyCollection<SheetId> sheetIds,
        GridRange sourceRange,
        StyleDiff diff)
    {
        _sheetIds = sheetIds.Distinct().ToList();
        _sourceRange = sourceRange;
        _diff = diff;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
                return protectedOutcome;
        }
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            foreach (var sourceAddress in _sourceRange.AllCells())
            {
                var address = new CellAddress(sheetId, sourceAddress.Row, sourceAddress.Col);
                var cell = sheet.GetCell(address);
                _snapshot.Add((sheetId, address, cell?.Clone()));

                if (cell is null)
                {
                    cell = Cell.FromValue(BlankValue.Instance);
                    sheet.SetCell(address, cell);
                }

                var baseStyle = ctx.Workbook.GetStyle(cell.StyleId);
                var newStyle = _diff.ApplyTo(baseStyle);
                cell.StyleId = ctx.Workbook.RegisterStyle(newStyle);
            }
        }

        return new CommandOutcome(true);
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
}
