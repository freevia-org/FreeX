using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class FormatCellsMergePlanner
{
    public static bool IsSelectionMerged(Sheet sheet, GridRange range) =>
        CellMergePlanner.IsSelectionMerged(sheet, range);

    public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        bool mergeCells,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        if (mergeCells)
        {
            return contentResolution == MergeCellContentResolution.ConcatenateAllCells
                ? CellMergePlanner.CreateMergeAndCenterCommands(sheet, sheetId, range, contentResolution)
                    .Where(command => command is not ApplyStyleCommand)
                    .ToList()
                : CellMergePlanner.CreateMergeCommands(sheet, sheetId, range, mergeCells: true);
        }

        return CellMergePlanner.CreateMergeCommands(sheet, sheetId, range, mergeCells: false);
    }
}
