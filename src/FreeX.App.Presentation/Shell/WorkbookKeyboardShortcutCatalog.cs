using Free.Shared.Shell;

namespace FreeX.App.Presentation.Shell;

public enum WorkbookShortcutRoute
{
    NewWorkbook,
    OpenWorkbook,
    SaveWorkbook,
    PrintWorkbook,
    Copy,
    Cut,
    Paste,
    PasteSpecial,
    Undo,
    Redo,
    ToggleBold,
    ToggleItalic,
    ToggleUnderline,
    ToggleStrikethrough,
    FillDown,
    FillRight,
    FlashFill,
    ToggleShowFormulas,
    ActivatePreviousSheet,
    ActivateNextSheet,
    SelectPreviousSheetGroup,
    SelectNextSheetGroup,
    OpenFormatCells,
    NumberFormatGeneral,
    NumberFormatNumber,
    NumberFormatTime,
    NumberFormatDate,
    NumberFormatCurrency,
    NumberFormatPercentage,
    NumberFormatScientific,
    ApplyOutlineBorder,
    ClearOutlineBorder,
    Find,
    Replace,
    GoTo,
    InsertFunction,
    AutoSum,
    WorkbookStatistics,
    InsertWorksheet
}

public enum KeyboardCommandShortcut
{
    NewWorkbook,
    OpenWorkbook,
    SaveWorkbook,
    Copy,
    Cut,
    Paste,
    SelectCurrentRegionOrAll,
    Undo,
    Redo,
    CreateTable,
    InsertHyperlink,
    OpenHyperlink,
    FillDown,
    FillRight,
    FlashFill,
    InsertCurrentDate,
    InsertCurrentTime,
    ToggleShowFormulas,
    ToggleOutlineSymbols,
    ActivatePreviousSheet,
    ActivateNextSheet,
    SelectPreviousSheetGroup,
    SelectNextSheetGroup,
    OpenFormatCells,
    Find,
    Replace,
    NameManager,
    CreateNamesFromSelection,
    InsertFunction,
    PasteName,
    SpellCheck,
    CloseWorkbook,
    RestoreWorkbookWindow,
    MoveWorkbookWindow,
    SizeWorkbookWindow,
    CalculateNow,
    CalculateSheet,
    CalculateFull,
    RebuildDependenciesAndCalculate,
    OpenErrorChecking,
    ToggleFormulaBarExpansion,
    ToggleFilter,
    ReapplyFilter,
    QuickAnalysis,
    OpenPrintPreview,
    PasteValues,
    GoTo,
    InsertEmbeddedChart,
    AutoSum,
    GroupSelection,
    UngroupSelection,
    InsertChartSheet,
    OpenFormatCellsFont,
    WorkbookStatistics,
    NewNote,
    NewThreadedComment,
    SaveAs,
    OpenHelp,
    ShowKeyTips,
    CycleShellFocus,
    SwitchToNextWorkbookWindow,
    SwitchToPreviousWorkbookWindow,
    MinimizeWorkbookWindow,
    MaximizeOrRestoreWorkbookWindow,
    OpenContextMenu,
    EditInFormulaBar,
    InsertWorksheet,
    ZoomIn,
    ZoomOut,
    CopyFormulaFromAbove,
    CopyValueFromAbove,
    OpenActiveDropdown,
    SelectVisibleCellsOnly,
    ScrollActiveCellIntoView,
    CycleSelectionCorner,
    SelectDirectPrecedents,
    SelectDirectDependents,
    SelectAllPrecedents,
    SelectAllDependents,
    SelectCellsWithComments,
    EditCell,
    ClearSelection,
    ClearSelectionAndEdit,
    RepeatLastAction
}

public enum WorkbookShortcutKey
{
    A,
    Apps,
    Back,
    B,
    C,
    D,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    Delete,
    Down,
    E,
    Enter,
    F,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    G,
    H,
    I,
    Insert,
    K,
    L,
    Left,
    N,
    O,
    Oem1,
    Oem3,
    OemCloseBrackets,
    OemMinus,
    OemOpenBrackets,
    OemPeriod,
    OemPlus,
    OemQuotes,
    OemSemicolon,
    PageDown,
    PageUp,
    P,
    Q,
    R,
    Right,
    S,
    Tab,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z
}

