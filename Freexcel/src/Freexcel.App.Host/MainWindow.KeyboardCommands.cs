using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void RegisterKeyboardCommandShortcuts()
    {
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewWorkbook, (_, _) => CreateNewWorkbook());
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
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FillDown, FillDownMenuItem_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FillRight, FillRightMenuItem_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.FlashFill, (_, _) => TryFlashFill());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentDate, (_, _) => InsertCurrentDateOrTime(insertTime: false));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertCurrentTime, (_, _) => InsertCurrentDateOrTime(insertTime: true));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleShowFormulas, ShowFormulasBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ActivatePreviousSheet, (_, _) => ActivateAdjacentVisibleSheet(-1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ActivateNextSheet, (_, _) => ActivateAdjacentVisibleSheet(1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectPreviousSheetGroup, (_, _) => SelectAdjacentVisibleSheetGroup(-1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SelectNextSheetGroup, (_, _) => SelectAdjacentVisibleSheetGroup(1));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenFormatCells, (_, _) => OpenFormatCellsDialog());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Find, FindButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Replace, ReplaceButton_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertFunction, InsertFunctionBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SpellCheck, SpellCheckBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CloseWorkbook, (_, _) => Close());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CalculateNow, CalcNowBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CalculateSheet, CalcSheetBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RebuildDependenciesAndCalculate, (_, _) => RebuildDependenciesAndCalculate());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleFormulaBarExpansion, FormulaBarExpandBtn_Click);
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ToggleFilter, FilterButton_Click);
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
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.SaveAs, (_, _) => SaveWorkbookWithDialog());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ShowKeyTips, (_, _) => EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel));
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CycleShellFocus, (_, _) => CycleShellFocus(reverse: Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift));
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
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.EditCell, (_, _) => EnterEditMode());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelection, (_, _) => ExecuteClearSelection());
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ClearSelectionAndEdit, (_, _) =>
        {
            ExecuteClearSelection();
            EnterEditMode();
        });
        _keyboardCommandDispatcher.Register(KeyboardCommandShortcut.RepeatLastAction, (_, _) => ExecuteRepeatLast());
    }
}
