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
                PrintWorkbookAsync: _ => ShowPrintDialogAsync(),
                ExportPdfXpsAsync: _ => ShowBackstageExportDialogAsync(),
                OpenPrintBackstageAsync: _ =>
                {
                    ShowBackstagePrintPane();
                    return Task.CompletedTask;
                }));

        WorkbookApplicationWorkareaCommandBinder.Bind(
            bindings,
            new WorkbookApplicationWorkareaCommandHandlers(
                ExecuteWorkbookApplicationWorkareaCommandAsync,
                TargetAddress,
                HasSelectedDrawingObject));

        bindings.EnsureBound(
            WorkbookApplicationCommandRouter.QuickAccessRoutes
                .Concat(WorkbookApplicationCommandRouter.WorksheetContextMenuRoutes)
                .Concat(WorkbookApplicationCommandRouter.KeyboardShortcutRoutes));
        return bindings;
    }

    private async ValueTask<bool> ExecuteWorkbookApplicationWorkareaCommandAsync(
        WorkbookApplicationWorkareaCommandRequest request)
    {
        var invocation = request.Invocation;
        switch (request.Intent)
        {
            case WorkbookApplicationCommandIntent.Undo:
                UndoLastEdit();
                break;
            case WorkbookApplicationCommandIntent.Redo:
                RedoLastEdit();
                break;
            case WorkbookApplicationCommandIntent.Cut:
                await CutSelectedRangeToClipboardAsync();
                break;
            case WorkbookApplicationCommandIntent.Copy:
                await CopySelectedRangeToClipboardAsync();
                break;
            case WorkbookApplicationCommandIntent.Paste:
                await PasteClipboardTextAsync();
                break;
            case WorkbookApplicationCommandIntent.PasteSpecial:
                await ShowPasteSpecialDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.FormatPainter:
                FormatPainterButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.ToggleBold:
                ToggleSelectedRangeBold(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.B);
                break;
            case WorkbookApplicationCommandIntent.ToggleItalic:
                ToggleSelectedRangeItalic(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.I);
                break;
            case WorkbookApplicationCommandIntent.ToggleUnderline:
                ToggleSelectedRangeUnderline(trackLaunchSmokeLiveCommandKey: KeyArgs(invocation)?.Key == Key.U);
                break;
            case WorkbookApplicationCommandIntent.ToggleStrikethrough:
                ToggleSelectedRangeStrikethrough();
                break;
            case WorkbookApplicationCommandIntent.OpenFillColor:
                _fillColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fillColorButton);
                break;
            case WorkbookApplicationCommandIntent.OpenFontColor:
                _fontColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fontColorButton);
                break;
            case WorkbookApplicationCommandIntent.OpenFormatCells:
                await ShowFormatCellsDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.InsertFunction:
                InsertFunction();
                break;
            case WorkbookApplicationCommandIntent.AutoSum:
                InsertAutoSumFormula("SUM");
                break;
            case WorkbookApplicationCommandIntent.CalculateNow:
                CalculateNow();
                break;
            case WorkbookApplicationCommandIntent.CalculateActiveSheet:
                CalculateActiveSheet();
                break;
            case WorkbookApplicationCommandIntent.RefreshAll:
                RefreshImportedData();
                break;
            case WorkbookApplicationCommandIntent.SortAscending:
                SortSelectedRange(ascending: true);
                break;
            case WorkbookApplicationCommandIntent.SortDescending:
                SortSelectedRange(ascending: false);
                break;
            case WorkbookApplicationCommandIntent.CustomSort:
                await ShowSortDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.ToggleFilter:
                ToggleAutoFilter();
                break;
            case WorkbookApplicationCommandIntent.ClearFilter:
                ClearActiveSheetFilters();
                break;
            case WorkbookApplicationCommandIntent.ReapplyFilter:
                ReapplyCurrentFilterSort();
                break;
            case WorkbookApplicationCommandIntent.OpenDataValidation:
                await ShowDataValidationDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.OpenNameManager:
                NameManager();
                break;
            case WorkbookApplicationCommandIntent.OpenSpelling:
                await ShowSpellingDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.CheckAccessibility:
                await ShowAccessibilityCheckerDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.ShareWorkbook:
                await ShareWorkbookAsync();
                break;
            case WorkbookApplicationCommandIntent.Zoom100:
                ZoomTo100Percent();
                break;
            case WorkbookApplicationCommandIntent.ZoomSelection:
                ZoomToSelection();
                break;
            case WorkbookApplicationCommandIntent.FreezePanes:
                FreezePanesAtActiveCell();
                break;
            case WorkbookApplicationCommandIntent.InsertWorksheet:
                AddNewSheet();
                break;
            case WorkbookApplicationCommandIntent.Find:
                await ShowFindDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.Replace:
                await ShowReplaceDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.GoTo:
                await ShowGoToDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.OpenSelectionPane:
                await OpenSelectionPaneDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.InsertCopiedCells:
            case WorkbookApplicationCommandIntent.InsertCells:
                await ShowInsertCellsDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.InsertRowAbove:
            case WorkbookApplicationCommandIntent.InsertRowBelow:
                InsertContextRow(request.Index);
                break;
            case WorkbookApplicationCommandIntent.InsertColumnLeft:
            case WorkbookApplicationCommandIntent.InsertColumnRight:
                InsertContextColumn(request.Index);
                break;
            case WorkbookApplicationCommandIntent.DeleteCells:
                await ShowDeleteCellsDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.DeleteRows:
                DeleteSheetRows();
                break;
            case WorkbookApplicationCommandIntent.DeleteColumns:
                DeleteSheetColumns();
                break;
            case WorkbookApplicationCommandIntent.PickFromDropDown:
                if (!OpenActiveDropdown())
                    RefreshShell(UiText.Get("DrawingInteract_PickListNoList"));
                break;
            case WorkbookApplicationCommandIntent.QuickAnalysis:
                await ShowQuickAnalysisDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.DefineName:
                DefineName();
                break;
            case WorkbookApplicationCommandIntent.CreateTable:
            case WorkbookApplicationCommandIntent.FormatAsTable:
                await InsertTableFromSelectionAsync();
                break;
            case WorkbookApplicationCommandIntent.TextToColumns:
                TextToColumns();
                break;
            case WorkbookApplicationCommandIntent.RemoveDuplicates:
                await ShowRemoveDuplicatesDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.HideRows:
                HideSelectedRows();
                break;
            case WorkbookApplicationCommandIntent.UnhideRows:
                UnhideSelectedRows();
                break;
            case WorkbookApplicationCommandIntent.RowHeight:
                await ShowRowHeightDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.AutoFitRowHeight:
                AutoFitSelectedRowHeight();
                break;
            case WorkbookApplicationCommandIntent.HideColumns:
                HideSelectedColumns();
                break;
            case WorkbookApplicationCommandIntent.UnhideColumns:
                UnhideSelectedColumns();
                break;
            case WorkbookApplicationCommandIntent.ColumnWidth:
                await ShowColumnWidthDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.AutoFitColumnWidth:
                AutoFitSelectedColumnWidth();
                break;
            case WorkbookApplicationCommandIntent.Group:
                GroupSelectedRows();
                break;
            case WorkbookApplicationCommandIntent.Ungroup:
                UngroupSelection();
                break;
            case WorkbookApplicationCommandIntent.NewThreadedComment:
                await ShowNewThreadedCommentDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.EditThreadedComment:
                await ShowEditThreadedCommentDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.ResolveThreadedComment:
            case WorkbookApplicationCommandIntent.UnresolveThreadedComment:
                ResolveActiveCellThreadedComment(request.State);
                break;
            case WorkbookApplicationCommandIntent.DeleteThreadedComment:
                DeleteActiveCellThreadedComment();
                break;
            case WorkbookApplicationCommandIntent.NewNote:
                await ShowNewNoteDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.EditNote:
                await ShowEditNoteDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.DeleteNote:
                DeleteActiveCellNote();
                break;
            case WorkbookApplicationCommandIntent.ShowNotes:
            case WorkbookApplicationCommandIntent.ShowAllNotes:
                ToggleAllNotesVisibility();
                break;
            case WorkbookApplicationCommandIntent.ShowHideNote:
                ToggleActiveCellNoteVisibility();
                break;
            case WorkbookApplicationCommandIntent.OpenHyperlink:
                await OpenSelectedHyperlinkAsync();
                break;
            case WorkbookApplicationCommandIntent.EditHyperlink:
                await ShowInsertHyperlinkDialogAsync();
                break;
            case WorkbookApplicationCommandIntent.PivotTableOptions:
                OpenPivotTableOptions();
                break;
            case WorkbookApplicationCommandIntent.ClearAll:
                ClearSelectedRangeAll();
                break;
            case WorkbookApplicationCommandIntent.ClearFormats:
                ClearSelectedRangeFormats();
                break;
            case WorkbookApplicationCommandIntent.ClearComments:
                ClearSelectedRangeComments();
                break;
            case WorkbookApplicationCommandIntent.ClearHyperlinks:
                RemoveSelectedRangeHyperlinks();
                break;
            case WorkbookApplicationCommandIntent.RemoveHyperlinks:
                ClearSelectedRangeHyperlinks();
                break;
            case WorkbookApplicationCommandIntent.ClearContents:
                ClearSelectedRangeContents();
                break;
            case WorkbookApplicationCommandIntent.FillDown:
                FillSelectedRange(FillCellsDirection.Down);
                break;
            case WorkbookApplicationCommandIntent.FillRight:
                FillSelectedRange(FillCellsDirection.Right);
                break;
            case WorkbookApplicationCommandIntent.FlashFill:
                FlashFillSelectedRange();
                break;
            case WorkbookApplicationCommandIntent.ToggleShowFormulas:
                ToggleShowFormulas();
                break;
            case WorkbookApplicationCommandIntent.ActivatePreviousSheet:
            case WorkbookApplicationCommandIntent.ActivateNextSheet:
                return SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: false);
            case WorkbookApplicationCommandIntent.SelectPreviousSheetGroup:
            case WorkbookApplicationCommandIntent.SelectNextSheetGroup:
                return SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: true);
            case WorkbookApplicationCommandIntent.NumberFormatGeneral:
            case WorkbookApplicationCommandIntent.NumberFormatNumber:
            case WorkbookApplicationCommandIntent.NumberFormatTime:
            case WorkbookApplicationCommandIntent.NumberFormatDate:
            case WorkbookApplicationCommandIntent.NumberFormatCurrency:
            case WorkbookApplicationCommandIntent.NumberFormatPercentage:
            case WorkbookApplicationCommandIntent.NumberFormatScientific:
                ApplySelectedRangeNumberFormatShortcut(request.NumberFormat ?? throw MissingPolicy(request));
                break;
            case WorkbookApplicationCommandIntent.ApplyOutlineBorder:
                ApplySelectedRangeBorderPreset(CellBorderPreset.Outside);
                break;
            case WorkbookApplicationCommandIntent.ClearOutlineBorder:
                ApplySelectedRangeBorderPreset(CellBorderPreset.NoBorder);
                break;
            case WorkbookApplicationCommandIntent.WorkbookStatistics:
                await ShowWorkbookStatisticsDialogAsync();
                break;
            default:
                throw new InvalidOperationException($"Unsupported workbook workarea command '{request.Intent}'.");
        }

        return true;
    }

    private static InvalidOperationException MissingPolicy(WorkbookApplicationWorkareaCommandRequest request) =>
        new($"Workbook workarea command '{request.Intent}' is missing portable policy data.");

    private object NativeSource(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeSource ?? this;

    private static RoutedEventArgs RoutedArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as RoutedEventArgs ?? new RoutedEventArgs();

    private static KeyEventArgs? KeyArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as KeyEventArgs;

    private CellAddress TargetAddress(WorkbookApplicationCommandInvocation invocation) =>
        invocation.TargetAddress ?? _session.ActiveCell;
}
