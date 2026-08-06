using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RegisterKeyboardCommandShortcuts()
    {
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.NewWorkbook, WorkbookShortcutRoute.NewWorkbook);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.OpenWorkbook, WorkbookShortcutRoute.OpenWorkbook);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.SaveWorkbook, WorkbookShortcutRoute.SaveWorkbook);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Copy, WorkbookShortcutRoute.Copy);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Cut, WorkbookShortcutRoute.Cut);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Paste, WorkbookShortcutRoute.Paste);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectCurrentRegionOrAll, (_, _) => SelectCurrentRegionOrAll());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Undo, WorkbookShortcutRoute.Undo);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Redo, WorkbookShortcutRoute.Redo);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateTable, TableBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertHyperlink, InsertLinkBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHyperlink, (_, _) => TryOpenSelectedHyperlink());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.FillDown, WorkbookShortcutRoute.FillDown);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.FillRight, WorkbookShortcutRoute.FillRight);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.FlashFill, WorkbookShortcutRoute.FlashFill);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentDate, (_, _) => InsertCurrentDateOrTime(insertTime: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentTime, (_, _) => InsertCurrentDateOrTime(insertTime: true));
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.ToggleShowFormulas, WorkbookShortcutRoute.ToggleShowFormulas);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleOutlineSymbols, (_, _) => ToggleOutlineSymbolsShortcut());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.ActivatePreviousSheet, WorkbookShortcutRoute.ActivatePreviousSheet);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.ActivateNextSheet, WorkbookShortcutRoute.ActivateNextSheet);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.SelectPreviousSheetGroup, WorkbookShortcutRoute.SelectPreviousSheetGroup);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.SelectNextSheetGroup, WorkbookShortcutRoute.SelectNextSheetGroup);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.OpenFormatCells, WorkbookShortcutRoute.OpenFormatCells);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Find, WorkbookShortcutRoute.Find);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.Replace, WorkbookShortcutRoute.Replace);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NameManager, NamedRangesButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateNamesFromSelection, CreateNamesFromSelectionBtn_Click);
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.InsertFunction, WorkbookShortcutRoute.InsertFunction);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.PasteName, (_, _) => OpenPasteNamesDialog());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SpellCheck, SpellCheckBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CloseWorkbook, (_, _) => Close());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RestoreWorkbookWindow, (_, _) => RestoreWorkbookWindow());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.MoveWorkbookWindow, (_, _) => BeginSystemWindowMove());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SizeWorkbookWindow, (_, _) => BeginSystemWindowSize());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CalculateNow, CalcNowBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CalculateSheet, CalcSheetBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CalculateFull, CalcFullBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, (_, _) => RebuildDependenciesAndCalculate());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenErrorChecking, ErrorCheckBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleFormulaBarExpansion, FormulaBarExpandBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleFilter, FilterButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ReapplyFilter, (_, _) => ReapplyAutoFilter());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.QuickAnalysis, (_, _) => ShowQuickAnalysisMenu());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.OpenPrintPreview, WorkbookShortcutRoute.PrintWorkbook);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.PasteValues, (_, _) => ExecutePaste(PasteMode.Values));
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.GoTo, WorkbookShortcutRoute.GoTo);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertEmbeddedChart, (_, _) => InsertEmbeddedChart());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertChartSheet, (_, _) => InsertChartSheet());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.AutoSum, WorkbookShortcutRoute.AutoSum);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.GroupSelection, GroupRowsBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.UngroupSelection, UngroupRowsBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenFormatCellsFont, (_, _) => OpenFormatCellsDialog(FormatCellsDialogTab.Font));
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.WorkbookStatistics, WorkbookShortcutRoute.WorkbookStatistics);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewNote, ReviewNewCommentBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewThreadedComment, ReviewNewThreadedCommentBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveAs, async (_, _) => await SaveWorkbookWithDialogAsync());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHelp, HelpOnlineBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ShowKeyTips, (_, _) => EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CycleShellFocus, (_, _) => CycleShellFocus(reverse: Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SwitchToNextWorkbookWindow, (_, _) => SwitchWorkbookWindow(forward: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow, (_, _) => SwitchWorkbookWindow(forward: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.MinimizeWorkbookWindow, MinimizeBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow, MaxRestoreBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenContextMenu, (_, _) => OpenKeyboardContextMenu());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.EditInFormulaBar, (_, _) => EditActiveCellInFormulaBar());
        RegisterPortableKeyboardCommand(KeyboardCommandShortcut.InsertWorksheet, WorkbookShortcutRoute.InsertWorksheet);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ZoomIn, ZoomInBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ZoomOut, ZoomOutBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CopyFormulaFromAbove, (_, _) => CopyFromAbove(CopyFromAboveMode.FormulaOrContent));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CopyValueFromAbove, (_, _) => CopyFromAbove(CopyFromAboveMode.Value));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenActiveDropdown, (_, _) => OpenActiveDropdown());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectVisibleCellsOnly, (_, _) => SelectGoToSpecialMatches(GoToSpecialKind.VisibleCellsOnly, showEmptyMessage: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ScrollActiveCellIntoView, (_, _) => ScrollActiveCellIntoView());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CycleSelectionCorner, (_, _) => CycleSelectionCorner());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectDirectPrecedents, (_, _) => SelectFormulaAuditCells(selectDependents: false, includeTransitive: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectDirectDependents, (_, _) => SelectFormulaAuditCells(selectDependents: true, includeTransitive: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectAllPrecedents, (_, _) => SelectFormulaAuditCells(selectDependents: false, includeTransitive: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectAllDependents, (_, _) => SelectFormulaAuditCells(selectDependents: true, includeTransitive: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectCellsWithComments, (_, _) => SelectGoToSpecialMatches(GoToSpecialKind.Comments, showEmptyMessage: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.EditCell, (_, _) => EnterEditMode());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelection, (_, _) => ExecuteClearSelection());
        // R75-commands-clear-delete-4-1: Backspace clears ONLY the active cell before entering edit
        // -- unlike the Delete key (ClearSelection above), which clears the whole selection. Matches
        // Excel: Backspace is never a bulk-clear operation.
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelectionAndEdit, (_, _) =>
        {
            ExecuteClearActiveCell();
            EnterEditMode();
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RepeatLastAction, (_, _) => ExecuteRepeatLast());

        _keyboardCommandDispatcher.EnsureRegistered(Enum.GetValues<KeyboardCommandShortcut>());
    }

    private void RegisterPortableKeyboardCommand(
        KeyboardCommandShortcut shortcut,
        WorkbookShortcutRoute shortcutRoute)
    {
        if (!WorkbookApplicationCommandRouter.TryRouteShortcut(shortcutRoute, out var route))
            throw new InvalidOperationException($"No application command route is registered for {shortcutRoute}.");

        _keyboardCommandDispatcher.Register(shortcut, async (sender, args) =>
            await WorkbookApplicationCommands.TryExecuteAsync(
                route,
                nativeSource: sender,
                nativeEventArgs: args));
    }
}
