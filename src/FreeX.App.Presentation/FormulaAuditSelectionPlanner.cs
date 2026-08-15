using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public sealed record FormulaAuditSelectionPlan(
    SheetId TargetSheetId,
    IReadOnlyList<CellAddress> Matches);

public static class FormulaAuditSelectionPlanner
{
    public static FormulaAuditSelectionPlan? Plan(
        Workbook workbook,
        CellAddress activeCell,
        bool selectDependents,
        bool includeTransitive)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        IReadOnlyList<CellAddress> matches;
        if (includeTransitive)
        {
            var arrows = selectDependents
                ? FormulaAuditingService.GetDependentTraceArrows(workbook, activeCell)
                : FormulaAuditingService.GetPrecedentTraceArrows(workbook, activeCell);
            matches = arrows
                .Select(arrow => selectDependents ? arrow.To : arrow.From)
                .ToList();
        }
        else
        {
            matches = selectDependents
                ? FormulaAuditingService.GetDirectDependents(workbook, activeCell)
                : FormulaAuditingService.GetDirectPrecedents(workbook, activeCell);
        }

        return Plan(activeCell.Sheet, matches);
    }

    public static FormulaAuditSelectionPlan? Plan(SheetId currentSheetId, IReadOnlyList<CellAddress> matches)
    {
        if (matches.Count == 0)
            return null;

        var targetSheetId = GetTargetSheetId(matches);
        var targetMatches = CollectMatchesOnSheet(matches, targetSheetId);

        return new FormulaAuditSelectionPlan(targetSheetId, targetMatches);
    }

    private static SheetId GetTargetSheetId(IReadOnlyList<CellAddress> matches) =>
        matches[0].Sheet;

    private static List<CellAddress> CollectMatchesOnSheet(IReadOnlyList<CellAddress> matches, SheetId targetSheetId) =>
        matches
            .Where(address => address.Sheet == targetSheetId)
            .Distinct()
            .ToList();
}
