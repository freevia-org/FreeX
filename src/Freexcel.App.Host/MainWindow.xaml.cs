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

    private void SelectRow(uint row)
    {
        HideValidationDropdown();
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, row, 1);
        _selectionCursor = new CellAddress(_currentSheetId, row, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = $"{row}:{row}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, col);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, col);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        var colName = FormatColumnReference(col);
        CellAddressBox.Text = $"{colName}:{colName}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, 1);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = FormatCellReference(_selectionAnchor.Value);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        const double colHeaderH = Freexcel.App.UI.GridView.ColHeaderHeight;
        double rowHeaderW = SheetGrid.ActualRowHeaderWidth;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        // ── Header area ───────────────────────────────────────────────────────
        if (pos.X < rowHeaderW || pos.Y < colHeaderH)
        {
            // Top-left corner: select all
            if (pos.X < rowHeaderW && pos.Y < colHeaderH)
            {
                SelectAll();
                return;
            }
            // Column header: select entire column
            if (pos.Y < colHeaderH)
            {
                foreach (var cm in viewport.ColMetrics)
                {
                    double left = cm.LeftOffset + rowHeaderW;
                    if (pos.X >= left && pos.X < left + cm.Width)
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                        {
                            uint anchorCol = _selectionAnchor.Value.Col;
                            _selectionCursor = new CellAddress(_currentSheetId, 1_048_576, cm.Col);
                            SheetGrid.SelectedRange = new GridRange(
                                new CellAddress(_currentSheetId, 1, Math.Min(anchorCol, cm.Col)),
                                new CellAddress(_currentSheetId, 1_048_576, Math.Max(anchorCol, cm.Col)));
                            var c1 = FormatColumnReference(Math.Min(anchorCol, cm.Col));
                            var c2 = FormatColumnReference(Math.Max(anchorCol, cm.Col));
                            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
                        }
                        else
                        {
                            SelectColumn(cm.Col);
                        }
                        return;
                    }
                }
                return;
            }
            // Row header: select entire row
            foreach (var rm in viewport.RowMetrics)
            {
                double top = rm.TopOffset + colHeaderH;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    {
                        uint anchorRow = _selectionAnchor.Value.Row;
                        _selectionCursor = new CellAddress(_currentSheetId, rm.Row, 16_384);
                        SheetGrid.SelectedRange = new GridRange(
                            new CellAddress(_currentSheetId, Math.Min(anchorRow, rm.Row), 1),
                            new CellAddress(_currentSheetId, Math.Max(anchorRow, rm.Row), 16_384));
                        var r1 = Math.Min(anchorRow, rm.Row);
                        var r2 = Math.Max(anchorRow, rm.Row);
                        CellAddressBox.Text = r1 == r2 ? $"{r1}:{r1}" : $"{r1}:{r2}";
                    }
                    else
                    {
                        SelectRow(rm.Row);
                    }
                    return;
                }
            }
            return;
        }

        // ── Cell area ─────────────────────────────────────────────────────────

        if (_formulaTraceArrows.Count > 0 &&
            Freexcel.App.UI.GridView.HitTestFormulaTraceMarker(viewport, _formulaTraceArrows, _currentSheetId, pos) is { } traceTarget)
        {
            NavigateToCell(traceTarget);
            RefreshSheetTabs();
            RefreshToolbar();
            RefreshStatusBar();
            e.Handled = true;
            return;
        }

        _activeSplitPaneRegion = Freexcel.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);
        var hitAddress = Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
        if (hitAddress is { } newAddr)
        {
            if (_inlineEditor?.IsVisible == true)
            {
                FormulaBar.Text = _inlineEditor.Text;
                var committed = CommitEdit();
                HideInlineEditor(commit: false);
                if (!committed)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_formatPainterActive)
            {
                if (SheetGrid.SelectedRange is { } selectedRange &&
                    selectedRange.Contains(newAddr) &&
                    (selectedRange.Start != selectedRange.End || e.ClickCount > 1))
                {
                    TryApplyFormatPainter(selectedRange);
                    e.Handled = true;
                    return;
                }

                SetActiveCell(newAddr);
                _formatPainterTargetSelectionActive = true;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
            {
                ExtendSelection(_selectionAnchor.Value, newAddr);
            }
            else
            {
                SetActiveCell(newAddr);
                if (e.ClickCount == 2)
                {
                    if (!TryShowPivotTableDetails(showMessage: false))
                        EnterEditMode();
                    e.Handled = true;
                }
                else
                {
                    // Start drag-select
                    _dragSelectActive = true;
                    SheetGrid.CaptureMouse();
                }
            }
        }
    }

    private void MainWindow_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Don't steal input from text boxes or combo boxes (formula bar, toolbar dropdowns)
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (SheetGrid.SelectedRange == null) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

        if (_selectionAnchor.HasValue)
        {
            ShowInlineEditor(_selectionAnchor.Value);
            if (_inlineEditor != null)
            {
                _inlineEditor.Text = e.Text;
                _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
            }
        }
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && IsStartScreenVisible())
        {
            HideStartScreen();
            e.Handled = true;
            return;
        }

        if (!KeyboardShortcutMatcher.TryGetCommandShortcut(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var commandShortcut))
        {
            return;
        }

        if (commandShortcut is not (KeyboardCommandShortcut.ShowKeyTips or KeyboardCommandShortcut.OpenContextMenu))
            return;

        ExecuteCommandShortcut(commandShortcut, sender, e);
        e.Handled = true;
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox and not ComboBox)
        {
            var keyTipKey = GetEffectiveKey(e);
            if (IsStandaloneAltKey(keyTipKey) && _ribbonKeyTipMode.IsActive)
            {
                _pendingStandaloneAltKeyTip = true;
                e.Handled = true;
                return;
            }

            if (_ribbonKeyTipMode.IsActive && Keyboard.Modifiers == ModifierKeys.None)
            {
                HandleActiveRibbonKeyTip(keyTipKey);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt && IsStandaloneAltKey(keyTipKey))
            {
                _pendingStandaloneAltKeyTip = true;
                e.Handled = true;
                return;
            }

            _pendingStandaloneAltKeyTip = false;

            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (IsStartScreenVisible())
                {
                    HideStartScreen();
                    e.Handled = true;
                    return;
                }

                CancelCopyAndTransientModes();
                e.Handled = true;
                return;
            }

            if (ExcelSelectionModePlanner.TryToggle(e.Key, Keyboard.Modifiers, _selectionMode, out var nextSelectionMode))
            {
                SetSelectionMode(nextSelectionMode);
                e.Handled = true;
                return;
            }

            if (ExcelWorksheetNavigationPlanner.TryToggleEndMode(e.Key, Keyboard.Modifiers, _endMode, out var nextEndMode))
            {
                SetEndMode(nextEndMode);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt &&
                RibbonKeyTipMode.ToKeyTipToken(keyTipKey) is { } keyTip &&
                TryHandleTopLevelRibbonKeyTip(keyTip))
            {
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt &&
                RibbonKeyTipMode.ToKeyTipToken(keyTipKey) is { } qatKeyTip &&
                TryInvokeTopLevelQatKeyTip(qatKeyTip))
            {
                ExitRibbonKeyTipMode();
                e.Handled = true;
                return;
            }

            if (KeyboardShortcutMatcher.TryGetCommandShortcut(e.Key, e.SystemKey, Keyboard.Modifiers, out var commandShortcut))
            {
                if ((commandShortcut == KeyboardCommandShortcut.ClearSelection ||
                     commandShortcut == KeyboardCommandShortcut.ClearSelectionAndEdit) &&
                    Keyboard.FocusedElement is TextBox)
                {
                    return;
                }

                ExecuteCommandShortcut(commandShortcut, sender, e);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetNumberFormatShortcut(e.Key, Keyboard.Modifiers, out var numberFormatShortcut))
            {
                ApplyNumberFormatShortcut(numberFormatShortcut);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetBorderShortcut(e.Key, Keyboard.Modifiers, out var borderShortcut))
            {
                if (borderShortcut == BorderKeyboardShortcut.Outline)
                    ApplyOutlineBorderShortcut();
                else
                    ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());

                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlPlus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardInsert();
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlMinus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardDelete();
                e.Handled = true;
                return;
            }
        }

        if (KeyboardShortcutMatcher.TryGetFontToggleShortcut(e.Key, Keyboard.Modifiers, out var fontToggleShortcut))
        {
            var button = fontToggleShortcut switch
            {
                FontToggleShortcut.Bold => BoldButton,
                FontToggleShortcut.Italic => ItalicButton,
                FontToggleShortcut.Strikethrough => StrikeButton,
                _ => UnderlineButton
            };
            ApplyFontToggleShortcut(fontToggleShortcut, button);
            e.Handled = true;
            return;
        }

        if (KeyboardShortcutMatcher.IsPasteSpecialShortcut(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            PasteSpecialBtn_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.TryGetSelectionShortcut(e.Key, Keyboard.Modifiers, out var selectionShortcut))
        {
            switch (selectionShortcut)
            {
                case KeyboardSelectionShortcut.SelectAll:
                    SelectAll();
                    break;
                case KeyboardSelectionShortcut.SelectCurrentRegion:
                    SelectCurrentRegionOnly();
                    break;
                case KeyboardSelectionShortcut.SelectWholeColumns:
                    SelectWholeColumnsFromSelection();
                    break;
                case KeyboardSelectionShortcut.SelectWholeRows:
                    SelectWholeRowsFromSelection();
                    break;
            }

            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.TryGetGridShortcut(e.Key, Keyboard.Modifiers, out var gridShortcut))
        {
            switch (gridShortcut)
            {
                case KeyboardGridShortcut.HideRows:
                    ExecuteRowsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideRows:
                    ExecuteRowsHidden(hidden: false);
                    break;
                case KeyboardGridShortcut.HideColumns:
                    ExecuteColumnsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideColumns:
                    ExecuteColumnsHidden(hidden: false);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (SheetGrid.SelectedRange == null) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool extendSelection = ExcelSelectionModePlanner.ShouldExtendSelection(_selectionMode, Keyboard.Modifiers);
        bool useDataBoundary = ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(e.Key, Keyboard.Modifiers, _endMode);
        bool ctrlHeld  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // When Shift or F8 extend mode is active the moving end is _selectionCursor; otherwise it's the active cell.
        var current = extendSelection && _selectionCursor.HasValue
            ? _selectionCursor.Value
            : SheetGrid.SelectedRange.Value.Start;

        var sheet = _workbook.GetSheet(_currentSheetId);
        int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
        int colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);

        CellAddress? target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            e.Key,
            e.SystemKey,
            Keyboard.Modifiers,
            current,
            colPageSize);

        target ??= e.Key switch
        {
            Key.Up    => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1)
                                  : new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, +1)
                                  : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Left  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                                  : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),

            Key.Home     => new CellAddress(_currentSheetId, ctrlHeld ? 1u : current.Row, 1u),
            Key.End      => ctrlHeld ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, _currentSheetId) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            Key.Enter => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col)
                : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Tab   => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),
            _         => null
        };

        if (target == null) return;

        if (_endMode)
            SetEndMode(false);

        // Enter and Tab (including Shift variants) move the active cell; they don't extend selection
        bool moveOnly = e.Key is Key.Enter or Key.Tab;
        if (_selectionMode == ExcelSelectionMode.Add && !moveOnly)
            AddOrMoveAdditionalSelection(target.Value, extendSelection);
        else if (extendSelection && !moveOnly && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target.Value);
        else
            SetActiveCell(target.Value);

        EnsureCellVisible(target.Value);
        e.Handled = true;
    }

    private void ExecuteCommandShortcut(KeyboardCommandShortcut shortcut, object sender, RoutedEventArgs e)
    {
        _keyboardCommandDispatcher.TryExecute(shortcut, sender, e);
    }

    private void CycleSelectionCorner()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var currentCorner = _selectionCursor ?? _selectionAnchor ?? range.Start;
        var nextCorner = SelectionCornerNavigator.GetNextCorner(range, currentCorner);
        _selectionAnchor = nextCorner;
        _selectionCursor = nextCorner;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(nextCorner), nextCorner);
        EnsureCellVisible(nextCorner);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ScrollActiveCellIntoView()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell)
            return;

        EnsureCellVisible(activeCell);
        FocusSheetGridIfNeeded();
    }

    private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_pendingStandaloneAltKeyTip)
            return;

        var keyTipKey = GetEffectiveKey(e);
        if (!IsStandaloneAltKey(keyTipKey))
            return;

        _pendingStandaloneAltKeyTip = false;
        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
        else
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);

        e.Handled = true;
    }

    private void SetActiveCell(CellAddress addr)
    {
        // If the cell belongs to a merged region, select the whole region
        var sheet = _workbook.GetSheet(_currentSheetId);
        var merge = sheet?.GetMergeRegion(addr);
        if (merge.HasValue)
        {
            _selectionAnchor = merge.Value.Start;
            _selectionCursor = merge.Value.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = merge.Value;
            CellAddressBox.Text = FormatCellReference(merge.Value.Start);
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            FormulaBar.Text = FormatFormulaBarText(mergedCell, merge.Value.Start);
            FocusSheetGridIfNeeded();
            RefreshToolbar();
            RefreshStatusBar();
            RefreshValidationDropdown();
            return;
        }

        _selectionAnchor = addr;
        _selectionCursor = addr;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = FormatCellReference(addr);

        var cell = sheet?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
        RefreshValidationDropdown();
    }

    private void SelectCurrentRegionOrAll()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion &&
            SheetGrid.SelectedRange != currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            var activeCellModel = sheet.GetCell(cell);
            FormulaBar.Text = FormatFormulaBarText(activeCellModel, cell);
            SheetGrid.Focus();
            RefreshToolbar();
            RefreshStatusBar();
            return;
        }

        SelectAll();
    }

    private void SelectCurrentRegionOnly()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            FormulaBar.Text = FormatFormulaBarText(sheet.GetCell(cell), cell);
            SheetGrid.Focus();
            RefreshToolbar();
            RefreshStatusBar();
        }
    }

    private void SelectWholeRowsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeRows(range), range.Start);
    }

    private void SelectWholeColumnsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeColumns(range), range.Start);
    }

    private void SetSelectionRange(GridRange range, CellAddress activeCell)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        var activeCellModel = sheet?.GetCell(activeCell);
        FormulaBar.Text = FormatFormulaBarText(activeCellModel, activeCell);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExtendSelection(CellAddress anchor, CellAddress to)
    {
        _selectionCursor = to;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        CellAddressBox.Text = FormatRangeReference(anchor, to);
        RefreshStatusBar();
    }

    private void AddOrMoveAdditionalSelection(CellAddress target, bool extendSelection)
    {
        var ranges = SheetGrid.SelectedRanges is { Count: > 0 }
            ? SheetGrid.SelectedRanges.ToList()
            : SheetGrid.SelectedRange is { } currentRange ? [currentRange] : [];

        if (!extendSelection)
            _selectionAnchor = target;

        var anchor = _selectionAnchor ?? target;
        _selectionCursor = target;
        var activeRange = new GridRange(anchor, target);

        if (ranges.Count > 0 && SheetGrid.SelectedRange is { } currentActive && ranges[^1] == currentActive)
            ranges[^1] = activeRange;
        else
            ranges.Add(activeRange);

        SheetGrid.SelectedRanges = ranges;
        SheetGrid.SelectedRange = activeRange;
        CellAddressBox.Text = FormatRangeReference(activeRange.Start, activeRange.End);

        var sheet = _workbook.GetSheet(_currentSheetId);
        FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(target), target);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private CellAddress? HitTestCell(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return null;
        return Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
    }

    private void SheetGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragSelectActive || e.LeftButton != MouseButtonState.Pressed) return;
        if (_selectionAnchor is not { } anchor) return;
        var hitAddr = HitTestCell(e.GetPosition(SheetGrid));
        if (hitAddr.HasValue)
            ExtendSelection(anchor, hitAddr.Value);
    }

    private void SheetGrid_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_formatPainterTargetSelectionActive)
        {
            _formatPainterTargetSelectionActive = false;
            _dragSelectActive = false;
            SheetGrid.ReleaseMouseCapture();

            if (SheetGrid.SelectedRange is { } selectedRange)
                TryApplyFormatPainter(selectedRange);

            e.Handled = true;
            return;
        }

        if (!_dragSelectActive) return;
        _dragSelectActive = false;
        SheetGrid.ReleaseMouseCapture();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _pendingStandaloneAltKeyTip = false;
        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
    }

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

    private static Key GetEffectiveKey(System.Windows.Input.KeyEventArgs e) =>
        e.SystemKey == Key.None ? e.Key : e.SystemKey;

    private static bool IsStandaloneAltKey(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.System;

    private void EnterRibbonKeyTipMode(RibbonKeyTipScope scope)
    {
        _ribbonKeyTipMode.Enter();
        _ribbonKeyTipScope = scope;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ShowKeyTipOverlay(scope);
    }

    private void ExitRibbonKeyTipMode()
    {
        if (_activeRibbonKeyTipMenu is not null)
            _activeRibbonKeyTipMenu.IsOpen = false;

        _ribbonKeyTipMode.Cancel();
        _ribbonKeyTipScope = RibbonKeyTipScope.None;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ClearKeyTipOverlay();
    }

    private void HandleActiveRibbonKeyTip(Key key)
    {
        if (key == Key.Escape)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        var token = RibbonKeyTipMode.ToKeyTipToken(key);
        if (token is null)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        _ribbonKeyTipSequence += token;

        if (_ribbonKeyTipScope == RibbonKeyTipScope.TopLevel)
        {
            if (TryHandleTopLevelRibbonKeyTip(_ribbonKeyTipSequence))
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            else if (TryInvokeTopLevelQatKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else
                ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu)
        {
            if (TryInvokeActiveMenuItemKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else if (RibbonTooltip.TryOpenSubmenuForKeyTip(_activeRibbonKeyTipMenu!, _ribbonKeyTipSequence))
                return;
            else if (!HasActiveMenuItemKeyTipPrefix(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();

            return;
        }

        if (TryInvokeVisibleCommandKeyTip(_ribbonKeyTipSequence))
        {
            ExitRibbonKeyTipMode();
            return;
        }

        if (!HasVisibleCommandKeyTipPrefix(_ribbonKeyTipSequence))
            ExitRibbonKeyTipMode();
    }

    private void ShowKeyTipOverlay(RibbonKeyTipScope scope)
    {
        if (KeyTipOverlay == null || RootGrid == null)
            return;

        RootGrid.UpdateLayout();
        KeyTipOverlay.Children.Clear();

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)))
            {
                continue;
            }

            if (!ShouldShowKeyTipElement(element, scope))
                continue;

            var keyTip = RibbonTooltip.GetKeyTip(element);
            if (string.IsNullOrWhiteSpace(keyTip))
                continue;

            Point origin;
            try
            {
                origin = element.TransformToAncestor(RootGrid).Transform(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var badge = CreateKeyTipBadge(keyTip);
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeSize = badge.DesiredSize;
            var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
                new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)),
                new Size(RootGrid.ActualWidth, RootGrid.ActualHeight),
                badgeSize);

            Canvas.SetLeft(badge, point.X);
            Canvas.SetTop(badge, point.Y);
            KeyTipOverlay.Children.Add(badge);
        }

        KeyTipOverlay.Visibility = KeyTipOverlay.Children.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool ShouldShowKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope)
    {
        var isQuickAccessButton =
            element is Button button &&
            button.ReadLocalValue(DockPanel.DockProperty) is Dock.Left;
        if (scope == RibbonKeyTipScope.TopLevel)
            return element is TabItem || isQuickAccessButton;

        return element is not TabItem && !isQuickAccessButton;
    }

    private void ClearKeyTipOverlay()
    {
        if (KeyTipOverlay == null)
            return;

        KeyTipOverlay.Children.Clear();
        KeyTipOverlay.Visibility = Visibility.Collapsed;
    }

    private static Border CreateKeyTipBadge(string keyTip) =>
        new()
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = keyTip,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black
            }
        };

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            yield return dependencyObject;

            foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                yield return descendant;
        }
    }

    private bool TryInvokeVisibleCommandKeyTip(string keyTip)
    {
        var visibleKeyTipElements = GetVisibleKeyTipElements(RibbonKeyTipScope.Commands).ToList();
        var match = RibbonKeyTipRouting.ResolveKeyTipElement(visibleKeyTipElements, keyTip);
        if (match is null)
            return false;

        if (match is ButtonBase button)
        {
            if (TryEnterMenuKeyTipScope(button))
                return false;

            if (button is ToggleButton toggleButton)
                toggleButton.IsChecked = toggleButton.IsChecked != true;

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu &&
                ReferenceEquals(_activeRibbonKeyTipMenu?.PlacementTarget, button))
            {
                return false;
            }

            return true;
        }

        if (match is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = true;
            return true;
        }

        return false;
    }

    private bool TryEnterMenuKeyTipScope(ButtonBase button)
    {
        if (button.ContextMenu is not { } menu ||
            !GetMenuItems(menu).Any(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item))))
        {
            return false;
        }

        OpenRibbonContextMenu(button, menu, enterKeyTipMenuScope: true);
        return true;
    }

    private void OpenRibbonContextMenu(ButtonBase button, ContextMenu menu, bool enterKeyTipMenuScope = false)
    {
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;

        if (enterKeyTipMenuScope || _ribbonKeyTipMode.IsActive)
            EnterRibbonMenuKeyTipScope(menu);
    }

    private void EnterRibbonMenuKeyTipScope(ContextMenu menu)
    {
        _activeRibbonKeyTipMenu = menu;
        _ribbonKeyTipScope = RibbonKeyTipScope.Menu;
        _ribbonKeyTipSequence = "";
        ClearKeyTipOverlay();
    }

    private bool TryInvokeActiveMenuItemKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipMenu is null)
            return false;

        var match = RibbonKeyTipRouting.ResolveMenuItem(GetMenuItems(_activeRibbonKeyTipMenu), keyTip);
        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match));
        return true;
    }

    private bool HasActiveMenuItemKeyTipPrefix(string keyTipPrefix) =>
        _activeRibbonKeyTipMenu is not null &&
        RibbonKeyTipRouting.HasMenuItemKeyTipPrefix(GetMenuItems(_activeRibbonKeyTipMenu), keyTipPrefix);

    private static IEnumerable<MenuItem> GetMenuItems(ItemsControl itemsControl)
    {
        foreach (var item in itemsControl.Items)
        {
            if (item is MenuItem menuItem)
            {
                yield return menuItem;

                foreach (var child in GetMenuItems(menuItem))
                    yield return child;
            }
        }
    }

    private bool TryInvokeTopLevelQatKeyTip(string keyTip)
    {
        var match = GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)
            .OfType<ButtonBase>()
            .FirstOrDefault(element =>
                string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));
        return true;
    }

    private bool HasVisibleCommandKeyTipPrefix(string keyTipPrefix) =>
        RibbonKeyTipRouting.HasKeyTipPrefix(GetVisibleKeyTipElements(RibbonKeyTipScope.Commands), keyTipPrefix);

    private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(RibbonKeyTipScope scope)
    {
        if (RootGrid == null)
            yield break;

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)) ||
                !ShouldShowKeyTipElement(element, scope) ||
                string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(element)))
            {
                continue;
            }

            yield return element;
        }
    }

    private bool IsStartScreenVisible() =>
        StartScreenOverlay?.Visibility == Visibility.Visible;

    private bool IsInsideStartScreenOverlay(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (ReferenceEquals(current, StartScreenOverlay))
                return true;
        }

        return false;
    }

    private static bool IsInsideUnselectedTabItem(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (current is TabItem tabItem)
                return !tabItem.IsSelected;
        }

        return false;
    }

    private static DependencyObject? GetTreeParent(DependencyObject element)
    {
        try
        {
            if (element is System.Windows.Media.Visual)
                return System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private void EnterEditMode()
    {
        if (_selectionAnchor.HasValue)
            ShowInlineEditor(_selectionAnchor.Value);
        else
        {
            FocusFormulaBarAtEnd();
        }
    }

    private void EditActiveCellInFormulaBar()
    {
        if (SheetGrid.SelectedRange?.Start is { } address)
        {
            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(address);
            FormulaBar.Text = FormatFormulaBarText(cell, address);
        }

        FocusFormulaBarAtEnd();
    }

    private void FocusFormulaBarAtEnd()
    {
        FormulaBar.Focus();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
    }

    private void ShowInlineEditor(CellAddress addr)
    {
        HideValidationDropdown();
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        var text = FormatFormulaBarText(cell, addr);

        if (_inlineEditor == null)
        {
            _inlineEditor = new System.Windows.Controls.TextBox
            {
                BorderThickness = new System.Windows.Thickness(2),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(33, 115, 70)),
                Padding         = new System.Windows.Thickness(1),
                FontFamily      = new System.Windows.Media.FontFamily("Calibri"),
                FontSize        = 15.0,
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
            };
            TextOptions.SetTextFormattingMode(_inlineEditor, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_inlineEditor, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_inlineEditor, TextHintingMode.Fixed);
            _inlineEditor.PreviewKeyDown += InlineEditor_KeyDown;
            _inlineEditor.LostFocus  += InlineEditor_LostFocus;
            _inlineEditor.TextChanged += (_, _) => FormulaBar.Text = _inlineEditor.Text;
            EditOverlay.Children.Add(_inlineEditor);
        }

        // Cell metrics are in unzoomed coordinates; the EditOverlay is not transformed, so scale.
        double zoom = _zoomLevel;
        double cx = (colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth) * zoom;
        double cy = (rowMetric.TopOffset  + Freexcel.App.UI.GridView.ColHeaderHeight) * zoom;
        double cellW = colMetric.Width  * zoom;
        double cellH = rowMetric.Height * zoom;

        _inlineEditor.Text = text;
        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, cx - 2);
        System.Windows.Controls.Canvas.SetTop(_inlineEditor,  cy - 2);
        _inlineEditor.Width  = cellW + 4;
        _inlineEditor.Height = Math.Max(cellH + 4, 20);
        _inlineEditor.Visibility  = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
        _inlineEditor.Focus();
        _inlineEditor.SelectAll();
    }

    private void HideInlineEditor(bool commit)
    {
        if (_inlineEditor == null) return;
        _inlineEditor.Visibility = Visibility.Collapsed;
        EditOverlay.IsHitTestVisible = false;
        if (commit)
            FormulaBar.Text = _inlineEditor.Text;
    }

    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true)
            return;

        if (SheetGrid.SelectedRange is not { } range ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            TryGetCellOverlayRect(range.Start) is not { } rect)
        {
            HideValidationDropdown();
            return;
        }

        var rule = DataValidationService.GetApplicable(sheet, range.Start)
            .FirstOrDefault(dv => dv.Type == DvType.List && dv.ShowDropdown);
        if (rule is null)
        {
            HideValidationDropdown();
            return;
        }

        var items = DataValidationService.GetListItems(rule, sheet, _workbook);
        if (items.Count == 0)
        {
            HideValidationDropdown();
            return;
        }

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = items;
        var currentText = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start)?.Value);
        _validationDropdown.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase));
        _suppressValidationDropdownCommit = false;

        var width = Math.Max(18, Math.Min(rect.Width, 160));
        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, rect.Right - width);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, rect.Top);
        _validationDropdown.Width = width;
        _validationDropdown.Height = Math.Max(18, rect.Height);
        _validationDropdown.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
    }

    private void EnsureValidationDropdown()
    {
        if (_validationDropdown is not null)
            return;

        _validationDropdown = new System.Windows.Controls.ComboBox
        {
            FontSize = 12,
            Padding = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(33, 115, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = "Pick from list"
        };
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is not null)
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true)
            EditOverlay.IsHitTestVisible = false;
    }

    private void OpenActiveDropdown()
    {
        RefreshValidationDropdown();
        if (_validationDropdown?.Visibility == Visibility.Visible)
        {
            _validationDropdown.Focus();
            _validationDropdown.IsDropDownOpen = true;
            return;
        }

        OpenAutoFilterDropdownForActiveCell();
    }

    private void OpenAutoFilterDropdownForActiveCell()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            SelectionRangeService.GetCurrentRegion(sheet, activeCell) is not { } currentRegion ||
            !AutoFilterDropdownPlanner.TryPlan(currentRegion, activeCell, out var plan))
        {
            return;
        }

        var menuPlan = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);
        if (menuPlan.Entries.All(entry => entry.Kind != AutoFilterMenuEntryKind.ChecklistItem))
            return;

        var dialog = new AutoFilterDialog(menuPlan)
        {
            Owner = this
        };
        PositionAutoFilterDialogAtActiveCell(dialog, activeCell);

        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, dialog.Result, "AutoFilter"))
            return;
        UpdateViewport();
    }

    private void PositionAutoFilterDialogAtActiveCell(Window dialog, CellAddress activeCell)
    {
        if (TryGetCellOverlayRect(activeCell) is not { } rect)
            return;

        var screenPoint = SheetGrid.PointToScreen(new System.Windows.Point(rect.Left, rect.Bottom));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = screenPoint.X;
        dialog.Top = screenPoint.Y;
    }

    private Rect? TryGetCellOverlayRect(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp is null)
            return null;

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + Freexcel.App.UI.GridView.ColHeaderHeight;
        return new Rect(left, top, colMetric.Width, rowMetric.Height);
    }

    private void ValidationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressValidationDropdownCommit ||
            _validationDropdown?.SelectedItem is not string selected ||
            SheetGrid.SelectedRange is not { } range)
        {
            return;
        }

        FormulaBar.Text = selected;
        CommitEdit();
        SetActiveCell(range.Start);
    }

    private void InlineEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4 && _inlineEditor is not null)
        {
            if (TryCycleFormulaReference(_inlineEditor))
            {
                FormulaBar.Text = _inlineEditor.Text;
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideInlineEditor(commit: false);
            // Restore original text in formula bar
            var addr = SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
            }
            CancelCopyAndTransientModes();
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }
        var current = SheetGrid.SelectedRange?.Start;
        if (current is null)
            return;

        var intent = ExcelEditKeyPlanner.GetIntent(
            e.Key,
            Keyboard.Modifiers,
            current.Value,
            pageSize: Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1),
            allowFormulaBarNavigationKeys: false);

        if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
        {
            InsertLineBreak(_inlineEditor!);
            FormulaBar.Text = _inlineEditor!.Text;
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitSelection)
        {
            FormulaBar.Text = _inlineEditor!.Text;
            if (CommitEditAcrossSelection())
                HideInlineEditor(commit: false);
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } next)
        {
            var text = _inlineEditor!.Text;
            FormulaBar.Text = text;
            if (CommitEdit())
            {
                HideInlineEditor(commit: false);
                SetActiveCell(next);
                EnsureCellVisible(next);
            }
            e.Handled = true;
        }
    }

    private static void InsertLineBreak(System.Windows.Controls.TextBox editor)
    {
        var edit = ExcelTextEditorPlanner.InsertLineBreak(
            editor.Text,
            editor.SelectionStart,
            editor.SelectionLength,
            Environment.NewLine);
        ApplyTextEdit(editor, edit);
    }

    private void InlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_inlineEditor?.IsVisible == true)
        {
            FormulaBar.Text = _inlineEditor.Text;
            HideInlineEditor(commit: true);
            CommitEdit();
        }
    }

    private void FocusSheetGridIfNeeded()
    {
        if (!ReferenceEquals(Keyboard.FocusedElement, SheetGrid))
            SheetGrid.Focus();
    }

    private void SetSelectionMode(ExcelSelectionMode mode)
    {
        _selectionMode = mode;
        if (mode != ExcelSelectionMode.Normal)
            _endMode = false;
        if (StatusStatsPanel is not null)
            StatusStatsPanel.Visibility = Visibility.Collapsed;
        if (StatusReadyText is null)
            return;

        StatusReadyText.Visibility = Visibility.Visible;
        StatusReadyText.Text = mode switch
        {
            ExcelSelectionMode.Extend => "Extend Selection",
            ExcelSelectionMode.Add => "Add to Selection",
            _ => "Ready"
        };
    }

    private void SetEndMode(bool enabled)
    {
        _endMode = enabled;
        if (enabled)
            _selectionMode = ExcelSelectionMode.Normal;
        if (StatusStatsPanel is not null)
            StatusStatsPanel.Visibility = Visibility.Collapsed;
        if (StatusReadyText is null)
            return;

        StatusReadyText.Visibility = Visibility.Visible;
        StatusReadyText.Text = enabled ? "End Mode" : "Ready";
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4)
        {
            if (TryCycleFormulaReference(FormulaBar))
                e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Restore the original cell value and return focus to grid
            var addr = SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
            }
            ClearClipboardVisualState();
            SheetGrid.Focus();
            e.Handled = true;
        }
        else if (SheetGrid.SelectedRange?.Start is { } current)
        {
            int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
            var intent = ExcelEditKeyPlanner.GetIntent(
                e.Key,
                e.KeyboardDevice.Modifiers,
                current,
                pageSize,
                allowFormulaBarNavigationKeys: true);

            if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
            {
                InsertLineBreak(FormulaBar);
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitSelection)
            {
                CommitEditAcrossSelection();
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } target)
            {
                if (CommitEdit())
                {
                    SetActiveCell(target);
                    EnsureCellVisible(target);
                }

                e.Handled = true;
            }
        }
    }

    private static bool TryCycleFormulaReference(System.Windows.Controls.TextBox editor)
    {
        var caretIndex = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretIndex;
        if (!ExcelTextEditorPlanner.TryCycleFormulaReference(editor.Text, caretIndex, out var edit))
            return false;

        ApplyTextEdit(editor, edit);
        return true;
    }

    private static void ApplyTextEdit(System.Windows.Controls.TextBox editor, ExcelTextEdit edit)
    {
        editor.Text = edit.Text;
        editor.SelectionStart = edit.SelectionStart;
        editor.SelectionLength = edit.SelectionLength;
    }

    private bool CommitEdit()
    {
        if (SheetGrid.SelectedRange == null) return false;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = FormulaBar.Text;

        if (!TryCreateCellFromEntryText(addr, text, out var newCell))
            return false;

        return CommitPreparedEdits([(addr, newCell)], text, [addr], "Edit Cell");
    }

    private bool CommitEditAcrossSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return false;
        var text = FormulaBar.Text;
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        foreach (var address in range.AllCells())
        {
            if (!TryCreateCellFromEntryText(address, text, out var newCell))
                return false;

            edits.Add((address, newCell));
        }

        if (edits.Count == 0)
            return false;

        return CommitPreparedEdits(
            edits,
            text,
            edits.Select(edit => edit.Address).ToList(),
            "Edit Selection");
    }

    private bool TryCreateCellFromEntryText(CellAddress addr, string text, out Cell newCell)
    {
        newCell = CellEntryParser.CreateCell(text, addr, _options.UseR1C1ReferenceStyle);

        if (newCell.Value is { } value)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            if (sheet != null)
            {
                var applicableRules = DataValidationService.GetApplicable(sheet, addr);
                DataValidation? violatingRule = null;
                string? violationMsg = null;
                foreach (var dv in applicableRules)
                {
                    var msg = DataValidationService.Validate(dv, value, sheet, addr, _workbook);
                    if (msg != null) { violatingRule = dv; violationMsg = msg; break; }
                }

                if (violationMsg != null && violatingRule != null)
                {
                    var dvRule = violatingRule;
                    var action = DataValidationService.GetInvalidEntryAction(dvRule);
                    if (action == DataValidationInvalidEntryAction.Block)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            MessageBoxButton.OK, icon);
                        RefreshValidationDropdown();
                        return false;
                    }

                    if (action == DataValidationInvalidEntryAction.AskToContinue)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        var buttons = dvRule.AlertStyle == DvAlertStyle.Information
                            ? MessageBoxButton.OKCancel
                            : MessageBoxButton.YesNo;
                        var result = MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            buttons, icon);
                        if (result is MessageBoxResult.No or MessageBoxResult.Cancel)
                        {
                            RefreshValidationDropdown();
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private bool CommitPreparedEdits(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string text,
        IReadOnlyList<CellAddress> fallbackAffectedCells,
        string title)
    {
        if (!TryExecuteEditCells(edits, title, out var outcome))
            return false;

        var affectedCells = outcome.AffectedCells ?? fallbackAffectedCells;
        if (text.StartsWith("="))
        {
            // For now, we manually register dependencies because we haven't automated this in the command yet.
            try
            {
                foreach (var affected in affectedCells)
                {
                    var formulaA1 = _options.UseR1C1ReferenceStyle
                        ? FormulaReferenceStyleService.ToA1(text.Substring(1), affected)
                        : text.Substring(1);
                    var lexer = new Lexer("=" + formulaA1);
                    var parser = new Parser(lexer.Tokenize());
                    var ast = parser.Parse();
                    _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, _workbook);
                }
            }
            catch
            {
                // Formula syntax is invalid; clear stale dependencies so this cell
                // does not incorrectly depend on previously-referenced cells.
                foreach (var affected in affectedCells)
                    _recalcEngine.ClearFormulaDependencies(affected);
            }
        }
        else
        {
            foreach (var affected in affectedCells)
                _recalcEngine.ClearFormulaDependencies(affected);
        }

        RecalculateIfAutomatic(affectedCells);
        UpdateViewport();
        RefreshStatusBar();
        RefreshValidationDropdown();
        return true;
    }

    private void UpdateTitleBar()
    {
        var groupSuffix = IsWorkbookGrouped() ? " [Group]" : "";
        var displayName = $"{_workbook.Name}{groupSuffix} - Freexcel";
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
    }

    private bool IsWorkbookGrouped()
        => SheetTabListPlanner.IsWorkbookGrouped(_workbook, _currentSheetId, _groupedSheetIds);

    // ── Start screen ─────────────────────────────────────────────────────────

    private bool? ShowOwnedDialog(Window dialog)
    {
        dialog.Owner = this;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.ShowActivated = true;
        Activate();
        return dialog.ShowDialog();
    }

    private MessageBoxResult ShowOwnedMessage(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        Activate();
        return MessageBox.Show(this, messageBoxText, caption, button, icon);
    }

    private bool TryHandleTopLevelRibbonKeyTip(string keyTip)
    {
        return RibbonTopLevelKeyTipRouter.Resolve(keyTip) switch
        {
            { Kind: RibbonTopLevelKeyTipActionKind.BackstageFile } => OpenFileBackstageFromKeyTip(),
            { Kind: RibbonTopLevelKeyTipActionKind.RibbonTab, RibbonTabHeader: { } header } => SelectRibbonTabByHeader(header),
            _ => false
        };
    }

    private bool SelectRibbonTabByHeader(string header)
    {
        if (RibbonTabs == null)
            return false;

        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem { Header: string tabHeader } &&
                string.Equals(tabHeader, header, StringComparison.OrdinalIgnoreCase))
            {
                RibbonTabs.SelectedItem = item;
                RibbonTabs.UpdateLayout();
                NormalizeRibbonSurface(forceCompact: true);
                return true;
            }
        }

        return false;
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

    private void InsertChartButton_Click(object sender, RoutedEventArgs e)
        => InsertChartOfType(ChartType.Column);

    private void InsertEmbeddedChart() => InsertChartOfType(ChartType.Column);

    private void InsertChartSheet()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        AddChartSheetCommand? command = null;
        IWorkbookCommand CreateCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            command = new AddChartSheetCommand(_currentSheetId, currentRange, ChartType.Column, "Chart");
            return command;
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Chart Sheet");
            return;
        }

        _repeatPostAction = null;
        if (command?.CreatedSheetId is { } createdSheetId)
        {
            _currentSheetId = createdSheetId;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
        }

        RefreshSheetTabs();
        UpdateViewport();
    }

    private void InsertChartOfType(ChartType type)
    {
        if (!ChartTypeSupport.IsRenderable(type))
        {
            ShowDeferredChartFamilyMessage();
            return;
        }

        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Chart",
                range,
                currentRange => new AddChartCommand(_currentSheetId, currentRange, type, "Chart")))
            return;

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
        var dialog = new SortDialog { Owner = this };
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

    // ── Group / Ungroup handlers ─────────────────────────────────────────────

    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand("Group", range, CreateGroupCommand))
            return;
        UpdateViewport();
    }

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Ungroup",
                range,
                currentRange => OutlineGroupingService.GetGroupingAxis(currentRange) == OutlineGroupingAxis.Columns
                    ? new GroupColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.End.Col, 0)
                    : new GroupRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.End.Row, 0)))
            return;

        UpdateViewport();
    }

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new CollapseColGroupCommand(_currentSheetId, 1)
                : new CollapseRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Collapse Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new ExpandColGroupCommand(_currentSheetId, 1)
                : new ExpandRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Expand Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private IWorkbookCommand CreateGroupCommand(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 1);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            int newLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, newLevel);
        }

        int rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, rowLevel);
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

    private void RefreshStatusBar()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = "Ready";
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var stats = StatusBarCalculator.Calculate(sheet, range);

        if (stats.Count == 0)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = StatusBarCalculator.GetReadyStatusText(sheet, range.Start);
            return;
        }

        StatusReadyText.Visibility  = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Visible;
        StatusAvgText.Text   = stats.Average.HasValue ? $"Average: {StatusBarCalculator.FormatNumber(stats.Average.Value)}" : "";
        StatusCountText.Text = $"Count: {stats.Count}";
        StatusSumText.Text   = $"Sum: {StatusBarCalculator.FormatNumber(stats.Sum)}";
        StatusMinText.Text   = stats.Min.HasValue ? $"Min: {StatusBarCalculator.FormatNumber(stats.Min.Value)}" : "";
        StatusMaxText.Text   = stats.Max.HasValue ? $"Max: {StatusBarCalculator.FormatNumber(stats.Max.Value)}" : "";
    }

    private (uint start, uint end) GetSelectedColRange(uint col)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && col >= sel.Value.Start.Col && col <= sel.Value.End.Col
            && sel.Value.Start.Col != sel.Value.End.Col)
            return (sel.Value.Start.Col, sel.Value.End.Col);
        return (col, col);
    }

    private (uint start, uint end) GetSelectedRowRange(uint row)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && row >= sel.Value.Start.Row && row <= sel.Value.End.Row
            && sel.Value.Start.Row != sel.Value.End.Row)
            return (sel.Value.Start.Row, sel.Value.End.Row);
        return (row, row);
    }

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = GetSelectedColRange(col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
        for (uint c = startCol; c <= endCol; c++)
            sheet.ColumnWidths[c] = newWidthPx / 8.0;
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { } snap
            ? (snap.StartCol, snap.EndCol)
            : GetSelectedColRange(col);
        RestoreColumnResizeSnapshot(sheet);
        _columnResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, startCol, endCol, newWidthPx / 8.0)))
            return;
        UpdateViewport();
    }

    private void OnRowResizing(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = GetSelectedRowRange(row);
        CaptureRowResizeSnapshot(sheet, startRow, endRow);
        for (uint r = startRow; r <= endRow; r++)
            sheet.RowHeights[r] = newHeightPx;
        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = _rowResizeSnapshot is { } snap
            ? (snap.StartRow, snap.EndRow)
            : GetSelectedRowRange(row);
        RestoreRowResizeSnapshot(sheet);
        _rowResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, startRow, endRow, newHeightPx)))
            return;
        UpdateViewport();
    }

    private void OnPageMarginsChanged(WorksheetPageMargins margins)
    {
        if (!TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, margins)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void CaptureColumnResizeSnapshot(Sheet sheet, uint startCol, uint endCol)
    {
        if (_columnResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartCol == startCol && existing.EndCol == endCol)
            return;

        var widths = new Dictionary<uint, (bool, double)>();
        for (uint c = startCol; c <= endCol; c++)
        {
            var had = sheet.ColumnWidths.TryGetValue(c, out var w);
            widths[c] = (had, w);
        }
        _columnResizeSnapshot = new ColumnResizeSnapshot(sheet.Id, startCol, endCol, widths);
    }

    private void RestoreColumnResizeSnapshot(Sheet sheet)
    {
        if (_columnResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (c, (had, w)) in snapshot.Widths)
        {
            if (had) sheet.ColumnWidths[c] = w;
            else sheet.ColumnWidths.Remove(c);
        }
    }

    private void CaptureRowResizeSnapshot(Sheet sheet, uint startRow, uint endRow)
    {
        if (_rowResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartRow == startRow && existing.EndRow == endRow)
            return;

        var heights = new Dictionary<uint, (bool, double)>();
        for (uint r = startRow; r <= endRow; r++)
        {
            var had = sheet.RowHeights.TryGetValue(r, out var h);
            heights[r] = (had, h);
        }
        _rowResizeSnapshot = new RowResizeSnapshot(sheet.Id, startRow, endRow, heights);
    }

    private void RestoreRowResizeSnapshot(Sheet sheet)
    {
        if (_rowResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (r, (had, h)) in snapshot.Heights)
        {
            if (had) sheet.RowHeights[r] = h;
            else sheet.RowHeights.Remove(r);
        }
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

    private void PivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || SheetGrid.SelectedRange is not { } sourceRange)
        {
            MessageBox.Show(
                "Select a source range with a header row before creating a PivotTable.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
        {
            MessageBox.Show(
                "PivotTable source data must include at least two columns and a header row.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotTableDialog(_workbook, _currentSheetId, sourceRange) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.SourceRangeText, out var dialogSourceRange))
        {
            MessageBox.Show("Enter a valid PivotTable source range.", "Insert PivotTable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sourceSheet = _workbook.GetSheet(dialogSourceRange.Start.Sheet) ?? sheet;
        var dataFieldIndex = PivotUiPlanner.ChooseDefaultDataField(sourceSheet, dialogSourceRange);
        var rowFieldIndex = dataFieldIndex == 0 ? 1 : 0;
        if (dialog.Result.DestinationKind == PivotTableDestinationKind.NewWorksheet)
        {
            var command = new AddPivotTableToNewWorksheetCommand(
                dialogSourceRange,
                PivotUiPlanner.GenerateUniquePivotTableName(sheet),
                rowFieldIndexes: [rowFieldIndex],
                dataFieldIndexes: [dataFieldIndex]);

            if (!TryExecuteCommand(command, "Insert PivotTable"))
                return;

            if (command.CreatedSheetId is { } createdSheetId)
            {
                _currentSheetId = createdSheetId;
                _groupedSheetIds.Clear();
                _groupedSheetIds.Add(_currentSheetId);
                SetActiveCell(new CellAddress(
                    _currentSheetId,
                    AddPivotTableToNewWorksheetCommand.InitialTargetRow,
                    AddPivotTableToNewWorksheetCommand.InitialTargetColumn));
            }

            RefreshSheetTabs();
            UpdateViewport();
            RefreshStatusBar();
            if (dialog.Result.OpenFieldList)
                RefreshPivotFieldListPane();
            return;
        }

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.DestinationRangeText, out var targetRange) ||
            targetRange.Start.Sheet != _currentSheetId)
        {
            MessageBox.Show("Enter a destination cell on the active worksheet.", "Insert PivotTable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = PivotUiPlanner.GenerateUniquePivotTableName(sheet);

        if (!TryExecuteCommand(
                new AddPivotTableCommand(
                    _currentSheetId,
                    dialogSourceRange,
                    targetRange,
                    name,
                    rowFieldIndexes: [rowFieldIndex],
                    dataFieldIndexes: [dataFieldIndex]),
                "Insert PivotTable"))
            return;

        UpdateViewport();
        if (dialog.Result.OpenFieldList)
            RefreshPivotFieldListPane();
    }

    private void RefreshPivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Refresh PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name), "Refresh PivotTable"))
            return;

        UpdateViewport();
    }

    private void PivotTableShowDetailsBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = TryShowPivotTableDetails(showMessage: true);
    }

    private bool TryShowPivotTableDetails(bool showMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selected = SheetGrid.SelectedRange?.Start;
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null || selected is null)
        {
            if (showMessage)
            {
                MessageBox.Show(
                    "Select a value cell inside an existing PivotTable before showing detail rows.",
                    "Show PivotTable Details",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        if (!TryExecuteCommand(
                new DrillDownPivotTableCommand(_currentSheetId, pivotTable.Name, selected.Value),
                "Show PivotTable Details"))
            return false;

        var detailSheet = _workbook.Sheets.LastOrDefault();
        if (detailSheet is not null)
            _currentSheetId = detailSheet.Id;
        RefreshSheetTabs();
        UpdateViewport();
        return true;
    }

    private void PivotChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Insert PivotChart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column, $"{pivotTable.Name} Chart"),
                "Insert PivotChart"))
            return;

        UpdateViewport();
    }

    private void PivotChartChangeTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before changing a PivotChart type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chart = sheet.Charts.FirstOrDefault(item =>
            item.IsPivotChart &&
            string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase));
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a PivotChart connected to this PivotTable before changing its type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangePivotChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), "Change PivotChart Type"))
            return;

        UpdateViewport();
    }

    private void PivotChartOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before changing PivotChart options.",
                "PivotChart Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a PivotChart connected to this PivotTable before changing its options.",
                "PivotChart Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotChartOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(
                new ConfigurePivotChartOptionsCommand(
                    _currentSheetId,
                    chart.Id,
                    dialog.Result.ChartStyleId,
                    dialog.Result.ShowFieldButtons),
                "PivotChart Options"))
            return;

        UpdateViewport();
    }

    private static ChartModel? FindPivotChartForPivotTable(Sheet sheet, PivotTableModel pivotTable) =>
        sheet.Charts.FirstOrDefault(item =>
            item.IsPivotChart &&
            string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase));

    private void OnPivotChartFieldButtonRequested(ChartModel chart, string fieldButton, System.Windows.Point position)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || !chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        _pivotChartContextFieldCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivotTable, headers, fieldButton);
        if (string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return;

        SetActiveCell(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => _pivotChartContextFieldCaption = null;
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private ContextMenu CreatePivotFieldContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += handler;
            menu.Items.Add(item);
        }

        Add("Sort A to Z", PivotFieldSortAscendingMenuItem_Click);
        Add("Sort Z to A", PivotFieldSortDescendingMenuItem_Click);
        Add("Select Items...", PivotFieldSelectItemsMenuItem_Click);
        Add("Label Filter...", PivotFieldLabelFilterMenuItem_Click);
        Add("Value Filter...", PivotFieldValueFilterMenuItem_Click);
        Add("Clear Filter", PivotFieldClearFilterMenuItem_Click);
        menu.Items.Add(new Separator());
        Add("Value Field Settings...", PivotFieldValueSettingsMenuItem_Click);
        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }

    private void RefreshPivotFieldListPane()
    {
        if (PivotFieldListPane is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
        {
            PivotFieldListPane.Visibility = Visibility.Collapsed;
            SetPivotContextualTabsVisible(false);
            PivotAvailableFieldsList.ItemsSource = null;
            PivotRowsList.ItemsSource = null;
            PivotColumnsList.ItemsSource = null;
            PivotFiltersList.ItemsSource = null;
            PivotValuesList.ItemsSource = null;
            return;
        }

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var displayedLayout = GetDisplayedPivotLayout(pivotTable);
        var rowFields = displayedLayout?.RowFields ?? pivotTable.RowFields;
        var columnFields = displayedLayout?.ColumnFields ?? pivotTable.ColumnFields;
        var pageFields = displayedLayout?.PageFields ?? pivotTable.PageFields;
        var dataFields = displayedLayout?.DataFields ?? pivotTable.DataFields;

        _pivotFieldListAvailableItems = headers
            .Select((caption, index) => new PivotFieldListItem(
                caption,
                rowFields.Any(field => field.SourceFieldIndex == index) ||
                columnFields.Any(field => field.SourceFieldIndex == index) ||
                pageFields.Any(field => field.SourceFieldIndex == index) ||
                dataFields.Any(field => field.SourceFieldIndex == index)))
            .ToList();
        ApplyPivotAvailableFieldFilter();
        PivotRowsList.ItemsSource = rowFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotColumnsList.ItemsSource = columnFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotFiltersList.ItemsSource = pageFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotValuesList.ItemsSource = dataFields
            .Select(field => field.Name)
            .ToList();
        PivotFieldListUpdateBtn.IsEnabled = _pendingPivotLayout is not null;
        PivotFieldListPane.Visibility = Visibility.Visible;
        SetPivotContextualTabsVisible(true);
    }

    private void RefreshSlicerTimelinePane()
    {
        if (SlicerTimelinePane is null)
            return;

        var slicers = _workbook.Slicers
            .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name))
            .Select(slicer => new SlicerPaneItem(
                slicer.Name,
                slicer.SourceFieldName ?? slicer.CacheName,
                BuildSlicerTiles(slicer)))
            .ToList();
        var timelines = _workbook.Timelines
            .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name))
            .Select(SlicerTimelinePlanner.BuildTimelineItem)
            .ToList();

        SlicerItemsControl.ItemsSource = slicers;
        TimelineItemsControl.ItemsSource = timelines;
        if (slicers.Count == 0 && timelines.Count == 0)
        {
            SlicerTimelinePane.Visibility = Visibility.Collapsed;
            _slicerTimelinePaneDismissed = false;
        }
        else if (!_slicerTimelinePaneDismissed)
            SlicerTimelinePane.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer)
    {
        return SlicerTimelinePlanner.BuildSlicerTiles(slicer, ReadSlicerSourceItems(slicer));
    }

    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return [];
        }

        foreach (var sheet in _workbook.Sheets)
        {
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase));
            if (pivotTable is null)
                continue;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, slicer.SourceFieldName);
            return sourceIndex is null ? [] : ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value);
        }

        return [];
    }

    private void SlicerTimelinePaneCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _slicerTimelinePaneDismissed = true;
        SlicerTimelinePane.Visibility = Visibility.Collapsed;
    }

    private void SlicerTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SlicerTileItem tile })
            return;

        var slicer = _workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, tile.SlicerName, StringComparison.OrdinalIgnoreCase));
        if (slicer is null)
            return;

        var allItems = ReadSlicerSourceItems(slicer).ToList();
        var selected = SlicerTimelinePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, tile.Caption);

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicer.Name, selected.ToList()), "Slicer"))
            return;

        UpdateViewport();
    }

    private void SlicerClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slicerName })
            return;

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, []), "Slicer"))
            return;

        UpdateViewport();
    }

    private void TimelineApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(
                new SetTimelineRangeCommand(
                    item.Name,
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedStartDate),
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedEndDate)),
                "Timeline"))
            return;

        UpdateViewport();
    }

    private void TimelineClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(new SetTimelineRangeCommand(item.Name, null, null), "Timeline"))
            return;

        UpdateViewport();
    }

    private void SetPivotContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (PivotTableAnalyzeTab is not null)
            PivotTableAnalyzeTab.Visibility = visibility;
        if (PivotTableDesignTab is not null)
            PivotTableDesignTab.Visibility = visibility;
    }

    private void PivotFieldListBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before showing the field list.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PivotFieldListPane.Visibility = PivotFieldListPane.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (PivotFieldListPane.Visibility == Visibility.Visible)
            RefreshPivotFieldListPane();
    }

    private void PivotChangeDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var dialog = new PivotTableDataSourceDialog(FormatWorkbookRange(pivotTable.SourceRange)) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.SourceRangeText) ||
            !TryParseWorkbookRange(sheet.Id, dialog.Result.SourceRangeText, out var sourceRange))
            return;

        if (!TryExecuteCommand(
                new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange),
                "Change PivotTable Data Source"))
            return;

        UpdateViewport();
    }

    private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var dialog = new InsertSlicerDialog(headers, fieldName) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.FieldName) ||
            string.IsNullOrWhiteSpace(dialog.Result.SlicerName))
            return;

        if (!TryExecuteCommand(new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName), "Insert Slicer"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private void PivotInsertTimelineBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var dialog = new InsertTimelineDialog(headers, fieldName) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.DateFieldName) ||
            string.IsNullOrWhiteSpace(dialog.Result.TimelineName))
            return;

        if (!TryExecuteCommand(new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName), "Insert Timeline"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotBlankRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                !pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotRowHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                !pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotColumnHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                !pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                !pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                !pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void ApplyPivotOptions(
        PivotTableModel pivotTable,
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableOptionsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    showRowGrandTotals,
                    showColumnGrandTotals,
                    showSubtotals,
                    subtotalPlacement,
                    repeatItemLabels,
                    blankLineAfterItems,
                    styleName,
                    showRowHeaders,
                    showColumnHeaders,
                    showRowStripes,
                    showColumnStripes,
                    reportLayout),
                "PivotTable Options"))
            return;

        UpdateViewport();
    }

    private void ShowPivotTableOptionsDialog()
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var dialog = new PivotTableOptionsDialog(pivotTable) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotOptions(pivotTable, dialog.Result);
    }

    private void PivotGroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        var currentField = PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value);
        var dialog = new PivotFieldGroupingDialog(headers, currentField) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotGroupingResult(pivotTable, dialog.Result);
    }

    private void PivotUngroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        ApplyPivotGroupingResult(
            pivotTable,
            PivotFieldGroupingDialog.CreateResult(
                PivotUiPlanner.FieldCaption(headers, sourceIndex.Value),
                sourceIndex.Value,
                PivotFieldGrouping.None,
                groupStart: null,
                groupEnd: null,
                groupInterval: null,
                ungroup: true));
    }

    private void PivotCalculatedFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var dialog = new PivotCalculatedFieldDialog { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedFields = pivotTable.CalculatedFields
            .Where(field => !string.Equals(field.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            calculatedFields,
            pivotTable.CalculatedItems.ToList());
    }

    private void PivotCalculatedItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable) ?? 0;
        var dialog = new PivotCalculatedItemDialog(headers, sourceIndex) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedItems = pivotTable.CalculatedItems
            .Where(item =>
                item.SourceFieldIndex != dialog.Result.SourceFieldIndex ||
                !string.Equals(item.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.CalculatedFields.ToList(),
            calculatedItems);
    }

    private void ApplyPivotGroupingResult(PivotTableModel pivotTable, PivotFieldGroupingDialogResult result)
    {
        var groupedField = new PivotFieldModel(
            result.SourceFieldIndex,
            Grouping: result.Ungroup ? PivotFieldGrouping.None : result.Grouping,
            GroupStart: result.Ungroup ? null : result.GroupStart,
            GroupEnd: result.Ungroup ? null : result.GroupEnd,
            GroupInterval: result.Ungroup ? null : result.GroupInterval);
        var fieldAlreadyInLayout =
            pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex == groupedField.SourceFieldIndex);
        var rowFields = fieldAlreadyInLayout
            ? ReplacePivotField(pivotTable.RowFields, groupedField)
            : pivotTable.RowFields.Append(groupedField).ToList();
        var columnFields = ReplacePivotField(pivotTable.ColumnFields, groupedField);
        var pageFields = ReplacePivotField(pivotTable.PageFields, groupedField);

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            rowFields,
            columnFields,
            pageFields,
            pivotTable.CalculatedFields.ToList(),
            pivotTable.CalculatedItems.ToList());
    }

    private void ApplyPivotAdvancedConfiguration(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableCalculatedItemsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    rowFields,
                    columnFields,
                    pageFields,
                    calculatedFields,
                    calculatedItems),
                "PivotTable Calculations"))
            return;

        _pendingPivotLayout = null;
        RefreshPivotFieldListPane();
        UpdateViewport();
    }

    private int? ResolveSelectedPivotSourceField(IReadOnlyList<string> headers, PivotTableModel pivotTable)
    {
        var selected = GetSelectedPivotFieldListItem();
        return PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, selected ?? "")
               ?? pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                   .FirstOrDefault()
                   ?.SourceFieldIndex;
    }

    private static List<PivotFieldModel> ReplacePivotField(
        IReadOnlyList<PivotFieldModel> fields,
        PivotFieldModel replacement) =>
        fields
            .Select(field => field.SourceFieldIndex == replacement.SourceFieldIndex
                ? replacement
                : field)
            .ToList();

    private void ApplyPivotOptions(PivotTableModel pivotTable, PivotTableOptionsDialogResult result) =>
        ApplyPivotOptions(
            pivotTable,
            result.ShowRowGrandTotals,
            result.ShowColumnGrandTotals,
            result.ShowSubtotals,
            result.SubtotalPlacement,
            result.RepeatItemLabels,
            result.BlankLineAfterItems,
            result.StyleName,
            result.ShowRowHeaders,
            result.ShowColumnHeaders,
            result.ShowRowStripes,
            result.ShowColumnStripes,
            result.ReportLayout);

    private bool TryGetActivePivotTable(out Sheet sheet, out PivotTableModel pivotTable)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        pivotTable = sheet is null ? null! : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)!;
        return sheet is not null && pivotTable is not null;
    }

    private void PivotFieldListCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        PivotFieldListPane.Visibility = Visibility.Collapsed;
    }

    private void PivotFieldToRowsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Rows);

    private void PivotFieldToColumnsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Columns);

    private void PivotFieldToValuesBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Values);

    private void PivotFieldToFiltersBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Filters);

    private void PivotFieldList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not ListBox list ||
            PivotUiPlanner.GetFieldListCaption(list.SelectedItem) is not { } caption)
        {
            return;
        }

        DragDrop.DoDragDrop(list, caption, DragDropEffects.Move);
    }

    private void PivotFieldList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox targetList ||
            e.Data.GetData(DataFormats.StringFormat) is not string caption ||
            GetPivotFieldDropZone(targetList) is not { } targetZone)
        {
            return;
        }

        MovePivotFieldToZone(caption, targetZone, targetList.SelectedIndex);
        e.Handled = true;
    }

    private void PivotAvailableFieldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PivotFieldListItem item } checkBox)
            return;

        TogglePivotAvailableField(item.Caption, checkBox.IsChecked == true);
    }

    private void TogglePivotAvailableField(string caption, bool isChecked)
    {
        if (isChecked)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
            if (sheet is null || pivotTable is null)
                return;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
            if (sourceIndex is null)
                return;

            var zone = PivotUiPlanner.IsNumericSourceField(sheet, pivotTable, sourceIndex.Value)
                ? PivotFieldDropZone.Values
                : PivotFieldDropZone.Rows;
            MovePivotFieldToZone(caption, zone, -1);
            return;
        }

        MovePivotFieldToZone(caption, PivotFieldDropZone.Available, -1);
    }

    private void PivotFieldRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        if (string.IsNullOrWhiteSpace(selected))
            return;

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var rowFields = sourceIndex is null
            ? pivotTable.RowFields.ToList()
            : pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = sourceIndex is null
            ? pivotTable.ColumnFields.ToList()
            : pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = sourceIndex is null
            ? pivotTable.PageFields.ToList()
            : pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, selected, StringComparison.CurrentCultureIgnoreCase) &&
                            (sourceIndex is null || field.SourceFieldIndex != sourceIndex.Value))
            .ToList();

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void PivotFieldSortAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Ascending);

    private void PivotFieldSortDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Descending);

    private void PivotFieldClearFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);

        var labelFilters = sourceIndex is null
            ? pivotTable.LabelFilters.ToList()
            : pivotTable.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var valueFilters = sourceIndex is null
            ? pivotTable.ValueFilters.ToList()
            : pivotTable.ValueFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        ApplyPivotFieldView(pivotTable, labelFilters, valueFilters, sorts);
    }

    private void PivotFieldSelectItemsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var existingItems = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceIndex.Value)
            ?.SelectedItems;
        var dialog = new PivotFieldFilterDialog(ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value), existingItems)
        {
            Owner = this,
            Title = $"{PivotUiPlanner.FieldCaption(headers, sourceIndex.Value)} Filter"
        };
        if (dialog.ShowDialog() != true)
            return;

        var allItems = ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value).ToList();
        var selectedItems = dialog.SelectedItems;
        var items = selectedItems.Count == 0 || selectedItems.Count == allItems.Count ? null : selectedItems;
        var rowFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.RowFields, sourceIndex.Value, items);
        var columnFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.ColumnFields, sourceIndex.Value, items);
        var pageFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.PageFields, sourceIndex.Value, items);

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, pivotTable.DataFields.ToList());
    }

    private void PivotFieldLabelFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotLabelFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var labelFilters = pivotTable.LabelFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, labelFilters, pivotTable.ValueFilters.ToList(), pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null || pivotTable.DataFields.Count == 0)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotValueFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var valueFilters = pivotTable.ValueFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), valueFilters, pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (dataFieldIndex is null)
        {
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
            if (sourceIndex is null)
                return;
            dataFieldIndex = pivotTable.DataFields.FindIndex(field => field.SourceFieldIndex == sourceIndex.Value);
            if (dataFieldIndex < 0)
                return;
        }

        var current = pivotTable.DataFields[dataFieldIndex.Value];
        var dialog = new PivotValueFieldSettingsDialog(current, headers) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var dataFields = pivotTable.DataFields.ToList();
        dataFields[dataFieldIndex.Value] = dialog.ResultDataField;

        ApplyPivotFieldListLayout(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            dataFields);
    }

    private void MoveSelectedPivotField(PivotFieldDropZone zone)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        if (sourceIndex is null)
            return;

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields.ToList();
        var field = new PivotFieldModel(sourceIndex.Value);

        switch (zone)
        {
            case PivotFieldDropZone.Rows:
                rowFields.Add(field);
                break;
            case PivotFieldDropZone.Columns:
                columnFields.Add(field);
                break;
            case PivotFieldDropZone.Filters:
                pageFields.Add(field);
                break;
            case PivotFieldDropZone.Values:
                if (dataFields.All(dataField => dataField.SourceFieldIndex != sourceIndex.Value))
                {
                    dataFields.Add(PivotUiPlanner.CreateDefaultDataField(sheet, pivotTable, headers, sourceIndex.Value));
                }
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void MovePivotFieldToZone(string caption, PivotFieldDropZone targetZone, int insertIndex)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, caption);
        var draggedDataField = pivotTable.DataFields.FirstOrDefault(field =>
            string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase));
        if (sourceIndex is null && draggedDataField is null)
            return;

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase) &&
                            field.SourceFieldIndex != sourceIndex)
            .ToList();

        if (targetZone == PivotFieldDropZone.Available)
        {
            ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
            return;
        }

        if (sourceIndex is null)
            return;

        switch (targetZone)
        {
            case PivotFieldDropZone.Rows:
                PivotUiPlanner.InsertOrAppend(rowFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Columns:
                PivotUiPlanner.InsertOrAppend(columnFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Filters:
                PivotUiPlanner.InsertOrAppend(pageFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Values:
                var valueField = draggedDataField ?? PivotUiPlanner.CreateDefaultDataField(sheet, pivotTable, headers, sourceIndex.Value);
                PivotUiPlanner.InsertOrAppend(dataFields, valueField, insertIndex);
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void ApplyPivotFieldSort(PivotSortDirection direction)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (sourceIndex is null && dataFieldIndex is null)
            return;

        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        if (dataFieldIndex is not null)
        {
            sorts.Add(new PivotSortModel(
                PivotSortTarget.Value,
                direction,
                DataFieldIndex: dataFieldIndex.Value,
                FieldIndex: pivotTable.RowFields.LastOrDefault()?.SourceFieldIndex ??
                            pivotTable.ColumnFields.LastOrDefault()?.SourceFieldIndex ??
                            0));
        }
        else
        {
            sorts.Add(new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: sourceIndex.GetValueOrDefault()));
        }

        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), pivotTable.ValueFilters.ToList(), sorts);
    }

    private void ApplyPivotFieldListLayout(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        bool forceApply = false)
    {
        if (dataFields.Count == 0)
        {
            MessageBox.Show(
                "A PivotTable requires at least one value field.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!forceApply && PivotFieldListDeferLayoutCheckBox.IsChecked == true)
        {
            _pendingPivotLayout = new PendingPivotLayout(
                pivotTable.Name,
                rowFields.ToList(),
                columnFields.ToList(),
                pageFields.ToList(),
                dataFields.ToList());
            RefreshPivotFieldListPane();
            return;
        }

        if (!TryExecuteCommand(
                new ConfigurePivotTableLayoutCommand(_currentSheetId, pivotTable.Name, rowFields, columnFields, pageFields, dataFields),
                "PivotTable Fields"))
            return;

        _pendingPivotLayout = null;
        UpdateViewport();
    }

    private void ApplyPivotFieldView(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableViewCommand(_currentSheetId, pivotTable.Name, labelFilters, valueFilters, sorts),
                "PivotTable Field"))
            return;

        UpdateViewport();
    }

    private string? GetSelectedPivotFieldListItem()
    {
        if (!string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return _pivotChartContextFieldCaption;

        foreach (var list in new[] { PivotAvailableFieldsList, PivotRowsList, PivotColumnsList, PivotValuesList, PivotFiltersList })
        {
            if (PivotUiPlanner.GetFieldListCaption(list.SelectedItem) is { } value)
                return value;
        }

        return null;
    }

    private Sheet GetPivotSourceSheet(Sheet fallbackSheet, PivotTableModel pivotTable) =>
        _workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? fallbackSheet;

    private List<string> ReadPivotSourceHeaders(Sheet sheet, PivotTableModel pivotTable)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var headers = new List<string>();
        var start = pivotTable.SourceRange.Start;
        for (var col = start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var caption = SpreadsheetDisplayFormatter.FormatCellValue(sourceSheet.GetValue(start.Row, col)).Trim();
            headers.Add(string.IsNullOrWhiteSpace(caption) ? $"Column {headers.Count + 1}" : caption);
        }

        return headers;
    }

    private IReadOnlyList<string> ReadPivotFieldItems(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        var values = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            var text = SpreadsheetDisplayFormatter.FormatCellValue(sourceSheet.GetValue(row, sourceColumn)).Trim();
            values.Add(string.IsNullOrWhiteSpace(text) ? "(blank)" : text);
        }

        return values.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private bool TryParseWorkbookRange(SheetId defaultSheetId, string input, out GridRange range)
        => WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            input,
            sheetName => _workbook.Sheets.FirstOrDefault(item =>
                string.Equals(item.Name, sheetName, StringComparison.CurrentCultureIgnoreCase))?.Id,
            out range);

    private string FormatWorkbookRange(GridRange range)
        => WorkbookRangeTextCodec.Format(
            range,
            _currentSheetId,
            sheetId => _workbook.GetSheet(sheetId)?.Name);

    private PivotFieldDropZone? GetPivotFieldDropZone(ListBox list)
    {
        if (ReferenceEquals(list, PivotRowsList))
            return PivotFieldDropZone.Rows;
        if (ReferenceEquals(list, PivotColumnsList))
            return PivotFieldDropZone.Columns;
        if (ReferenceEquals(list, PivotFiltersList))
            return PivotFieldDropZone.Filters;
        if (ReferenceEquals(list, PivotValuesList))
            return PivotFieldDropZone.Values;
        if (ReferenceEquals(list, PivotAvailableFieldsList))
            return PivotFieldDropZone.Available;
        return null;
    }

    private enum PivotFieldDropZone
    {
        Available,
        Rows,
        Columns,
        Values,
        Filters
    }

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertChartDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        InsertChartOfType(dialog.Result.ChartType);
    }

    private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Change Chart Type", out var chart))
            return;

        var dialog = new ChangeChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), "Change Chart Type"))
            return;

        UpdateViewport();
    }

    private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Select Data Source", out var chart))
            return;

        var dialog = new SelectDataSourceDialog(
            FormatRangeReference(chart.DataRange.Start, chart.DataRange.End),
            chart.FirstColIsCategories)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, out var dataRange))
        {
            MessageBox.Show("Enter a valid chart data range.", "Select Data Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(
                new ChangeChartSourceCommand(
                    _currentSheetId,
                    chart.Id,
                    dataRange,
                    firstRowIsHeader: chart.FirstRowIsHeader,
                    firstColIsCategories: dialog.Result.FirstColumnIsCategories),
                "Select Data Source"))
            return;

        UpdateViewport();
    }

    private void MoveChartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Move Chart", out var chart))
            return;

        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is null)
            return;

        var dialog = new MoveChartDialog(currentSheet.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.Result.TargetKind == MoveChartTargetKind.NewChartSheet)
        {
            if (!TryExecuteCommand(new MoveChartToNewSheetCommand(_currentSheetId, chart.Id, dialog.Result.TargetName), "Move Chart"))
                return;

            var createdSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (createdSheet is not null)
                _currentSheetId = createdSheet.Id;
        }
        else
        {
            var targetSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (targetSheet is null)
            {
                MessageBox.Show("Target sheet was not found.", "Move Chart", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryExecuteCommand(new MoveChartCommand(_currentSheetId, chart.Id, targetSheet.Id), "Move Chart"))
                return;

            _currentSheetId = targetSheet.Id;
        }

        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void ChartStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Chart Styles", "Insert or select a chart before choosing a chart style.", out var chart))
            return;

        var dialog = new ChartStyleDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new SetChartStyleCommand(_currentSheetId, chart.Id, dialog.Result.ChartStyleId), "Chart Styles"))
            return;

        UpdateViewport();
    }

    private void FormatChartAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Chart Area", "Insert or select a chart before formatting the chart area.", out var chart))
            return;

        var dialog = new ChartAreaLegendDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Chart Area", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private bool TryGetActiveNormalChart(string caption, out ChartModel chart)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        chart = sheet?.Charts.FirstOrDefault(item => !item.IsPivotChart) ?? null!;
        if (chart is not null)
            return true;

        MessageBox.Show(
            "Insert or select a chart before using this command.",
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Column);
    private void ChartStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedColumn);
    private void ChartPercentStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedColumn);
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Line);
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Pie);
    private void ChartDoughnutMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Doughnut);
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Bar);
    private void ChartStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedBar);
    private void ChartPercentStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedBar);
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Area);
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Scatter);
    private void ChartBubbleMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Bubble);
    private void ChartRadarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Radar);
    private void ChartStockMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Stock);
    private void DeferredChartFamilyMenuItem_Click(object sender, RoutedEventArgs e) => ShowDeferredChartFamilyMessage();

    private static void ShowDeferredChartFamilyMessage() =>
        MessageBox.Show(
            "This chart family is retained when opening XLSX files, but authoring and rendering are deferred until its data model and renderer are implemented.",
            "Chart family deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "First Slice Angle",
                "Insert or select a pie or doughnut chart before changing first-slice angle.",
                chart => chart.Type is ChartType.Pie or ChartType.Doughnut,
                "First-slice angle only applies to pie and doughnut charts.",
                chart => new ChartLayoutOptions(FirstSliceAngle: chart.FirstSliceAngle >= 270 ? 0 : chart.FirstSliceAngle + 90)))
            return;

        UpdateViewport();
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Doughnut Hole Size",
                "Insert or select a doughnut chart before changing hole size.",
                chart => chart.Type == ChartType.Doughnut,
                "Doughnut hole size only applies to doughnut charts.",
                chart => new ChartLayoutOptions(
                    DoughnutHoleSize: chart.DoughnutHoleSize switch
                    {
                        < 0.45 => 0.55,
                        < 0.7 => 0.75,
                        _ => 0.35
                    })))
            return;

        UpdateViewport();
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Explode Slice",
                "Insert or select a pie or doughnut chart before exploding a slice.",
                chart => chart.Type is (ChartType.Pie or ChartType.Doughnut) && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Exploded slices require a pie or doughnut chart with chart data.",
                chart =>
                {
                    var sliceCount = ChartTypeSupport.GetDataPointCount(chart);
                    var nextIndex = chart.ExplodedSliceIndex < 0
                        ? 0
                        : chart.ExplodedSliceIndex + 1 >= sliceCount ? -1 : chart.ExplodedSliceIndex + 1;
                    var nextDistance = nextIndex < 0
                        ? 0.1
                        : chart.ExplodedSliceDistance >= 0.22 ? 0.1 : chart.ExplodedSliceDistance + 0.06;
                    return new ChartLayoutOptions(ExplodedSliceIndex: nextIndex, ExplodedSliceDistance: nextDistance);
                }))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ShowChartDataLabelsDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Labels", "Insert or select a chart before changing data labels.", out var chart))
            return;

        var dialog = new ChartDataLabelsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Labels", chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ChartDataLabelCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Category Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCategoryName: !chart.ShowDataLabelCategoryName));
    }

    private void ChartDataLabelSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Series Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelSeriesName: !chart.ShowDataLabelSeriesName));
    }

    private void ChartDataLabelPercentageBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Percentage",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: !chart.ShowDataLabelPercentage));
    }

    private void ChartDataLabelSeparatorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Separator",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelSeparator: chart.DataLabelSeparator switch
                {
                    ChartDataLabelSeparator.Comma => ChartDataLabelSeparator.Semicolon,
                    ChartDataLabelSeparator.Semicolon => ChartDataLabelSeparator.NewLine,
                    ChartDataLabelSeparator.NewLine => ChartDataLabelSeparator.Space,
                    _ => ChartDataLabelSeparator.Comma
                }));
    }

    private void ChartDataLabelNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Number Format",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelNumberFormat: ChartOptionCycler.NextDataLabelNumberFormat(chart.DataLabelNumberFormat)));
    }

    private void ChartDataLabelCalloutBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Callout",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCallouts: !chart.ShowDataLabelCallouts));
    }

    private void ChartDataLabelFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Fill",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFillColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelFillColor)));
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Text",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelTextColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelTextColor)));
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Border",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelBorderColor: ChartOptionCycler.NextSeriesColor(chart.DataLabelBorderColor),
                DataLabelBorderThickness: chart.DataLabelBorderThickness >= 3 ? 0.75 : chart.DataLabelBorderThickness + 0.75));
    }

    private void ChartDataLabelSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Size",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFontSize: chart.DataLabelFontSize >= 16 ? 9 : chart.DataLabelFontSize + 1));
    }

    private void ChartDataLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Angle",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelAngle: ChartOptionCycler.NextAxisLabelAngle(chart.DataLabelAngle)));
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        const string caption = "Format Data Point Label";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing point data-label formatting.",
                chart => ChartOptionCycler.GetSeriesCount(chart) > 0 && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Add chart data points before changing point data-label formatting.",
                chart =>
                {
                    var formats = chart.PointDataLabelFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0 && format.PointIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartPointDataLabelFormat(0, 0);
                    var updated = current with
                    {
                        FillColor = ChartOptionCycler.NextSeriesColor(current.FillColor),
                        BorderColor = ChartOptionCycler.NextSeriesColor(current.BorderColor ?? current.FillColor),
                        BorderThickness = current.BorderThickness is null or >= 3 ? 0.75 : current.BorderThickness.Value + 0.75,
                        TextColor = ChartOptionCycler.NextSeriesColor(current.TextColor),
                        FontSize = current.FontSize is null or >= 16 ? 9 : current.FontSize.Value + 1
                    };
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(
                        ShowDataLabels: true,
                        PointDataLabelFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Area Fill",
            chart => new ChartLayoutOptions(ChartAreaFillColor: ChartOptionCycler.NextSeriesColor(chart.ChartAreaFillColor)));
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Color",
            chart => new ChartLayoutOptions(ChartTitleTextColor: ChartOptionCycler.NextSeriesColor(chart.ChartTitleTextColor)));
    }

    private void ChartTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Size",
            chart => new ChartLayoutOptions(ChartTitleFontSize: chart.ChartTitleFontSize >= 24 ? 12 : chart.ChartTitleFontSize + 2));
    }

    private void ChartAxisTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Color",
            chart => new ChartLayoutOptions(AxisTitleTextColor: ChartOptionCycler.NextSeriesColor(chart.AxisTitleTextColor)));
    }

    private void ChartAxisTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Size",
            chart => new ChartLayoutOptions(AxisTitleFontSize: chart.AxisTitleFontSize >= 18 ? 9 : chart.AxisTitleFontSize + 1));
    }

    private void ChartPlotAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Fill",
            chart => new ChartLayoutOptions(PlotAreaFillColor: ChartOptionCycler.NextSeriesColor(chart.PlotAreaFillColor)));
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Border",
            chart => new ChartLayoutOptions(
                PlotAreaBorderColor: ChartOptionCycler.NextSeriesColor(chart.PlotAreaBorderColor),
                PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3 ? 1 : chart.PlotAreaBorderThickness + 0.75));
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Text",
            chart => new ChartLayoutOptions(LegendTextColor: ChartOptionCycler.NextSeriesColor(chart.LegendTextColor)));
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Fill",
            chart => new ChartLayoutOptions(LegendFillColor: ChartOptionCycler.NextSeriesColor(chart.LegendFillColor)));
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Border",
            chart => new ChartLayoutOptions(
                LegendBorderColor: ChartOptionCycler.NextSeriesColor(chart.LegendBorderColor),
                LegendBorderThickness: chart.LegendBorderThickness >= 3 ? 0.75 : chart.LegendBorderThickness + 0.75));
    }

    private void ChartLegendSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Font Size",
            chart => new ChartLayoutOptions(LegendFontSize: chart.LegendFontSize >= 16 ? 9 : chart.LegendFontSize + 1));
    }

    private void ChartLegendOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Overlay",
            chart => new ChartLayoutOptions(ShowLegend: true, LegendOverlay: !chart.LegendOverlay));
    }

    private void ToggleDataLabelOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing data label options.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ToggleChartAreaOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing chart area formatting.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ShowChartTrendlineDialog()
    {
        if (!TryGetFirstChartForDialog("Format Trendline", "Insert or select a chart before changing trendlines.", out var chart))
            return;

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            ShowCommandError(new CommandOutcome(false, "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts."), "Format Trendline");
            return;
        }

        var dialog = new ChartTrendlineOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Trendline", chart, dialog.Result.ToOptions());
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Moving Average Period",
                "Insert or select a chart before changing moving-average period.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.MovingAverage,
                    TrendlinePeriod: chart.TrendlinePeriod >= 6 ? 2 : chart.TrendlinePeriod + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Polynomial Order",
                "Insert or select a chart before changing polynomial order.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.Polynomial,
                    TrendlineOrder: chart.TrendlineOrder >= 6 ? 2 : chart.TrendlineOrder + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineEquationBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Equation",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineEquation: !chart.ShowTrendlineEquation));
    }

    private void ChartTrendlineRSquaredBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "R-squared",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineRSquared: !chart.ShowTrendlineRSquared));
    }

    private void ChartTrendlineColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Color",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineColor: ChartOptionCycler.NextTrendlineColor(chart.TrendlineColor)));
    }

    private void ChartTrendlineDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Dash",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineDashStyle: chart.TrendlineDashStyle switch
                {
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => ChartLineDashStyle.Dash
                }));
    }

    private void ChartTrendlineWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Width",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineThickness: chart.TrendlineThickness >= 3 ? 1.5 : chart.TrendlineThickness + 0.75));
    }

    private void ChartErrorBarsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Error Bars", "Insert or select a chart before changing error bars.", out var chart))
            return;

        var dialog = new ChartErrorBarsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Error Bars", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private void ToggleTrendlineInfo(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing trendline information.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendline information is currently supported for column, line, bar, scatter, bubble, and area charts.",
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis",
                "Insert or select a chart before changing secondary axes.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) &&
                         (chart.ShowSecondaryAxis || ChartOptionCycler.GetSeriesCount(chart) >= 2),
                "Secondary value axes require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    ShowSecondaryAxis: !chart.ShowSecondaryAxis,
                    SecondaryAxisSeriesIndexes: [])))
            return;

        UpdateViewport();
    }

    private void ChartXAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: true);
    }

    private void ChartYAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: false);
    }

    private void ChartXAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: true);
    }

    private void ChartYAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: false);
    }

    private void ChartXAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ChartXAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: true);
    }

    private void ChartYAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: false);
    }

    private void ChartXAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: true);
    }

    private void ChartXAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: true);
    }

    private void ChartYAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: false);
    }

    private void ChartYAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: false);
    }

    private void ChartXAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: true);
    }

    private void ChartYAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartAxisFormatDialog(useXAxis: false);
    }

    private void ShowChartAxisFormatDialog(bool useXAxis)
    {
        var caption = useXAxis ? "Format X Axis" : "Format Y Axis";
        if (!TryGetFirstChartForDialog(caption, "Insert or select a chart before changing axis options.", out var chart))
            return;

        var dialog = new ChartAxisFormatDialog(chart, useXAxis) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(useXAxis ? "Format X Axis" : "Format Y Axis", chart, dialog.Result.ToOptions());
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Ticks" : "Y Axis Ticks";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis ticks.",
                null,
                null,
                chart =>
                {
                    var (major, minor) = useXAxis
                        ? ChartOptionCycler.NextAxisTickState(chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle)
                        : ChartOptionCycler.NextAxisTickState(chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisMajorTickStyle: major, XAxisMinorTickStyle: minor)
                        : new ChartLayoutOptions(YAxisMajorTickStyle: major, YAxisMinorTickStyle: minor);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Labels" : "Y Axis Labels";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis labels.",
                null,
                null,
                chart => useXAxis
                    ? new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels)
                    : new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels)))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Font" : "Y Axis Label Font";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLabelTextColor : chart.YAxisLabelTextColor;
                    var currentSize = useXAxis ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
                    var nextColor = ChartOptionCycler.NextSeriesColor(currentColor);
                    var nextSize = currentSize >= 14 ? 9 : currentSize + 1;
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelTextColor: nextColor, XAxisLabelFontSize: nextSize)
                        : new ChartLayoutOptions(YAxisLabelTextColor: nextColor, YAxisLabelFontSize: nextSize);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Angle" : "Y Axis Label Angle";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label rotation.",
                null,
                null,
                chart =>
                {
                    var currentAngle = useXAxis ? chart.XAxisLabelAngle : chart.YAxisLabelAngle;
                    var nextAngle = ChartOptionCycler.NextAxisLabelAngle(currentAngle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelAngle: nextAngle)
                        : new ChartLayoutOptions(YAxisLabelAngle: nextAngle);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Line" : "Y Axis Line";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis line formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLineColor : chart.YAxisLineColor;
                    var currentThickness = useXAxis ? chart.XAxisLineThickness : chart.YAxisLineThickness;
                    var (nextColor, nextThickness) = ChartOptionCycler.NextAxisLineState(currentColor, currentThickness);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLineColor: nextColor, XAxisLineThickness: nextThickness)
                        : new ChartLayoutOptions(YAxisLineColor: nextColor, YAxisLineThickness: nextThickness);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Gridlines" : "Y Axis Gridlines";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis gridlines.",
                null,
                null,
                chart =>
                {
                    var (showMajor, showMinor) = useXAxis
                        ? ChartOptionCycler.NextGridlineState(chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines)
                        : ChartOptionCycler.NextGridlineState(chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines);
                    return useXAxis
                        ? new ChartLayoutOptions(ShowXAxisMajorGridlines: showMajor, ShowXAxisMinorGridlines: showMinor)
                        : new ChartLayoutOptions(ShowYAxisMajorGridlines: showMajor, ShowYAxisMinorGridlines: showMinor);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        var caption = useXAxis ? "X Gridline Style" : "Y Gridline Style";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing gridline formatting.",
                null,
                null,
                chart =>
                {
                    var currentMajorColor = useXAxis ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
                    var currentMinorColor = useXAxis ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
                    var currentThickness = useXAxis ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
                    var nextMajorColor = ChartOptionCycler.NextSeriesColor(currentMajorColor);
                    var nextMinorColor = ChartOptionCycler.NextSeriesColor(currentMinorColor ?? currentMajorColor);
                    var nextThickness = currentThickness >= 3 ? 1 : currentThickness + 0.5;
                    return useXAxis
                        ? new ChartLayoutOptions(
                            XAxisMajorGridlineColor: nextMajorColor,
                            XAxisMinorGridlineColor: nextMinorColor,
                            XAxisGridlineThickness: nextThickness,
                            ShowXAxisMajorGridlines: true)
                        : new ChartLayoutOptions(
                            YAxisMajorGridlineColor: nextMajorColor,
                            YAxisMinorGridlineColor: nextMinorColor,
                            YAxisGridlineThickness: nextThickness,
                            ShowYAxisMajorGridlines: true);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Number Format" : "Y Axis Number Format";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis number formats.",
                null,
                null,
                chart =>
                {
                    var next = ChartOptionCycler.NextDataLabelNumberFormat(useXAxis ? chart.XAxisNumberFormat : chart.YAxisNumberFormat);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisNumberFormat: next)
                        : new ChartLayoutOptions(YAxisNumberFormat: next);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        var caption = useXAxis ? "X Log Scale" : "Y Log Scale";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis scale.");

            if (useXAxis && !ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("X-axis log scale is currently supported for bar, scatter, and bubble charts with value X axes.");

            if (!useXAxis && !ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("Y-axis log scale is currently supported for column, line, area, scatter, and bubble charts with value Y axes.");

            var enableLog = useXAxis ? !chart.XAxisLogScale : !chart.YAxisLogScale;
            var options = useXAxis
                ? new ChartLayoutOptions(XAxisLogScale: enableLog)
                : new ChartLayoutOptions(YAxisLogScale: enableLog);

            if (enableLog && ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var positiveMinimum = minimum > 0 ? minimum : 1;
                var positiveMaximum = maximum > positiveMinimum ? maximum : positiveMinimum * 10;
                options = useXAxis
                    ? options with { XAxisMinimum = positiveMinimum, XAxisMaximum = positiveMaximum }
                    : options with { YAxisMinimum = positiveMinimum, YAxisMaximum = positiveMaximum };
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Bounds" : "Y Axis Bounds";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis bounds.");

            var hasBounds = useXAxis
                ? chart.XAxisMinimum is not null || chart.XAxisMaximum is not null
                : chart.YAxisMinimum is not null || chart.YAxisMaximum is not null;
            if (!hasBounds &&
                (useXAxis
                    ? !ChartTypeSupport.SupportsXAxisBounds(chart.Type)
                    : !ChartTypeSupport.SupportsYAxisBounds(chart.Type)))
                return new FailedWorkbookCommand("Axis bounds are currently supported for chart value axes only.");

            ChartLayoutOptions options;
            if (hasBounds)
            {
                options = useXAxis
                    ? new ChartLayoutOptions(ClearXAxisBounds: true)
                    : new ChartLayoutOptions(ClearYAxisBounds: true);
            }
            else if (ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var majorUnit = Math.Max(double.Epsilon, (maximum - minimum) / 5);
                var minorUnit = Math.Max(double.Epsilon, majorUnit / 2);
                options = useXAxis
                    ? new ChartLayoutOptions(XAxisMinimum: minimum, XAxisMaximum: maximum, XAxisMajorUnit: majorUnit, XAxisMinorUnit: minorUnit)
                    : new ChartLayoutOptions(YAxisMinimum: minimum, YAxisMaximum: maximum, YAxisMajorUnit: majorUnit, YAxisMinorUnit: minorUnit);
            }
            else
            {
                return new FailedWorkbookCommand("Add numeric chart data before setting axis bounds.");
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis Series",
                "Insert or select a chart before changing secondary-axis series.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) && ChartOptionCycler.GetSeriesCount(chart) >= 2,
                "Secondary value axes require a supported chart with at least two data series.",
                chart =>
                {
                    var next = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, ChartOptionCycler.GetSeriesCount(chart));
                    return new ChartLayoutOptions(
                        ShowSecondaryAxis: next.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes: next.SeriesIndexes);
                }))
            return;

        UpdateViewport();
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart",
                "Insert or select a chart before changing combo chart options.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) &&
                         (chart.UseComboLineForSecondarySeries || ChartTypeSupport.SupportsComboLineOverlay(chart)),
                "Combo line overlays require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    UseComboLineForSecondarySeries: !chart.UseComboLineForSecondarySeries,
                    ComboLineSeriesIndexes: !chart.UseComboLineForSecondarySeries ? chart.ComboLineSeriesIndexes : [])))
            return;

        UpdateViewport();
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart Series",
                "Insert or select a chart before changing combo chart series.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) && ChartTypeSupport.SupportsComboLineOverlay(chart),
                "Combo line overlays require a supported chart with at least two data series.",
                chart =>
                {
                    var nextIndexes = ChartOptionCycler.GetNextComboLineSeries(chart, ChartOptionCycler.GetSeriesCount(chart));
                    return new ChartLayoutOptions(
                        UseComboLineForSecondarySeries: nextIndexes.Length > 0,
                        ComboLineSeriesIndexes: nextIndexes);
                }))
            return;

        UpdateViewport();
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ChartSeriesWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Width",
            format => format with
            {
                StrokeThickness = format.StrokeThickness is null or >= 4 ? 1.5 : format.StrokeThickness.Value + 0.75
            });
    }

    private void ChartSeriesDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Dash",
            format => format with
            {
                DashStyle = format.DashStyle switch
                {
                    null => ChartLineDashStyle.Dash,
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => null
                }
            });
    }

    private void ChartSeriesMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ShowChartSeriesFormatDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Series", "Insert or select a chart before changing series formatting.", out var chart))
            return;

        var seriesCount = ChartOptionCycler.GetSeriesCount(chart);
        if (seriesCount <= 0)
        {
            ShowCommandError(new CommandOutcome(false, "Add data series before changing series formatting."), "Format Data Series");
            return;
        }

        var dialog = new ChartSeriesFormatDialog(chart, ChartOptionCycler.GetSeriesCount(chart)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Series", chart, dialog.Result.ToOptions(chart.SeriesFormats));
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Marker Size",
            format => format with
            {
                MarkerSize = format.MarkerSize is null or >= 12 ? 5 : format.MarkerSize.Value + 2
            },
            chart => ChartTypeSupport.SupportsSeriesMarkers(chart.Type),
            "Series marker shape and size are currently supported for line and scatter charts.");
    }

    private void ToggleSeriesFormat(
        string caption,
        Func<ChartSeriesFormat, ChartSeriesFormat> update,
        Func<ChartModel, bool>? canApply = null,
        string? unsupportedMessage = null)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing series formatting.",
                chart => ChartOptionCycler.GetSeriesCount(chart) > 0 && (canApply?.Invoke(chart) ?? true),
                unsupportedMessage ?? "Add data series before changing series formatting.",
                chart =>
                {
                    var formats = chart.SeriesFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartSeriesFormat(0);
                    var updated = update(current);
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(SeriesFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private void InsertChartOfType(string type)
    {
        InsertChartOfType(ChartOptionCycler.ParseChartType(type));
    }

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

    private enum RibbonKeyTipScope
    {
        None,
        TopLevel,
        Commands,
        Menu
    }

    // ── Data tab additions ────────────────────────────────────────────────────

    // ── View tab ─────────────────────────────────────────────────────────────

    // ── QAT / title bar ──────────────────────────────────────────────────────

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();
    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();

    // ── Formula bar expand chevron ────────────────────────────────────────────

    // ── Sheet tab nav arrows ──────────────────────────────────────────────────

    // ── Help tab ──────────────────────────────────────────────────────────────



}

