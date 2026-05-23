using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class KeyboardShortcutMatcher
{
    private static readonly KeyboardCommandShortcutRule[] CommandShortcutRules =
    [
        new(KeyboardCommandShortcut.NewWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.N),
        new(KeyboardCommandShortcut.OpenWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.O),
        new(KeyboardCommandShortcut.SaveWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.S),
        new(KeyboardCommandShortcut.Copy, (key, modifiers) => (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) && key == Key.C ||
            modifiers == ModifierKeys.Control && key == Key.Insert),
        new(KeyboardCommandShortcut.Cut, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.X ||
            modifiers == ModifierKeys.Shift && key == Key.Delete),
        new(KeyboardCommandShortcut.Paste, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.V ||
            modifiers == ModifierKeys.Shift && key == Key.Insert),
        new(KeyboardCommandShortcut.SelectCurrentRegionOrAll, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.A),
        new(KeyboardCommandShortcut.Undo, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Z),
        new(KeyboardCommandShortcut.Redo, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Y),
        new(KeyboardCommandShortcut.CreateTable, (key, modifiers) => modifiers == ModifierKeys.Control && key is Key.T or Key.L),
        new(KeyboardCommandShortcut.InsertHyperlink, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.K),
        new(KeyboardCommandShortcut.FillDown, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.D),
        new(KeyboardCommandShortcut.FillRight, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.R),
        new(KeyboardCommandShortcut.FlashFill, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.E),
        new(KeyboardCommandShortcut.InsertCurrentDate, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemSemicolon),
        new(KeyboardCommandShortcut.InsertCurrentTime, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemSemicolon),
        new(KeyboardCommandShortcut.ToggleShowFormulas, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Oem3),
        new(KeyboardCommandShortcut.ActivatePreviousSheet, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.PageUp),
        new(KeyboardCommandShortcut.ActivateNextSheet, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.PageDown),
        new(KeyboardCommandShortcut.SelectPreviousSheetGroup, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.PageUp),
        new(KeyboardCommandShortcut.SelectNextSheetGroup, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.PageDown),
        new(KeyboardCommandShortcut.OpenFormatCells, (key, modifiers) => modifiers == ModifierKeys.Control && key is Key.D1 or Key.NumPad1),
        new(KeyboardCommandShortcut.Find, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F),
        new(KeyboardCommandShortcut.Replace, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.H),
        new(KeyboardCommandShortcut.InsertFunction, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F3),
        new(KeyboardCommandShortcut.SpellCheck, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F7),
        new(KeyboardCommandShortcut.CloseWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key is Key.F4 or Key.W),
        new(KeyboardCommandShortcut.CalculateNow, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F9),
        new(KeyboardCommandShortcut.CalculateSheet, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F9),
        new(KeyboardCommandShortcut.CalculateNow, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.F9),
        new(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F9),
        new(KeyboardCommandShortcut.ToggleFormulaBarExpansion, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.U),
        new(KeyboardCommandShortcut.ToggleFilter, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.L),
        new(KeyboardCommandShortcut.QuickAnalysis, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Q),
        new(KeyboardCommandShortcut.OpenPrintPreview, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.P),
        new(KeyboardCommandShortcut.PasteValues, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.V),
        new(KeyboardCommandShortcut.GoTo, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F5 || modifiers == ModifierKeys.Control && key == Key.G),
        new(KeyboardCommandShortcut.InsertEmbeddedChart, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.F1),
        new(KeyboardCommandShortcut.AutoSum, (key, modifiers) => modifiers == ModifierKeys.Alt && key is Key.OemPlus or Key.Add),
        new(KeyboardCommandShortcut.GroupSelection, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.Right),
        new(KeyboardCommandShortcut.UngroupSelection, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.Left),
        new(KeyboardCommandShortcut.InsertChartSheet, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F11),
        new(KeyboardCommandShortcut.OpenFormatCellsFont, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is Key.F or Key.P),
        new(KeyboardCommandShortcut.WorkbookStatistics, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.G),
        new(KeyboardCommandShortcut.NewNote, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F2),
        new(KeyboardCommandShortcut.NewThreadedComment, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F2),
        new(KeyboardCommandShortcut.SaveAs, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F12),
        new(KeyboardCommandShortcut.ShowKeyTips, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F10),
        new(KeyboardCommandShortcut.OpenContextMenu, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F10 || modifiers == ModifierKeys.None && key == Key.Apps),
        new(KeyboardCommandShortcut.EditInFormulaBar, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F2),
        new(KeyboardCommandShortcut.InsertWorksheet, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F1 || modifiers == ModifierKeys.Shift && key == Key.F11),
        new(KeyboardCommandShortcut.ZoomIn, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is Key.OemPlus or Key.Add),
        new(KeyboardCommandShortcut.ZoomOut, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is Key.OemMinus or Key.Subtract),
        new(KeyboardCommandShortcut.CopyFormulaFromAbove, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemQuotes),
        new(KeyboardCommandShortcut.CopyValueFromAbove, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemQuotes),
        new(KeyboardCommandShortcut.OpenActiveDropdown, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.Down),
        new(KeyboardCommandShortcut.SelectVisibleCellsOnly, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.Oem1),
        new(KeyboardCommandShortcut.ScrollActiveCellIntoView, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Back),
        new(KeyboardCommandShortcut.CycleSelectionCorner, (key, modifiers) => modifiers == ModifierKeys.Control && key is Key.OemPeriod or Key.Decimal),
        new(KeyboardCommandShortcut.SelectDirectPrecedents, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemOpenBrackets),
        new(KeyboardCommandShortcut.SelectDirectDependents, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemCloseBrackets),
        new(KeyboardCommandShortcut.SelectAllPrecedents, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemOpenBrackets),
        new(KeyboardCommandShortcut.SelectAllDependents, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemCloseBrackets),
        new(KeyboardCommandShortcut.SelectCellsWithComments, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.O),
        new(KeyboardCommandShortcut.EditCell, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F2),
        new(KeyboardCommandShortcut.ClearSelection, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.Delete),
        new(KeyboardCommandShortcut.ClearSelectionAndEdit, (key, modifiers) => !HasControl(modifiers) && key == Key.Back),
        new(KeyboardCommandShortcut.RepeatLastAction, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F4),
    ];

    public static bool IsCtrlPlus(Key key, Key systemKey, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control &&
            (key is Key.Add or Key.OemPlus || systemKey is Key.Add or Key.OemPlus) ||
        modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
            (key == Key.OemPlus || systemKey == Key.OemPlus);

    public static bool IsCtrlMinus(Key key, Key systemKey, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control &&
        (key is Key.Subtract or Key.OemMinus || systemKey is Key.Subtract or Key.OemMinus);

    public static bool IsPasteSpecialShortcut(Key key, Key systemKey, ModifierKeys modifiers) =>
        (key == Key.V || systemKey == Key.V) && modifiers == (ModifierKeys.Control | ModifierKeys.Alt);

    public static bool TryGetGridShortcut(Key key, ModifierKeys modifiers, out KeyboardGridShortcut shortcut)
    {
        shortcut = default;
        if (modifiers == ModifierKeys.Control && key is Key.D9 or Key.NumPad9)
        {
            shortcut = KeyboardGridShortcut.HideRows;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is Key.D9 or Key.NumPad9)
        {
            shortcut = KeyboardGridShortcut.UnhideRows;
            return true;
        }

        if (modifiers == ModifierKeys.Control && key is Key.D0 or Key.NumPad0)
        {
            shortcut = KeyboardGridShortcut.HideColumns;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is Key.D0 or Key.NumPad0)
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

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is Key.Multiply or Key.D8)
        {
            shortcut = KeyboardSelectionShortcut.SelectCurrentRegion;
            return true;
        }

        return false;
    }

    public static bool TryGetCommandShortcut(Key key, Key systemKey, ModifierKeys modifiers, out KeyboardCommandShortcut shortcut)
    {
        shortcut = default;
        var effectiveKey = key == Key.None ? systemKey : key;
        foreach (var rule in CommandShortcutRules)
        {
            if (!rule.Matches(effectiveKey, modifiers))
                continue;

            shortcut = rule.Shortcut;
            return true;
        }

        return false;
    }

    public static bool TryGetNumberFormatShortcut(Key key, ModifierKeys modifiers, out NumberFormatShortcut shortcut)
    {
        shortcut = default;
        if (modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return false;

        shortcut = key switch
        {
            Key.Oem3 => NumberFormatShortcut.General,
            Key.D1 => NumberFormatShortcut.Number,
            Key.D2 => NumberFormatShortcut.Time,
            Key.D3 => NumberFormatShortcut.Date,
            Key.D4 => NumberFormatShortcut.Currency,
            Key.D5 => NumberFormatShortcut.Percentage,
            Key.D6 => NumberFormatShortcut.Scientific,
            _ => default
        };

        return key is Key.Oem3 or Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6;
    }

    public static bool TryGetFontToggleShortcut(Key key, ModifierKeys modifiers, out FontToggleShortcut shortcut)
    {
        shortcut = default;
        if ((key == Key.B && modifiers == ModifierKeys.Control) ||
            (key is Key.D2 or Key.NumPad2 && modifiers == ModifierKeys.Control))
        {
            shortcut = FontToggleShortcut.Bold;
            return true;
        }

        if ((key == Key.I && modifiers == ModifierKeys.Control) ||
            (key is Key.D3 or Key.NumPad3 && modifiers == ModifierKeys.Control))
        {
            shortcut = FontToggleShortcut.Italic;
            return true;
        }

        if ((key == Key.U && modifiers == ModifierKeys.Control) ||
            (key is Key.D4 or Key.NumPad4 && modifiers == ModifierKeys.Control))
        {
            shortcut = FontToggleShortcut.Underline;
            return true;
        }

        if (key is Key.D5 or Key.NumPad5 && modifiers == ModifierKeys.Control)
        {
            shortcut = FontToggleShortcut.Strikethrough;
            return true;
        }

        return false;
    }

    public static bool TryGetBorderShortcut(Key key, ModifierKeys modifiers, out BorderKeyboardShortcut shortcut)
    {
        shortcut = default;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.D7)
        {
            shortcut = BorderKeyboardShortcut.Outline;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemMinus)
        {
            shortcut = BorderKeyboardShortcut.ClearOutline;
            return true;
        }

        return false;
    }

    private static bool HasControl(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Control) != 0;

    private readonly record struct KeyboardCommandShortcutRule(
        KeyboardCommandShortcut Shortcut,
        Func<Key, ModifierKeys, bool> Matches);
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
    FillDown,
    FillRight,
    FlashFill,
    InsertCurrentDate,
    InsertCurrentTime,
    ToggleShowFormulas,
    ActivatePreviousSheet,
    ActivateNextSheet,
    SelectPreviousSheetGroup,
    SelectNextSheetGroup,
    OpenFormatCells,
    Find,
    Replace,
    InsertFunction,
    SpellCheck,
    CloseWorkbook,
    CalculateNow,
    CalculateSheet,
    RebuildDependenciesAndCalculate,
    ToggleFormulaBarExpansion,
    ToggleFilter,
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
    ShowKeyTips,
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
