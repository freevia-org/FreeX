using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class KeyboardShortcutMatcher
{
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
        if (modifiers == ModifierKeys.Control && effectiveKey == Key.N)
        {
            shortcut = KeyboardCommandShortcut.NewWorkbook;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.O)
        {
            shortcut = KeyboardCommandShortcut.OpenWorkbook;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.S)
        {
            shortcut = KeyboardCommandShortcut.SaveWorkbook;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && effectiveKey == Key.C)
        {
            shortcut = KeyboardCommandShortcut.Copy;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && effectiveKey == Key.X)
        {
            shortcut = KeyboardCommandShortcut.Cut;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.V)
        {
            shortcut = KeyboardCommandShortcut.Paste;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && effectiveKey == Key.A)
        {
            shortcut = KeyboardCommandShortcut.SelectCurrentRegionOrAll;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && effectiveKey == Key.Z)
        {
            shortcut = KeyboardCommandShortcut.Undo;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && effectiveKey == Key.Y)
        {
            shortcut = KeyboardCommandShortcut.Redo;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey is Key.T or Key.L)
        {
            shortcut = KeyboardCommandShortcut.CreateTable;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.K)
        {
            shortcut = KeyboardCommandShortcut.InsertHyperlink;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.D)
        {
            shortcut = KeyboardCommandShortcut.FillDown;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.R)
        {
            shortcut = KeyboardCommandShortcut.FillRight;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.E)
        {
            shortcut = KeyboardCommandShortcut.FlashFill;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.OemSemicolon)
        {
            shortcut = KeyboardCommandShortcut.InsertCurrentDate;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.OemSemicolon)
        {
            shortcut = KeyboardCommandShortcut.InsertCurrentTime;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.Oem3)
        {
            shortcut = KeyboardCommandShortcut.ToggleShowFormulas;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.PageUp)
        {
            shortcut = KeyboardCommandShortcut.ActivatePreviousSheet;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.PageDown)
        {
            shortcut = KeyboardCommandShortcut.ActivateNextSheet;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.PageUp)
        {
            shortcut = KeyboardCommandShortcut.SelectPreviousSheetGroup;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.PageDown)
        {
            shortcut = KeyboardCommandShortcut.SelectNextSheetGroup;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey is Key.D1 or Key.NumPad1)
        {
            shortcut = KeyboardCommandShortcut.OpenFormatCells;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.F)
        {
            shortcut = KeyboardCommandShortcut.Find;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.H)
        {
            shortcut = KeyboardCommandShortcut.Replace;
            return true;
        }

        if (modifiers == ModifierKeys.Shift && effectiveKey == Key.F3)
        {
            shortcut = KeyboardCommandShortcut.InsertFunction;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F7)
        {
            shortcut = KeyboardCommandShortcut.SpellCheck;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey is Key.F4 or Key.W)
        {
            shortcut = KeyboardCommandShortcut.CloseWorkbook;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F9)
        {
            shortcut = KeyboardCommandShortcut.CalculateNow;
            return true;
        }

        if (modifiers == ModifierKeys.Shift && effectiveKey == Key.F9)
        {
            shortcut = KeyboardCommandShortcut.CalculateSheet;
            return true;
        }

        if (effectiveKey == Key.F9 && modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            shortcut = KeyboardCommandShortcut.CalculateNow;
            return true;
        }

        if (effectiveKey == Key.F9 && modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift))
        {
            shortcut = KeyboardCommandShortcut.RebuildDependenciesAndCalculate;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.U)
        {
            shortcut = KeyboardCommandShortcut.ToggleFormulaBarExpansion;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.L)
        {
            shortcut = KeyboardCommandShortcut.ToggleFilter;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.Q)
        {
            shortcut = KeyboardCommandShortcut.QuickAnalysis;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.P)
        {
            shortcut = KeyboardCommandShortcut.OpenPrintPreview;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.V)
        {
            shortcut = KeyboardCommandShortcut.PasteValues;
            return true;
        }

        if ((modifiers == ModifierKeys.None && effectiveKey == Key.F5) ||
            (modifiers == ModifierKeys.Control && effectiveKey == Key.G))
        {
            shortcut = KeyboardCommandShortcut.GoTo;
            return true;
        }

        if (modifiers == ModifierKeys.Alt && effectiveKey == Key.F1)
        {
            shortcut = KeyboardCommandShortcut.InsertEmbeddedChart;
            return true;
        }

        if (modifiers == ModifierKeys.Alt && effectiveKey is Key.OemPlus or Key.Add)
        {
            shortcut = KeyboardCommandShortcut.AutoSum;
            return true;
        }

        if (modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && effectiveKey == Key.Right)
        {
            shortcut = KeyboardCommandShortcut.GroupSelection;
            return true;
        }

        if (modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && effectiveKey == Key.Left)
        {
            shortcut = KeyboardCommandShortcut.UngroupSelection;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F11)
        {
            shortcut = KeyboardCommandShortcut.InsertChartSheet;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey is Key.F or Key.P)
        {
            shortcut = KeyboardCommandShortcut.OpenFormatCellsFont;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.G)
        {
            shortcut = KeyboardCommandShortcut.WorkbookStatistics;
            return true;
        }

        if (modifiers == ModifierKeys.Shift && effectiveKey == Key.F2)
        {
            shortcut = KeyboardCommandShortcut.NewNote;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.F2)
        {
            shortcut = KeyboardCommandShortcut.NewThreadedComment;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F12)
        {
            shortcut = KeyboardCommandShortcut.SaveAs;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F10)
        {
            shortcut = KeyboardCommandShortcut.ShowKeyTips;
            return true;
        }

        if ((modifiers == ModifierKeys.Shift && effectiveKey == Key.F10) ||
            (modifiers == ModifierKeys.None && effectiveKey == Key.Apps))
        {
            shortcut = KeyboardCommandShortcut.OpenContextMenu;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.F2)
        {
            shortcut = KeyboardCommandShortcut.EditInFormulaBar;
            return true;
        }

        if ((modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && effectiveKey == Key.F1) ||
            (modifiers == ModifierKeys.Shift && effectiveKey == Key.F11))
        {
            shortcut = KeyboardCommandShortcut.InsertWorksheet;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && effectiveKey is Key.OemPlus or Key.Add)
        {
            shortcut = KeyboardCommandShortcut.ZoomIn;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && effectiveKey is Key.OemMinus or Key.Subtract)
        {
            shortcut = KeyboardCommandShortcut.ZoomOut;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.OemQuotes)
        {
            shortcut = KeyboardCommandShortcut.CopyFormulaFromAbove;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.OemQuotes)
        {
            shortcut = KeyboardCommandShortcut.CopyValueFromAbove;
            return true;
        }

        if (modifiers == ModifierKeys.Alt && effectiveKey == Key.Down)
        {
            shortcut = KeyboardCommandShortcut.OpenActiveDropdown;
            return true;
        }

        if (modifiers == ModifierKeys.Alt && effectiveKey == Key.Oem1)
        {
            shortcut = KeyboardCommandShortcut.SelectVisibleCellsOnly;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.Back)
        {
            shortcut = KeyboardCommandShortcut.ScrollActiveCellIntoView;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey is Key.OemPeriod or Key.Decimal)
        {
            shortcut = KeyboardCommandShortcut.CycleSelectionCorner;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.OemOpenBrackets)
        {
            shortcut = KeyboardCommandShortcut.SelectDirectPrecedents;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.OemCloseBrackets)
        {
            shortcut = KeyboardCommandShortcut.SelectDirectDependents;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.OemOpenBrackets)
        {
            shortcut = KeyboardCommandShortcut.SelectAllPrecedents;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.OemCloseBrackets)
        {
            shortcut = KeyboardCommandShortcut.SelectAllDependents;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.O)
        {
            shortcut = KeyboardCommandShortcut.SelectCellsWithComments;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F2)
        {
            shortcut = KeyboardCommandShortcut.EditCell;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) == 0 && effectiveKey == Key.Delete)
        {
            shortcut = KeyboardCommandShortcut.ClearSelection;
            return true;
        }

        if ((modifiers & ModifierKeys.Control) == 0 && effectiveKey == Key.Back)
        {
            shortcut = KeyboardCommandShortcut.ClearSelectionAndEdit;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F4)
        {
            shortcut = KeyboardCommandShortcut.RepeatLastAction;
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
        if ((key == Key.B && (modifiers & ModifierKeys.Control) != 0) ||
            (key is Key.D2 or Key.NumPad2 && modifiers == ModifierKeys.Control))
        {
            shortcut = FontToggleShortcut.Bold;
            return true;
        }

        if ((key == Key.I && (modifiers & ModifierKeys.Control) != 0) ||
            (key is Key.D3 or Key.NumPad3 && modifiers == ModifierKeys.Control))
        {
            shortcut = FontToggleShortcut.Italic;
            return true;
        }

        if ((key == Key.U && (modifiers & ModifierKeys.Control) != 0) ||
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
