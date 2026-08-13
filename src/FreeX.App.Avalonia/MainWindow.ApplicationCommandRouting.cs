using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Shell.WorkbookApplicationWorkareaCommandEndpoint;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    private WorkbookApplicationCommandBindings? _workbookApplicationCommandBindings;

    private WorkbookApplicationCommandBindings WorkbookApplicationCommands =>
        _workbookApplicationCommandBindings ??= CreateWorkbookApplicationCommandBindings();

    private WorkbookApplicationCommandBindings CreateWorkbookApplicationCommandBindings()
    {
        return WorkbookApplicationCommandBindingFactory.Create(
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
                }),
            new WorkbookApplicationWorkareaCommandHandlers(
                CreateWorkbookApplicationWorkareaCommandEndpointProfile(),
                TargetAddress,
                HasSelectedDrawingObject));
    }

    private WorkbookApplicationWorkareaCommandEndpointProfile
        CreateWorkbookApplicationWorkareaCommandEndpointProfile() =>
        new WorkbookApplicationWorkareaCommandEndpointProfile
        {
            Undo = Handled(() => UndoLastEdit()),
            Redo = Handled(() => RedoLastEdit()),
            Cut = Handled(async () => await CutSelectedRangeToClipboardAsync()),
            Copy = Handled(async () => await CopySelectedRangeToClipboardAsync()),
            Paste = Handled(async () => await PasteClipboardTextAsync()),
            PasteSpecial = Handled(async () => await ShowPasteSpecialDialogAsync()),
            FormatPainter = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FormatPainterButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            ToggleBold = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (_, _) => ToggleSelectedRangeBold()),
            ToggleItalic = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (_, _) => ToggleSelectedRangeItalic()),
            ToggleUnderline = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (_, _) => ToggleSelectedRangeUnderline()),
            ToggleStrikethrough = Handled(() => ToggleSelectedRangeStrikethrough()),
            OpenFillColor = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                _fillColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fillColorButton)),
            OpenFontColor = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                _fontColorButton.Flyout?.ShowAt(invocation.NativeSource as Control ?? _fontColorButton)),
            OpenFormatCells = Handled(async () => await ShowFormatCellsDialogAsync()),
            InsertFunction = Handled<WorkbookApplicationCommandInvocation>(invocation => InsertFunction()),
            AutoSum = Handled(() => InsertAutoSumFormula("SUM")),
            CalculateNow = Handled<WorkbookApplicationCommandInvocation>(invocation => CalculateNow()),
            CalculateActiveSheet = Handled<WorkbookApplicationCommandInvocation>(invocation => CalculateActiveSheet()),
            RefreshAll = Handled<WorkbookApplicationCommandInvocation>(invocation => RefreshImportedData()),
            SortAscending = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SortSelectedRange(ascending: true)),
            SortDescending = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SortSelectedRange(ascending: false)),
            CustomSort = Handled(async () => await ShowSortDialogAsync()),
            ToggleFilter = Handled<WorkbookApplicationCommandInvocation>(invocation => ToggleAutoFilter()),
            ClearFilter = Handled(() => ClearActiveSheetFilters()),
            ReapplyFilter = Handled<WorkbookApplicationCommandVariant>(variant => ReapplyCurrentFilterSort()),
            OpenDataValidation = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowDataValidationDialogAsync()),
            OpenNameManager = Handled<WorkbookApplicationCommandInvocation>(invocation => NameManager()),
            OpenSpelling = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowSpellingDialogAsync()),
            CheckAccessibility = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowAccessibilityCheckerDialogAsync()),
            ShareWorkbook = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShareWorkbookAsync()),
            Zoom100 = Handled<WorkbookApplicationCommandInvocation>(invocation => ZoomTo100Percent()),
            ZoomSelection = Handled<WorkbookApplicationCommandInvocation>(invocation => ZoomToSelection()),
            FreezePanes = Handled<WorkbookApplicationCommandInvocation>(invocation => FreezePanesAtActiveCell()),
            InsertWorksheet = Handled<WorkbookApplicationCommandInvocation>(invocation => AddNewSheet()),
            Find = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowFindDialogAsync()),
            Replace = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowReplaceDialogAsync()),
            GoTo = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowGoToDialogAsync()),
            OpenSelectionPane = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await OpenSelectionPaneDialogAsync()),
            InsertCopiedCells = Handled(async () => await ShowInsertCellsDialogAsync()),
            InsertCells = Handled(async () => await ShowInsertCellsDialogAsync()),
            InsertRow = Handled<uint>(index => InsertContextRow(index)),
            InsertColumn = Handled<uint>(index => InsertContextColumn(index)),
            DeleteCells = Handled(async () => await ShowDeleteCellsDialogAsync()),
            DeleteRows = Handled(() => DeleteSheetRows()),
            DeleteColumns = Handled(() => DeleteSheetColumns()),
            PickFromDropDown = Handled(() =>
            {
                if (!OpenActiveDropdown())
                    RefreshShell(UiText.Get("DrawingInteract_PickListNoList"));
            }),
            QuickAnalysis = Handled(async () => await ShowQuickAnalysisDialogAsync()),
            DefineName = Handled(() => DefineName()),
            CreateTable = Handled(async () => await InsertTableFromSelectionAsync()),
            FormatAsTable = Handled(async () => await InsertTableFromSelectionAsync()),
            TextToColumns = Handled(() => TextToColumns()),
            RemoveDuplicates = Handled(async () => await ShowRemoveDuplicatesDialogAsync()),
            HideRows = Handled(() => HideSelectedRows()),
            UnhideRows = Handled(() => UnhideSelectedRows()),
            RowHeight = Handled(async () => await ShowRowHeightDialogAsync()),
            AutoFitRowHeight = Handled(() => AutoFitSelectedRowHeight()),
            HideColumns = Handled(() => HideSelectedColumns()),
            UnhideColumns = Handled(() => UnhideSelectedColumns()),
            ColumnWidth = Handled(async () => await ShowColumnWidthDialogAsync()),
            AutoFitColumnWidth = Handled(() => AutoFitSelectedColumnWidth()),
            Group = Handled(() => GroupSelectedRows()),
            Ungroup = Handled(() => UngroupSelection()),
            NewThreadedComment = Handled(async () => await ShowNewThreadedCommentDialogAsync()),
            EditThreadedComment = Handled(async () => await ShowEditThreadedCommentDialogAsync()),
            SetThreadedCommentResolution = Handled<CellAddress, bool>((address, state) =>
                ResolveActiveCellThreadedComment(state)),
            DeleteThreadedComment = Handled(() => DeleteActiveCellThreadedComment()),
            NewNote = Handled(async () => await ShowNewNoteDialogAsync()),
            EditNote = Handled(async () => await ShowEditNoteDialogAsync()),
            DeleteNote = Handled(() => DeleteActiveCellNote()),
            ShowNotes = Handled(() => ToggleAllNotesVisibility()),
            ShowHideNote = Handled<CellAddress>(address => ToggleActiveCellNoteVisibility()),
            ShowAllNotes = Handled(() => ToggleAllNotesVisibility()),
            OpenHyperlink = Handled<CellAddress>(async address => await OpenSelectedHyperlinkAsync()),
            EditHyperlink = Handled(async () => await ShowInsertHyperlinkDialogAsync()),
            PivotTableOptions = Handled<CellAddress>(address => OpenPivotTableOptions()),
            ClearAll = Handled(() => ClearSelectedRangeAll()),
            ClearFormats = Handled(() => ClearSelectedRangeFormats()),
            ClearComments = Handled(() => ClearSelectedRangeComments()),
            ClearHyperlinks = Handled(() => RemoveSelectedRangeHyperlinks()),
            RemoveHyperlinks = Handled(() => ClearSelectedRangeHyperlinks()),
            ClearContents = Handled(() => ClearSelectedRangeContents()),
            FillDown = Handled(() => FillSelectedRange(FillCellsDirection.Down)),
            FillRight = Handled(() => FillSelectedRange(FillCellsDirection.Right)),
            FlashFill = Handled(() => FlashFillSelectedRange()),
            ToggleShowFormulas = Handled(() => ToggleShowFormulas()),
            ActivateAdjacentSheet = Result<int>(direction =>
                SelectAdjacentVisibleSheetFromKeyboard(direction, selectRange: false)),
            SelectAdjacentSheetGroup = Result<int>(direction =>
                SelectAdjacentVisibleSheetFromKeyboard(direction, selectRange: true)),
            ApplyNumberFormat = Handled<NumberFormatShortcut>(numberFormat =>
                ApplySelectedRangeNumberFormatShortcut(numberFormat)),
            ApplyOutlineBorder = Handled(() => ApplySelectedRangeBorderPreset(CellBorderPreset.Outside)),
            ClearOutlineBorder = Handled(() => ApplySelectedRangeBorderPreset(CellBorderPreset.NoBorder)),
            WorkbookStatistics = Handled<WorkbookApplicationCommandInvocation>(async invocation =>
                await ShowWorkbookStatisticsDialogAsync()),
        };

    private object NativeSource(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeSource ?? this;

    private static RoutedEventArgs RoutedArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as RoutedEventArgs ?? new RoutedEventArgs();

    private static KeyEventArgs? KeyArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as KeyEventArgs;

    private CellAddress TargetAddress(WorkbookApplicationCommandInvocation invocation) =>
        invocation.TargetAddress ?? _session.ActiveCell;
}
