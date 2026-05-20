using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.IO.Packaging;
using System.Windows.Threading;

namespace Freexcel.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window
{
    private const double MaximizedSafeInsetDip = 8.0;
    private const double SheetTabNavScrollAmount = 140.0;
    private const double SheetTabScrollEpsilon = 0.5;

    private readonly ILogger<MainWindow> _logger;
    private readonly IViewportService _viewportService;
    private readonly ICommandBus _commandBus;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
    private readonly RibbonKeyTipMode _ribbonKeyTipMode = new();
    private readonly KeyboardCommandDispatcher _keyboardCommandDispatcher = new();
    private RibbonKeyTipScope _ribbonKeyTipScope = RibbonKeyTipScope.None;
    private string _ribbonKeyTipSequence = "";
    private ContextMenu? _activeRibbonKeyTipMenu;
    private bool _pendingStandaloneAltKeyTip;
    private readonly WorkbookRef _workbookRef;
    private Workbook _workbook;
    private SheetId _currentSheetId;
    private readonly System.Collections.ObjectModel.ObservableCollection<SheetTabViewModel> _sheetTabs = [];
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private SheetId? _dragSheetTabId;
    private System.Windows.Point _dragSheetTabStart;
    private bool _suppressToolbarSync;
    private bool _suppressViewOptionSync;
    private bool _suppressAppViewOptionSync;
    private bool _isOpeningFile;
    private CellAddress? _selectionAnchor;
    private CellAddress? _selectionCursor;
    private ExcelSelectionMode _selectionMode = ExcelSelectionMode.Normal;
    private bool _endMode;
    private bool _dragSelectActive;
    private Freexcel.App.UI.SplitPaneRegion _activeSplitPaneRegion = Freexcel.App.UI.SplitPaneRegion.BottomRight;
    private readonly Dictionary<SheetId, SplitPaneViewportOffsets> _splitPaneViewportOffsets = [];
    private readonly List<FormulaTraceArrow> _formulaTraceArrows = [];
    private readonly RecentFilesStore _recentFiles;
    private readonly IWorkbookShareService _shareService = new WindowsWorkbookShareService();
    private List<RecentFileViewModel> _allRecentItems = [];
    private FreexcelOptions _options = FreexcelOptions.Load();
    private string? _currentFilePath;
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private bool _ribbonCompact;
    private bool _normalizingRibbonSurface;
    private CellColor _borderPickerColor = CellColor.Black;
    private BorderStyle _borderPickerStyle = BorderStyle.Thin;
    private static readonly (string Label, string Code)[] NumberFormatOptions =
    [
        ("General", "General"),
        ("Number (0.00)", "0.00"),
        ("Currency ($#,##0.00)", "$#,##0.00"),
        ("Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)"),
        ("Percentage (0%)", "0%"),
        ("Fraction (# ?/?)", "# ?/?"),
        ("Scientific (0.00E+00)", "0.00E+00"),
        ("Date (yyyy-MM-dd)", "yyyy-MM-dd"),
        ("Time (HH:mm:ss)", "HH:mm:ss"),
        ("Text (@)", "@")
    ];
    private System.Windows.Controls.TextBox? _inlineEditor;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private ColumnResizeSnapshot? _columnResizeSnapshot;
    private RowResizeSnapshot? _rowResizeSnapshot;
    private Action<CommandOutcome>? _repeatPostAction;
    private string? _pivotChartContextFieldCaption;
    private bool _slicerTimelinePaneDismissed;

