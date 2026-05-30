namespace FreeX.App.Host;

internal static class CellShiftDialogPlanner
{
    private static readonly IReadOnlyList<CellShiftDialogOption> InsertChoices =
    [
        new(CellShiftDialogChoice.ShiftCellsRight, UiText.Get("CellShift_Insert_ShiftCellsRight")),
        new(CellShiftDialogChoice.ShiftCellsDown, UiText.Get("CellShift_Insert_ShiftCellsDown")),
        new(CellShiftDialogChoice.EntireRow, UiText.Get("CellShift_Insert_EntireRow")),
        new(CellShiftDialogChoice.EntireColumn, UiText.Get("CellShift_Insert_EntireColumn"))
    ];

    private static readonly IReadOnlyList<CellShiftDialogOption> DeleteChoices =
    [
        new(CellShiftDialogChoice.ShiftCellsLeft, UiText.Get("CellShift_Delete_ShiftCellsLeft")),
        new(CellShiftDialogChoice.ShiftCellsUp, UiText.Get("CellShift_Delete_ShiftCellsUp")),
        new(CellShiftDialogChoice.EntireRow, UiText.Get("CellShift_Delete_EntireRow")),
        new(CellShiftDialogChoice.EntireColumn, UiText.Get("CellShift_Delete_EntireColumn"))
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
