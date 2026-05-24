using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static partial class KeyboardShortcutMatcher
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
        new(KeyboardCommandShortcut.CycleShellFocus, (key, modifiers) => modifiers is ModifierKeys.None or ModifierKeys.Shift && key == Key.F6),
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

    private readonly record struct KeyboardCommandShortcutRule(
        KeyboardCommandShortcut Shortcut,
        Func<Key, ModifierKeys, bool> Matches);
}
