using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies a style diff to the same row/column range across multiple grouped sheets.
/// Uses the same used-range clamp as <see cref="ApplyStyleCommand"/> to avoid materialising
/// millions of style-only entries when a whole column or row is selected.
/// </summary>
public sealed class GroupedApplyStyleCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly GridRange _sourceRange;
    private readonly StyleDiff _diff;
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    private const int BytesPerCell = 200;

    public string Label => "Apply Style to Grouped Sheets";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_sourceRange.CellCount * _sheetIds.Count * BytesPerCell, int.MaxValue);

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
            if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
                return protectedOutcome;
        }
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];
        var styleCache = new Dictionary<StyleId, StyleId>();

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);

            // Compute the zone in which new style-only entries are created for empty cells.
            // Same clamp strategy as ApplyStyleCommand.
            var styleOnlyCreateZone = ApplyStyleCommand.StyleOnlyCreateZone(sheet, _sourceRange);

            // --- Pass 1: content cells anywhere in the selection ---
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                if (row < _sourceRange.Start.Row || row > _sourceRange.End.Row) continue;
                if (col < _sourceRange.Start.Col || col > _sourceRange.End.Col) continue;

                var address = new CellAddress(sheetId, row, col);
                _snapshot.Add((sheetId, address, cell.Clone(), null));
                cell.StyleId = StyleDiffStyleCache.GetOrRegister(
                    ctx.Workbook, _diff, cell.StyleId, styleCache);
            }

            // --- Pass 2: empty cells within the style-only create zone ---
            if (styleOnlyCreateZone.HasValue)
            {
                var zone = styleOnlyCreateZone.Value;
                for (var r = zone.Start.Row; r <= zone.End.Row; r++)
                {
                    for (var c = zone.Start.Col; c <= zone.End.Col; c++)
                    {
                        if (sheet.GetCell(r, c) is not null)
                            continue;

                        var address = new CellAddress(sheetId, r, c);
                        var oldStyleOnly = sheet.GetStyleOnly(r, c);
                        _snapshot.Add((sheetId, address, null, oldStyleOnly));

                        var newStyleId = StyleDiffStyleCache.GetOrRegister(
                            ctx.Workbook,
                            _diff,
                            oldStyleOnly ?? StyleId.Default,
                            styleCache);
                        sheet.SetStyleOnly(r, c, newStyleId);
                    }
                }
            }

            // --- Pass 3: pre-existing style-only entries outside the create zone ---
            // Materialise before the loop to avoid mutating _styleOnly while iterating it.
            var preExistingStyleOnly = sheet.GetStyleOnlyEntries().ToList();
            foreach (var ((row, col), existingStyleId) in preExistingStyleOnly)
            {
                if (row < _sourceRange.Start.Row || row > _sourceRange.End.Row) continue;
                if (col < _sourceRange.Start.Col || col > _sourceRange.End.Col) continue;

                if (styleOnlyCreateZone.HasValue)
                {
                    var z = styleOnlyCreateZone.Value;
                    if (row >= z.Start.Row && row <= z.End.Row &&
                        col >= z.Start.Col && col <= z.End.Col)
                    {
                        continue;
                    }
                }

                if (sheet.GetCell(row, col) is not null)
                    continue;

                var addr = new CellAddress(sheetId, row, col);
                _snapshot.Add((sheetId, addr, null, existingStyleId));

                var updated = StyleDiffStyleCache.GetOrRegister(
                    ctx.Workbook, _diff, existingStyleId, styleCache);
                sheet.SetStyleOnly(row, col, updated);
            }
        }

        // Report the affected cells (mirroring GroupedEditCellsCommand's own affected-cell list)
        // so WorkbookSession's undo/redo selection-restore path (ApplySuccessfulHistoryResult /
        // CommandOutcome.AffectedCells contract) knows which sheet(s) and range this grouped style
        // command touched -- without this, undoing it while a different sheet is active had
        // nothing to switch back to or restore a selection for.
        return new CommandOutcome(true, AffectedCells: _snapshot.ConvertAll(s => s.Address));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        foreach (var (sheetId, address, oldCell, oldStyleOnly) in _snapshot)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(address.Row, address.Col);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }
        }
    }
}
