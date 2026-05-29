using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class FormatCellsMergePlanner
{
    public static bool IsSelectionMerged(Sheet sheet, GridRange range) =>
        sheet.MergedRegions.Any(region => region.Overlaps(range));

    public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells)
    {
        if (mergeCells)
            return range.CellCount <= 1 ? [] : [new MergeCellsCommand(sheetId, range)];

        return sheet.MergedRegions
            .Where(region => region.Overlaps(range))
            .Select(region => (IWorkbookCommand)new UnmergeCellsCommand(sheetId, region))
            .ToList();
    }
}
