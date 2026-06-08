using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class CellMergePlanner
{
    public static bool IsSelectionMerged(Sheet sheet, GridRange range) =>
        sheet.MergedRegions.Any(region => region.Overlaps(range));

    public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(SheetId sheetId, GridRange range)
    {
        var commands = new List<IWorkbookCommand>();
        if (range.CellCount > 1)
            commands.Add(new MergeCellsCommand(sheetId, range));

        commands.Add(new ApplyStyleCommand(sheetId, range, new StyleDiff(HAlign: HorizontalAlignment.Center)));
        return commands;
    }

    public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells)
    {
        if (mergeCells)
            return range.CellCount <= 1 ? [] : [new MergeCellsCommand(sheetId, range)];

        return CreateUnmergeCommands(sheet, sheetId, range);
    }

    public static IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(Sheet sheet, SheetId sheetId, GridRange range) =>
        sheet.MergedRegions
            .Where(region => region.Overlaps(range))
            .Select(region => (IWorkbookCommand)new UnmergeCellsCommand(sheetId, region))
            .ToList();
}
