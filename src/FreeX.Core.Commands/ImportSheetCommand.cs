using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ImportSheetCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures a (Cell?, StyleId?) pair for every
    // cell the import overwrites -- the same shape MoveRangeCommand/CopyRangeCommand use 400
    // bytes/cell for. Importing a large external range should count proportionally, not the flat
    // 200-byte default.
    private const int BytesPerCell = 400;

    private readonly SheetId _targetSheetId;
    private readonly CellAddress _destination;
    private readonly IReadOnlyList<(uint RowOffset, uint ColOffset, Cell Cell)> _sourceCells;
    private readonly uint _sourceRowCount;
    private readonly uint _sourceColCount;
    private readonly (uint RowCount, uint ColCount)? _previousExtent;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Import Data";

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? _sourceCells.Count) * BytesPerCell, int.MaxValue);

    /// <summary>
    /// <paramref name="previousExtent"/> is the row/column extent (RowCount x ColCount) the PRIOR
    /// import into this same <paramref name="destination"/> anchor occupied, when this call is a
    /// refresh of a remembered source rather than a fresh import. When the refreshed source has
    /// shrunk, cells inside the previous rectangle that fall outside the new one are cleared during
    /// <see cref="Apply"/> so a Data ▸ Refresh All against a source that lost rows/columns doesn't
    /// leave the old values behind as if they were still part of the import (round 134 fix). Callers
    /// that remember an import for refresh persist this extent in shared presentation/service
    /// state, set from the source sheet's used range after each successful import or refresh and
    /// fed back in as <paramref name="previousExtent"/> on the next one. Omitted (null) for a
    /// first-time import, which has no prior extent to reconcile against.
    /// </summary>
    public ImportSheetCommand(
        SheetId targetSheetId,
        CellAddress destination,
        Sheet sourceSheet,
        (uint RowCount, uint ColCount)? previousExtent = null)
    {
        _targetSheetId = targetSheetId;
        _destination = destination;
        _previousExtent = previousExtent;
        var usedRange = sourceSheet.GetUsedRange();
        if (usedRange is null)
        {
            _sourceRowCount = 0;
            _sourceColCount = 0;
            _sourceCells = [];
            return;
        }

        _sourceRowCount = usedRange.Value.RowCount;
        _sourceColCount = usedRange.Value.ColCount;
        _sourceCells = sourceSheet.EnumerateCells()
            .Select(c => (
                RowOffset: c.Address.Row - usedRange.Value.Start.Row,
                ColOffset: c.Address.Col - usedRange.Value.Start.Col,
                Cell: c.Cell.Clone()))
            .ToList();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_destination.Sheet != _targetSheetId)
            return new CommandOutcome(false, "Import destination must be on the target sheet.");
        if (_sourceCells.Count > 0 &&
            !WorksheetBounds.TryGetRectangleEnd(_destination, _sourceRowCount, _sourceColCount, out _))
        {
            return new CommandOutcome(false, "Import destination range is outside the worksheet bounds.");
        }

        var targetSheet = ctx.GetSheet(_targetSheetId);
        var targetCells = BuildTargetCells();
        var clearAddresses = BuildClearAddresses();

        foreach (var (address, _) in targetCells)
        {
            if (!CommandGuards.CanEditCell(ctx.Workbook, targetSheet, address))
                return CommandGuards.RejectSheetProtected();
        }

        foreach (var address in clearAddresses)
        {
            if (!CommandGuards.CanEditCell(ctx.Workbook, targetSheet, address))
                return CommandGuards.RejectSheetProtected();
        }

        _snapshot = [];
        var affected = new List<CellAddress>(targetCells.Count + clearAddresses.Count);
        foreach (var (address, cell) in targetCells)
        {
            var oldCell = targetSheet.GetCell(address)?.Clone();
            var oldStyleOnly = targetSheet.GetStyleOnly(address.Row, address.Col);
            _snapshot.Add((address, oldCell, oldStyleOnly));

            var newCell = cell.Clone();
            if (oldCell is not null)
                newCell.StyleId = oldCell.StyleId;
            else if (oldStyleOnly.HasValue)
                newCell.StyleId = oldStyleOnly.Value;
            targetSheet.SetCell(address, newCell);
            affected.Add(address);
        }

        // Round 134 fix: the previous import's extent can reach cells beyond what this (possibly
        // shrunk) refresh writes above -- e.g. a source that lost rows/columns. Clear exactly that
        // leftover rectangle-difference so it doesn't linger and read as if it were still part of the
        // freshly imported data. Bounded to the previously remembered extent only, never the sheet's
        // full used range, so user content the import never touched is left untouched.
        foreach (var address in clearAddresses)
        {
            var oldCell = targetSheet.GetCell(address)?.Clone();
            var oldStyleOnly = targetSheet.GetStyleOnly(address.Row, address.Col);
            _snapshot.Add((address, oldCell, oldStyleOnly));

            targetSheet.ClearCell(address);
            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var targetSheet = ctx.GetSheet(_targetSheetId);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                targetSheet.ClearCell(address);
                RestoreStyleOnly(targetSheet, address, oldStyleOnly);
            }
            else
            {
                targetSheet.SetCell(address, oldCell.Clone());
            }
        }
    }

    private List<(CellAddress Address, Cell Cell)> BuildTargetCells()
    {
        var result = new List<(CellAddress Address, Cell Cell)>(_sourceCells.Count);
        foreach (var (rowOffset, colOffset, cell) in _sourceCells)
        {
            if (!WorksheetBounds.TryOffset(_destination, _targetSheetId, rowOffset, colOffset, out var address))
                throw new InvalidOperationException("Import destination range is outside the worksheet bounds.");

            result.Add((
                address,
                cell));
        }

        return result;
    }

    /// <summary>
    /// The cells inside the remembered <see cref="_previousExtent"/> rectangle (anchored at the same
    /// <see cref="_destination"/> as the new import) that fall OUTSIDE the new source's rectangle --
    /// i.e. exactly the leftover cells a shrunk refresh must clear. Empty when this is not a refresh
    /// (<see cref="_previousExtent"/> is null) or the new import is at least as large in both
    /// dimensions as the previous one (nothing left over to clear).
    /// </summary>
    private List<CellAddress> BuildClearAddresses()
    {
        var result = new List<CellAddress>();
        if (_previousExtent is not { } previous)
            return result;

        // Rows below the new import's row extent, across the full previous column width.
        for (var row = _sourceRowCount; row < previous.RowCount; row++)
        {
            for (uint col = 0; col < previous.ColCount; col++)
                AddClearAddressIfInBounds(result, row, col);
        }

        // Columns to the right of the new import's column extent, within the rows the new import
        // still covers (rows beyond that are already fully handled by the loop above).
        var overlapRowCount = Math.Min(_sourceRowCount, previous.RowCount);
        for (uint row = 0; row < overlapRowCount; row++)
        {
            for (var col = _sourceColCount; col < previous.ColCount; col++)
                AddClearAddressIfInBounds(result, row, col);
        }

        return result;
    }

    private void AddClearAddressIfInBounds(List<CellAddress> result, uint rowOffset, uint colOffset)
    {
        // A previously valid extent should always re-offset cleanly, but guard defensively rather
        // than throw -- there is nothing to clear for an address that no longer fits the worksheet.
        if (WorksheetBounds.TryOffset(_destination, _targetSheetId, rowOffset, colOffset, out var address))
            result.Add(address);
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}