    private sealed record ColumnResizeSnapshot(SheetId SheetId, uint StartCol, uint EndCol, Dictionary<uint, (bool Had, double Width)> Widths);
    private sealed record RowResizeSnapshot(SheetId SheetId, uint StartRow, uint EndRow, Dictionary<uint, (bool Had, double Height)> Heights);
    public MainWindow(
        ILogger<MainWindow> logger,
        IViewportService viewportService,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        IEnumerable<IFileAdapter> fileAdapters,
        WorkbookRef workbookRef,
        Workbook workbook)
    {
        _logger = logger;
        _viewportService = viewportService;
        _commandBus = commandBus;
        _recalcEngine = recalcEngine;
        _fileAdapters = fileAdapters;
        _workbookRef = workbookRef;
        _workbook = workbook;
        _recentFiles = RecentFilesStore.Load();

        InitializeComponent();
        RegisterKeyboardCommandShortcuts();

        _currentSheetId = _workbook.Sheets[0].Id;
        SheetTabsControl.ItemsSource = _sheetTabs;
        
        // Wire up scrollbars
        VerticalScroll.ValueChanged += Scroll_ValueChanged;
        HorizontalScroll.ValueChanged += Scroll_ValueChanged;
        VerticalScroll.Scroll += VerticalScroll_Scroll;
        HorizontalScroll.Scroll += HorizontalScroll_Scroll;
        VerticalScroll.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        HorizontalScroll.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        
        // Wire up grid interactions
        SheetGrid.MouseDown += SheetGrid_MouseDown;
        SheetGrid.ColumnResized  += OnColumnResized;
        SheetGrid.RowResized     += OnRowResized;
        SheetGrid.ColumnResizing += OnColumnResizing;
        SheetGrid.RowResizing    += OnRowResizing;
        SheetGrid.AutofillRequested += OnAutofillRequested;
        SheetGrid.ContextMenuRequested += OnGridContextMenuRequested;
        SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested;
        SheetGrid.PageMarginsChanged += OnPageMarginsChanged;
        SheetGrid.SplitDividerMoved += OnSplitDividerMoved;
        SheetGrid.SplitPaneScrollbarScrolled += OnSplitPaneScrollbarScrolled;
        SheetGrid.MouseMove  += SheetGrid_MouseMove;
        SheetGrid.MouseUp    += SheetGrid_MouseUp;
        SheetGrid.MouseWheel += SheetGrid_MouseWheel;
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        this.KeyDown += MainWindow_KeyDown;
        this.KeyUp += MainWindow_KeyUp;
        this.Deactivated += MainWindow_Deactivated;
        this.TextInput += MainWindow_TextInput;
        
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        StateChanged += (_, _) =>
        {
            UpdateMaximizedContentInset();
            if (MaxRestoreBtn != null)
                MaxRestoreBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        _logger.LogInformation("MainWindow initialized with Workbook {WorkbookId}", _workbook.Id);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizedContentInset();

        // Populate from installed Windows fonts
        var fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontNameBox.ItemsSource = fonts;
        FontNameBox.SelectedItem = fonts.Contains("Calibri") ? "Calibri" : fonts[0];

        var sizes = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        FontSizeBox.ItemsSource = sizes;
        FontSizeBox.SelectedItem = "11";

        NumberFormatBox.ItemsSource = NumberFormatOptions.Select(option => option.Label).ToArray();
        NumberFormatBox.SelectedIndex = 0;

        PopulateFormatTableGalleryMenu();
        ApplyOptionsToView();
        NormalizeRibbonSurface(forceCompact: true);
        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
    }

    private void ApplyOptionsToView()
    {
        SheetGrid.UseR1C1ReferenceStyle = _options.UseR1C1ReferenceStyle;
        _suppressAppViewOptionSync = true;
        try
        {
            if (ViewFormulaBarChk is not null)
                ViewFormulaBarChk.IsChecked = _options.ShowFormulaBar;
            if (FormulaBarBorder is not null)
                FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
            _formulaBarExpanded = _options.FormulaBarExpanded;
            ApplyFormulaBarExpansion();
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        if (SheetGrid.SelectedRange is { } range)
        {
            CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
            var sheet = _workbook.GetSheet(_currentSheetId);
            FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(range.Start), range.Start);
        }
    }

    private void RecalculateWorkbook()
    {
        _recalcEngine.RecalculateAllFormulas(_workbook);
    }

    private void RebuildDependenciesAndCalculate()
    {
        _recalcEngine.RebuildFormulaDependencies(_workbook);
        _recalcEngine.RecalculateAllFormulas(_workbook);
        UpdateViewport();
    }

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        if (_workbook.CalculationMode == WorkbookCalculationMode.Automatic)
            _recalcEngine.Recalculate(_workbook, changedCells);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRibbonCompactMode();
        UpdateViewport();
    }

