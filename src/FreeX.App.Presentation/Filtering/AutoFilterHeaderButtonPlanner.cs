using FreeX.App.Presentation.AutoFilter;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterHeaderButtonPlanner
{
    public static GridRange? TryGetAutoFilterRange(Sheet sheet) =>
        AutoFilterRangeResolver.TryGetAutoFilterRange(sheet);

    public static IReadOnlyList<CellAddress> GetHeaderButtonCells(Sheet sheet)
    {
        if (TryGetAutoFilterRange(sheet) is not { } range)
            return [];

        var headerRow = range.Start.Row;
        var cells = new List<CellAddress>((int)range.ColCount);
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            cells.Add(new CellAddress(sheet.Id, headerRow, col));

        return cells;
    }

    public static bool IsFilterButtonCell(Sheet sheet, uint row, uint col)
    {
        if (TryGetAutoFilterRange(sheet) is not { } range)
            return false;

        return row == range.Start.Row && col >= range.Start.Col && col <= range.End.Col;
    }
}
