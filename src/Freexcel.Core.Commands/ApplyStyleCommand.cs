using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Applies a partial style override to every cell in a range.
/// Only non-null StyleDiff fields are changed; others are preserved.
/// </summary>
public sealed class ApplyStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly StyleDiff _diff;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Apply Style";

    public ApplyStyleCommand(SheetId sheetId, GridRange range, StyleDiff diff)
    {
        _sheetId = sheetId;
        _range   = range;
        _diff    = diff;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = [];

        foreach (var addr in _range.AllCells())
        {
            var cell = sheet.GetCell(addr);
            _snapshot.Add((addr, cell?.Clone()));

            if (cell is null)
            {
                cell = Cell.FromValue(BlankValue.Instance);
                sheet.SetCell(addr, cell);
            }

            var baseStyle = ctx.Workbook.GetStyle(cell.StyleId);
            var newStyle  = _diff.ApplyTo(baseStyle);
            cell.StyleId  = ctx.Workbook.RegisterStyle(newStyle);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
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
