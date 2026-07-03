using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Pastes complete cell payloads, including values/formulas and formatting.
/// </summary>
public sealed class PasteCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _cells;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRuns;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns)>? _snapshot;

    public string Label => _cells.Count == 1 ? "Paste Cell" : $"Paste {_cells.Count} Cells";

    public PasteCellsCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
        IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = null)
    {
        _sheetId = sheetId;
        _cells = cells;
        _richTextRuns = richTextRuns;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _cells)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>(_cells.Count);

        foreach (var (addr, cell) in _cells)
        {
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(addr, out var oldRuns);
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone(), sheet.GetStyleOnly(addr.Row, addr.Col), hadRichTextRuns, oldRuns));
            sheet.SetCell(addr, cell.Clone());

            if (_richTextRuns is not null && _richTextRuns.TryGetValue(addr, out var newRuns))
                sheet.RichTextRuns[addr] = newRuns;
            else
                sheet.RichTextRuns.Remove(addr);

            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns) in _snapshot)
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

            if (hadRichTextRuns && oldRichTextRuns is not null)
                sheet.RichTextRuns[addr] = oldRichTextRuns;
            else
                sheet.RichTextRuns.Remove(addr);
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
