using Free.Shared.Shell;

namespace FreeX.App.Presentation.Shell;

public enum WorkbookShortcutRoute
{
    NewWorkbook,
    OpenBackstage,
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
    OpenBackstage,
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
    ToggleRibbonVisibility,
    CycleShellFocus,
    SwitchToNextWorkbookWindow,
    SwitchToPreviousWorkbookWindow,
    MinimizeWorkbookWindow,
    MaximizeOrRestoreWorkbookWindow,
    OpenContextMenu,
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

public readonly record struct WorkbookShortcutChord(
    WorkbookShortcutKey Key,
    FreeKeyModifiers Modifiers = FreeKeyModifiers.None);

public sealed record WorkbookShortcutRouteRule(
    WorkbookShortcutRoute Route,
    WorkbookShortcutChord WindowsChord,
    WorkbookShortcutChord? NativeMenuChord = null);

public static class WorkbookKeyboardShortcutCatalog
{
    public static IReadOnlyList<ApplicationKeyboardShortcut<
        KeyboardCommandShortcut,
        WorkbookShortcutKey,
        FreeKeyModifiers>> ApplicationCommandShortcuts { get; } =
    [
        C(KeyboardCommandShortcut.CreateTable, WorkbookShortcutKey.T, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.CreateTable, WorkbookShortcutKey.L, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.InsertCurrentDate, WorkbookShortcutKey.OemSemicolon, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.InsertCurrentTime, WorkbookShortcutKey.OemSemicolon, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleOutlineSymbols, WorkbookShortcutKey.D8, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.PasteName, WorkbookShortcutKey.F3),
        C(KeyboardCommandShortcut.NameManager, WorkbookShortcutKey.F3, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.CreateNamesFromSelection, WorkbookShortcutKey.F3, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.SpellCheck, WorkbookShortcutKey.F7),
        C(KeyboardCommandShortcut.RestoreWorkbookWindow, WorkbookShortcutKey.F5, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.MoveWorkbookWindow, WorkbookShortcutKey.F7, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SizeWorkbookWindow, WorkbookShortcutKey.F8, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, WorkbookShortcutKey.F6, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, WorkbookShortcutKey.Tab, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, WorkbookShortcutKey.F6, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, WorkbookShortcutKey.Tab, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.MinimizeWorkbookWindow, WorkbookShortcutKey.F9, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow, WorkbookShortcutKey.F10, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, WorkbookShortcutKey.F9, FreeKeyModifiers.Control | FreeKeyModifiers.Alt | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.OpenErrorChecking, WorkbookShortcutKey.F10, FreeKeyModifiers.Alt | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleFormulaBarExpansion, WorkbookShortcutKey.U, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ToggleFilter, WorkbookShortcutKey.L, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ReapplyFilter, WorkbookShortcutKey.L, FreeKeyModifiers.Control | FreeKeyModifiers.Alt),
        C(KeyboardCommandShortcut.QuickAnalysis, WorkbookShortcutKey.Q, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.InsertEmbeddedChart, WorkbookShortcutKey.F1, FreeKeyModifiers.Alt),
        // Excel: Ctrl+F1 shows or hides the ribbon commands ("Show tabs only").
        C(KeyboardCommandShortcut.ToggleRibbonVisibility, WorkbookShortcutKey.F1, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.InsertChartSheet, WorkbookShortcutKey.F11),
        C(KeyboardCommandShortcut.GroupSelection, WorkbookShortcutKey.Right, FreeKeyModifiers.Alt | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.UngroupSelection, WorkbookShortcutKey.Left, FreeKeyModifiers.Alt | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.OpenFormatCellsFont, WorkbookShortcutKey.F, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.OpenFormatCellsFont, WorkbookShortcutKey.P, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.NewNote, WorkbookShortcutKey.F2, FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.NewThreadedComment, WorkbookShortcutKey.F2, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ZoomIn, WorkbookShortcutKey.OemPlus, FreeKeyModifiers.Control | FreeKeyModifiers.Alt),
        C(KeyboardCommandShortcut.ZoomOut, WorkbookShortcutKey.OemMinus, FreeKeyModifiers.Control | FreeKeyModifiers.Alt),
        C(KeyboardCommandShortcut.CopyFormulaFromAbove, WorkbookShortcutKey.OemQuotes, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.CopyValueFromAbove, WorkbookShortcutKey.OemQuotes, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ScrollActiveCellIntoView, WorkbookShortcutKey.Back, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.CycleSelectionCorner, WorkbookShortcutKey.OemPeriod, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SelectDirectPrecedents, WorkbookShortcutKey.OemOpenBrackets, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SelectDirectDependents, WorkbookShortcutKey.OemCloseBrackets, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.SelectAllPrecedents, WorkbookShortcutKey.OemOpenBrackets, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.SelectAllDependents, WorkbookShortcutKey.OemCloseBrackets, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.ClearSelectionAndEdit, WorkbookShortcutKey.Back),
        C(KeyboardCommandShortcut.ClearSelectionAndEdit, WorkbookShortcutKey.Back, FreeKeyModifiers.Shift),
        C(KeyboardCommandShortcut.CloseWorkbook, WorkbookShortcutKey.F4, FreeKeyModifiers.Control),
        C(KeyboardCommandShortcut.OpenActiveDropdown, WorkbookShortcutKey.Down, FreeKeyModifiers.Alt),
    ];

