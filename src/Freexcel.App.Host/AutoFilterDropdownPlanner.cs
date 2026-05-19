using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AutoFilterDropdownPlan(GridRange Range, uint FilterColumnOffset);

public static class AutoFilterDropdownPlanner
{
    public static bool TryPlan(GridRange currentRegion, CellAddress activeCell, out AutoFilterDropdownPlan plan)
    {
        plan = default!;
        if (activeCell.Sheet != currentRegion.Start.Sheet ||
            activeCell.Row != currentRegion.Start.Row ||
            activeCell.Col < currentRegion.Start.Col ||
            activeCell.Col > currentRegion.End.Col)
        {
            return false;
        }

        plan = new AutoFilterDropdownPlan(currentRegion, activeCell.Col - currentRegion.Start.Col);
        return true;
    }
}
