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
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, StyleOnlySource? OldStyleOnlySource)>? _snapshot;
    private List<(SheetId SheetId, CellAddress Address, IReadOnlyList<CellTextRun> OldRuns)>? _richTextSnapshot;

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

        // R92-render-cellstyle-inheritance-5-3 (grouped-sheet parity): classify THIS command the
        // same way ApplyStyleCommand does, so the style-only passes below enforce Excel's fixed
        // row-beats-column precedence at a row/column intersection on every grouped sheet, instead
        // of "whichever command ran last wins". The classification depends only on _sourceRange
        // (shared across all grouped sheets), so it is computed once outside the per-sheet loop.
        var commandSource = ApplyStyleCommand.DetermineStyleOnlySource(_sourceRange);

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
                _snapshot.Add((sheetId, address, cell.Clone(), null, null));
                cell.StyleId = StyleDiffStyleCache.GetOrRegister(
                    ctx.Workbook, _diff, cell.StyleId, styleCache);

                // Parity with ApplyStyleCommand.Apply's Pass 1: a whole-cell font-formatting change
                // (Bold/Italic/Underline/Strikethrough/Font Name/Font Size/Font Color) must win over
                // a stale per-run rich-text override for the same property, or the newly applied
                // uniform value stays masked by old run formatting on grouped sheets.
                if (sheet.RichTextRuns.TryGetValue(address, out var runs) && ApplyStyleCommand.AffectsRichRunFont(_diff))
                {
                    _richTextSnapshot ??= [];
                    _richTextSnapshot.Add((sheetId, address, runs));
                    sheet.RichTextRuns[address] = ApplyStyleCommand.ClearOverriddenRunProperties(runs, _diff);
                }
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

                        var oldStyleOnly = sheet.GetStyleOnly(r, c);
                        var oldSource = sheet.GetStyleOnlySource(r, c);

                        // A column-format op must never overwrite a row-sourced entry -- the row's
                        // format always wins at that intersection, on every grouped sheet.
                        if (commandSource == StyleOnlySource.Column && oldSource == StyleOnlySource.Row)
                            continue;

                        var address = new CellAddress(sheetId, r, c);
                        _snapshot.Add((sheetId, address, null, oldStyleOnly, oldSource));

                        // A row-format op overtaking a column-sourced entry REPLACES it outright
                        // (matching ApplyStyleCommand / real Excel) rather than merging on top.
                        var baseStyleId = commandSource == StyleOnlySource.Row && oldSource == StyleOnlySource.Column
                            ? StyleId.Default
                            : oldStyleOnly ?? StyleId.Default;

                        var newStyleId = StyleDiffStyleCache.GetOrRegister(
                            ctx.Workbook,
                            _diff,
                            baseStyleId,
                            styleCache);
                        sheet.SetStyleOnly(r, c, newStyleId);
                        if (commandSource.HasValue)
                            sheet.SetStyleOnlySource(r, c, commandSource.Value);
                        else
                            sheet.ClearStyleOnlySource(r, c);
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

                var existingSource = sheet.GetStyleOnlySource(row, col);

                // Same row-beats-column precedence as Pass 2.
                if (commandSource == StyleOnlySource.Column && existingSource == StyleOnlySource.Row)
                    continue;

                var addr = new CellAddress(sheetId, row, col);
                _snapshot.Add((sheetId, addr, null, existingStyleId, existingSource));

                var updatedBaseStyleId = commandSource == StyleOnlySource.Row && existingSource == StyleOnlySource.Column
                    ? StyleId.Default
                    : existingStyleId;

                var updated = StyleDiffStyleCache.GetOrRegister(
                    ctx.Workbook, _diff, updatedBaseStyleId, styleCache);
                sheet.SetStyleOnly(row, col, updated);
                if (commandSource.HasValue)
                    sheet.SetStyleOnlySource(row, col, commandSource.Value);
                else
                    sheet.ClearStyleOnlySource(row, col);
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

        foreach (var (sheetId, address, oldCell, oldStyleOnly, oldStyleOnlySource) in _snapshot)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                {
                    sheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                    // Restore the pre-existing entry's provenance tag too, so undoing this command
                    // doesn't leave a stale Row/Column tag (or lose one) at this address.
                    if (oldStyleOnlySource.HasValue)
                        sheet.SetStyleOnlySource(address.Row, address.Col, oldStyleOnlySource.Value);
                    else
                        sheet.ClearStyleOnlySource(address.Row, address.Col);
                }
                else
                {
                    sheet.ClearStyleOnly(address.Row, address.Col);
                }
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }
        }

        if (_richTextSnapshot is not null)
        {
            foreach (var (sheetId, address, oldRuns) in _richTextSnapshot)
                ctx.GetSheet(sheetId).RichTextRuns[address] = oldRuns;
        }
    }
}
