using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using SharedApplyPlanner = FreeX.App.Presentation.Consolidate.ConsolidateApplyPlanner;
using SharedDialogPlanner = FreeX.App.Presentation.Consolidate.ConsolidateDialogPlanner;

namespace FreeX.App.Avalonia;

internal static class ConsolidateShellPlanner
{
    public static IReadOnlyList<(ConsolidateFunction Function, string Label)> FunctionChoices =>
        SharedDialogPlanner.FunctionChoices;

    public static ConsolidateCellValue[,] ReadSource(Sheet sheet, GridRange range) =>
        SharedApplyPlanner.ReadSource(sheet, range);

    public static ConsolidateCellValue ToCellValue(ScalarValue? value) =>
        SharedApplyPlanner.ToCellValue(value);

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> MapToEdits(
        SheetId sheetId,
        ConsolidateResult result,
        CellAddress destination) =>
        SharedApplyPlanner.MapToEdits(sheetId, result, destination);

    public static ScalarValue ToScalar(ConsolidateOutputCell cell) =>
        SharedApplyPlanner.ToScalar(cell);

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits) =>
        SharedApplyPlanner.FindOverwriteTargets(sheet, edits);
}
