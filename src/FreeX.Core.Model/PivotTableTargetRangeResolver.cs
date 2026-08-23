namespace FreeX.Core.Model;

public static class PivotTableTargetRangeResolver
{
    public static GridRange GetOccupiedRange(Sheet sheet, PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pivotTable);

        uint? minRow = null;
        uint? minCol = null;
        uint? maxRow = null;
        uint? maxCol = null;
        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;

            minRow = minRow is null ? row : Math.Min(minRow.Value, row);
            minCol = minCol is null ? col : Math.Min(minCol.Value, col);
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        if (minRow is null || minCol is null || maxRow is null || maxCol is null)
            return new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);

        return new GridRange(
            new CellAddress(sheet.Id, minRow.Value, minCol.Value),
            new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
    }
}
