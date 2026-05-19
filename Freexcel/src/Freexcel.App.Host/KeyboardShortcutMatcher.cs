using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class KeyboardShortcutMatcher
{
    public static bool IsCtrlPlus(Key key, Key systemKey, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control &&
        (key is Key.Add or Key.OemPlus || systemKey is Key.Add or Key.OemPlus);

    public static bool IsCtrlMinus(Key key, Key systemKey, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control &&
        (key is Key.Subtract or Key.OemMinus || systemKey is Key.Subtract or Key.OemMinus);

    public static bool IsPasteSpecialShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.V && modifiers == (ModifierKeys.Control | ModifierKeys.Alt);

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
        if (modifiers == ModifierKeys.Control && effectiveKey is Key.T or Key.L)
        {
            shortcut = KeyboardCommandShortcut.CreateTable;
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

        if (effectiveKey == Key.F9 &&
            (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) ||
             modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)))
        {
            shortcut = KeyboardCommandShortcut.CalculateNow;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && effectiveKey == Key.U)
        {
            shortcut = KeyboardCommandShortcut.ToggleFormulaBarExpansion;
            return true;
        }

        if (modifiers == ModifierKeys.Control && effectiveKey == Key.Q)
        {
            shortcut = KeyboardCommandShortcut.QuickAnalysis;
            return true;
        }

        if (modifiers == ModifierKeys.Alt && effectiveKey == Key.F1)
        {
            shortcut = KeyboardCommandShortcut.InsertEmbeddedChart;
            return true;
        }

        if (modifiers == ModifierKeys.None && effectiveKey == Key.F11)
        {
            shortcut = KeyboardCommandShortcut.InsertChartSheet;
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
    SelectCurrentRegion
}

public enum KeyboardCommandShortcut
{
    CreateTable,
    InsertFunction,
    SpellCheck,
    CalculateNow,
    CalculateSheet,
    ToggleFormulaBarExpansion,
    QuickAnalysis,
    InsertEmbeddedChart,
    InsertChartSheet
}

public enum BorderKeyboardShortcut
{
    Outline,
    ClearOutline
}
