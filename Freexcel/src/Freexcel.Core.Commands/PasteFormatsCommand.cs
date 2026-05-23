using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Pastes cell formatting without changing existing values or formulas.
/// </summary>
public sealed class PasteFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, StyleId StyleId)> _formats;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _formats.Count == 1 ? "Paste Format" : $"Paste {_formats.Count} Formats";

    public PasteFormatsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, StyleId StyleId)> formats)
    {
        _sheetId = sheetId;
        _formats = formats;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = [];
        var affected = new List<CellAddress>(_formats.Count);

        foreach (var (addr, styleId) in _formats)
        {
            var oldCell = sheet.GetCell(addr)?.Clone();
            _snapshot.Add((addr, oldCell));

            var newCell = oldCell?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            newCell.StyleId = styleId;
            sheet.SetCell(addr, newCell);
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
