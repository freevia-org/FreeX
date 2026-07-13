using System.Windows.Input;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public static partial class KeyboardShortcutMatcher
{
    private static readonly KeyboardCommandShortcutRule[] CommandShortcutRules = CreateCommandShortcutRules();

    private static KeyboardCommandShortcutRule[] CreateCommandShortcutRules()
    {
        var rules = new List<KeyboardCommandShortcutRule>();
        foreach (var rule in WorkbookKeyboardShortcutCatalog.Rules.Where(rule => WorkbookKeyboardShortcutCatalog.IsCommandRoute(rule.Route)))
        {
            rules.Add(
                new KeyboardCommandShortcutRule(
                    ToKeyboardCommandShortcut(rule.Route),
                    (key, modifiers) =>
                        TryGetWorkbookShortcutKey(key, out var shortcutKey) &&
                        shortcutKey == rule.WindowsChord.Key &&
                        ToWorkbookModifiers(modifiers) == rule.WindowsChord.Modifiers));
        }

        rules.AddRange(
        [
        new(KeyboardCommandShortcut.SelectCurrentRegionOrAll, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.A),
        new(KeyboardCommandShortcut.CreateTable, (key, modifiers) => modifiers == ModifierKeys.Control && key is (Key.T or Key.L)),
        new(KeyboardCommandShortcut.InsertHyperlink, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.K),
        new(KeyboardCommandShortcut.OpenHyperlink, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Enter),
        new(KeyboardCommandShortcut.InsertCurrentDate, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemSemicolon),
        new(KeyboardCommandShortcut.InsertCurrentTime, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemSemicolon),
        new(KeyboardCommandShortcut.ToggleOutlineSymbols, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.D8),
        new(KeyboardCommandShortcut.ActivatePreviousSheet, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.PageUp),
        new(KeyboardCommandShortcut.ActivateNextSheet, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.PageDown),
        new(KeyboardCommandShortcut.SelectPreviousSheetGroup, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.PageUp),
        new(KeyboardCommandShortcut.SelectNextSheetGroup, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.PageDown),
        new(KeyboardCommandShortcut.OpenFormatCells, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.NumPad1),
        new(KeyboardCommandShortcut.NameManager, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F3),
        new(KeyboardCommandShortcut.CreateNamesFromSelection, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F3),
        new(KeyboardCommandShortcut.PasteName, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F3),
        new(KeyboardCommandShortcut.SpellCheck, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F7),
        new(KeyboardCommandShortcut.CloseWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key is (Key.F4 or Key.W)),
        new(KeyboardCommandShortcut.RestoreWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F5),
        new(KeyboardCommandShortcut.MoveWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F7),
        new(KeyboardCommandShortcut.SizeWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F8),
        new(KeyboardCommandShortcut.CalculateNow, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F9),
        new(KeyboardCommandShortcut.CalculateSheet, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F9),
        new(KeyboardCommandShortcut.CalculateNow, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.F9),
        new(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F9),
        new(KeyboardCommandShortcut.OpenErrorChecking, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F10),
        new(KeyboardCommandShortcut.ToggleFormulaBarExpansion, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.U),
        new(KeyboardCommandShortcut.ToggleFilter, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.L),
        new(KeyboardCommandShortcut.ReapplyFilter, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.L),
        new(KeyboardCommandShortcut.QuickAnalysis, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Q),
        new(KeyboardCommandShortcut.OpenPrintPreview, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.P ||
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F12),
        new(KeyboardCommandShortcut.PasteValues, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.V),
        new(KeyboardCommandShortcut.InsertEmbeddedChart, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.F1),
        new(KeyboardCommandShortcut.GroupSelection, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.Right),
        new(KeyboardCommandShortcut.UngroupSelection, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.Left),
        new(KeyboardCommandShortcut.InsertChartSheet, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F11),
        new(KeyboardCommandShortcut.OpenFormatCellsFont, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is (Key.F or Key.P)),
        new(KeyboardCommandShortcut.NewNote, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F2),
        new(KeyboardCommandShortcut.NewThreadedComment, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F2),
        new(KeyboardCommandShortcut.SaveAs, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F12),
        new(KeyboardCommandShortcut.OpenHelp, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F1),
        new(KeyboardCommandShortcut.ShowKeyTips, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F10),
        new(KeyboardCommandShortcut.CycleShellFocus, (key, modifiers) => modifiers is ModifierKeys.None or ModifierKeys.Shift && key == Key.F6),
        new(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key is (Key.F6 or Key.Tab)),
        new(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key is (Key.F6 or Key.Tab)),
        new(KeyboardCommandShortcut.MinimizeWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F9),
        new(KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F10),
        new(KeyboardCommandShortcut.OpenContextMenu, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F10 || modifiers == ModifierKeys.None && key == Key.Apps),
        new(KeyboardCommandShortcut.EditInFormulaBar, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.F2),
        new(KeyboardCommandShortcut.InsertWorksheet, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F1),
        new(KeyboardCommandShortcut.ZoomIn, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is (Key.OemPlus or Key.Add)),
        new(KeyboardCommandShortcut.ZoomOut, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is (Key.OemMinus or Key.Subtract)),
        new(KeyboardCommandShortcut.CopyFormulaFromAbove, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemQuotes),
        new(KeyboardCommandShortcut.CopyValueFromAbove, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemQuotes),
        new(KeyboardCommandShortcut.OpenActiveDropdown, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.Down),
        new(KeyboardCommandShortcut.SelectVisibleCellsOnly, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.Oem1),
        new(KeyboardCommandShortcut.ScrollActiveCellIntoView, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Back),
        new(KeyboardCommandShortcut.CycleSelectionCorner, (key, modifiers) => modifiers == ModifierKeys.Control && key is (Key.OemPeriod or Key.Decimal)),
        new(KeyboardCommandShortcut.SelectDirectPrecedents, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemOpenBrackets),
        new(KeyboardCommandShortcut.SelectDirectDependents, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.OemCloseBrackets),
        new(KeyboardCommandShortcut.SelectAllPrecedents, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemOpenBrackets),
        new(KeyboardCommandShortcut.SelectAllDependents, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.OemCloseBrackets),
        new(KeyboardCommandShortcut.SelectCellsWithComments, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.O),
        new(KeyboardCommandShortcut.EditCell, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F2),
        new(KeyboardCommandShortcut.ClearSelection, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.Delete),
        new(KeyboardCommandShortcut.ClearSelectionAndEdit, (key, modifiers) => modifiers is ModifierKeys.None or ModifierKeys.Shift && key == Key.Back),
        new(KeyboardCommandShortcut.RepeatLastAction, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F4),
        ]);

        return rules.ToArray();
    }

    private static KeyboardCommandShortcut ToKeyboardCommandShortcut(WorkbookShortcutRoute route) =>
        route switch
        {
            WorkbookShortcutRoute.NewWorkbook => KeyboardCommandShortcut.NewWorkbook,
            WorkbookShortcutRoute.OpenWorkbook => KeyboardCommandShortcut.OpenWorkbook,
            WorkbookShortcutRoute.SaveWorkbook => KeyboardCommandShortcut.SaveWorkbook,
            WorkbookShortcutRoute.Copy => KeyboardCommandShortcut.Copy,
            WorkbookShortcutRoute.Cut => KeyboardCommandShortcut.Cut,
            WorkbookShortcutRoute.Paste => KeyboardCommandShortcut.Paste,
            WorkbookShortcutRoute.Undo => KeyboardCommandShortcut.Undo,
            WorkbookShortcutRoute.Redo => KeyboardCommandShortcut.Redo,
            WorkbookShortcutRoute.FillDown => KeyboardCommandShortcut.FillDown,
            WorkbookShortcutRoute.FillRight => KeyboardCommandShortcut.FillRight,
            WorkbookShortcutRoute.FlashFill => KeyboardCommandShortcut.FlashFill,
            WorkbookShortcutRoute.ToggleShowFormulas => KeyboardCommandShortcut.ToggleShowFormulas,
            WorkbookShortcutRoute.OpenFormatCells => KeyboardCommandShortcut.OpenFormatCells,
            WorkbookShortcutRoute.Find => KeyboardCommandShortcut.Find,
            WorkbookShortcutRoute.Replace => KeyboardCommandShortcut.Replace,
            WorkbookShortcutRoute.GoTo => KeyboardCommandShortcut.GoTo,
            WorkbookShortcutRoute.InsertFunction => KeyboardCommandShortcut.InsertFunction,
            WorkbookShortcutRoute.AutoSum => KeyboardCommandShortcut.AutoSum,
            WorkbookShortcutRoute.WorkbookStatistics => KeyboardCommandShortcut.WorkbookStatistics,
            WorkbookShortcutRoute.InsertWorksheet => KeyboardCommandShortcut.InsertWorksheet,
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

    private readonly record struct KeyboardCommandShortcutRule(
        KeyboardCommandShortcut Shortcut,
        Func<Key, ModifierKeys, bool> Matches);
}
