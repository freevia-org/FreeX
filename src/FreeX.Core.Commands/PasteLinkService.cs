using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class PasteLinkService
{
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        string sourceSheetName,
        bool transpose) =>
        CreateLinkedCells(sourceRange, destination, destinationRange: null, sourceSheetName, transpose);

    // R36-commands-paste-special-4-2: when the caller knows the full destination selection (not
    // just its top-left anchor), tile the linked-formula footprint across every whole repeat of
    // the source range that fits the selection -- mirroring how
    // PasteCommandFactory.CreateInternalPasteCommand tiles Values/Formulas/Formats/All onto a
    // destination selection that is a whole multiple of the copied range, instead of only ever
    // filling the selection's top-left cell. A trailing partial tile (selection size not an
    // exact multiple of the source range) is left untouched, matching that same tiling behavior.
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        GridRange? destinationRange,
        string sourceSheetName,
        bool transpose)
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

        var linkedCells = new List<(CellAddress Address, Cell Cell)>();
        for (var rowTileOffset = 0U; rowTileOffset + pasteRowCount <= targetRowCount; rowTileOffset += pasteRowCount)
        {
            for (var colTileOffset = 0U; colTileOffset + pasteColCount <= targetColCount; colTileOffset += pasteColCount)
            {
                for (uint row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
                {
                    for (uint col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
                    {
                        var rowOffset = row - sourceRange.Start.Row;
                        var colOffset = col - sourceRange.Start.Col;
                        // rowTileOffset/colTileOffset are already expressed in TARGET (post-transpose)
                        // row/col space (they are bounded by pasteRowCount/pasteColCount, which swap
                        // source row/col counts under transpose) -- so they always add to
                        // destination.Row/destination.Col respectively. Only the within-tile
                        // rowOffset/colOffset (still in SOURCE space) need to swap under transpose.
                        var target = transpose
                            ? new CellAddress(
                                destination.Sheet,
                                destination.Row + rowTileOffset + colOffset,
                                destination.Col + colTileOffset + rowOffset)
                            : new CellAddress(
                                destination.Sheet,
                                destination.Row + rowTileOffset + rowOffset,
                                destination.Col + colTileOffset + colOffset);
                        var sourceAddress = new CellAddress(sourceRange.Start.Sheet, row, col);
                        linkedCells.Add((target, Cell.FromFormula($"{SheetNameFormatter.QuoteIfNeeded(sourceSheetName)}!{sourceAddress.ToA1()}")));
                    }
                }
            }
        }

        return linkedCells;
    }
}