    private string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, _options.UseR1C1ReferenceStyle);

    private string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, _options.UseR1C1ReferenceStyle);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, _options.UseR1C1ReferenceStyle);

    private string FormatFormulaBarText(Cell? cell, CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, _options.UseR1C1ReferenceStyle);

    // ── Header / select-all helpers ───────────────────────────────────────────

    private void RefreshToolbar()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);

        _suppressToolbarSync = true;
        BoldButton.IsChecked      = style.Bold;
        ItalicButton.IsChecked    = style.Italic;
        UnderlineButton.IsChecked = style.Underline && !style.Strikethrough;
        StrikeButton.IsChecked    = style.Strikethrough;
        AlignTopBtn.IsChecked     = style.VerticalAlignment == CellVAlign.Top;
        AlignMiddleBtn.IsChecked  = style.VerticalAlignment == CellVAlign.Center;
        AlignBottomBtn.IsChecked  = style.VerticalAlignment == CellVAlign.Bottom;
        AlignLeftBtn.IsChecked    = style.HorizontalAlignment == CellHAlign.Left;
        AlignCenterBtn.IsChecked  = style.HorizontalAlignment == CellHAlign.Center;
        AlignRightBtn.IsChecked   = style.HorizontalAlignment == CellHAlign.Right;
        WrapTextBtn.IsChecked     = style.WrapText;
        if (FontNameBox.Items.Contains(style.FontName))
            FontNameBox.SelectedItem = style.FontName;
        var sizeStr = style.FontSize.ToString("0.#");
        if (FontSizeBox.Items.Contains(sizeStr))
            FontSizeBox.SelectedItem = sizeStr;
        _suppressToolbarSync = false;
    }

    private void ApplyStyleDiff(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyle(diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(() => _workbook, _commandBus, NavigateToCell, replaceMode: false)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(() => _workbook, _commandBus, NavigateToCell, replaceMode: true)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void NavigateToCell(CellAddress addr)
    {
        _currentSheetId = addr.Sheet;
        SetActiveCell(addr);
        EnsureCellVisible(addr);
        UpdateViewport();
    }

    private void RefreshSheetProtectionUi()
    {
        if (ProtectSheetButton is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);
        ProtectSheetButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectSheetButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectSheetButton, uiText.TooltipDescription);
    }

    private void RefreshWorkbookProtectionUi()
    {
        if (ProtectWorkbookButton is null)
            return;

        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        ProtectWorkbookButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectWorkbookButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectWorkbookButton, uiText.TooltipDescription);
    }

    private static void ShowCommandError(CommandOutcome outcome, string title)
    {
        if (outcome.Success) return;

        MessageBox.Show(outcome.ErrorMessage ?? "The command could not be completed.",
            title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title, out CommandOutcome outcome)
    {
        outcome = _commandBus.Execute(_workbook.Id, command);
        if (outcome.Success)
            return true;

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title) =>
        TryExecuteCommand(command, title, out _);

    private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds()
    {
        var groupedVisibleSheets = _workbook.Sheets
            .Where(sheet => !sheet.IsHidden && _groupedSheetIds.Contains(sheet.Id))
            .Select(sheet => sheet.Id)
            .ToList();

        return groupedVisibleSheets.Count > 1 && groupedVisibleSheets.Contains(_currentSheetId)
            ? groupedVisibleSheets
            : [_currentSheetId];
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title,
        out CommandOutcome outcome)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title) =>
        TryExecuteEditCells(edits, title, out _);

    private bool TryExecuteApplyStyle(GridRange range, StyleDiff diff, string title)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
            : new ApplyStyleCommand(_currentSheetId, range, diff);
        return TryExecuteCommand(command, title);
    }

    private bool TryExecuteRepeatableApplyStyle(StyleDiff diff, string title)
    {
        IWorkbookCommand CreateCommand()
        {
            var range = SheetGrid.SelectedRange ?? new GridRange(
                new CellAddress(_currentSheetId, 1, 1),
                new CellAddress(_currentSheetId, 1, 1));
            var targetSheetIds = CurrentGroupedEditSheetIds();
            return targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
                : new ApplyStyleCommand(_currentSheetId, range, diff);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var targetSheetIds = CurrentGroupedEditSheetIds();
            return targetSheetIds.Count > 1
                ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
                : createCommand(_currentSheetId);
        }

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableGroupedSheetCommand(title, createCommand, out _);

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var range = SheetGrid.SelectedRange ?? fallbackRange;
            return createCommand(range);
        }

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableCurrentRangeCommand(title, fallbackRange, createCommand, out _);

    private bool TryExecuteRepeatableChartLayout(
        string caption,
        string missingMessage,
        Func<ChartModel, bool>? canApply,
        string? unsupportedMessage,
        Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (chart is null)
                return new FailedWorkbookCommand(missingMessage);
            if (canApply is not null && !canApply(chart))
                return new FailedWorkbookCommand(unsupportedMessage ?? "This chart command is not supported for the selected chart.");
            return new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart));
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, caption);
        return false;
    }

    private bool TryGetFirstChartForDialog(string caption, string missingMessage, out ChartModel chart)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        chart = sheet?.Charts.FirstOrDefault()!;
        if (chart is not null)
            return true;

        ShowCommandError(new CommandOutcome(false, missingMessage), caption);
        return false;
    }

    private bool ApplyChartLayoutDialogResult(string caption, ChartModel chart, ChartLayoutOptions options)
    {
        if (!TryExecuteCommand(new SetChartLayoutCommand(_currentSheetId, chart.Id, options), caption))
            return false;

        UpdateViewport();
        return true;
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
            : createCommand(_currentSheetId);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteGroupedSheetCommand(title, createCommand, out _);

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: true)))
            return;
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: false)))
            return;
        UpdateViewport();
    }

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dialog = new SortDialog(columnChoices: SortDialog.BuildColumnChoices(range)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var keys = dialog.ResultSortKeys;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, keys)))
            return;
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyFilterPrompt(range, filterColOffset: 0);
    }

    private void ApplyFilterPrompt(GridRange range, uint filterColOffset)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = sheet is null
            ? new AutoFilterDialog(Array.Empty<AutoFilterChecklistItem>())
            : new AutoFilterDialog(AutoFilterDropdownPlanner.CreateMenuPlan(sheet, new AutoFilterDropdownPlan(range, filterColOffset)));
        dialog.Owner = this;
        dialog.Title = "Filter";
        if (dialog.ShowDialog() != true) return;

        if (!ApplyAutoFilterDialogResult(range, filterColOffset, dialog.Result, "Filter"))
            return;
        UpdateViewport();
    }

    private bool ApplyAutoFilterDialogResult(GridRange range, uint filterColOffset, AutoFilterDialogResult result, string title)
    {
        if (result.SortDirection != AutoFilterSortDirection.None)
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Sort",
                    range,
                    currentRange => new SortCommand(_currentSheetId, currentRange, filterColOffset, result.SortDirection == AutoFilterSortDirection.Ascending)))
                return false;
            return true;
        }

        var value = result.CriteriaText;
        var filterText = value.TrimStart();
        if (!string.IsNullOrWhiteSpace(filterText) &&
            (filterText.StartsWith("top:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase)))
        {
            if (!FilterInputParser.TryParseTopBottom(value, out var count, out var top, out var percent, out var error))
            {
                MessageBox.Show(error ?? "Enter top:n, bottom:n, toppercent:n, or bottompercent:n.",
                    title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => percent
                        ? TopBottomFilterCommand.Percent(_currentSheetId, currentRange, filterColOffset, count, top)
                        : new TopBottomFilterCommand(_currentSheetId, currentRange, filterColOffset, count, top)))
                return false;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText) &&
            FilterInputParser.TryParseAverage(value, out var aboveAverage))
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new AverageFilterCommand(_currentSheetId, currentRange, filterColOffset, aboveAverage)))
                return false;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filterText) &&
            (filterText.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
             filterText.Equals("nonblank", StringComparison.OrdinalIgnoreCase) ||
             filterText.Equals("non-blank", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date=", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date>", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("date<", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("datebetween:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("contains:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("notcontains:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("begins:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("ends:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("text=", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("text<>", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith("between:", StringComparison.OrdinalIgnoreCase) ||
             filterText.StartsWith('>') ||
             filterText.StartsWith('<') ||
             filterText.StartsWith('=')))
        {
            if (!FilterInputParser.TryParseCriterion(value, out var criterion, out var error) || criterion is null)
            {
                MessageBox.Show(error ?? "Enter a supported filter criterion.",
                    title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new FilterConditionCommand(_currentSheetId, currentRange, filterColOffset, criterion)))
                return false;
            return true;
        }

        if (string.IsNullOrWhiteSpace(filterText) && result.SelectedValues.Count == 0)
        {
            MessageBox.Show("Select at least one filter item.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var allowedValues = FilterInputParser.ParseAllowedValues(value);
        if (allowedValues.Count == 0)
            allowedValues = result.SelectedValues;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset, allowedValues: allowedValues)))
            return false;

        return true;
    }

    private void CfRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "CF Rule");
            return;
        }

        var dialog = new ConditionalFormatThresholdDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var cf = new ConditionalFormat
        {
            AppliesTo    = range,
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = dialog.Result.ThresholdText,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, GroupedSheetRangePlanner.CloneConditionalFormatForSheet(cf, sheetId))))
            return;
        UpdateViewport();
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "Data Validation");
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var dlg = new DataValidationDialog
        {
            Owner = this,
            SelectionSource = DataValidationService.FormatListSourceRange(range, sheet?.Name, sheet?.Name)
        };
        if (dlg.ShowDialog() != true) return;

        if (dlg.ClearRequested)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Clear Data Validation",
                    sheetId => new ClearDataValidationCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId))))
                return;

            UpdateViewport();
            return;
        }

        if (dlg.Result == null) return;

        var dv = dlg.Result;
        dv.AppliesTo = range;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Data Validation",
                sheetId =>
                {
                    var rule = GroupedSheetRangePlanner.CloneDataValidationForSheet(dv, sheetId);
                    rule.AppliesTo = GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    return new SetDataValidationCommand(sheetId, rule);
                }))
            return;
        UpdateViewport();
    }

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset: 0, allowedValues: [])))
            return;
        UpdateViewport();
    }

    private void NamedRangesButton_Click(object sender, RoutedEventArgs e)
    {
        var initialRange = SheetGrid.SelectedRange;
        var dlg = new NamedRangeDialog(_workbook, _commandBus, initialRange)
        {
            Owner = this
        };
        dlg.ShowDialog();
        UpdateViewport();
    }

    private void ExecuteUndo()
    {
        var outcome = _commandBus.Undo(_workbook.Id);
        if (!outcome.Success) return;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExecuteRedo()
    {
        var outcome = _commandBus.Redo(_workbook.Id);
        if (!outcome.Success) return;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExecuteRepeatLast()
    {
        var postAction = _repeatPostAction;
        var outcome = _commandBus.RepeatLast(_workbook.Id);
        if (!outcome.Success) return;
        postAction?.Invoke(outcome);
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    // ── Ribbon cells (insert / delete rows & columns) ────────────────────────

    private void InsertRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertRows(range.Start.Row);
    }

    private void DeleteRowBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void InsertColBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertColumns(range.Start.Col);
    }

    private void DeleteColBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedColumns();

    private void InsertRows(uint beforeRow)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Row", sheetId => new InsertRowsCommand(sheetId, beforeRow)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, beforeCol)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Row - currentRange.Start.Row + 1;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, count);
                }))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Col - currentRange.Start.Col + 1;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, count);
                }))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ApplyNumberFormatShortcut(NumberFormatShortcut shortcut) =>
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatShortcutService.GetFormat(shortcut)));

    private void CopyFromAbove(CopyFromAboveMode mode)
    {
        if (SheetGrid.SelectedRange?.Start is not { } target)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null ||
            CopyFromAbovePlanner.CreateEdit(sheet, target, mode) is not { } edit)
            return;

        if (!TryExecuteEditCells([edit], mode == CopyFromAboveMode.Value ? "Copy Value from Above" : "Copy Formula from Above"))
            return;

        RecalculateIfAutomatic([target]);
        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(target), target);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ApplyFontToggleShortcut(FontToggleShortcut shortcut, ToggleButton button)
    {
        var enabled = !(button.IsChecked == true);
        if (shortcut == FontToggleShortcut.Underline)
        {
            SetToolbarToggleStates(underline: enabled, strike: enabled ? false : null);
            ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled));
            return;
        }

        if (shortcut == FontToggleShortcut.Strikethrough)
        {
            SetToolbarToggleStates(strike: enabled, underline: enabled ? false : null);
            ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled));
            return;
        }

        button.IsChecked = enabled;
        ApplyStyleDiff(FontToggleShortcutService.CreateDiff(shortcut, enabled));
    }

    private void ApplyOutlineBorderShortcut()
    {
        ApplyRangeBorderPreset(BorderShortcutService.GetOutlineBorderDiff, "Outline Border");
    }

    private void ExecuteKeyboardInsert()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var plan = KeyboardInsertDeletePlanner.PlanInsert(range);
        if (plan == KeyboardInsertDeletePlan.Rows)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;
        }
        else if (plan == KeyboardInsertDeletePlan.Columns)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;
        }
        else if (!ExecuteKeyboardInsertCellsWithPrompt(range))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteKeyboardDelete()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var plan = KeyboardInsertDeletePlanner.PlanDelete(range);
        if (plan == KeyboardInsertDeletePlan.Rows)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;
        }
        else if (plan == KeyboardInsertDeletePlan.Columns)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;
        }
        else if (!ExecuteKeyboardDeleteCellsWithPrompt(range))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private bool ExecuteKeyboardInsertCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return false;

        return choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Cells",
                sheetId => new InsertCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    InsertCellsShiftDirection.Down)),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new InsertRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                }),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new InsertColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                }),
            _ => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Cells",
                sheetId => new InsertCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    InsertCellsShiftDirection.Right))
        };
    }

    private bool ExecuteKeyboardDeleteCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
            return false;

        return choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftUp => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Cells",
                sheetId => new DeleteCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    DeleteCellsShiftDirection.Up)),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                }),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                }),
            _ => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Cells",
                sheetId => new DeleteCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    DeleteCellsShiftDirection.Left))
        };
    }

    private bool TryShowCellShiftDialog(CellShiftDialogMode mode, out KeyboardInsertDeleteDialogChoice choice)
    {
        var dialog = new CellShiftDialog(mode) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            choice = default;
            return false;
        }

        choice = CellShiftDialog.ToKeyboardChoice(mode, dialog.SelectedChoice);
        return true;
    }

    private void ExecuteRowsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Row" : "Unhide Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var (startRow, endRow) = SelectionRangeService.GetRowSpan(currentRange);
                    return new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void ExecuteColumnsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Column" : "Unhide Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var (startCol, endCol) = SelectionRangeService.GetColumnSpan(currentRange);
                    return new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var currentStyle = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        var dlg = new FormatCellsDialog(currentStyle, initialTab) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultDiff is null) return;
        ApplyStyleDiff(dlg.ResultDiff);
    }

    private void OnAutofillRequested(GridRange sourceRange, GridRange fillRange)
    {
        var cmd = new AutofillCommand(_currentSheetId, sourceRange, fillRange);
        if (!TryExecuteCommand(cmd, "Autofill"))
            return;

        RecalculateIfAutomatic(fillRange.AllCells().ToList());
        UpdateViewport();
        RefreshStatusBar();
    }

    private void UpdateMaximizedContentInset()
    {
        if (RootGrid is null)
            return;

        RootGrid.Margin = WindowState == WindowState.Maximized
            ? GetMaximizedSafeInset()
            : new Thickness(0);
    }

    private static Thickness GetMaximizedSafeInset()
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        var inset = Math.Ceiling(Math.Max(
            MaximizedSafeInsetDip,
            Math.Max(
                Math.Max(resize.Left, resize.Right),
                Math.Max(resize.Top, resize.Bottom))));

        return new Thickness(inset);
    }

    // ── Print / Export ────────────────────────────────────────────────────────

    // ── Format Painter ───────────────────────────────────────────────────────

    private void InsertCurrentDateOrTime(bool insertTime)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = insertTime
            ? DateTimeEntryService.CurrentTime(DateTime.Now)
            : DateTimeEntryService.CurrentDate(DateTime.Now);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                insertTime ? "Insert Time" : "Insert Date",
                range,
                currentRange => CreateSingleCellEditCommand(currentRange.Start, Cell.FromValue(value)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    // ── Insert tab ────────────────────────────────────────────────────────────

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e)    => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e)  => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var selected = SheetGrid.SelectedRange;
        var dialog = new SparklineDialog(
            selected?.ToString() ?? "",
            "",
            SparklineInputParser.ParseDialogKindChoice(type))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!SparklineInputParser.TryParseDataRange(dialog.Result.DataRangeText, _currentSheetId, out var dataRange))
        {
            MessageBox.Show("Invalid data range.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SparklineInputParser.TryParseLocation(dialog.Result.LocationText, _currentSheetId, out var location))
        {
            MessageBox.Show("Invalid location cell.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var kind = SparklineInputParser.ToModelKind(dialog.Result.Kind);

        var fallbackLocationRange = new GridRange(location, location);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Sparkline",
                fallbackLocationRange,
                currentRange => new AddSparklineCommand(_currentSheetId, dataRange, currentRange.Start, kind)))
            return;

        SetActiveCell(location);
        EnsureCellVisible(location);
        UpdateViewport();
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var dialog = new HyperlinkDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Link",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetHyperlinkCommand(
                    _currentSheetId,
                    currentRange.Start,
                    dialog.Result.Target,
                    dialog.Result.DisplayText)))
            return;
        UpdateViewport();
    }

    private void InsertCommentBtn_Click(object sender, RoutedEventArgs e)    => ReviewNewThreadedCommentBtn_Click(sender, e);
    private void HeaderFooterBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var dialog = new HeaderFooterDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Header & Footer",
                sheetId => new SetHeaderFooterCommand(
                    sheetId,
                    dialog.Header,
                    dialog.Footer,
                    dialog.FirstPageHeader,
                    dialog.FirstPageFooter,
                    dialog.EvenPageHeader,
                    dialog.EvenPageFooter,
                    dialog.DifferentFirstPage,
                    dialog.DifferentOddEvenPages,
                    dialog.ScaleWithDocument,
                    dialog.AlignWithMargins)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }
    private void SymbolPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SymbolPickerDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedChar == '\0') return;
        if (SheetGrid.SelectedRange is null) return;
        var selectedChar = dlg.SelectedChar;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Symbol",
                SheetGrid.SelectedRange.Value,
                currentRange =>
                {
                    var currentAddress = currentRange.Start;
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    var currentExisting = currentSheet?.GetCell(currentAddress)?.Value as TextValue;
                    var currentText = (currentExisting?.Value ?? "") + selectedChar;
                    return CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)));
                }))
            return;
        UpdateViewport();
    }

    private IWorkbookCommand CreateSingleCellEditCommand(CellAddress address, Cell cell)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)> { (address, cell) };
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    // ── Draw tab stubs ────────────────────────────────────────────────────────

    // ── Data tab additions ────────────────────────────────────────────────────

    // ── View tab ─────────────────────────────────────────────────────────────

    // ── QAT / title bar ──────────────────────────────────────────────────────

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();
    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();

    // ── Formula bar expand chevron ────────────────────────────────────────────

    // ── Sheet tab nav arrows ──────────────────────────────────────────────────

    // ── Help tab ──────────────────────────────────────────────────────────────



}

