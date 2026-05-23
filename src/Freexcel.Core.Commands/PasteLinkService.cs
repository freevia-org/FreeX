using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class PasteLinkService
{
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        string sourceSheetName,
        bool transpose)
    {
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
                linkedCells.Add((target, Cell.FromFormula($"{QuoteSheetName(sourceSheetName)}!{sourceAddress.ToA1()}")));
            }
        }

        return linkedCells;
    }

    private static string QuoteSheetName(string sheetName) =>
        "'" + sheetName.Replace("'", "''", StringComparison.Ordinal) + "'";
}
