using FreeX.Core.Model;

namespace FreeX.App.Host;

public readonly record struct FlashFillCommandPlan(
    uint FillColumn,
    uint SourceColumn,
    uint StartRow,
    uint EndRow);

public static class FlashFillRangePlanner
{
    public static FlashFillCommandPlan Plan(Sheet sheet, GridRange range)
    {
        var fillColumn = range.Start.Col;
        var sourceColumn = fillColumn > 1 ? fillColumn - 1 : fillColumn + 1;
        var startRow = range.Start.Row;
        var endRow = range.End.Row;

        if (startRow == endRow)
        {
            startRow = FindContiguousExampleStart(sheet, fillColumn, sourceColumn, startRow);
            endRow = FindAdjacentDataEnd(sheet, fillColumn, sourceColumn, endRow);
        }

        return new FlashFillCommandPlan(fillColumn, sourceColumn, startRow, endRow);
    }

    private static uint FindContiguousExampleStart(
        Sheet sheet,
        uint fillColumn,
        uint sourceColumn,
        uint selectedRow)
    {
        var startRow = selectedRow;
        while (startRow > 1)
        {
            var previousRow = startRow - 1;
            if (!HasValue(sheet, previousRow, fillColumn) || !HasValue(sheet, previousRow, sourceColumn))
                break;

            startRow = previousRow;
        }

        return startRow;
    }

    private static uint FindAdjacentDataEnd(
        Sheet sheet,
        uint fillColumn,
        uint sourceColumn,
        uint selectedRow)
    {
        var endRow = selectedRow;
        for (var row = selectedRow + 1; row <= CellAddress.MaxRow; row++)
        {
            if (!HasValue(sheet, row, fillColumn) && !HasValue(sheet, row, sourceColumn))
                break;

            endRow = row;
        }

        return endRow;
    }

    private static bool HasValue(Sheet sheet, uint row, uint column) =>
        sheet.GetValue(row, column) is not BlankValue;
}
