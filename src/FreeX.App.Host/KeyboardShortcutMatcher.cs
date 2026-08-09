using System.Windows.Input;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public static partial class KeyboardShortcutMatcher
{
    public static bool IsCtrlPlus(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var effectiveKey = GetEffectiveKey(key, systemKey);
        return modifiers == ModifierKeys.Control &&
                effectiveKey is Key.Add or Key.OemPlus ||
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                effectiveKey == Key.OemPlus;
    }

    public static bool IsCtrlMinus(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var effectiveKey = GetEffectiveKey(key, systemKey);
        return modifiers == ModifierKeys.Control &&
            effectiveKey is Key.Subtract or Key.OemMinus;
    }

    public static bool IsPasteSpecialShortcut(Key key, Key systemKey, ModifierKeys modifiers) =>
        TryGetWorkbookShortcutKey(GetEffectiveKey(key, systemKey), out var shortcutKey) &&
        WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
            shortcutKey,
            ToWorkbookModifiers(modifiers),
            out var route) &&
        route == WorkbookShortcutRoute.PasteSpecial;

    public static bool TryGetGridShortcut(Key key, ModifierKeys modifiers, out KeyboardGridShortcut shortcut)
    {
        shortcut = default;
        if (modifiers == ModifierKeys.Control && key is (Key.D9 or Key.NumPad9))
        {
            shortcut = KeyboardGridShortcut.HideRows;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is (Key.D9 or Key.NumPad9))
        {
            shortcut = KeyboardGridShortcut.UnhideRows;
            return true;
        }

        if (modifiers == ModifierKeys.Control && key is (Key.D0 or Key.NumPad0))
        {
            shortcut = KeyboardGridShortcut.HideColumns;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is (Key.D0 or Key.NumPad0))
        {
            shortcut = KeyboardGridShortcut.UnhideColumns;
            return true;
        }

        return false;
    }

    public static bool TryGetSelectionShortcut(Key key, ModifierKeys modifiers, out KeyboardSelectionShortcut shortcut)
    {
        shortcut = default;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Space)
        {
            shortcut = KeyboardSelectionShortcut.SelectAll;
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.Space)
        {
            shortcut = KeyboardSelectionShortcut.SelectWholeColumns;
            return true;
        }

        if (modifiers == ModifierKeys.Shift && key == Key.Space)
        {
            shortcut = KeyboardSelectionShortcut.SelectWholeRows;
            return true;
        }

        if (modifiers == ModifierKeys.Control && key == Key.Multiply ||
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is (Key.Multiply or Key.D8))
        {
            shortcut = KeyboardSelectionShortcut.SelectCurrentRegion;
            return true;
        }

        return false;
    }

    public static bool TryGetCommandShortcut(Key key, Key systemKey, ModifierKeys modifiers, out KeyboardCommandShortcut shortcut)
    {
        shortcut = default;
        var effectiveKey = GetEffectiveKey(key, systemKey);
        foreach (var rule in CommandShortcutRules)
        {
            if (!rule.Matches(effectiveKey, modifiers))
                continue;

            shortcut = rule.Shortcut;
            return true;
        }

        return false;
    }

    private static Key GetEffectiveKey(Key key, Key systemKey) =>
        key is Key.None or Key.System ? systemKey : key;

    private static bool TryGetWorkbookShortcutKey(Key key, out WorkbookShortcutKey shortcutKey)
    {
        shortcutKey = key switch
        {
            Key.A => WorkbookShortcutKey.A,
            Key.Back => WorkbookShortcutKey.Back,
            Key.B => WorkbookShortcutKey.B,
            Key.C => WorkbookShortcutKey.C,
            Key.D => WorkbookShortcutKey.D,
            Key.D1 => WorkbookShortcutKey.D1,
            Key.D2 or Key.NumPad2 => WorkbookShortcutKey.D2,
            Key.D3 or Key.NumPad3 => WorkbookShortcutKey.D3,
            Key.D4 or Key.NumPad4 => WorkbookShortcutKey.D4,
            Key.D5 or Key.NumPad5 => WorkbookShortcutKey.D5,
            Key.D6 => WorkbookShortcutKey.D6,
            Key.D7 => WorkbookShortcutKey.D7,
            Key.Delete => WorkbookShortcutKey.Delete,
            Key.E => WorkbookShortcutKey.E,
            Key.F => WorkbookShortcutKey.F,
            Key.F3 => WorkbookShortcutKey.F3,
            Key.F5 => WorkbookShortcutKey.F5,
            Key.F11 => WorkbookShortcutKey.F11,
            Key.F12 => WorkbookShortcutKey.F12,
            Key.G => WorkbookShortcutKey.G,
            Key.H => WorkbookShortcutKey.H,
            Key.I => WorkbookShortcutKey.I,
            Key.Insert => WorkbookShortcutKey.Insert,
            Key.N => WorkbookShortcutKey.N,
            Key.O => WorkbookShortcutKey.O,
            Key.Oem3 => WorkbookShortcutKey.Oem3,
            Key.OemMinus => WorkbookShortcutKey.OemMinus,
            Key.OemPlus or Key.Add => WorkbookShortcutKey.OemPlus,
            Key.PageDown => WorkbookShortcutKey.PageDown,
            Key.PageUp => WorkbookShortcutKey.PageUp,
            Key.P => WorkbookShortcutKey.P,
            Key.R => WorkbookShortcutKey.R,
            Key.S => WorkbookShortcutKey.S,
            Key.U => WorkbookShortcutKey.U,
            Key.V => WorkbookShortcutKey.V,
            Key.X => WorkbookShortcutKey.X,
            Key.Y => WorkbookShortcutKey.Y,
            Key.Z => WorkbookShortcutKey.Z,
            _ => default
        };

        return key is
            Key.A or
            Key.Back or
            Key.B or
            Key.C or
            Key.D or
            Key.D1 or
            Key.D2 or
            Key.NumPad2 or
            Key.D3 or
            Key.NumPad3 or
            Key.D4 or
            Key.NumPad4 or
            Key.D5 or
            Key.NumPad5 or
            Key.D6 or
            Key.D7 or
            Key.Delete or
            Key.E or
            Key.F or
            Key.F3 or
            Key.F5 or
            Key.F11 or
            Key.F12 or
            Key.G or
            Key.H or
            Key.I or
            Key.Insert or
            Key.N or
            Key.O or
            Key.Oem3 or
            Key.OemMinus or
            Key.OemPlus or
            Key.Add or
            Key.PageDown or
            Key.PageUp or
            Key.P or
            Key.R or
            Key.S or
            Key.U or
            Key.V or
            Key.X or
            Key.Y or
            Key.Z;
    }

    private static WorkbookShortcutModifiers ToWorkbookModifiers(ModifierKeys modifiers)
    {
        var result = WorkbookShortcutModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
            result |= WorkbookShortcutModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            result |= WorkbookShortcutModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            result |= WorkbookShortcutModifiers.Shift;
        return result;
    }

    public static bool TryGetNumberFormatShortcut(Key key, ModifierKeys modifiers, out NumberFormatShortcut shortcut)
    {
        shortcut = default;
        if (!TryGetWorkbookNumberFormatShortcutKey(key, out var shortcutKey) ||
            !WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                shortcutKey,
                ToWorkbookModifiers(modifiers),
                out var route))
        {
            return false;
        }

        shortcut = route switch
        {
            WorkbookShortcutRoute.NumberFormatGeneral => NumberFormatShortcut.General,
            WorkbookShortcutRoute.NumberFormatNumber => NumberFormatShortcut.Number,
            WorkbookShortcutRoute.NumberFormatTime => NumberFormatShortcut.Time,
            WorkbookShortcutRoute.NumberFormatDate => NumberFormatShortcut.Date,
            WorkbookShortcutRoute.NumberFormatCurrency => NumberFormatShortcut.Currency,
            WorkbookShortcutRoute.NumberFormatPercentage => NumberFormatShortcut.Percentage,
            WorkbookShortcutRoute.NumberFormatScientific => NumberFormatShortcut.Scientific,
            _ => default
        };

        return WorkbookKeyboardShortcutCatalog.IsNumberFormatRoute(route);
    }

    // R125-keyboard-numberformat-numpad-1: this resolver used to omit the NumPad1-6 aliases that
    // TryGetWorkbookShortcutKey above (used for every other Ctrl/Ctrl+Shift+digit route, e.g.
    // ToggleBold/FillDown) already grants Key.D2-D5, and that the Avalonia shell's own
    // TryGetWorkbookShortcutKey (MainWindow.cs) grants for ALL of D1-D6 uniformly. Concretely,
    // Ctrl+Shift+NumPad1..6 applied a Number/Time/Date/Currency/Percentage/Scientific format on
    // the Avalonia shell but silently did nothing on this WPF host -- a real cross-shell keyboard
    // divergence, not merely a missing nice-to-have. Match the Avalonia resolver's aliasing so the
    // two shells agree.
    private static bool TryGetWorkbookNumberFormatShortcutKey(Key key, out WorkbookShortcutKey shortcutKey)
    {
        shortcutKey = key switch
        {
            Key.Oem3 => WorkbookShortcutKey.Oem3,
            Key.D1 or Key.NumPad1 => WorkbookShortcutKey.D1,
            Key.D2 or Key.NumPad2 => WorkbookShortcutKey.D2,
            Key.D3 or Key.NumPad3 => WorkbookShortcutKey.D3,
            Key.D4 or Key.NumPad4 => WorkbookShortcutKey.D4,
            Key.D5 or Key.NumPad5 => WorkbookShortcutKey.D5,
            Key.D6 or Key.NumPad6 => WorkbookShortcutKey.D6,
            _ => default
        };

        return key is
            Key.Oem3 or
            Key.D1 or Key.NumPad1 or
            Key.D2 or Key.NumPad2 or
            Key.D3 or Key.NumPad3 or
            Key.D4 or Key.NumPad4 or
            Key.D5 or Key.NumPad5 or
            Key.D6 or Key.NumPad6;
    }

    public static bool TryGetFontToggleShortcut(Key key, ModifierKeys modifiers, out FontToggleShortcut shortcut)
    {
        shortcut = default;
        if (!TryGetWorkbookShortcutKey(key, out var shortcutKey) ||
            !WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                shortcutKey,
                ToWorkbookModifiers(modifiers),
                out var route))
        {
            return false;
        }

        shortcut = route switch
        {
            WorkbookShortcutRoute.ToggleBold => FontToggleShortcut.Bold,
            WorkbookShortcutRoute.ToggleItalic => FontToggleShortcut.Italic,
            WorkbookShortcutRoute.ToggleUnderline => FontToggleShortcut.Underline,
            WorkbookShortcutRoute.ToggleStrikethrough => FontToggleShortcut.Strikethrough,
            _ => default
        };

        return WorkbookKeyboardShortcutCatalog.IsFontToggleRoute(route);
    }

    public static bool TryGetBorderShortcut(Key key, ModifierKeys modifiers, out BorderKeyboardShortcut shortcut)
    {
        shortcut = default;
        if (!TryGetWorkbookShortcutKey(key, out var shortcutKey) ||
            !WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                shortcutKey,
                ToWorkbookModifiers(modifiers),
                out var route))
        {
            return false;
        }

        shortcut = route switch
        {
            WorkbookShortcutRoute.ApplyOutlineBorder => BorderKeyboardShortcut.Outline,
            WorkbookShortcutRoute.ClearOutlineBorder => BorderKeyboardShortcut.ClearOutline,
            _ => default
        };

        return WorkbookKeyboardShortcutCatalog.IsBorderRoute(route);
    }
}

public enum KeyboardGridShortcut
{
    HideRows,
    UnhideRows,
    HideColumns,
    UnhideColumns
}

public enum KeyboardSelectionShortcut
{
    SelectAll,
    SelectCurrentRegion,
    SelectWholeColumns,
    SelectWholeRows
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

public enum BorderKeyboardShortcut
{
    Outline,
    ClearOutline
}
