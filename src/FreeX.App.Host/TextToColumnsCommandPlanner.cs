using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class TextToColumnsCommandPlanner
{
    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        SheetId currentSheetId,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        var plans = BuildSheetPlans(workbook, targetSheetIds, sourceRange, result);
        if (plans.Count <= 1)
        {
            var plan = plans.Count == 0 ? null : plans[0];
            return new EditCellsCommand(plan?.SheetId ?? currentSheetId, plan?.Edits ?? []);
        }

        return new CompositeWorkbookCommand(
            "Text to Columns",
            plans
                .Select(plan => (IWorkbookCommand)new EditCellsCommand(plan.SheetId, plan.Edits))
                .ToList());
    }

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange sourceRange,
        TextToColumnsDialogResult result) =>
        TextToColumnsApplyPlanner.FindOverwriteTargets(workbook, targetSheetIds, sourceRange, result);

    internal static IReadOnlyList<TextToColumnsSheetApplyPlan> BuildSheetPlans(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange sourceRange,
        TextToColumnsDialogResult result) =>
        TextToColumnsApplyPlanner.BuildSheetPlans(workbook, targetSheetIds, sourceRange, result);
}
