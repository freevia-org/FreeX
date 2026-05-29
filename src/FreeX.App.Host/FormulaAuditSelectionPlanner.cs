using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record FormulaAuditSelectionPlan(
    SheetId TargetSheetId,
    IReadOnlyList<CellAddress> Matches);

public static class FormulaAuditSelectionPlanner
{
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