[Flags]
public enum WorkbookShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public readonly record struct WorkbookShortcutChord(
    WorkbookShortcutKey Key,
    WorkbookShortcutModifiers Modifiers = WorkbookShortcutModifiers.None);

public sealed record WorkbookShortcutRouteRule(
    WorkbookShortcutRoute Route,
    WorkbookShortcutChord WindowsChord,
    WorkbookShortcutChord? NativeMenuChord = null);

public static class WorkbookKeyboardShortcutCatalog
{
    public static IReadOnlyList<ApplicationKeyboardShortcut<
        KeyboardCommandShortcut,
        WorkbookShortcutKey,
        WorkbookShortcutModifiers>> ApplicationCommandShortcuts { get; } =
    [
        C(KeyboardCommandShortcut.CreateTable, WorkbookShortcutKey.T, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.CreateTable, WorkbookShortcutKey.L, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.InsertCurrentDate, WorkbookShortcutKey.OemSemicolon, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.InsertCurrentTime, WorkbookShortcutKey.OemSemicolon, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleOutlineSymbols, WorkbookShortcutKey.D8, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.PasteName, WorkbookShortcutKey.F3),
        C(KeyboardCommandShortcut.NameManager, WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.CreateNamesFromSelection, WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.SpellCheck, WorkbookShortcutKey.F7),
        C(KeyboardCommandShortcut.RestoreWorkbookWindow, WorkbookShortcutKey.F5, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.MoveWorkbookWindow, WorkbookShortcutKey.F7, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SizeWorkbookWindow, WorkbookShortcutKey.F8, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, WorkbookShortcutKey.F6, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, WorkbookShortcutKey.Tab, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, WorkbookShortcutKey.F6, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, WorkbookShortcutKey.Tab, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.MinimizeWorkbookWindow, WorkbookShortcutKey.F9, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow, WorkbookShortcutKey.F10, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, WorkbookShortcutKey.F9, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.OpenErrorChecking, WorkbookShortcutKey.F10, WorkbookShortcutModifiers.Alt | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleFormulaBarExpansion, WorkbookShortcutKey.U, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleFilter, WorkbookShortcutKey.L, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ReapplyFilter, WorkbookShortcutKey.L, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt),
        C(KeyboardCommandShortcut.QuickAnalysis, WorkbookShortcutKey.Q, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.InsertEmbeddedChart, WorkbookShortcutKey.F1, WorkbookShortcutModifiers.Alt),
        C(KeyboardCommandShortcut.InsertChartSheet, WorkbookShortcutKey.F11),
        C(KeyboardCommandShortcut.GroupSelection, WorkbookShortcutKey.Right, WorkbookShortcutModifiers.Alt | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.UngroupSelection, WorkbookShortcutKey.Left, WorkbookShortcutModifiers.Alt | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.OpenFormatCellsFont, WorkbookShortcutKey.F, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.OpenFormatCellsFont, WorkbookShortcutKey.P, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.NewNote, WorkbookShortcutKey.F2, WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.NewThreadedComment, WorkbookShortcutKey.F2, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.EditInFormulaBar, WorkbookShortcutKey.F2, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.ZoomIn, WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt),
        C(KeyboardCommandShortcut.ZoomOut, WorkbookShortcutKey.OemMinus, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt),
        C(KeyboardCommandShortcut.CopyFormulaFromAbove, WorkbookShortcutKey.OemQuotes, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.CopyValueFromAbove, WorkbookShortcutKey.OemQuotes, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ScrollActiveCellIntoView, WorkbookShortcutKey.Back, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.CycleSelectionCorner, WorkbookShortcutKey.OemPeriod, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SelectDirectPrecedents, WorkbookShortcutKey.OemOpenBrackets, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SelectDirectDependents, WorkbookShortcutKey.OemCloseBrackets, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.SelectAllPrecedents, WorkbookShortcutKey.OemOpenBrackets, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.SelectAllDependents, WorkbookShortcutKey.OemCloseBrackets, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.ClearSelectionAndEdit, WorkbookShortcutKey.Back),
        C(KeyboardCommandShortcut.ClearSelectionAndEdit, WorkbookShortcutKey.Back, WorkbookShortcutModifiers.Shift),
        C(KeyboardCommandShortcut.CloseWorkbook, WorkbookShortcutKey.F4, WorkbookShortcutModifiers.Control),
        C(KeyboardCommandShortcut.OpenActiveDropdown, WorkbookShortcutKey.Down, WorkbookShortcutModifiers.Alt),
    ];

