using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record TextToColumnsSheetCommandPlan(
    SheetId SheetId,
    GridRange SourceRange,
    CellAddress Destination,
    IReadOnlyList<(CellAddress Address, Cell NewCell)> Edits);

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
            var plan = plans.FirstOrDefault();
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
        TextToColumnsDialogResult result)
    {
        var targets = new List<CellAddress>();
        foreach (var plan in BuildSheetPlans(workbook, targetSheetIds, sourceRange, result))
        {
            var sheet = workbook.GetSheet(plan.SheetId);
            if (sheet is null)
                continue;

            targets.AddRange(TextToColumnsPlanner.FindOverwriteTargets(sheet, plan.Edits, plan.SourceRange));
        }

        return targets;
    }

    internal static IReadOnlyList<TextToColumnsSheetCommandPlan> BuildSheetPlans(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        return targetSheetIds
            .Distinct()
            .Select(sheetId => BuildSheetPlan(workbook.GetSheet(sheetId), sourceRange, result))
            .Where(plan => plan is not null)
            .Cast<TextToColumnsSheetCommandPlan>()
            .ToList();
    }

    private static TextToColumnsSheetCommandPlan? BuildSheetPlan(
        Sheet? sheet,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        if (sheet is null)
            return null;

        var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(sourceRange, sheet.Id);
        var destination = RemapDestination(result.Destination ?? sourceRange.Start, sheet.Id);
        var edits = result.SplitMode == TextToColumnsSplitMode.FixedWidth
            ? TextToColumnsPlanner.BuildFixedWidthEdits(
                sheet,
                sheetRange,
                destination,
                result.FixedWidthBreakPositions ?? [],
                result.ColumnFormats,
                result.AdvancedOptions)
            : TextToColumnsPlanner.BuildEdits(
                sheet,
                sheetRange,
                destination,
                result.Delimiters,
                result.TextQualifierChar,
                result.TreatConsecutiveDelimitersAsOne,
                result.ColumnFormats,
                result.AdvancedOptions);

        return new TextToColumnsSheetCommandPlan(sheet.Id, sheetRange, destination, edits);
    }

    private static CellAddress RemapDestination(CellAddress destination, SheetId sheetId) =>
        new(sheetId, destination.Row, destination.Col);
}
