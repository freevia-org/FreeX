using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterHeaderButtonPlanner
{
    public static GridRange? TryGetAutoFilterRange(Sheet sheet) =>
        AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange(sheet);

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

    public static IReadOnlySet<uint>? GetActiveColumnOffsets(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        HashSet<uint>? active = null;
        if (sheet.AutoFilter is { } autoFilter)
        {
            foreach (var column in autoFilter.FilterColumns)
                (active ??= []).Add((uint)column.ColumnId);
        }

        if (active is not null)
            return active;

        foreach (var table in sheet.StructuredTables)
        {
            if (!table.Range.Equals(range))
                continue;

            foreach (var column in table.FilterColumns)
                (active ??= []).Add((uint)column.ColumnId);
            break;
        }

        return active;
    }

    public static bool IsColumnActive(Sheet sheet, GridRange range, uint column) =>
        column >= range.Start.Col &&
        column <= range.End.Col &&
        GetActiveColumnOffsets(sheet, range)?.Contains(column - range.Start.Col) == true;
}
