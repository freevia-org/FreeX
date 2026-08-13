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

        foreach (var shortcut in WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts)
        {
            rules.Add(
                new KeyboardCommandShortcutRule(
                    shortcut.Command,
                    (key, modifiers) =>
                        TryGetWorkbookShortcutKey(key, out var shortcutKey) &&
                        shortcutKey == shortcut.Key &&
                        ToWorkbookModifiers(modifiers) == shortcut.Modifiers));
        }

        rules.AddRange(
        [
            new(KeyboardCommandShortcut.SelectCurrentRegionOrAll, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.A),
            new(KeyboardCommandShortcut.InsertHyperlink, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.K),
            new(KeyboardCommandShortcut.OpenHyperlink, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.Enter),
            new(KeyboardCommandShortcut.OpenFormatCells, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.NumPad1),
            new(KeyboardCommandShortcut.CloseWorkbook, (key, modifiers) => modifiers == ModifierKeys.Control && key == Key.W),
            new(KeyboardCommandShortcut.CalculateNow, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F9),
            new(KeyboardCommandShortcut.CalculateSheet, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F9),
            new(KeyboardCommandShortcut.CalculateFull, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.F9),
            new(KeyboardCommandShortcut.PasteValues, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.V),
            new(KeyboardCommandShortcut.SaveAs, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F12),
            new(KeyboardCommandShortcut.OpenHelp, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F1),
            new(KeyboardCommandShortcut.ShowKeyTips, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F10),
            new(KeyboardCommandShortcut.CycleShellFocus, (key, modifiers) => modifiers is ModifierKeys.None or ModifierKeys.Shift && key == Key.F6),
            new(KeyboardCommandShortcut.OpenContextMenu, (key, modifiers) => modifiers == ModifierKeys.Shift && key == Key.F10 || modifiers == ModifierKeys.None && key == Key.Apps),
            new(KeyboardCommandShortcut.InsertWorksheet, (key, modifiers) => modifiers == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.F1),
            new(KeyboardCommandShortcut.SelectVisibleCellsOnly, (key, modifiers) => modifiers == ModifierKeys.Alt && key == Key.Oem1),
            new(KeyboardCommandShortcut.SelectCellsWithComments, (key, modifiers) => modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.O),
            new(KeyboardCommandShortcut.EditCell, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.F2),
            new(KeyboardCommandShortcut.ClearSelection, (key, modifiers) => modifiers == ModifierKeys.None && key == Key.Delete),
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
            WorkbookShortcutRoute.PrintWorkbook => KeyboardCommandShortcut.OpenPrintPreview,
            WorkbookShortcutRoute.Copy => KeyboardCommandShortcut.Copy,
            WorkbookShortcutRoute.Cut => KeyboardCommandShortcut.Cut,
            WorkbookShortcutRoute.Paste => KeyboardCommandShortcut.Paste,
            WorkbookShortcutRoute.Undo => KeyboardCommandShortcut.Undo,
            WorkbookShortcutRoute.Redo => KeyboardCommandShortcut.Redo,
            WorkbookShortcutRoute.FillDown => KeyboardCommandShortcut.FillDown,
            WorkbookShortcutRoute.FillRight => KeyboardCommandShortcut.FillRight,
            WorkbookShortcutRoute.FlashFill => KeyboardCommandShortcut.FlashFill,
            WorkbookShortcutRoute.ToggleShowFormulas => KeyboardCommandShortcut.ToggleShowFormulas,
            WorkbookShortcutRoute.ActivatePreviousSheet => KeyboardCommandShortcut.ActivatePreviousSheet,
            WorkbookShortcutRoute.ActivateNextSheet => KeyboardCommandShortcut.ActivateNextSheet,
            WorkbookShortcutRoute.SelectPreviousSheetGroup => KeyboardCommandShortcut.SelectPreviousSheetGroup,
            WorkbookShortcutRoute.SelectNextSheetGroup => KeyboardCommandShortcut.SelectNextSheetGroup,
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
