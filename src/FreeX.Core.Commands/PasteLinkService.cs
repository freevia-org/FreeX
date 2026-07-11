using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class PasteLinkService
{
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        string sourceSheetName,
        bool transpose)
    {
        // R21: match every other paste path (e.g. PasteCommandFactory.CreateInternalPasteCommand,
        // which calls WorksheetBounds.TryGetRectangleEnd) by rejecting destinations that would
        // place any linked cell outside the worksheet grid, instead of silently writing an
        // off-grid formula cell that Sheet.SetCell/XLSX save have no bounds checking for.
        var targetRowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var targetColCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        if (!WorksheetBounds.TryGetRectangleEnd(destination, targetRowCount, targetColCount, out _))
            return [];

        var linkedCells = new List<(CellAddress Address, Cell Cell)>();
        for (uint row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
        {
            for (uint col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            {
                var rowOffset = row - sourceRange.Start.Row;
                var colOffset = col - sourceRange.Start.Col;
                var target = transpose
                    ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
                    : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
                var sourceAddress = new CellAddress(sourceRange.Start.Sheet, row, col);
                linkedCells.Add((target, Cell.FromFormula($"{SheetNameFormatter.QuoteIfNeeded(sourceSheetName)}!{sourceAddress.ToA1()}")));
            }
        }

        return linkedCells;
    }
}