    public static IReadOnlyList<WorkbookShortcutRouteRule> Rules { get; } =
    [
        new(
            WorkbookShortcutRoute.NewWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.N, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.N, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.OpenWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.O, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.O, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.OpenWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.SaveWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.S, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.S, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.SaveWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.PrintWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.P, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.P, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.PrintWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Copy,
            new WorkbookShortcutChord(WorkbookShortcutKey.C, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.C, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Copy, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.Cut,
            new WorkbookShortcutChord(WorkbookShortcutKey.X, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.X, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Cut, new WorkbookShortcutChord(WorkbookShortcutKey.Delete, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Paste,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Paste, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.PasteSpecial,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Alt)),
        new(
            WorkbookShortcutRoute.Undo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Undo, new WorkbookShortcutChord(WorkbookShortcutKey.Back, WorkbookShortcutModifiers.Alt)),
        new(WorkbookShortcutRoute.Redo, new WorkbookShortcutChord(WorkbookShortcutKey.Y, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.Redo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ToggleBold,
            new WorkbookShortcutChord(WorkbookShortcutKey.B, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.B, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleBold, new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleItalic,
            new WorkbookShortcutChord(WorkbookShortcutKey.I, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.I, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleItalic, new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleUnderline,
            new WorkbookShortcutChord(WorkbookShortcutKey.U, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.U, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleUnderline, new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleStrikethrough,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FillDown,
            new WorkbookShortcutChord(WorkbookShortcutKey.D, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FillRight,
            new WorkbookShortcutChord(WorkbookShortcutKey.R, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.R, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FlashFill,
            new WorkbookShortcutChord(WorkbookShortcutKey.E, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.E, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleShowFormulas,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ActivatePreviousSheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.ActivateNextSheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.SelectPreviousSheetGroup,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.SelectNextSheetGroup,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.OpenFormatCells,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.NumberFormatGeneral,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatNumber,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatTime,
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatDate,
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatCurrency,
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatPercentage,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatScientific,
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ApplyOutlineBorder,
            new WorkbookShortcutChord(WorkbookShortcutKey.D7, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D7, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ClearOutlineBorder,
            new WorkbookShortcutChord(WorkbookShortcutKey.OemMinus, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.OemMinus, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Find,
            new WorkbookShortcutChord(WorkbookShortcutKey.F, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.F, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.Replace,
            new WorkbookShortcutChord(WorkbookShortcutKey.H, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.H, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.GoTo,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control)),
        new(WorkbookShortcutRoute.GoTo, new WorkbookShortcutChord(WorkbookShortcutKey.F5)),
        new(
            WorkbookShortcutRoute.InsertFunction,
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.AutoSum,
            new WorkbookShortcutChord(WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Alt),
            new WorkbookShortcutChord(WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Alt)),
        new(
            WorkbookShortcutRoute.WorkbookStatistics,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.InsertWorksheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, WorkbookShortcutModifiers.Shift))
    ];

    private static readonly ApplicationKeyboardShortcutCatalog<
        WorkbookShortcutRoute,
        WorkbookShortcutKey,
        WorkbookShortcutModifiers> WindowsRoutes = new(
            Rules.Select(rule => new ApplicationKeyboardShortcut<
                WorkbookShortcutRoute,
                WorkbookShortcutKey,
                WorkbookShortcutModifiers>(
                    rule.Route,
                    rule.WindowsChord.Key,
                    rule.WindowsChord.Modifiers)));

    private static readonly ApplicationKeyboardShortcutCatalog<
        WorkbookShortcutRoute,
        WorkbookShortcutKey,
        WorkbookShortcutModifiers> NativeMenuRoutes = new(
            Rules
                .Where(rule => rule.NativeMenuChord is not null)
                .Select(rule => new ApplicationKeyboardShortcut<
                    WorkbookShortcutRoute,
                    WorkbookShortcutKey,
                    WorkbookShortcutModifiers>(
                        rule.Route,
                        rule.NativeMenuChord!.Value.Key,
                        rule.NativeMenuChord.Value.Modifiers)));

    private static readonly ApplicationKeyboardShortcutCatalog<
        KeyboardCommandShortcut,
        WorkbookShortcutKey,
        WorkbookShortcutModifiers> ApplicationCommands = new(ApplicationCommandShortcuts);

    /// <summary>
    /// Maps a platform key enum name onto the portable shortcut key catalog, including the aliases
    /// used by the WPF and Avalonia key enums.
    /// </summary>
    public static bool TryParseKeyName(string? keyName, out WorkbookShortcutKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(keyName))
            return false;

        var canonicalName = keyName switch
        {
            "NumPad1" => nameof(WorkbookShortcutKey.D1),
            "NumPad2" => nameof(WorkbookShortcutKey.D2),
            "NumPad3" => nameof(WorkbookShortcutKey.D3),
            "NumPad4" => nameof(WorkbookShortcutKey.D4),
            "NumPad5" => nameof(WorkbookShortcutKey.D5),
            "NumPad6" => nameof(WorkbookShortcutKey.D6),
            "Add" => nameof(WorkbookShortcutKey.OemPlus),
            "Subtract" => nameof(WorkbookShortcutKey.OemMinus),
            "Decimal" => nameof(WorkbookShortcutKey.OemPeriod),
            "Next" => nameof(WorkbookShortcutKey.PageDown),
            "Prior" => nameof(WorkbookShortcutKey.PageUp),
            "Oem1" => nameof(WorkbookShortcutKey.OemSemicolon),
            "Oem4" => nameof(WorkbookShortcutKey.OemOpenBrackets),
            "Oem6" => nameof(WorkbookShortcutKey.OemCloseBrackets),
            "Oem7" => nameof(WorkbookShortcutKey.OemQuotes),
            _ => keyName
        };

        return Enum.TryParse(canonicalName, ignoreCase: false, out key) &&
            Enum.IsDefined(key);
    }

    public static bool TryGetWindowsRoute(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        out WorkbookShortcutRoute route) =>
        WindowsRoutes.TryResolve(key, modifiers, out route);

    public static bool TryGetNativeMenuRoute(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        out WorkbookShortcutRoute route) =>
        NativeMenuRoutes.TryResolve(key, modifiers, out route);

    public static bool TryGetApplicationCommand(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        out KeyboardCommandShortcut command) =>
        ApplicationCommands.TryResolve(key, modifiers, out command);

    public static bool IsCommandRoute(WorkbookShortcutRoute route) =>
        route != WorkbookShortcutRoute.PasteSpecial &&
        !IsFontToggleRoute(route) &&
        !IsNumberFormatRoute(route) &&
        !IsBorderRoute(route);

    public static bool IsFontToggleRoute(WorkbookShortcutRoute route) =>
        route is
            WorkbookShortcutRoute.ToggleBold or
            WorkbookShortcutRoute.ToggleItalic or
            WorkbookShortcutRoute.ToggleUnderline or
            WorkbookShortcutRoute.ToggleStrikethrough;

    public static bool IsNumberFormatRoute(WorkbookShortcutRoute route) =>
        route is
            WorkbookShortcutRoute.NumberFormatGeneral or
            WorkbookShortcutRoute.NumberFormatNumber or
            WorkbookShortcutRoute.NumberFormatTime or
            WorkbookShortcutRoute.NumberFormatDate or
            WorkbookShortcutRoute.NumberFormatCurrency or
            WorkbookShortcutRoute.NumberFormatPercentage or
            WorkbookShortcutRoute.NumberFormatScientific;

    public static bool IsBorderRoute(WorkbookShortcutRoute route) =>
        route is WorkbookShortcutRoute.ApplyOutlineBorder or WorkbookShortcutRoute.ClearOutlineBorder;

    public static WorkbookShortcutChord GetNativeMenuChord(WorkbookShortcutRoute route) =>
        Rules
            .Where(rule => rule.Route == route && rule.NativeMenuChord is not null)
            .Select(rule => rule.NativeMenuChord!.Value)
            .Single();

    private static ApplicationKeyboardShortcut<
        KeyboardCommandShortcut,
        WorkbookShortcutKey,
        WorkbookShortcutModifiers> C(
            KeyboardCommandShortcut command,
            WorkbookShortcutKey key,
            WorkbookShortcutModifiers modifiers = WorkbookShortcutModifiers.None) =>
        new(command, key, modifiers);
}
