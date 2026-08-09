using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RegisterKeyboardCommandShortcuts()
    {
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewWorkbook, async (_, _) => await RequestNewWorkbookAsync());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenWorkbook, OpenButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveWorkbook, SaveButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Copy, (_, _) => ExecuteCopy());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Cut, (_, _) => ExecuteCopy(isCut: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Paste, (_, _) => ExecutePaste());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectCurrentRegionOrAll, (_, _) => SelectCurrentRegionOrAll());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Undo, (_, _) => ExecuteUndo());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Redo, (_, _) => ExecuteRedo());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateTable, TableBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertHyperlink, InsertLinkBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHyperlink, (_, _) => TryOpenSelectedHyperlink());
        // R129-model-drawing-fill-1: Ctrl+D/Ctrl+R (fill down/right) must no-op, not fill, while a
        // picture/shape/text box/chart is genuinely selected -- same family as the Backspace guard
        // below (R123-model-drawing-backspace-1): Excel never lets a fill command act on the
        // underlying active cell just because it happens to sit under a selected object.
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FillDown, (sender, e) =>
        {
            if (HasSelectedDrawingObject())
                return;

            FillDownMenuItem_Click(sender, e);
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FillRight, (sender, e) =>
        {
            if (HasSelectedDrawingObject())
                return;

            FillRightMenuItem_Click(sender, e);
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FlashFill, (_, _) => TryFlashFill());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentDate, (_, _) => InsertCurrentDateOrTime(insertTime: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentTime, (_, _) => InsertCurrentDateOrTime(insertTime: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleShowFormulas, ShowFormulasBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleOutlineSymbols, (_, _) => ToggleOutlineSymbolsShortcut());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ActivatePreviousSheet, (_, _) => ActivateAdjacentVisibleSheet(-1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ActivateNextSheet, (_, _) => ActivateAdjacentVisibleSheet(1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectPreviousSheetGroup, (_, _) => SelectAdjacentVisibleSheetGroup(-1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectNextSheetGroup, (_, _) => SelectAdjacentVisibleSheetGroup(1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenFormatCells, (_, _) => OpenFormatCellsDialog());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Find, FindButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Replace, ReplaceButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NameManager, NamedRangesButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateNamesFromSelection, CreateNamesFromSelectionBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertFunction, InsertFunctionBtn_Click);
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
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenPrintPreview, (_, _) => OpenPrintBackstage());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.PasteValues, (_, _) => ExecutePaste(PasteMode.Values));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.GoTo, FindGoToMenuItem_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertEmbeddedChart, (_, _) => InsertEmbeddedChart());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertChartSheet, (_, _) => InsertChartSheet());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.AutoSum, (_, _) => InsertAutoSumFormula("SUM"));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.GroupSelection, GroupRowsBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.UngroupSelection, UngroupRowsBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenFormatCellsFont, (_, _) => OpenFormatCellsDialog(FormatCellsDialogTab.Font));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.WorkbookStatistics, WorkbookStatisticsBtn_Click);
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
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertWorksheet, AddSheetButton_Click);
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
        // R129-model-drawing-f2-1: F2 must no-op, not open the underlying active cell for edit,
        // while a picture/shape/text box/chart is genuinely selected -- same family as the
        // Backspace/Fill guards above.
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.EditCell, (_, _) =>
        {
            if (HasSelectedDrawingObject())
                return;

            EnterEditMode();
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelection, (_, _) => ExecuteClearSelection());
        // R75-commands-clear-delete-4-1: Backspace clears ONLY the active cell before entering edit
        // -- unlike the Delete key (ClearSelection above), which clears the whole selection. Matches
        // Excel: Backspace is never a bulk-clear operation.
        // R123-model-drawing-backspace-1: but when a picture/shape/text box/chart is genuinely
        // selected (SheetGrid.SelectedObjectId/-Kind), Backspace must be a total no-op -- Excel
        // never deletes the object (that's Delete-only, see TryDeleteSelectedDrawingObject) and
        // never touches whatever cell happened to be active before the object was clicked. Without
        // this guard, ExecuteClearActiveCell/EnterEditMode below silently clear and open that
        // unrelated cell for edit while the object stays selected on screen.
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelectionAndEdit, (_, _) =>
        {
            if (HasSelectedDrawingObject())
                return;

            ExecuteClearActiveCell();
            EnterEditMode();
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RepeatLastAction, (_, _) => ExecuteRepeatLast());

        _keyboardCommandDispatcher.EnsureRegistered(Enum.GetValues<KeyboardCommandShortcut>());
    }
}
