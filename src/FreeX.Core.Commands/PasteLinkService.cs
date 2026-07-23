using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class PasteLinkService
{
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        string sourceSheetName,
        bool transpose) =>
        CreateLinkedCells(sourceRange, destination, destinationRange: null, sourceSheetName, transpose, sourceAreas: null);

    // R36-commands-paste-special-4-2 / R37-meta-3: when the caller knows the full destination
    // selection (not just its top-left anchor), tile the linked-formula footprint across the
    // ENTIRE destination selection -- mirroring how
    // PasteCommandFactory.CreateInternalPasteCommand's EnumerateTiledAddresses tiles
    // Values/Formulas/Formats/All: every destination cell in the selection gets a linked formula,
    // with the source cell chosen by wrapping the offset modulo the source range's row/column
    // count. Unlike an earlier version of this method, a destination selection that is NOT an
    // exact whole multiple of the source range is NOT left with an untouched trailing partial
    // tile -- the last (partial) tile wraps back to the start of the source range, exactly like
    // EnumerateTiledAddresses does for Values/Formulas/Formats/All, so Paste Link produces the
    // same destination footprint as an ordinary paste of the same source onto the same selection.
    // R78-commands-paste-special-5-2: `sourceAreas`, when supplied with more than one area,
    // records every individually Ctrl+clicked area of a multi-area source selection (mirroring
    // InternalClipboard.SourceAreas in MainWindow.ClipboardCommands.cs). `sourceRange` remains
    // only the BOUNDING BOX of those areas, so without this, a destination cell aligned with the
    // gap between disjoint areas (never part of the selection) would still get planted with a
    // spurious link formula pointing at that gap's source cell.
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        GridRange? destinationRange,
        string sourceSheetName,
        bool transpose,
        IReadOnlyList<GridRange>? sourceAreas = null)
    {
        // R21: match every other paste path (e.g. PasteCommandFactory.CreateInternalPasteCommand,
        // which calls WorksheetBounds.TryGetRectangleEnd) by rejecting destinations that would
        // place any linked cell outside the worksheet grid, instead of silently writing an
        // off-grid formula cell that Sheet.SetCell/XLSX save have no bounds checking for.
        var pasteRowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pasteColCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var targetRowCount = destinationRange is { } dr && dr.RowCount > pasteRowCount ? dr.RowCount : pasteRowCount;
        var targetColCount = destinationRange is { } dr2 && dr2.ColCount > pasteColCount ? dr2.ColCount : pasteColCount;
        if (!WorksheetBounds.TryGetRectangleEnd(destination, targetRowCount, targetColCount, out _))
            return [];

        var multiAreaSources = sourceAreas is { Count: > 1 } areas ? areas : null;
        var linkedCells = new List<(CellAddress Address, Cell Cell)>();
        for (var rowOffset = 0U; rowOffset < targetRowCount; rowOffset++)
        {
            for (var colOffset = 0U; colOffset < targetColCount; colOffset++)
            {
                // Same wraparound formula as PasteCommandFactory.EnumerateTiledAddresses: the
                // offset within the (possibly transposed) destination footprint is reduced modulo
                // the corresponding SOURCE dimension so a trailing partial tile wraps back to the
                // start of the source range instead of being skipped.
                var sourceRowOffset = transpose
                    ? colOffset % sourceRange.RowCount
                    : rowOffset % sourceRange.RowCount;
                var sourceColOffset = transpose
                    ? rowOffset % sourceRange.ColCount
                    : colOffset % sourceRange.ColCount;
                var sourceAddress = new CellAddress(
                    sourceRange.Start.Sheet,
                    sourceRange.Start.Row + sourceRowOffset,
                    sourceRange.Start.Col + sourceColOffset);
                // R78-commands-paste-special-5-2: a cell that falls in the gap between disjoint
                // Ctrl-clicked source areas was never part of the copied selection -- its aligned
                // destination cell must be left completely alone, not planted with a link formula
                // to that never-selected gap cell.
                if (multiAreaSources is not null && !multiAreaSources.Any(area => area.Contains(sourceAddress)))
                    continue;
                var target = new CellAddress(
                    destination.Sheet,
                    destination.Row + rowOffset,
                    destination.Col + colOffset);
                linkedCells.Add((target, Cell.FromFormula($"{SheetNameFormatter.QuoteIfNeeded(sourceSheetName)}!{sourceAddress.ToA1()}")));
            }
        }

        return linkedCells;
    }
}