    public static IReadOnlyList<WorkbookShortcutRouteRule> Rules { get; } =
    [
        new(
            WorkbookShortcutRoute.NewWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.N, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.N, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.OpenBackstage,
            new WorkbookShortcutChord(WorkbookShortcutKey.O, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.O, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.OpenWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.SaveWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.S, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.S, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.SaveWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.PrintWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.P, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.P, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.PrintWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, FreeKeyModifiers.Control | FreeKeyModifiers.Shift)),
        // Excel: Ctrl+F2 displays the print preview area on the Print tab in the Backstage view.
        new(WorkbookShortcutRoute.PrintWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F2, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.Copy,
            new WorkbookShortcutChord(WorkbookShortcutKey.C, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.C, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.Copy, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.Cut,
            new WorkbookShortcutChord(WorkbookShortcutKey.X, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.X, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.Cut, new WorkbookShortcutChord(WorkbookShortcutKey.Delete, FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Paste,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.Paste, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.PasteSpecial,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, FreeKeyModifiers.Control | FreeKeyModifiers.Alt),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, FreeKeyModifiers.Meta | FreeKeyModifiers.Alt)),
        new(
            WorkbookShortcutRoute.Undo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.Undo, new WorkbookShortcutChord(WorkbookShortcutKey.Back, FreeKeyModifiers.Alt)),
        new(WorkbookShortcutRoute.Redo, new WorkbookShortcutChord(WorkbookShortcutKey.Y, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.Redo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ToggleBold,
            new WorkbookShortcutChord(WorkbookShortcutKey.B, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.B, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleBold, new WorkbookShortcutChord(WorkbookShortcutKey.D2, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleItalic,
            new WorkbookShortcutChord(WorkbookShortcutKey.I, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.I, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleItalic, new WorkbookShortcutChord(WorkbookShortcutKey.D3, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleUnderline,
            new WorkbookShortcutChord(WorkbookShortcutKey.U, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.U, FreeKeyModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleUnderline, new WorkbookShortcutChord(WorkbookShortcutKey.D4, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleStrikethrough,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, FreeKeyModifiers.Control),
            // Excel for Mac: Cmd+Shift+X. Ctrl+5 stays live through the Windows chord.
            new WorkbookShortcutChord(WorkbookShortcutKey.X, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.FillDown,
            new WorkbookShortcutChord(WorkbookShortcutKey.D, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.FillRight,
            new WorkbookShortcutChord(WorkbookShortcutKey.R, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.R, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.FlashFill,
            new WorkbookShortcutChord(WorkbookShortcutKey.E, FreeKeyModifiers.Control),
            // Excel for Mac: Cmd+E.
            new WorkbookShortcutChord(WorkbookShortcutKey.E, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.ToggleShowFormulas,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.ActivatePreviousSheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.ActivateNextSheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.SelectPreviousSheetGroup,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.SelectNextSheetGroup,
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.OpenFormatCells,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.NumberFormatGeneral,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatNumber,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatTime,
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatDate,
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatCurrency,
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatPercentage,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatScientific,
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ApplyOutlineBorder,
            new WorkbookShortcutChord(WorkbookShortcutKey.D7, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D7, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ClearOutlineBorder,
            new WorkbookShortcutChord(WorkbookShortcutKey.OemMinus, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.OemMinus, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Find,
            new WorkbookShortcutChord(WorkbookShortcutKey.F, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.F, FreeKeyModifiers.Meta)),
        new(
            WorkbookShortcutRoute.Replace,
            new WorkbookShortcutChord(WorkbookShortcutKey.H, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.H, FreeKeyModifiers.Control)),
        new(
            WorkbookShortcutRoute.GoTo,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, FreeKeyModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, FreeKeyModifiers.Control)),
        new(WorkbookShortcutRoute.GoTo, new WorkbookShortcutChord(WorkbookShortcutKey.F5)),
        new(
            WorkbookShortcutRoute.InsertFunction,
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.AutoSum,
            new WorkbookShortcutChord(WorkbookShortcutKey.OemPlus, FreeKeyModifiers.Alt),
            // Excel for Mac: Cmd+Shift+T.
            new WorkbookShortcutChord(WorkbookShortcutKey.T, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.WorkbookStatistics,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, FreeKeyModifiers.Control | FreeKeyModifiers.Shift)),
        new(
            WorkbookShortcutRoute.InsertWorksheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, FreeKeyModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, FreeKeyModifiers.Shift))
    ];

    private static readonly ApplicationKeyboardShortcutCatalog<
        WorkbookShortcutRoute,
        WorkbookShortcutKey,
        FreeKeyModifiers> WindowsRoutes = new(
            Rules.Select(rule => new ApplicationKeyboardShortcut<
                WorkbookShortcutRoute,
                WorkbookShortcutKey,
                FreeKeyModifiers>(
                    rule.Route,
                    rule.WindowsChord.Key,
                    rule.WindowsChord.Modifiers)));

    private static readonly ApplicationKeyboardShortcutCatalog<
        WorkbookShortcutRoute,
        WorkbookShortcutKey,
        FreeKeyModifiers> NativeMenuRoutes = new(
            Rules
                .Where(rule => rule.NativeMenuChord is not null)
                .Select(rule => new ApplicationKeyboardShortcut<
                    WorkbookShortcutRoute,
                    WorkbookShortcutKey,
                    FreeKeyModifiers>(
                        rule.Route,
                        rule.NativeMenuChord!.Value.Key,
                        rule.NativeMenuChord.Value.Modifiers)));

    private static readonly ApplicationKeyboardShortcutCatalog<
        KeyboardCommandShortcut,
        WorkbookShortcutKey,
        FreeKeyModifiers> ApplicationCommands = new(ApplicationCommandShortcuts);

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
        FreeKeyModifiers modifiers,
        out WorkbookShortcutRoute route) =>
        WindowsRoutes.TryResolve(key, modifiers, out route);

    public static bool TryGetNativeMenuRoute(
        WorkbookShortcutKey key,
        FreeKeyModifiers modifiers,
        out WorkbookShortcutRoute route) =>
        NativeMenuRoutes.TryResolve(key, modifiers, out route);

    public static bool TryGetApplicationCommand(
        WorkbookShortcutKey key,
        FreeKeyModifiers modifiers,
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
        FreeKeyModifiers> C(
            KeyboardCommandShortcut command,
            WorkbookShortcutKey key,
            FreeKeyModifiers modifiers = FreeKeyModifiers.None) =>
        new(command, key, modifiers);
}
