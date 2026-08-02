using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies the same cell edits to the same row/column addresses on multiple grouped sheets.
/// </summary>
public sealed class GroupedEditCellsCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly SheetId _sourceSheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _sourceEdits;
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata, bool HadPhoneticGuide, CellPhoneticGuide? OldPhoneticGuide)>? _snapshot;

    // R115-data-table-master-formula-refresh: mirrors EditCellsCommand's _appliedTableEffects --
    // a grouped edit that lands on a registered Data Table's master formula cell on one of the
    // grouped sheets must refresh that sheet's table body too, undone in the same transaction.
    private readonly List<IWorkbookCommand> _appliedTableEffects = [];

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
                    return CommandGuards.RejectSheetProtected();
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();
        var appliedEdits = new List<(CellAddress Address, Cell NewCell)>();

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            foreach (var (sourceAddress, sourceCell) in _sourceEdits)
            {
                var address = RemapAddress(sourceAddress, sheetId);
                var oldCell = sheet.GetCell(address)?.Clone();
                var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(address, out var oldRuns);
                var hadHyperlink = sheet.Hyperlinks.TryGetValue(address, out var oldHyperlink);
                var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(address, out var oldHyperlinkMetadata);
                var hadPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(address, out var oldPhoneticGuide);
                _snapshot.Add((
                    sheetId,
                    address,
                    oldCell,
                    sheet.GetStyleOnly(address.Row, address.Col),
                    hadRichTextRuns,
                    oldRuns,
                    hadHyperlink,
                    oldHyperlink,
                    hadHyperlinkMetadata,
                    oldHyperlinkMetadata,
                    hadPhoneticGuide,
                    oldPhoneticGuide));

                var appliedCell = sourceCell.Clone();
                if (appliedCell.StyleId == StyleId.Default)
                {
                    if (oldCell is not null)
                        appliedCell.StyleId = oldCell.StyleId;
                    else if (sheet.GetStyleOnly(address.Row, address.Col) is { } styleOnly)
                        appliedCell.StyleId = styleOnly;
                }

                sheet.SetCell(address, appliedCell);

                // The cell's content is being replaced, so any rich-text runs, hyperlink and
                // phonetic guide that belonged to the old content are stale and must not carry
                // over to the new content (matching EditCellsCommand's handling of the same
                // dictionaries).
                sheet.RichTextRuns.Remove(address);
                sheet.Hyperlinks.Remove(address);
                sheet.HyperlinkMetadata.Remove(address);
                sheet.CellPhoneticGuides.Remove(address);

                affected.Add(address);
                appliedEdits.Add((address, appliedCell));
            }
        }

        // R115-data-table-master-formula-refresh: see the matching call in EditCellsCommand.Apply --
        // a Data Table's body is a one-time text-baked substitution of its master formula, so
        // re-derive it here whenever this grouped edit lands on that master/header formula cell on
        // any of the grouped sheets.
        var dataTableRefreshCells = DataTableAutoRefreshEffects.Apply(ctx, appliedEdits, _appliedTableEffects);
        if (dataTableRefreshCells.Count > 0)
            affected.AddRange(dataTableRefreshCells);

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        for (var i = _appliedTableEffects.Count - 1; i >= 0; i--)
            _appliedTableEffects[i].Revert(ctx);
        _appliedTableEffects.Clear();

        foreach (var (sheetId, address, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata, hadPhoneticGuide, oldPhoneticGuide) in _snapshot)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (oldCell is null)
            {
                sheet.ClearCell(address);
                RestoreStyleOnly(sheet, address, oldStyleOnly);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }

            if (hadRichTextRuns && oldRichTextRuns is not null)
                sheet.RichTextRuns[address] = oldRichTextRuns;
            else
                sheet.RichTextRuns.Remove(address);

            if (hadHyperlink && oldHyperlink is not null)
                sheet.Hyperlinks[address] = oldHyperlink;
            else
                sheet.Hyperlinks.Remove(address);

            if (hadHyperlinkMetadata && oldHyperlinkMetadata is not null)
                sheet.HyperlinkMetadata[address] = oldHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(address);

            if (hadPhoneticGuide && oldPhoneticGuide is not null)
                sheet.CellPhoneticGuides[address] = oldPhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(address);
        }
    }

    private CellAddress RemapAddress(CellAddress address, SheetId targetSheetId)
    {
        var sourceAddress = address.Sheet == _sourceSheetId
            ? address
            : new CellAddress(_sourceSheetId, address.Row, address.Col);
        return new CellAddress(targetSheetId, sourceAddress.Row, sourceAddress.Col);
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}
