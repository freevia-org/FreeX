namespace FreeX.App.Host;

internal static class CellShiftDialogPlanner
{
    private static readonly IReadOnlyList<CellShiftDialogOption> InsertChoices =
    [
        new(CellShiftDialogChoice.ShiftCellsRight, "Shift cells _right"),
        new(CellShiftDialogChoice.ShiftCellsDown, "Shift cells _down"),
        new(CellShiftDialogChoice.EntireRow, "Entire _row"),
        new(CellShiftDialogChoice.EntireColumn, "Entire _column")
    ];

    private static readonly IReadOnlyList<CellShiftDialogOption> DeleteChoices =
    [
        new(CellShiftDialogChoice.ShiftCellsLeft, "Shift cells _left"),
        new(CellShiftDialogChoice.ShiftCellsUp, "Shift cells _up"),
        new(CellShiftDialogChoice.EntireRow, "Entire _row"),
        new(CellShiftDialogChoice.EntireColumn, "Entire _column")
    ];

    public static IReadOnlyList<CellShiftDialogOption> GetAvailableChoices(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert ? InsertChoices : DeleteChoices;

    public static KeyboardInsertDeleteDialogChoice ToKeyboardChoice(CellShiftDialogMode mode, CellShiftDialogChoice choice) =>
        (mode, choice) switch
        {
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.ShiftCellsDown) => KeyboardInsertDeleteDialogChoice.ShiftDown,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.ShiftCellsUp) => KeyboardInsertDeleteDialogChoice.ShiftUp,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, _) => DefaultKeyboardChoice(CellShiftDialogMode.Delete),
            _ => DefaultKeyboardChoice(CellShiftDialogMode.Insert)
        };

    private static KeyboardInsertDeleteDialogChoice DefaultKeyboardChoice(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Delete
            ? KeyboardInsertDeleteDialogChoice.ShiftLeft
            : KeyboardInsertDeleteDialogChoice.ShiftRight;
}
