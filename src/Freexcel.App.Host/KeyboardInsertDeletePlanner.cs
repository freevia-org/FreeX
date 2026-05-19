using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum KeyboardInsertDeletePlan
{
    Rows,
    Columns,
    CellShiftDialog
}

public static class KeyboardInsertDeletePlanner
{
    public static KeyboardInsertDeletePlan PlanInsert(GridRange range) => Plan(range);

    public static KeyboardInsertDeletePlan PlanDelete(GridRange range) => Plan(range);

    private static KeyboardInsertDeletePlan Plan(GridRange range)
    {
        if (SelectionRangeService.IsWholeRowSelection(range))
            return KeyboardInsertDeletePlan.Rows;

        if (SelectionRangeService.IsWholeColumnSelection(range))
            return KeyboardInsertDeletePlan.Columns;

        return KeyboardInsertDeletePlan.CellShiftDialog;
    }
}
