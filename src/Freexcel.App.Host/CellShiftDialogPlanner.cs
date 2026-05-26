namespace Freexcel.App.Host;

internal static class CellShiftDialogPlanner
{
    public static IReadOnlyList<CellShiftDialogOption> GetAvailableChoices(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert
            ? [
                new(CellShiftDialogChoice.ShiftCellsRight, "Shift cells _right"),
                new(CellShiftDialogChoice.ShiftCellsDown, "Shift cells _down"),
                new(CellShiftDialogChoice.EntireRow, "Entire _row"),
                new(CellShiftDialogChoice.EntireColumn, "Entire _column")
            ]
            : [
                new(CellShiftDialogChoice.ShiftCellsLeft, "Shift cells _left"),
                new(CellShiftDialogChoice.ShiftCellsUp, "Shift cells _up"),
                new(CellShiftDialogChoice.EntireRow, "Entire _row"),
                new(CellShiftDialogChoice.EntireColumn, "Entire _column")
            ];

    public static KeyboardInsertDeleteDialogChoice ToKeyboardChoice(CellShiftDialogMode mode, CellShiftDialogChoice choice) =>
        (mode, choice) switch
        {
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.ShiftCellsDown) => KeyboardInsertDeleteDialogChoice.ShiftDown,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.ShiftCellsUp) => KeyboardInsertDeleteDialogChoice.ShiftUp,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, _) => KeyboardInsertDeleteDialogChoice.ShiftLeft,
            _ => KeyboardInsertDeleteDialogChoice.ShiftRight
        };
}
