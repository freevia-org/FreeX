using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record FormulaAuditSelectionPlan(
    SheetId TargetSheetId,
    IReadOnlyList<CellAddress> Matches);

public static class FormulaAuditSelectionPlanner
{
    public static FormulaAuditSelectionPlan? Plan(SheetId currentSheetId, IReadOnlyList<CellAddress> matches)
    {
        if (matches.Count == 0)
            return null;

        var targetSheetId = matches[0].Sheet;
        var targetMatches = matches
            .Where(address => address.Sheet == targetSheetId)
            .Distinct()
            .ToList();

        return new FormulaAuditSelectionPlan(targetSheetId, targetMatches);
    }
}
