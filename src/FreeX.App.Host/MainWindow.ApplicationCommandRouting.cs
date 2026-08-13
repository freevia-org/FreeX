using System.Windows;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Shell.WorkbookApplicationWorkareaCommandEndpoint;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private WorkbookApplicationCommandBindings? _workbookApplicationCommandBindings;

    private WorkbookApplicationCommandBindings WorkbookApplicationCommands =>
        _workbookApplicationCommandBindings ??= CreateWorkbookApplicationCommandBindings();

    private WorkbookApplicationCommandBindings CreateWorkbookApplicationCommandBindings()
    {
        return WorkbookApplicationCommandBindingFactory.Create(
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
                OpenPrintBackstageAsync: _ => RunApplicationFrameCommand(OpenPrintBackstage)),
            new WorkbookApplicationWorkareaCommandHandlers(
                CreateWorkbookApplicationWorkareaCommandEndpointProfile(),
                TargetAddress,
                HasSelectedDrawingObject));
    }

    private WorkbookApplicationWorkareaCommandEndpointProfile
        CreateWorkbookApplicationWorkareaCommandEndpointProfile() =>
        new WorkbookApplicationWorkareaCommandEndpointProfile
        {
            Undo = Handled(() => ExecuteUndo()),
            Redo = Handled(() => ExecuteRedo()),
            Cut = Handled(() => ExecuteCopy(isCut: true)),
            Copy = Handled(() => ExecuteCopy()),
            Paste = Handled(() => ExecutePaste()),
            PasteSpecial = Handled(() => PasteSpecialBtn_Click(this, new RoutedEventArgs())),
            FormatPainter = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FormatPainterBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            ToggleBold = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (invocation, variant) =>
                    ExecuteFontToggle(variant, "Bold", BoldButton_Click, FontToggleShortcut.Bold)),
            ToggleItalic = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (invocation, variant) =>
                    ExecuteFontToggle(variant, "Italic", ItalicButton_Click, FontToggleShortcut.Italic)),
            ToggleUnderline = Handled<WorkbookApplicationCommandInvocation, WorkbookApplicationCommandVariant>(
                (invocation, variant) =>
                    ExecuteFontToggle(variant, "Underline", UnderlineButton_Click, FontToggleShortcut.Underline)),
            ToggleStrikethrough = Handled(() => ApplyFontToggleShortcut(FontToggleShortcut.Strikethrough)),
            OpenFillColor = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FillColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            OpenFontColor = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FontColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            OpenFormatCells = Handled(() => OpenFormatCellsDialog()),
            InsertFunction = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                InsertFunctionBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            AutoSum = Handled(() => InsertAutoSumFormula("SUM")),
            CalculateNow = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                CalcNowBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            CalculateActiveSheet = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                CalcSheetBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            RefreshAll = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                RefreshAllBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            SortAscending = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SortAscButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            SortDescending = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SortDescButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            CustomSort = Handled(() => SortCustomMenuItem_Click(this, new RoutedEventArgs())),
            ToggleFilter = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FilterButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            ClearFilter = Handled(() => ClearFilterButton_Click(this, new RoutedEventArgs())),
            ReapplyFilter = Handled<WorkbookApplicationCommandVariant>(variant =>
            {
                if (variant == WorkbookApplicationCommandVariant.KeyboardShortcut)
                    ReapplyAutoFilter();
                else
                    FilterReapplyMenuItem_Click(this, new RoutedEventArgs());
            }),
            OpenDataValidation = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                ValidationButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            OpenNameManager = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                NamedRangesButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            OpenSpelling = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SpellCheckBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            CheckAccessibility = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                AccessibilityCheckerBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            ShareWorkbook = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                ShareWorkbookBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            Zoom100 = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                Zoom100Btn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            ZoomSelection = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                ZoomSelectionBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            FreezePanes = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FreezeAtSelectionMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation))),
            InsertWorksheet = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                AddSheetButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            Find = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FindButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            Replace = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                ReplaceButton_Click(NativeSource(invocation), RoutedArgs(invocation))),
            GoTo = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                FindGoToMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation))),
            OpenSelectionPane = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                SelectionPaneBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
            InsertCopiedCells = Handled(() => ExecuteInsertCopiedCells()),
            InsertCells = Handled(() => InsertCellsMenuItem_Click(this, new RoutedEventArgs())),
            InsertRow = Handled<uint>(index => InsertRows(index)),
            InsertColumn = Handled<uint>(index => InsertColumns(index)),
            DeleteCells = Handled(() => DeleteCellsMenuItem_Click(this, new RoutedEventArgs())),
            DeleteRows = Handled(() => DeleteSelectedRows()),
            DeleteColumns = Handled(() => DeleteSelectedColumns()),
            PickFromDropDown = Handled(() => OpenActiveDropdown()),
            QuickAnalysis = Handled(() => ShowQuickAnalysisMenu()),
            DefineName = Handled(() => DefineNameBtn_Click(this, new RoutedEventArgs())),
            CreateTable = Handled(() => TableBtn_Click(this, new RoutedEventArgs())),
            FormatAsTable = Handled(() => FormatTableBtn_Click(this, new RoutedEventArgs())),
            TextToColumns = Handled(() => TextToColumnsBtn_Click(this, new RoutedEventArgs())),
            RemoveDuplicates = Handled(() => RemoveDuplicatesBtn_Click(this, new RoutedEventArgs())),
            HideRows = Handled(() => ExecuteRowsHidden(hidden: true)),
            UnhideRows = Handled(() => ExecuteRowsHidden(hidden: false)),
            RowHeight = Handled(() => FormatRowHeightMenuItem_Click(this, new RoutedEventArgs())),
            AutoFitRowHeight = Handled(() => FormatAutoRowMenuItem_Click(this, new RoutedEventArgs())),
            HideColumns = Handled(() => ExecuteColumnsHidden(hidden: true)),
            UnhideColumns = Handled(() => ExecuteColumnsHidden(hidden: false)),
            ColumnWidth = Handled(() => FormatColWidthMenuItem_Click(this, new RoutedEventArgs())),
            AutoFitColumnWidth = Handled(() => FormatAutoColMenuItem_Click(this, new RoutedEventArgs())),
            Group = Handled(() => GroupRowsBtn_Click(this, new RoutedEventArgs())),
            Ungroup = Handled(() => UngroupRowsBtn_Click(this, new RoutedEventArgs())),
            NewThreadedComment = Handled(() =>
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs())),
            EditThreadedComment = Handled(() =>
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs())),
            SetThreadedCommentResolution = Handled<CellAddress, bool>(ResolveContextThreadedComment),
            DeleteThreadedComment = Handled(() =>
                ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs())),
            NewNote = Handled(() => ReviewNewCommentBtn_Click(this, new RoutedEventArgs())),
            EditNote = Handled(() => ReviewNewCommentBtn_Click(this, new RoutedEventArgs())),
            DeleteNote = Handled(() => ReviewDeleteCommentBtn_Click(this, new RoutedEventArgs())),
            ShowNotes = Handled(() => ReviewShowNotesBtn_Click(this, new RoutedEventArgs())),
            ShowHideNote = Handled<CellAddress>(address => ExecuteShowHideNote(address)),
            ShowAllNotes = Handled(() => ExecuteShowAllNotes()),
            OpenHyperlink = Handled<CellAddress>(address => TryOpenHyperlink(address)),
            EditHyperlink = Handled(() => InsertLinkBtn_Click(this, new RoutedEventArgs())),
            PivotTableOptions = Handled<CellAddress>(address => ShowPivotTableOptionsDialog(address)),
            ClearAll = Handled(() => ClearAllMenuItem_Click(this, new RoutedEventArgs())),
            ClearFormats = Handled(() => ClearFormats()),
            ClearComments = Handled(() => ClearCommentsMenuItem_Click(this, new RoutedEventArgs())),
            ClearHyperlinks = Handled(() => ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs())),
            RemoveHyperlinks = Handled(() => RemoveHyperlinkMenuItem_Click(this, new RoutedEventArgs())),
            ClearContents = Handled(() => ExecuteClearSelection()),
            FillDown = Handled(() => FillDownMenuItem_Click(this, new RoutedEventArgs())),
            FillRight = Handled(() => FillRightMenuItem_Click(this, new RoutedEventArgs())),
            FlashFill = Handled(() => TryFlashFill()),
            ToggleShowFormulas = Handled(() => ShowFormulasBtn_Click(this, new RoutedEventArgs())),
            ActivateAdjacentSheet = Handled<int>(direction => ActivateAdjacentVisibleSheet(direction)),
            SelectAdjacentSheetGroup = Handled<int>(direction => SelectAdjacentVisibleSheetGroup(direction)),
            ApplyNumberFormat = Handled<NumberFormatShortcut>(numberFormat =>
                ApplyNumberFormatShortcut(numberFormat)),
            ApplyOutlineBorder = Handled(() => ApplyOutlineBorderShortcut()),
            ClearOutlineBorder = Handled(() =>
                ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff())),
            WorkbookStatistics = Handled<WorkbookApplicationCommandInvocation>(invocation =>
                WorkbookStatisticsBtn_Click(NativeSource(invocation), RoutedArgs(invocation))),
        };

    private void ExecuteFontToggle(
        WorkbookApplicationCommandVariant variant,
        string commandKey,
        Action<object, RoutedEventArgs> quickAccessHandler,
        FontToggleShortcut shortcut)
    {
        if (variant == WorkbookApplicationCommandVariant.QuickAccessToolbar)
            ExecuteToggleQuickAccessCommand(commandKey, quickAccessHandler);
        else
            ApplyFontToggleShortcut(shortcut);
    }

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
