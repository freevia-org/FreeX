using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ChartDataSourcePlanner
{
    public static GridRange ResolveInsertionRange(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.Start.Sheet != sheet.Id || selectedRange.CellCount != 1)
            return selectedRange;

        var activeCell = selectedRange.Start;
        StructuredTableModel? table = null;
        foreach (var candidate in sheet.StructuredTables)
        {
            if (!candidate.Range.Contains(activeCell))
                continue;

            if (table is null || candidate.Range.CellCount < table.Range.CellCount)
                table = candidate;
        }

        if (table is not null)
            return table.Range;

        return SelectionRangeService.GetCurrentRegion(sheet, activeCell) ?? selectedRange;
    }
}
