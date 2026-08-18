using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Pastes cell formatting without changing existing values or formulas.
/// </summary>
public sealed class PasteFormatsCommand : IWorkbookCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: the undo snapshot holds a full Cell clone plus style PER
    // FORMATTED CELL (see Apply below), scaling with _formats.Count, not a flat per-command
    // constant. Without this, CommandBus's 50 MB undo byte-budget bills every Paste Formats at the
    // 200-byte IEstimatesMemory default regardless of size.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, StyleId StyleId)> _formats;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => _formats.Count == 1 ? "Paste Format" : $"Paste {_formats.Count} Formats";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min((long)_formats.Count * BytesPerCell, int.MaxValue);

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
            _snapshot.Add((addr, oldCell, sheet.GetStyleOnly(addr.Row, addr.Col)));

            // A destination cell that is a non-anchor (hidden/covered) member of an existing
            // merged region must keep its own pre-merge style hidden, matching the guard
            // PasteCellsCommand/PasteSpecialCellsCommand already apply: only the merge's
            // top-left anchor cell is ever visibly/actually restyled. Writing a pasted style
            // into a covered cell would silently corrupt the formatting Unmerge later reveals.
            var mergeRegion = sheet.GetMergeRegion(addr);
            if (mergeRegion is { } region && !region.Start.Equals(addr))
                continue;

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
        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                RestoreStyleOnly(sheet, addr, oldStyleOnly);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
        }
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}
