using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum KeyboardInsertDeletePlan
{
    Rows,
    Columns,
    CellShiftDialog
}

public enum KeyboardInsertDeleteDialogChoice
{
    ShiftRight,
    ShiftDown,
    ShiftLeft,
    ShiftUp,
    EntireRow,
    EntireColumn
}

public static class KeyboardInsertDeletePlanner
{
    public static KeyboardInsertDeletePlan PlanInsert(GridRange range) => Plan(range);

    public static KeyboardInsertDeletePlan PlanDelete(GridRange range) => Plan(range);

    public static bool TryParseInsertDialogChoice(string input, out KeyboardInsertDeleteDialogChoice choice) =>
        TryParseDialogChoice(input, insert: true, out choice);

    public static bool TryParseDeleteDialogChoice(string input, out KeyboardInsertDeleteDialogChoice choice) =>
        TryParseDialogChoice(input, insert: false, out choice);

    private static KeyboardInsertDeletePlan Plan(GridRange range)
    {
        if (SelectionRangeService.IsWholeRowSelection(range))
            return KeyboardInsertDeletePlan.Rows;

        if (SelectionRangeService.IsWholeColumnSelection(range))
            return KeyboardInsertDeletePlan.Columns;

        return KeyboardInsertDeletePlan.CellShiftDialog;
    }

    private static bool TryParseDialogChoice(
        string input,
        bool insert,
        out KeyboardInsertDeleteDialogChoice choice)
    {
        switch (input.Trim().ToLowerInvariant())
        {
            case "right" or "r" when insert:
                choice = KeyboardInsertDeleteDialogChoice.ShiftRight;
                return true;
            case "down" or "d" when insert:
                choice = KeyboardInsertDeleteDialogChoice.ShiftDown;
                return true;
            case "left" or "l" when !insert:
                choice = KeyboardInsertDeleteDialogChoice.ShiftLeft;
                return true;
            case "up" or "u" when !insert:
                choice = KeyboardInsertDeleteDialogChoice.ShiftUp;
                return true;
            case "row" or "rows" or "entire row":
                choice = KeyboardInsertDeleteDialogChoice.EntireRow;
                return true;
            case "column" or "columns" or "col" or "cols" or "entire column":
                choice = KeyboardInsertDeleteDialogChoice.EntireColumn;
                return true;
            default:
                choice = default;
                return false;
        }
    }
}
