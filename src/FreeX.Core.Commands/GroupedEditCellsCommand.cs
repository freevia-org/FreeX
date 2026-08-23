using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies the same cell edits to the same row/column addresses on multiple grouped sheets.
/// </summary>
public sealed class GroupedEditCellsCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly SheetId _sourceSheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _sourceEdits;
    private List<(SheetId SheetId, CellEditCompanionSnapshot Snapshot)>? _snapshot;

    // R115-data-table-master-formula-refresh: mirrors EditCellsCommand's _appliedTableEffects --
    // a grouped edit that lands on a registered Data Table's master formula cell on one of the
    // grouped sheets must refresh that sheet's table body too, undone in the same transaction.
    private readonly List<IWorkbookCommand> _appliedTableEffects = [];

    // R119-commands-undo-byte-budget-1: the undo snapshot captures one full per-cell tuple
    // (Cell clone + style + hyperlink/metadata + rich-text runs + phonetic guide) for EVERY
    // grouped sheet the source edits are replayed onto, so the real retained size scales with
    // _sheetIds.Count * _sourceEdits.Count, not a flat per-command constant (see PasteCellsCommand
    // for the same per-cell shape on a single sheet).
    private const int BytesPerCell = 300;

    public string Label => "Edit Grouped Sheets";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min((long)_sheetIds.Count * _sourceEdits.Count * BytesPerCell, int.MaxValue);

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
            var addresses = new List<CellAddress>(_sourceEdits.Count);
            foreach (var (sourceAddress, _) in _sourceEdits)
            {
                var address = RemapAddress(sourceAddress, sheetId);
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return CommandGuards.RejectSheetProtected();
                addresses.Add(address);
            }

            // R156-grouped-edit-array-split-guard: mirrors EditCellsCommand.Apply's check --
            // this is the whole-command validation pass (nothing is mutated yet), so rejecting
            // here for ANY one grouped sheet stops the edit on ALL of them. Applying to the
            // sheets that pass and skipping the ones that don't would silently desynchronize the
            // grouped sheets, which is worse than the pre-fix all-or-nothing bug.
            if (CommandGuards.RejectIfSplitsArray(sheet, addresses, allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
                return splitsArrayRejection;
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
                var snapshot = CellEditCompanionSnapshot.Capture(sheet, address);
                _snapshot.Add((sheetId, snapshot));

                var appliedCell = sourceCell.Clone();
                if (appliedCell.StyleId == StyleId.Default)
                {
                    if (snapshot.Cell is not null)
                        appliedCell.StyleId = snapshot.Cell.StyleId;
                    else if (snapshot.StyleOnly is { } styleOnly)
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

        foreach (var (sheetId, snapshot) in _snapshot)
            snapshot.Restore(ctx.GetSheet(sheetId));
    }

    private CellAddress RemapAddress(CellAddress address, SheetId targetSheetId)
    {
        var sourceAddress = address.Sheet == _sourceSheetId
            ? address
            : new CellAddress(_sourceSheetId, address.Row, address.Col);
        return new CellAddress(targetSheetId, sourceAddress.Row, sourceAddress.Col);
    }
}
