using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    private WorkbookApplicationCommandBindings? _workbookApplicationCommandBindings;

    private WorkbookApplicationCommandBindings WorkbookApplicationCommands =>
        _workbookApplicationCommandBindings ??= CreateWorkbookApplicationCommandBindings();

    private WorkbookApplicationCommandBindings CreateWorkbookApplicationCommandBindings()
    {
        var bindings = new WorkbookApplicationCommandBindings();

        WorkbookApplicationFrameCommandBinder.Bind(
            bindings,
            new WorkbookApplicationFrameCommandHandlers(
                NewWorkbookAsync: async invocation =>
                {
                    if (invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar)
                        await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);
                    else
                        await CreateNewWorkbookAsync();
                },
                OpenWorkbookAsync: async invocation =>
                {
                    if (invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar)
                        await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Open);
                    else
                        await OpenWorkbookAsync();
                },
                SaveWorkbookAsync: _ => SaveCurrentWorkbookAsync(),
                SaveWorkbookAsAsync: _ => ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.SaveAs),
                PrintWorkbookAsync: async invocation =>
                {
                    if (invocation.Route.Source == WorkbookApplicationCommandSource.KeyboardShortcut)
                        ShowBackstagePrintPane();
                    else
                        await ShowPrintDialogAsync();
                },
                ExportPdfXpsAsync: _ => ShowBackstageExportDialogAsync()));
        bindings.Bind(WorkbookApplicationCommandIntent.Undo, _ => UndoLastEdit());
        bindings.Bind(WorkbookApplicationCommandIntent.Redo, _ => RedoLastEdit());
        bindings.BindAsync(WorkbookApplicationCommandIntent.Cut, _ => CutSelectedRangeToClipboardAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.Copy, _ => CopySelectedRangeToClipboardAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.Paste, _ => PasteClipboardTextAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.PasteSpecial, _ => ShowPasteSpecialDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.FormatPainter, invocation =>
            FormatPainterButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleBold, invocation =>
            ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.B));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleItalic, invocation =>
            ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.I));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleUnderline, invocation =>
            ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.U));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleStrikethrough, _ => ToggleSelectedRangeStrikethrough());
        bindings.Bind(WorkbookApplicationCommandIntent.OpenFillColor, invocation =>
            _fillColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fillColorButton));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenFontColor, invocation =>
            _fontColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fontColorButton));
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenFormatCells, _ => ShowFormatCellsDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.InsertFunction, _ => InsertFunction());
        bindings.Bind(WorkbookApplicationCommandIntent.AutoSum, _ => InsertAutoSumFormula("SUM"));
        bindings.Bind(WorkbookApplicationCommandIntent.CalculateNow, _ => CalculateNow());
        bindings.Bind(WorkbookApplicationCommandIntent.CalculateActiveSheet, _ => CalculateActiveSheet());
        bindings.Bind(WorkbookApplicationCommandIntent.RefreshAll, _ => RefreshImportedData());
        bindings.Bind(WorkbookApplicationCommandIntent.SortAscending, _ => SortSelectedRange(ascending: true));
        bindings.Bind(WorkbookApplicationCommandIntent.SortDescending, _ => SortSelectedRange(ascending: false));
        bindings.BindAsync(WorkbookApplicationCommandIntent.CustomSort, _ => ShowSortDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleFilter, _ => ToggleAutoFilter());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearFilter, _ => ClearActiveSheetFilters());
        bindings.Bind(WorkbookApplicationCommandIntent.ReapplyFilter, _ => ReapplyCurrentFilterSort());
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenDataValidation, _ => ShowDataValidationDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.OpenNameManager, _ => NameManager());
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenSpelling, _ => ShowSpellingDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.CheckAccessibility, _ => ShowAccessibilityCheckerDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.ShareWorkbook, _ => ShareWorkbookAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.Zoom100, _ => ZoomTo100Percent());
        bindings.Bind(WorkbookApplicationCommandIntent.ZoomSelection, _ => ZoomToSelection());
        bindings.Bind(WorkbookApplicationCommandIntent.FreezePanes, _ => FreezePanesAtActiveCell());
        bindings.Bind(WorkbookApplicationCommandIntent.InsertWorksheet, _ => AddNewSheet());
        bindings.BindAsync(WorkbookApplicationCommandIntent.Find, _ => ShowFindDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.Replace, _ => ShowReplaceDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.GoTo, _ => ShowGoToDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenSelectionPane, _ => OpenSelectionPaneDialogAsync());

        bindings.BindAsync(WorkbookApplicationCommandIntent.InsertCopiedCells, _ => ShowInsertCellsDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.InsertCells, _ => ShowInsertCellsDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.InsertRowAbove, invocation =>
            InsertContextRow(TargetAddress(invocation).Row));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertRowBelow, invocation =>
            InsertContextRow(TargetAddress(invocation).Row + 1));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertColumnLeft, invocation =>
            InsertContextColumn(TargetAddress(invocation).Col));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertColumnRight, invocation =>
            InsertContextColumn(TargetAddress(invocation).Col + 1));
        bindings.BindAsync(WorkbookApplicationCommandIntent.DeleteCells, _ => ShowDeleteCellsDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteRows, _ => DeleteSheetRows());
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteColumns, _ => DeleteSheetColumns());
        bindings.BindHandled(WorkbookApplicationCommandIntent.PickFromDropDown, _ =>
        {
            if (OpenActiveDropdown())
                return true;

            RefreshShell(UiText.Get("DrawingInteract_PickListNoList"));
            return true;
        });
        bindings.BindAsync(WorkbookApplicationCommandIntent.QuickAnalysis, _ => ShowQuickAnalysisDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.DefineName, _ => DefineName());
        bindings.BindAsync(WorkbookApplicationCommandIntent.CreateTable, _ => InsertTableFromSelectionAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.FormatAsTable, _ => InsertTableFromSelectionAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.TextToColumns, _ => TextToColumns());
        bindings.BindAsync(WorkbookApplicationCommandIntent.RemoveDuplicates, _ => ShowRemoveDuplicatesDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.HideRows, _ => HideSelectedRows());
        bindings.Bind(WorkbookApplicationCommandIntent.UnhideRows, _ => UnhideSelectedRows());
        bindings.BindAsync(WorkbookApplicationCommandIntent.RowHeight, _ => ShowRowHeightDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.AutoFitRowHeight, _ => AutoFitSelectedRowHeight());
        bindings.Bind(WorkbookApplicationCommandIntent.HideColumns, _ => HideSelectedColumns());
        bindings.Bind(WorkbookApplicationCommandIntent.UnhideColumns, _ => UnhideSelectedColumns());
        bindings.BindAsync(WorkbookApplicationCommandIntent.ColumnWidth, _ => ShowColumnWidthDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.AutoFitColumnWidth, _ => AutoFitSelectedColumnWidth());
        bindings.Bind(WorkbookApplicationCommandIntent.Group, _ => GroupSelectedRows());
        bindings.Bind(WorkbookApplicationCommandIntent.Ungroup, _ => UngroupSelection());
        bindings.BindAsync(WorkbookApplicationCommandIntent.NewThreadedComment, _ => ShowNewThreadedCommentDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.EditThreadedComment, _ => ShowEditThreadedCommentDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.ResolveThreadedComment, _ =>
            ResolveActiveCellThreadedComment(resolved: true));
        bindings.Bind(WorkbookApplicationCommandIntent.UnresolveThreadedComment, _ =>
            ResolveActiveCellThreadedComment(resolved: false));
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteThreadedComment, _ => DeleteActiveCellThreadedComment());
        bindings.BindAsync(WorkbookApplicationCommandIntent.NewNote, _ => ShowNewNoteDialogAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.EditNote, _ => ShowEditNoteDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteNote, _ => DeleteActiveCellNote());
        bindings.Bind(WorkbookApplicationCommandIntent.ShowNotes, _ => ToggleAllNotesVisibility());
        bindings.Bind(WorkbookApplicationCommandIntent.ShowHideNote, _ => ToggleActiveCellNoteVisibility());
        bindings.Bind(WorkbookApplicationCommandIntent.ShowAllNotes, _ => ToggleAllNotesVisibility());
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenHyperlink, _ => OpenSelectedHyperlinkAsync());
        bindings.BindAsync(WorkbookApplicationCommandIntent.EditHyperlink, _ => ShowInsertHyperlinkDialogAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.PivotTableOptions, _ => OpenPivotTableOptions());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearAll, _ => ClearSelectedRangeAll());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearFormats, _ => ClearSelectedRangeFormats());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearComments, _ => ClearSelectedRangeComments());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearHyperlinks, _ => RemoveSelectedRangeHyperlinks());
        bindings.Bind(WorkbookApplicationCommandIntent.RemoveHyperlinks, _ => ClearSelectedRangeHyperlinks());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearContents, _ => ClearSelectedRangeContents());

        bindings.Bind(WorkbookApplicationCommandIntent.FillDown, _ =>
        {
            if (!HasSelectedDrawingObject())
                FillSelectedRange(FillCellsDirection.Down);
        });
        bindings.Bind(WorkbookApplicationCommandIntent.FillRight, _ =>
        {
            if (!HasSelectedDrawingObject())
                FillSelectedRange(FillCellsDirection.Right);
        });
        bindings.Bind(WorkbookApplicationCommandIntent.FlashFill, _ => FlashFillSelectedRange());
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleShowFormulas, _ => ToggleShowFormulas());
        bindings.BindHandled(WorkbookApplicationCommandIntent.ActivatePreviousSheet, _ =>
            SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false));
        bindings.BindHandled(WorkbookApplicationCommandIntent.ActivateNextSheet, _ =>
            SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false));
        bindings.BindHandled(WorkbookApplicationCommandIntent.SelectPreviousSheetGroup, _ =>
            SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true));
        bindings.BindHandled(WorkbookApplicationCommandIntent.SelectNextSheetGroup, _ =>
            SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatGeneral, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.General));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatNumber, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Number));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatTime, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Time));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatDate, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Date));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatCurrency, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Currency));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatPercentage, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Percentage));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatScientific, _ =>
            ApplySelectedRangeNumberFormatShortcut(NumberFormatShortcut.Scientific));
        bindings.Bind(WorkbookApplicationCommandIntent.ApplyOutlineBorder, _ =>
            ApplySelectedRangeBorderPreset(CellBorderPreset.Outside));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearOutlineBorder, _ =>
            ApplySelectedRangeBorderPreset(CellBorderPreset.NoBorder));
        bindings.BindAsync(WorkbookApplicationCommandIntent.WorkbookStatistics, _ =>
            ShowWorkbookStatisticsDialogAsync());

        bindings.EnsureBound(
            WorkbookApplicationCommandRouter.QuickAccessRoutes
                .Concat(WorkbookApplicationCommandRouter.WorksheetContextMenuRoutes)
                .Concat(WorkbookApplicationCommandRouter.KeyboardShortcutRoutes));
        return bindings;
    }

    private object NativeSource(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeSource ?? this;

    private static RoutedEventArgs RoutedArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as RoutedEventArgs ?? new RoutedEventArgs();

    private static KeyEventArgs? KeyArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as KeyEventArgs;

    private CellAddress TargetAddress(WorkbookApplicationCommandInvocation invocation) =>
        invocation.TargetAddress ?? _session.ActiveCell;
}
