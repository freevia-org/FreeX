using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ChartDataSourcePlanner
{
    public static GridRange ResolveInsertionRange(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.Start.Sheet != sheet.Id || selectedRange.CellCount != 1)
            return selectedRange;

        var activeCell = selectedRange.Start;
        var table = sheet.StructuredTables
            .Where(table => table.Range.Contains(activeCell))
            .OrderBy(table => table.Range.CellCount)
            .FirstOrDefault();
        if (table is not null)
            return table.Range;

        return SelectionRangeService.GetCurrentRegion(sheet, activeCell) ?? selectedRange;
    }
}
