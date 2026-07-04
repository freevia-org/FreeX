using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Command to edit the value or formula of one or more cells.
/// Captures previous cell state for undo.
/// </summary>
public sealed class EditCellsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _edits;
    private readonly IReadOnlyList<CellAddress> _affectedCells;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata)>? _snapshot;

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public EditCellsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        _sheetId = sheetId;
        _edits = edits;
        var affectedCells = new CellAddress[edits.Count];
        for (var i = 0; i < edits.Count; i++)
            affectedCells[i] = edits[i].Address;
        _affectedCells = affectedCells;
    }

    /// <summary>Convenience constructor for editing a single cell value.</summary>
    public EditCellsCommand(SheetId sheetId, CellAddress address, ScalarValue value)
        : this(sheetId, [(address, Cell.FromValue(value))])
    {
    }

    /// <summary>Convenience factory for editing a single cell value.</summary>
    public static EditCellsCommand ForValue(SheetId sheetId, CellAddress address, ScalarValue value)
        => new(sheetId, address, value);

    /// <summary>Convenience constructor for setting a single cell formula.</summary>
    public static EditCellsCommand ForFormula(SheetId sheetId, CellAddress address, string formulaText)
    {
        return new EditCellsCommand(sheetId, [(address, Cell.FromFormula(formulaText))]);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _edits)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        _snapshot = [];

        foreach (var (addr, newCell) in _edits)
        {
            // Save old state for undo
            var oldCell = sheet.GetCell(addr)?.Clone();
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(addr, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(addr, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(addr, out var oldHyperlinkMetadata);
            _snapshot.Add((
                addr,
                oldCell,
                sheet.GetStyleOnly(addr.Row, addr.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata));

            // Apply new state while preserving the cell's existing formatting.
            var appliedCell = newCell.Clone();
            if (oldCell is not null)
                appliedCell.StyleId = oldCell.StyleId;
            sheet.SetCell(addr, appliedCell);

            // The cell's content is being replaced, so any rich-text runs and hyperlink that
            // belonged to the old content are stale and must not carry over to the new content
            // (matching ClearContentsCommand/FillCellsCommand's handling of the same dictionaries).
            sheet.RichTextRuns.Remove(addr);
            sheet.Hyperlinks.Remove(addr);
            sheet.HyperlinkMetadata.Remove(addr);
        }

        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;

        var sheet = ctx.GetSheet(_sheetId);

        foreach (var (addr, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata) in _snapshot)
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

            if (hadHyperlink && oldHyperlink is not null)
                sheet.Hyperlinks[addr] = oldHyperlink;
            else
                sheet.Hyperlinks.Remove(addr);

            if (hadHyperlinkMetadata && oldHyperlinkMetadata is not null)
                sheet.HyperlinkMetadata[addr] = oldHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(addr);
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
