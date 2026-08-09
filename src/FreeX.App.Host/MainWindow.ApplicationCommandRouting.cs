using System.Windows;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
                NewWorkbookAsync: _ => RequestNewWorkbookAsync(),
                OpenWorkbookAsync: invocation => RunApplicationFrameCommand(() =>
                    OpenButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
                SaveWorkbookAsync: invocation => RunApplicationFrameCommand(() =>
                    SaveButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
                SaveWorkbookAsAsync: invocation => RunApplicationFrameCommand(() =>
                    SaveAsButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
                PrintWorkbookAsync: invocation => RunApplicationFrameCommand(() =>
                    PrintButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
                ExportPdfXpsAsync: invocation => RunApplicationFrameCommand(() =>
                    ExportPdfButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
                OpenPrintBackstageAsync: _ => RunApplicationFrameCommand(OpenPrintBackstage)));

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

    private ValueTask<bool> ExecuteWorkbookApplicationWorkareaCommandAsync(
        WorkbookApplicationWorkareaCommandRequest request)
    {
        var invocation = request.Invocation;
        switch (request.Intent)
        {
            case WorkbookApplicationCommandIntent.Undo:
                ExecuteUndo();
                break;
            case WorkbookApplicationCommandIntent.Redo:
                ExecuteRedo();
                break;
            case WorkbookApplicationCommandIntent.Cut:
                ExecuteCopy(isCut: true);
                break;
            case WorkbookApplicationCommandIntent.Copy:
                ExecuteCopy();
                break;
            case WorkbookApplicationCommandIntent.Paste:
                ExecutePaste();
                break;
            case WorkbookApplicationCommandIntent.PasteSpecial:
                PasteSpecialBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.FormatPainter:
                FormatPainterBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.ToggleBold:
                ExecuteFontToggle(request, "Bold", BoldButton_Click, FontToggleShortcut.Bold);
                break;
            case WorkbookApplicationCommandIntent.ToggleItalic:
                ExecuteFontToggle(request, "Italic", ItalicButton_Click, FontToggleShortcut.Italic);
                break;
            case WorkbookApplicationCommandIntent.ToggleUnderline:
                ExecuteFontToggle(request, "Underline", UnderlineButton_Click, FontToggleShortcut.Underline);
                break;
            case WorkbookApplicationCommandIntent.ToggleStrikethrough:
                ApplyFontToggleShortcut(FontToggleShortcut.Strikethrough);
                break;
            case WorkbookApplicationCommandIntent.OpenFillColor:
                FillColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.OpenFontColor:
                FontColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.OpenFormatCells:
                OpenFormatCellsDialog();
                break;
            case WorkbookApplicationCommandIntent.InsertFunction:
                InsertFunctionBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.AutoSum:
                InsertAutoSumFormula("SUM");
                break;
            case WorkbookApplicationCommandIntent.CalculateNow:
                CalcNowBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.CalculateActiveSheet:
                CalcSheetBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.RefreshAll:
                RefreshAllBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.SortAscending:
                SortAscButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.SortDescending:
                SortDescButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.CustomSort:
                SortCustomMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ToggleFilter:
                FilterButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.ClearFilter:
                ClearFilterButton_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ReapplyFilter:
                if (request.Variant == WorkbookApplicationCommandVariant.KeyboardShortcut)
                    ReapplyAutoFilter();
                else
                    FilterReapplyMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.OpenDataValidation:
                ValidationButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.OpenNameManager:
                NamedRangesButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.OpenSpelling:
                SpellCheckBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.CheckAccessibility:
                AccessibilityCheckerBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.ShareWorkbook:
                ShareWorkbookBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.Zoom100:
                Zoom100Btn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.ZoomSelection:
                ZoomSelectionBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.FreezePanes:
                FreezeAtSelectionMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.InsertWorksheet:
                AddSheetButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.Find:
                FindButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.Replace:
                ReplaceButton_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.GoTo:
                FindGoToMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.OpenSelectionPane:
                SelectionPaneBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            case WorkbookApplicationCommandIntent.InsertCopiedCells:
                ExecuteInsertCopiedCells();
                break;
            case WorkbookApplicationCommandIntent.InsertCells:
                InsertCellsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.InsertRowAbove:
            case WorkbookApplicationCommandIntent.InsertRowBelow:
                InsertRows(request.Index);
                break;
            case WorkbookApplicationCommandIntent.InsertColumnLeft:
            case WorkbookApplicationCommandIntent.InsertColumnRight:
                InsertColumns(request.Index);
                break;
            case WorkbookApplicationCommandIntent.DeleteCells:
                DeleteCellsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.DeleteRows:
                DeleteSelectedRows();
                break;
            case WorkbookApplicationCommandIntent.DeleteColumns:
                DeleteSelectedColumns();
                break;
            case WorkbookApplicationCommandIntent.PickFromDropDown:
                OpenActiveDropdown();
                break;
            case WorkbookApplicationCommandIntent.QuickAnalysis:
                ShowQuickAnalysisMenu();
                break;
            case WorkbookApplicationCommandIntent.DefineName:
                DefineNameBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.CreateTable:
                TableBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.FormatAsTable:
                FormatTableBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.TextToColumns:
                TextToColumnsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.RemoveDuplicates:
                RemoveDuplicatesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.HideRows:
                ExecuteRowsHidden(hidden: true);
                break;
            case WorkbookApplicationCommandIntent.UnhideRows:
                ExecuteRowsHidden(hidden: false);
                break;
            case WorkbookApplicationCommandIntent.RowHeight:
                FormatRowHeightMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.AutoFitRowHeight:
                FormatAutoRowMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.HideColumns:
                ExecuteColumnsHidden(hidden: true);
                break;
            case WorkbookApplicationCommandIntent.UnhideColumns:
                ExecuteColumnsHidden(hidden: false);
                break;
            case WorkbookApplicationCommandIntent.ColumnWidth:
                FormatColWidthMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.AutoFitColumnWidth:
                FormatAutoColMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.Group:
                GroupRowsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.Ungroup:
                UngroupRowsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.NewThreadedComment:
            case WorkbookApplicationCommandIntent.EditThreadedComment:
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ResolveThreadedComment:
            case WorkbookApplicationCommandIntent.UnresolveThreadedComment:
                ResolveContextThreadedComment(RequiredTarget(request), request.State);
                break;
            case WorkbookApplicationCommandIntent.DeleteThreadedComment:
                ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.NewNote:
            case WorkbookApplicationCommandIntent.EditNote:
                ReviewNewCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.DeleteNote:
                ReviewDeleteCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ShowNotes:
                ReviewShowNotesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ShowHideNote:
                ExecuteShowHideNote(RequiredTarget(request));
                break;
            case WorkbookApplicationCommandIntent.ShowAllNotes:
                ExecuteShowAllNotes();
                break;
            case WorkbookApplicationCommandIntent.OpenHyperlink:
                TryOpenHyperlink(RequiredTarget(request));
                break;
            case WorkbookApplicationCommandIntent.EditHyperlink:
                InsertLinkBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.PivotTableOptions:
                ShowPivotTableOptionsDialog(RequiredTarget(request));
                break;
            case WorkbookApplicationCommandIntent.ClearAll:
                ClearAllMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ClearFormats:
                ClearFormats();
                break;
            case WorkbookApplicationCommandIntent.ClearComments:
                ClearCommentsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ClearHyperlinks:
                ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.RemoveHyperlinks:
                RemoveHyperlinkMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ClearContents:
                ExecuteClearSelection();
                break;
            case WorkbookApplicationCommandIntent.FillDown:
                FillDownMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.FillRight:
                FillRightMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.FlashFill:
                TryFlashFill();
                break;
            case WorkbookApplicationCommandIntent.ToggleShowFormulas:
                ShowFormulasBtn_Click(this, new RoutedEventArgs());
                break;
            case WorkbookApplicationCommandIntent.ActivatePreviousSheet:
            case WorkbookApplicationCommandIntent.ActivateNextSheet:
                ActivateAdjacentVisibleSheet(request.Direction);
                break;
            case WorkbookApplicationCommandIntent.SelectPreviousSheetGroup:
            case WorkbookApplicationCommandIntent.SelectNextSheetGroup:
                SelectAdjacentVisibleSheetGroup(request.Direction);
                break;
            case WorkbookApplicationCommandIntent.NumberFormatGeneral:
            case WorkbookApplicationCommandIntent.NumberFormatNumber:
            case WorkbookApplicationCommandIntent.NumberFormatTime:
            case WorkbookApplicationCommandIntent.NumberFormatDate:
            case WorkbookApplicationCommandIntent.NumberFormatCurrency:
            case WorkbookApplicationCommandIntent.NumberFormatPercentage:
            case WorkbookApplicationCommandIntent.NumberFormatScientific:
                ApplyNumberFormatShortcut(request.NumberFormat ?? throw MissingPolicy(request));
                break;
            case WorkbookApplicationCommandIntent.ApplyOutlineBorder:
                ApplyOutlineBorderShortcut();
                break;
            case WorkbookApplicationCommandIntent.ClearOutlineBorder:
                ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
                break;
            case WorkbookApplicationCommandIntent.WorkbookStatistics:
                WorkbookStatisticsBtn_Click(NativeSource(invocation), RoutedArgs(invocation));
                break;
            default:
                throw new InvalidOperationException($"Unsupported workbook workarea command '{request.Intent}'.");
        }

        return ValueTask.FromResult(true);
    }

    private void ExecuteFontToggle(
        WorkbookApplicationWorkareaCommandRequest request,
        string commandKey,
        Action<object, RoutedEventArgs> quickAccessHandler,
        FontToggleShortcut shortcut)
    {
        if (request.Variant == WorkbookApplicationCommandVariant.QuickAccessToolbar)
            ExecuteToggleQuickAccessCommand(commandKey, quickAccessHandler);
        else
            ApplyFontToggleShortcut(shortcut);
    }

    private static CellAddress RequiredTarget(WorkbookApplicationWorkareaCommandRequest request) =>
        request.TargetAddress ?? throw MissingPolicy(request);

    private static InvalidOperationException MissingPolicy(WorkbookApplicationWorkareaCommandRequest request) =>
        new($"Workbook workarea command '{request.Intent}' is missing portable policy data.");

    private static object NativeSource(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeSource ?? invocation;

    private static RoutedEventArgs RoutedArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as RoutedEventArgs ?? new RoutedEventArgs();

    private static Task RunApplicationFrameCommand(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private CellAddress TargetAddress(WorkbookApplicationCommandInvocation invocation) =>
        invocation.TargetAddress ?? SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
}
