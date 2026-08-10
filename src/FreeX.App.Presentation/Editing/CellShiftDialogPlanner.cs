namespace FreeX.App.Presentation.Editing;

public enum CellShiftDialogMode
{
    Insert,
    Delete
}

public enum CellShiftDialogChoice
{
    ShiftCellsRight,
    ShiftCellsDown,
    ShiftCellsLeft,
    ShiftCellsUp,
    EntireRow,
    EntireColumn
}

public sealed record CellShiftDialogOption(CellShiftDialogChoice Choice, string LabelKey);

public sealed record CellShiftDialogOptionPresentation(
    CellShiftDialogChoice Choice,
    string LabelKey,
    string AutomationName,
    string AutomationId,
    string HelpText);

public sealed record CellShiftDialogSurface(
    string TitleKey,
    string PromptKey,
    string GroupHeaderKey,
    IReadOnlyList<CellShiftDialogOptionPresentation> Options);

public static class CellShiftDialogPlanner
{
    private static readonly CellShiftDialogSurface InsertSurface = new(
        "CellShift_InsertTitle",
        "CellShift_InsertPrompt",
        "CellShift_InsertGroupHeader",
        [
            Option(CellShiftDialogChoice.ShiftCellsRight, "CellShift_Insert_ShiftCellsRight", "Shift cells right", "Insert cells and shift existing cells to the right."),
            Option(CellShiftDialogChoice.ShiftCellsDown, "CellShift_Insert_ShiftCellsDown", "Shift cells down", "Insert cells and shift existing cells down."),
            Option(CellShiftDialogChoice.EntireRow, "CellShift_Insert_EntireRow", "Entire row", "Apply the operation to the entire selected row."),
            Option(CellShiftDialogChoice.EntireColumn, "CellShift_Insert_EntireColumn", "Entire column", "Apply the operation to the entire selected column.")
        ]);

    private static readonly CellShiftDialogSurface DeleteSurface = new(
        "CellShift_DeleteTitle",
        "CellShift_DeletePrompt",
        "CellShift_DeleteGroupHeader",
        [
            Option(CellShiftDialogChoice.ShiftCellsLeft, "CellShift_Delete_ShiftCellsLeft", "Shift cells left", "Delete cells and shift remaining cells left."),
            Option(CellShiftDialogChoice.ShiftCellsUp, "CellShift_Delete_ShiftCellsUp", "Shift cells up", "Delete cells and shift remaining cells up."),
            Option(CellShiftDialogChoice.EntireRow, "CellShift_Delete_EntireRow", "Entire row", "Apply the operation to the entire selected row."),
            Option(CellShiftDialogChoice.EntireColumn, "CellShift_Delete_EntireColumn", "Entire column", "Apply the operation to the entire selected column.")
        ]);

    private static readonly IReadOnlyList<CellShiftDialogOption> InsertChoices =
        InsertSurface.Options.Select(option => new CellShiftDialogOption(option.Choice, option.LabelKey)).ToArray();

    private static readonly IReadOnlyList<CellShiftDialogOption> DeleteChoices =
        DeleteSurface.Options.Select(option => new CellShiftDialogOption(option.Choice, option.LabelKey)).ToArray();

    private static readonly IReadOnlyList<CellShiftDialogOptionPresentation> InsertCellChoices =
        InsertSurface.Options.Take(2).ToArray();

    private static readonly IReadOnlyList<CellShiftDialogOptionPresentation> DeleteCellChoices =
        DeleteSurface.Options.Take(2).ToArray();

    public static CellShiftDialogSurface GetSurface(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert ? InsertSurface : DeleteSurface;

    public static IReadOnlyList<CellShiftDialogOption> GetAvailableChoices(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert ? InsertChoices : DeleteChoices;

    public static IReadOnlyList<CellShiftDialogOptionPresentation> GetCellSelectionChoices(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert ? InsertCellChoices : DeleteCellChoices;

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

    private static CellShiftDialogOptionPresentation Option(
        CellShiftDialogChoice choice,
        string labelKey,
        string automationName,
        string helpText) =>
        new(choice, labelKey, automationName, $"CellShift{choice}Option", helpText);
}
