using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Packaging;

namespace Freexcel.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IViewportService _viewportService;
    private readonly ICommandBus _commandBus;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
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
    private CellAddress? _selectionAnchor;
    private CellAddress? _selectionCursor;
    private bool _dragSelectActive;
    private Freexcel.App.UI.SplitPaneRegion _activeSplitPaneRegion = Freexcel.App.UI.SplitPaneRegion.BottomRight;
    private readonly Dictionary<SheetId, SplitPaneViewportOffsets> _splitPaneViewportOffsets = [];
    private readonly List<FormulaTraceArrow> _formulaTraceArrows = [];
    private readonly RecentFilesStore _recentFiles;
    private List<RecentFileViewModel> _allRecentItems = [];
    private FreexcelOptions _options = FreexcelOptions.Load();
    private string? _currentFilePath;
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private bool _formatPainterActive;
    private StyleId _formatPainterStyleId;
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private System.Windows.Controls.TextBox? _inlineEditor;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private ColumnResizeSnapshot? _columnResizeSnapshot;
    private RowResizeSnapshot? _rowResizeSnapshot;

    private enum PasteMode { All, Values, Formulas, Formats }
    private record InternalClipboard(GridRange SourceRange, List<(CellAddress Source, Cell Cell)> Cells);
    private InternalClipboard? _internalClipboard;
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

        _currentSheetId = _workbook.Sheets[0].Id;
        SheetTabsControl.ItemsSource = _sheetTabs;
        
        // Wire up scrollbars
        VerticalScroll.ValueChanged += Scroll_ValueChanged;
        HorizontalScroll.ValueChanged += Scroll_ValueChanged;
        
        // Wire up grid interactions
        SheetGrid.MouseDown += SheetGrid_MouseDown;
        SheetGrid.ColumnResized  += OnColumnResized;
        SheetGrid.RowResized     += OnRowResized;
        SheetGrid.ColumnResizing += OnColumnResizing;
        SheetGrid.RowResizing    += OnRowResizing;
        SheetGrid.AutofillRequested += OnAutofillRequested;
        SheetGrid.ContextMenuRequested += OnGridContextMenuRequested;
        SheetGrid.PageMarginsChanged += OnPageMarginsChanged;
        SheetGrid.SplitDividerMoved += OnSplitDividerMoved;
        SheetGrid.SplitPaneScrollbarScrolled += OnSplitPaneScrollbarScrolled;
        SheetGrid.MouseMove  += SheetGrid_MouseMove;
        SheetGrid.MouseUp    += SheetGrid_MouseUp;
        SheetGrid.MouseWheel += SheetGrid_MouseWheel;
        this.KeyDown += MainWindow_KeyDown;
        this.TextInput += MainWindow_TextInput;
        
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        StateChanged += (_, _) =>
        {
            if (MaxRestoreBtn != null)
                MaxRestoreBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        _logger.LogInformation("MainWindow initialized with Workbook {WorkbookId}", _workbook.Id);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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

        var formats = new[] { "General", "Number (0.00)", "Currency ($#,##0.00)", "Percentage (0%)", "Date (yyyy-MM-dd)", "Time (HH:mm:ss)", "Text (@)" };
        NumberFormatBox.ItemsSource = formats;
        NumberFormatBox.SelectedIndex = 0;

        ApplyOptionsToView();
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

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        if (_workbook.CalculationMode == WorkbookCalculationMode.Automatic)
            _recalcEngine.Recalculate(_workbook, changedCells);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewport();
    }

    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateViewport();
    }

    private string FormatCellReference(CellAddress address) =>
        _options.UseR1C1ReferenceStyle
            ? $"R{address.Row}C{address.Col}"
            : address.ToA1();

    private string FormatColumnReference(uint column) =>
        _options.UseR1C1ReferenceStyle
            ? $"C{column}"
            : CellAddress.NumberToColumnName(column);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        start == end
            ? FormatCellReference(start)
            : $"{FormatCellReference(start)}:{FormatCellReference(end)}";

    private string FormatFormulaBarText(Cell? cell, CellAddress address)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
        {
            var formula = _options.UseR1C1ReferenceStyle
                ? FormulaReferenceStyleService.ToR1C1(cell.FormulaText, address)
                : cell.FormulaText;
            return "=" + formula;
        }

        return FormatCellValue(cell?.Value);
    }

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
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
            {
                ExtendSelection(_selectionAnchor.Value, newAddr);
            }
            else
            {
                SetActiveCell(newAddr);
                if (e.ClickCount == 2)
                    EnterEditMode();
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

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox and not ComboBox)
        {
            var keyTipKey = e.SystemKey == Key.None ? e.Key : e.SystemKey;
            if (Keyboard.Modifiers == ModifierKeys.Alt && TryHandleRibbonKeyTip(keyTipKey))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CreateNewWorkbook();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PrintButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.D1 || e.Key == Key.NumPad1) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenFormatCellsDialog();
                e.Handled = true;
                return;
            }
            if (TryGetNumberFormatShortcut(e, out var numberFormatShortcut))
            {
                ApplyNumberFormatShortcut(numberFormatShortcut);
                e.Handled = true;
                return;
            }
            if (IsCtrlShiftAmpersand(e))
            {
                ApplyOutlineBorderShortcut();
                e.Handled = true;
                return;
            }
            if (IsCtrlShiftUnderscore(e))
            {
                ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowFormulasBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.L && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                FilterButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ActivateAdjacentVisibleSheet(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ActivateAdjacentVisibleSheet(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                AddSheetButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FillDownMenuItem_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FillRightMenuItem_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                TryFlashFill();
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.D5 || e.Key == Key.NumPad5) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ApplyFontToggleShortcut(FontToggleShortcut.Strikethrough, StrikeButton);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertLinkBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemSemicolon && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertCurrentDateOrTime(insertTime: false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemSemicolon && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                InsertCurrentDateOrTime(insertTime: true);
                e.Handled = true;
                return;
            }
            if ((e.SystemKey == Key.OemPlus || e.SystemKey == Key.Add) && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                InsertAutoSumFormula("SUM");
                e.Handled = true;
                return;
            }
            if (e.SystemKey == Key.Right && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
            {
                GroupRowsBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.SystemKey == Key.Left && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
            {
                UngroupRowsBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (IsCtrlPlus(e))
            {
                ExecuteKeyboardInsert();
                e.Handled = true;
                return;
            }
            if (IsCtrlMinus(e))
            {
                ExecuteKeyboardDelete();
                e.Handled = true;
                return;
            }
        }

        if (IsBoldShortcut(e))
        {
            ApplyFontToggleShortcut(FontToggleShortcut.Bold, BoldButton);
            e.Handled = true;
            return;
        }
        if (IsItalicShortcut(e))
        {
            ApplyFontToggleShortcut(FontToggleShortcut.Italic, ItalicButton);
            e.Handled = true;
            return;
        }
        if (IsUnderlineShortcut(e))
        {
            ApplyFontToggleShortcut(FontToggleShortcut.Underline, UnderlineButton);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FindButton_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ReplaceButton_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteCopy();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteCopy(isCut: true);
            ExecuteClearSelection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecutePaste();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SelectCurrentRegionOrAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SelectWholeColumnsFromSelection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SelectWholeRowsFromSelection();
            e.Handled = true;
            return;
        }
        if (IsCtrl9(e))
        {
            ExecuteRowsHidden(hidden: true);
            e.Handled = true;
            return;
        }
        if (IsCtrlShift9(e))
        {
            ExecuteRowsHidden(hidden: false);
            e.Handled = true;
            return;
        }
        if (IsCtrl0(e))
        {
            ExecuteColumnsHidden(hidden: true);
            e.Handled = true;
            return;
        }
        if (IsCtrlShift0(e))
        {
            ExecuteColumnsHidden(hidden: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteUndo();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteRedo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && (Keyboard.Modifiers & ModifierKeys.Control) == 0
            && Keyboard.FocusedElement is not TextBox)
        {
            ExecuteClearSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            EnterEditMode();
            e.Handled = true;
            return;
        }

        if (SheetGrid.SelectedRange == null) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrlHeld  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // When Shift is held the moving end is _selectionCursor; otherwise it's the active cell.
        var current = shiftHeld && _selectionCursor.HasValue
            ? _selectionCursor.Value
            : SheetGrid.SelectedRange.Value.Start;

        var sheet = _workbook.GetSheet(_currentSheetId);
        int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);

        CellAddress? target = e.Key switch
        {
            Key.Up    => ctrlHeld ? FindDataBoundaryCol(sheet, current.Row, current.Col, -1)
                                  : new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down  => ctrlHeld ? FindDataBoundaryCol(sheet, current.Row, current.Col, +1)
                                  : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Left  => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, +1)
                                  : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),

            Key.Home     => new CellAddress(_currentSheetId, ctrlHeld ? 1u : current.Row, 1u),
            Key.End      => ctrlHeld ? (CellAddress?)CtrlEndCell(sheet) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            Key.Enter => new CellAddress(_currentSheetId, current.Row + 1, current.Col),
            Key.Tab   => new CellAddress(_currentSheetId, current.Row, current.Col + 1),
            _         => null
        };

        if (target == null) return;

        bool moveOnly = e.Key is Key.Enter or Key.Tab;
        if (shiftHeld && !moveOnly && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target.Value);
        else
            SetActiveCell(target.Value);

        EnsureCellVisible(target.Value);
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
            SheetGrid.Focus();
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
        SheetGrid.Focus();
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
        SheetGrid.Focus();
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
        if (!_dragSelectActive) return;
        _dragSelectActive = false;
        SheetGrid.ReleaseMouseCapture();
    }

    private void SheetGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int notches = e.Delta / 120;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+Scroll = zoom
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + notches * 10));
            e.Handled = true;
            return;
        }

        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (SheetGrid.Viewport?.SplitPanes is not null &&
            !Freexcel.App.UI.GridView.CanScrollSplitPaneRegion(_activeSplitPaneRegion, horizontal))
        {
            e.Handled = true;
            return;
        }

        if (TryScrollIndependentSplitPane(horizontal, notches))
        {
            e.Handled = true;
            return;
        }

        if (horizontal)
        {
            HorizontalScroll.Value = Math.Max(HorizontalScroll.Minimum,
                Math.Min(HorizontalScroll.Maximum, HorizontalScroll.Value - notches * 3));
        }
        else
        {
            VerticalScroll.Value = Math.Max(VerticalScroll.Minimum,
                Math.Min(VerticalScroll.Maximum, VerticalScroll.Value - notches * 3));
        }
        e.Handled = true;
    }

    private bool TryScrollIndependentSplitPane(bool horizontal, int notches)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return false;

        if (horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.TopRight)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.HorizontalTopRight is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.TopRightLeftCol
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.HorizontalTopRight,
                current ?? Math.Max(1, (uint)HorizontalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return true;
        }

        if (!horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.BottomLeft)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.VerticalBottomLeft is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.BottomLeftTopRow
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.VerticalBottomLeft,
                current ?? Math.Max(1, (uint)VerticalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { BottomLeftTopRow = target.Index };
            UpdateViewport();
            return true;
        }

        return false;
    }

    private void OnSplitPaneScrollbarScrolled(Freexcel.App.UI.SplitPaneScrollbarScrollTarget target)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return;

        _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets);
        offsets ??= new SplitPaneViewportOffsets();

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.TopRight,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Horizontal
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return;
        }

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.BottomLeft,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Vertical
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { BottomLeftTopRow = target.Index };
            UpdateViewport();
        }
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
        UnderlineButton.IsChecked = style.Underline;
        StrikeButton.IsChecked    = style.Strikethrough;
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
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void EnterEditMode()
    {
        if (_selectionAnchor.HasValue)
            ShowInlineEditor(_selectionAnchor.Value);
        else
        {
            FormulaBar.Focus();
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
        }
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
                FontSize        = 11.0 * (96.0 / 72.0),
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
            };
            _inlineEditor.KeyDown    += InlineEditor_KeyDown;
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
        var currentText = FormatCellValue(sheet.GetCell(range.Start)?.Value);
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
            SheetGrid.ClipboardRange = null;
            SheetGrid.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Enter or Key.Tab)
        {
            var text = _inlineEditor!.Text;
            HideInlineEditor(commit: true);
            FormulaBar.Text = text;
            CommitEdit();
            var current = SheetGrid.SelectedRange?.Start;
            if (current.HasValue)
            {
                var next = e.Key == Key.Tab
                    ? new CellAddress(_currentSheetId, current.Value.Row, current.Value.Col + 1)
                    : new CellAddress(_currentSheetId, current.Value.Row + 1, current.Value.Col);
                SetActiveCell(next);
                EnsureCellVisible(next);
            }
            e.Handled = true;
        }
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

    private static string FormatCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd"),
        ErrorValue err => err.Code,
        _ => ""
    };

    private void EnsureCellVisible(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp == null) return;

        var rows = vp.RowMetrics;
        if (rows.Count > 0 && !rows.Any(r => r.Row == addr.Row))
        {
            uint firstRow = rows[0].Row;
            uint lastRow  = rows[^1].Row;
            if (addr.Row < firstRow)
                VerticalScroll.Value = Math.Max(1, addr.Row);
            else
                VerticalScroll.Value = Math.Max(1, addr.Row - (lastRow - firstRow));
        }

        var cols = vp.ColMetrics;
        if (cols.Count > 0 && !cols.Any(c => c.Col == addr.Col))
        {
            uint firstCol = cols[0].Col;
            uint lastCol  = cols[^1].Col;
            if (addr.Col < firstCol)
                HorizontalScroll.Value = Math.Max(1, addr.Col);
            else
                HorizontalScroll.Value = Math.Max(1, addr.Col - (lastCol - firstCol));
        }
    }

    // ── Navigation helpers ────────────────────────────────────────────────────

    private bool CellHasData(Sheet? sheet, uint row, uint col)
    {
        if (sheet == null) return false;
        var v = sheet.GetValue(new CellAddress(_currentSheetId, row, col));
        return v != null && v is not BlankValue;
    }

    private CellAddress FindDataBoundaryCol(Sheet? sheet, uint row, uint col, int dir)
    {
        const uint maxRow = 1_048_576;
        bool startFull = CellHasData(sheet, row, col);
        uint r = row;
        while (true)
        {
            long next = (long)r + dir;
            if (next < 1 || next > maxRow) break;
            uint nr = (uint)next;
            bool nextFull = CellHasData(sheet, nr, col);
            if (startFull && !nextFull) break;   // stop before gap
            r = nr;
            if (!startFull && nextFull) break;   // landed on first data cell
        }
        return new CellAddress(_currentSheetId, r, col);
    }

    private CellAddress FindDataBoundaryRow(Sheet? sheet, uint row, uint col, int dir)
    {
        const uint maxCol = 16_384;
        bool startFull = CellHasData(sheet, row, col);
        uint c = col;
        while (true)
        {
            long next = (long)c + dir;
            if (next < 1 || next > maxCol) break;
            uint nc = (uint)next;
            bool nextFull = CellHasData(sheet, row, nc);
            if (startFull && !nextFull) break;
            c = nc;
            if (!startFull && nextFull) break;
        }
        return new CellAddress(_currentSheetId, row, c);
    }

    private CellAddress CtrlEndCell(Sheet? sheet)
    {
        uint maxRow = 1, maxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > maxRow) maxRow = addr.Row;
                if (addr.Col > maxCol) maxCol = addr.Col;
            }
        return new CellAddress(_currentSheetId, maxRow, maxCol);
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4)
        {
            if (TryCycleFormulaReference(FormulaBar))
                e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter
            && (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
        {
            var current = SheetGrid.SelectedRange?.Start;
            CommitEdit();
            if (current.HasValue)
            {
                var next = new CellAddress(_currentSheetId, current.Value.Row + 1, current.Value.Col);
                SetActiveCell(next);
                EnsureCellVisible(next);
            }
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
            SheetGrid.ClipboardRange = null;
            SheetGrid.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down or Key.Tab or Key.PageUp or Key.PageDown)
        {
            var current = SheetGrid.SelectedRange?.Start;
            CommitEdit();
            if (current.HasValue)
            {
                int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
                var target = e.Key switch
                {
                    Key.Up       => new CellAddress(_currentSheetId, current.Value.Row > 1 ? current.Value.Row - 1 : 1u, current.Value.Col),
                    Key.Down     => new CellAddress(_currentSheetId, Math.Min(current.Value.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Value.Col),
                    Key.Tab      => new CellAddress(_currentSheetId, current.Value.Row, Math.Min(current.Value.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),
                    Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Value.Row - pageSize), current.Value.Col),
                    Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Value.Row + (uint)pageSize), current.Value.Col),
                    _            => current.Value
                };
                SetActiveCell(target);
                EnsureCellVisible(target);
            }
            e.Handled = true;
        }
    }

    private static bool TryCycleFormulaReference(System.Windows.Controls.TextBox editor)
    {
        if (!editor.Text.StartsWith("=", StringComparison.Ordinal))
            return false;

        if (!FormulaReferenceCycler.TryCycleReferenceAtCaret(
                editor.Text,
                editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretIndex,
                out var cycled,
                out var selectionStart,
                out var selectionLength))
            return false;

        editor.Text = cycled;
        editor.SelectionStart = selectionStart;
        editor.SelectionLength = selectionLength;
        return true;
    }

    private void CommitEdit()
    {
        if (SheetGrid.SelectedRange == null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = FormulaBar.Text;

        Cell newCell;
        if (text.StartsWith("="))
        {
            var formula = text.Substring(1);
            if (_options.UseR1C1ReferenceStyle)
                formula = FormulaReferenceStyleService.ToA1(formula, addr);
            newCell = Cell.FromFormula(formula);
        }
        else
        {
            ScalarValue value;
            if (double.TryParse(text, out var d)) value = new NumberValue(d);
            else if (bool.TryParse(text, out var b)) value = new BoolValue(b);
            else value = new TextValue(text);

            // Soft validation: check data validation rules and warn but still apply
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
                        return;
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
                            return;
                        }
                    }
                }
            }

            newCell = Cell.FromValue(value);
        }

        if (!TryExecuteEditCells([(addr, newCell)], "Edit Cell", out var outcome))
            return;

        var affectedCells = outcome.AffectedCells ?? [addr];
        if (text.StartsWith("="))
        {
            // For now, we manually register dependencies because we haven't automated this in the command yet.
            try
            {
                var lexer = new Lexer(text);
                var parser = new Parser(lexer.Tokenize());
                var ast = parser.Parse();
                foreach (var affected in affectedCells)
                    _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, _workbook);
            }
            catch { /* ignore parse errors for now */ }
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
    }

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
            SyncZoomFromSheet(sheet.ZoomPercent);

        uint topRow  = Math.Max((sheet?.FrozenRows ?? 0) + 1, (uint)VerticalScroll.Value);
        uint leftCol = Math.Max((sheet?.FrozenCols ?? 0) + 1, (uint)HorizontalScroll.Value);

        // AvailableHeight/Width is divided by zoom so viewport covers the right number of cells
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - Freexcel.App.UI.GridView.ColHeaderHeight) / _zoomLevel,
            AvailableWidth:  (SheetGrid.ActualWidth  - SheetGrid.ActualRowHeaderWidth)  / _zoomLevel,
            SplitPaneOffsets: GetSplitPaneViewportOffsets(sheet, topRow, leftCol)
        );

        var viewport = _viewportService.GetViewport(_workbook, _currentSheetId, request);
        SheetGrid.Viewport = viewport;
        SheetGrid.FormulaTraceSheetId = _currentSheetId;
        SheetGrid.FormulaTraceArrows = _formulaTraceArrows;
        SheetGrid.Charts = sheet?.Charts;
        SheetGrid.TextBoxes = sheet?.TextBoxes;
        SheetGrid.DrawingShapes = sheet?.DrawingShapes;
        SheetGrid.Pictures = sheet?.Pictures;
        SheetGrid.WorksheetBackground = sheet?.BackgroundImage;
        SheetGrid.Sparklines = sheet?.Sparklines;
        SheetGrid.SparklineValues = sheet is null ? null : BuildSparklineValues(sheet);
        SheetGrid.MergedRegions = sheet?.MergedRegions;
        SheetGrid.WorksheetViewMode = sheet?.ViewMode ?? WorksheetViewMode.Normal;
        SheetGrid.ShowGridLines = sheet?.ShowGridlines ?? true;
        SheetGrid.ShowHeaders = sheet?.ShowHeadings ?? true;
        SheetGrid.ShowRulers = sheet?.ShowRulers ?? true;
        _suppressViewOptionSync = true;
        try
        {
            if (ViewGridlinesChk is not null)
                ViewGridlinesChk.IsChecked = SheetGrid.ShowGridLines;
            if (ViewHeadersChk is not null)
                ViewHeadersChk.IsChecked = SheetGrid.ShowHeaders;
            if (ViewRulerChk is not null)
                ViewRulerChk.IsChecked = SheetGrid.ShowRulers;
        }
        finally
        {
            _suppressViewOptionSync = false;
        }
        SheetGrid.RowPageBreaks = sheet?.RowPageBreaks;
        SheetGrid.ColumnPageBreaks = sheet?.ColumnPageBreaks;
        SheetGrid.PrintArea = sheet?.PrintArea;
        SheetGrid.SplitRow = sheet?.SplitRow;
        SheetGrid.SplitColumn = sheet?.SplitColumn;
        SheetGrid.PageMargins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        SheetGrid.PageOrientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        SheetGrid.PaperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;

        // Adjust scrollbar range to the used data range + buffer, thumb to visible area
        UpdateScrollbarMaximums(sheet);
        VerticalScroll.ViewportSize   = viewport.RowMetrics.Count;
        HorizontalScroll.ViewportSize = viewport.ColMetrics.Count;
        VerticalScroll.LargeChange    = Math.Max(1, viewport.RowMetrics.Count);
        HorizontalScroll.LargeChange  = Math.Max(1, viewport.ColMetrics.Count);
        RefreshValidationDropdown();
    }

    private SplitPaneViewportOffsets? GetSplitPaneViewportOffsets(Sheet? sheet, uint topRow, uint leftCol)
    {
        if (sheet is null || (!sheet.SplitRow.HasValue && !sheet.SplitColumn.HasValue))
            return null;

        _splitPaneViewportOffsets.TryGetValue(sheet.Id, out var offsets);
        return new SplitPaneViewportOffsets(
            sheet.SplitColumn.HasValue ? offsets?.TopRightLeftCol ?? leftCol : null,
            sheet.SplitRow.HasValue ? offsets?.BottomLeftTopRow ?? topRow : null);
    }

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        // Compute the farthest cell with data
        uint usedMaxRow = 1, usedMaxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > usedMaxRow) usedMaxRow = addr.Row;
                if (addr.Col > usedMaxCol) usedMaxCol = addr.Col;
            }

        var vp = SheetGrid.Viewport;
        uint visRows = (uint)Math.Max(10, vp?.RowMetrics.Count ?? 40);
        uint visCols = (uint)Math.Max(5,  vp?.ColMetrics.Count ?? 15);
        uint topRow  = Math.Max(1, (uint)VerticalScroll.Value);
        uint leftCol = Math.Max(1, (uint)HorizontalScroll.Value);

        // Maximum extends one page beyond either the last used cell or the current
        // scroll position — whichever is farther.  This mirrors Excel: the scrollbar
        // reflects actual data range but expands as you navigate and shrinks back
        // once you return to the used area.
        uint vMaxRow = Math.Max(usedMaxRow + visRows, topRow  + visRows + 1);
        uint vMaxCol = Math.Max(usedMaxCol + visCols, leftCol + visCols + 1);

        VerticalScroll.Maximum   = Math.Min(vMaxRow, Freexcel.Core.Model.CellAddress.MaxRow);
        HorizontalScroll.Maximum = Math.Min(vMaxCol, Freexcel.Core.Model.CellAddress.MaxCol);
    }

    private void UpdateTitleBar()
    {
        var groupSuffix = IsWorkbookGrouped() ? " [Group]" : "";
        var displayName = $"{_workbook.Name}{groupSuffix} - Freexcel";
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
    }

    private bool IsWorkbookGrouped()
    {
        var groupedVisibleSheets = _workbook.Sheets.Count(sheet => !sheet.IsHidden && _groupedSheetIds.Contains(sheet.Id));
        return groupedVisibleSheets > 1 && _groupedSheetIds.Contains(_currentSheetId);
    }

    // ── Start screen ─────────────────────────────────────────────────────────

    private void ShowStartScreen()
    {
        UpdateSsGreeting();
        UpdateSsRecentList();
        ShowHomeView();
        StartScreenOverlay.Visibility = Visibility.Visible;
    }

    private void HideStartScreen()
    {
        StartScreenOverlay.Visibility = Visibility.Collapsed;
        SheetGrid.Focus();
    }

    private void ShowHomeView()
    {
        SsHomeView.Visibility = Visibility.Visible;
        SsInfoView.Visibility = Visibility.Collapsed;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtn");
    }

    private void ShowInfoView()
    {
        SsHomeView.Visibility = Visibility.Collapsed;
        SsInfoView.Visibility = Visibility.Visible;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        UpdateInfoView();
    }

    private void UpdateInfoView()
    {
        InfoWorkbookName.Text = _workbook.Name;
        InfoFilePath.Text = _currentFilePath ?? "Not saved yet";
        InfoSheetCount.Text = _workbook.Sheets.Count.ToString();
        InfoFormat.Text = _currentFilePath is not null
            ? System.IO.Path.GetExtension(_currentFilePath).ToLower()
            : ".xlsx";
    }

    private void UpdateSsGreeting()
    {
        var hour = DateTime.Now.Hour;
        SsGreeting.Text = hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _    => "Good evening"
        };
    }

    private void UpdateSsRecentList(string filter = "")
    {
        _allRecentItems = _recentFiles.Entries
            .Where(e => System.IO.File.Exists(e.Path))
            .Select(e => new RecentFileViewModel(e))
            .ToList();

        SsRecentList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allRecentItems
            : _allRecentItems
                .Where(vm => vm.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void CreateNewWorkbook()
    {
        var wb = new Workbook("Book1");
        wb.AddSheet("Sheet1");
        _workbook = wb;
        _workbookRef.Current = wb;
        _currentSheetId = wb.Sheets[0].Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
        UpdateTitleBar();
        RecalculateWorkbook();
        SheetGrid.SelectedRange = null;
        _selectionAnchor = null;
        _selectionCursor = null;
        CellAddressBox.Text = "A1";
        FormulaBar.Text = "";
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void OpenFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLower();
        var adapter = _fileAdapters.FirstOrDefault(a => a.Extension == ext);
        if (adapter == null) return;

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            _currentXlsxFeatureReport = ext == ".xlsx"
                ? XlsxFeatureInspector.Inspect(stream)
                : null;
            if (stream.CanSeek)
                stream.Position = 0;
            _workbook = adapter.Load(stream);
            _workbookRef.Current = _workbook;
            _workbook.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            _currentSheetId = _workbook.Sheets[0].Id;
            _currentFilePath = path;
            UpdateTitleBar();

            RecalculateWorkbook();
            _recentFiles.AddOrUpdate(path);
            UpdateViewport();
            RefreshSheetTabs();
            HideStartScreen();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Start screen button handlers
    private void SsBackBtn_Click(object sender, RoutedEventArgs e)       => HideStartScreen();
    private void SsNewBtn_Click(object sender, RoutedEventArgs e)        { CreateNewWorkbook(); HideStartScreen(); }
    private void SsBlankWorkbook_Click(object sender, RoutedEventArgs e) { CreateNewWorkbook(); HideStartScreen(); }
    private void SsOpenBtn_Click(object sender, RoutedEventArgs e)       => OpenButton_Click(sender, e);
    private void SsCloseBtn_Click(object sender, RoutedEventArgs e)      => Application.Current.Shutdown();
    private void SsHomeRibbonBtn_Click(object sender, RoutedEventArgs e) => ShowStartScreen();

    private void RibbonTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RibbonTabs.SelectedItem == FileTab)
        {
            // Switch back to Home immediately so the tab never stays selected
            RibbonTabs.SelectedIndex = 1;
            ShowStartScreen();
        }
    }
    private void SsShareBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowExcludedShareMessage();
    }

    private void SsHomeNavBtn_Click(object sender, RoutedEventArgs e)    => ShowHomeView();
    private void SsInfoBtn_Click(object sender, RoutedEventArgs e)       => ShowInfoView();

    private void SsMoreTemplates_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://create.microsoft.com/en-us/excel",
            UseShellExecute = true
        });
    }

    private void SsOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _options = dlg.Result;
            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
            ApplyOptionsToView();
            UpdateViewport();
        }
    }

    private void ApplyFormulaErrorCheckingOptions(IReadOnlySet<string> disabledErrorCodes)
    {
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var shouldDisable = disabledErrorCodes.Contains(rule.ErrorCode);
            var isDisabled = _workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode);
            if (shouldDisable == isDisabled)
                continue;

            if (!TryExecuteCommand(
                    new SetFormulaErrorCheckingRuleCommand(rule.ErrorCode, enabled: !shouldDisable),
                    "Error Checking Options"))
            {
                return;
            }
        }
    }

    private bool TryHandleRibbonKeyTip(Key key)
    {
        return key switch
        {
            Key.F => OpenFileBackstageFromKeyTip(),
            Key.H => SelectRibbonTabByHeader("Home"),
            Key.N => SelectRibbonTabByHeader("Insert"),
            Key.P => SelectRibbonTabByHeader("Page Layout"),
            Key.M => SelectRibbonTabByHeader("Formulas"),
            Key.A => SelectRibbonTabByHeader("Data"),
            Key.R => SelectRibbonTabByHeader("Review"),
            Key.W => SelectRibbonTabByHeader("View"),
            _ => false
        };
    }

    private bool OpenFileBackstageFromKeyTip()
    {
        ShowStartScreen();
        if (RibbonTabs != null)
            RibbonTabs.SelectedIndex = 1;
        return true;
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
                return true;
            }
        }

        return false;
    }

    private void SsRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is RecentFileViewModel vm)
            OpenFile(vm.Path);
    }

    private void SsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateSsRecentList(SsSearchBox.Text);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };

        if (dialog.ShowDialog() == true)
            OpenFile(dialog.FileName);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
        {
            SaveWorkbookToTarget(target!);
            return;
        }

        SaveWorkbookWithDialog();
    }

    private void SaveWorkbookWithDialog()
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = _workbook.Name,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
            var adapter = _fileAdapters.FirstOrDefault(a => a.Extension == ext);
            if (adapter == null) return;
            SaveWorkbookToTarget(new FileSaveTarget(dialog.FileName, adapter));
        }
    }

    private void SaveWorkbookToTarget(FileSaveTarget target)
    {
        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return;

        try
        {
            using var stream = System.IO.File.Create(target.Path);
            target.Adapter.Save(_workbook, stream);
            _currentFilePath = target.Path;
            _recentFiles.AddOrUpdate(target.Path);
            UpdateTitleBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save file:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmUnsupportedXlsxFeatureSave()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return true;

        var featureList = string.Join(", ",
            _currentXlsxFeatureReport.Features
                .Select(f => FormatUnsupportedFeatureKind(f.Kind))
                .Distinct()
                .OrderBy(name => name));

        var result = MessageBox.Show(
            "This workbook contains features Freexcel does not preserve yet. " +
            $"Saving to .xlsx may remove: {featureList}.\n\nContinue saving?",
            "Unsupported XLSX Features",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private static string FormatUnsupportedFeatureKind(XlsxUnsupportedFeatureKind kind) => kind switch
    {
        XlsxUnsupportedFeatureKind.Macros => "macros",
        XlsxUnsupportedFeatureKind.PivotTables => "pivot tables",
        XlsxUnsupportedFeatureKind.Charts => "charts",
        XlsxUnsupportedFeatureKind.Slicers => "slicers",
        XlsxUnsupportedFeatureKind.Timelines => "timelines",
        XlsxUnsupportedFeatureKind.ExternalLinks => "external links",
        XlsxUnsupportedFeatureKind.EmbeddedObjects => "embedded objects",
        XlsxUnsupportedFeatureKind.CustomXmlParts => "custom XML parts",
        _ => kind.ToString()
    };

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

    private void InsertChartOfType(ChartType type)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteCommand(new AddChartCommand(_currentSheetId, range, type, "Chart"), "Insert Chart"))
            return;

        UpdateViewport();
    }

    private void RefreshSheetTabs()
    {
        if (_workbook.Sheets.All(s => s.IsHidden) && _workbook.Sheets.Count > 0)
            _workbook.Sheets[0].IsHidden = false;
        if (_workbook.GetSheet(_currentSheetId)?.IsHidden == true)
            _currentSheetId = _workbook.Sheets.First(s => !s.IsHidden).Id;

        var visibleIds = _workbook.Sheets.Where(s => !s.IsHidden).Select(s => s.Id).ToHashSet();
        _groupedSheetIds.RemoveWhere(id => !visibleIds.Contains(id));
        _sheetTabs.Clear();
        foreach (var sheet in _workbook.Sheets.Where(s => !s.IsHidden))
        {
            if (_groupedSheetIds.Count == 0 && sheet.Id == _currentSheetId)
                _groupedSheetIds.Add(sheet.Id);
            _sheetTabs.Add(new SheetTabViewModel(sheet.Id, sheet.Name, sheet.TabColor)
            {
                IsActive = sheet.Id == _currentSheetId,
                IsGrouped = _groupedSheetIds.Contains(sheet.Id)
            });
        }
        UpdateTitleBar();
    }

    private string GenerateUniqueSheetName()
    {
        for (int i = _workbook.Sheets.Count + 1; i <= 10_000; i++)
        {
            var name = $"Sheet{i}";
            if (_workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
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

    private static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(new CellAddress(sheetId, range.Start.Row, range.Start.Col),
            new CellAddress(sheetId, range.End.Row, range.End.Col));

    private static ConditionalFormat CloneConditionalFormatForSheet(ConditionalFormat source, SheetId sheetId) =>
        new()
        {
            AppliesTo = RemapRangeToSheet(source.AppliesTo, sheetId),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            DataBarColor = source.DataBarColor,
            AboveAverage = source.AboveAverage,
            FormulaText  = source.FormulaText,
            StopIfTrue   = source.StopIfTrue
        };

    private static DataValidation CloneDataValidationForSheet(DataValidation source, SheetId sheetId) =>
        new()
        {
            AppliesTo = RemapRangeToSheet(source.AppliesTo, sheetId),
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage
        };

    private void SheetTab_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        _dragSheetTabId = tab.Id;
        _dragSheetTabStart = e.GetPosition(SheetTabsControl);
        _currentSheetId = tab.Id;
        UpdateGroupedSheetsForClick(tab.Id);
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetTab_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSheetTabId is not { } draggedId || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(SheetTabsControl);
        if (Math.Abs(current.X - _dragSheetTabStart.X) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        var target = FindSheetTabViewModel(e.OriginalSource as System.Windows.DependencyObject);
        if (target is null || target.Id == draggedId)
            return;

        var sheets = _workbook.Sheets.ToList();
        var fromIndex = sheets.FindIndex(s => s.Id == draggedId);
        var toIndex = sheets.FindIndex(s => s.Id == target.Id);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        if (!TryExecuteCommand(new MoveSheetCommand(fromIndex, toIndex), "Move Sheet"))
            return;

        _currentSheetId = draggedId;
        _dragSheetTabStart = current;
        RefreshSheetTabs();
    }

    private void SheetTab_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragSheetTabId = null;
    }

    private void SheetTab_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        _currentSheetId = tab.Id;
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(tab.Id);
        _sheetGroupAnchor ??= tab.Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetTab_LabelMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        var tab = (sender as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        if (tab is null) return;
        RenameSheetFromTab(tab);
    }

    private void AddSheetButton_Click(object sender, RoutedEventArgs e)
    {
        var name = GenerateUniqueSheetName();
        var outcome = _commandBus.Execute(_workbook.Id, new AddSheetCommand(name));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[^1].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void UpdateGroupedSheetsForClick(SheetId clickedSheetId)
    {
        var visibleSheetIds = _workbook.Sheets.Where(s => !s.IsHidden).Select(s => s.Id).ToList();
        var modifiers = Keyboard.Modifiers;
        IReadOnlyList<SheetId> selected;
        if ((modifiers & ModifierKeys.Shift) != 0 && _sheetGroupAnchor.HasValue)
        {
            selected = SheetGroupSelectionService.SelectRange(visibleSheetIds, _sheetGroupAnchor.Value, clickedSheetId);
        }
        else if ((modifiers & ModifierKeys.Control) != 0)
        {
            selected = SheetGroupSelectionService.Toggle(clickedSheetId, _groupedSheetIds);
            _sheetGroupAnchor = clickedSheetId;
        }
        else
        {
            selected = SheetGroupSelectionService.SelectSingle(clickedSheetId);
            _sheetGroupAnchor = clickedSheetId;
        }

        _groupedSheetIds.Clear();
        foreach (var id in selected)
            _groupedSheetIds.Add(id);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(clickedSheetId);
    }

    private static string? PromptForInput(string prompt, string defaultValue)
    {
        var win = new Window
        {
            Title = prompt, Width = 300, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var tb = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(10) };
        var btn = new System.Windows.Controls.Button { Content = "OK", Margin = new Thickness(10, 0, 10, 10) };
        var sp = new System.Windows.Controls.StackPanel();
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        win.Content = sp;
        string? result = null;
        btn.Click += (_, _) => { result = tb.Text; win.Close(); };
        win.ShowDialog();
        return result;
    }

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var cmd = new SortCommand(_currentSheetId, range, sortByColOffset: 0, ascending: true);
        if (!TryExecuteCommand(cmd, "Sort"))
            return;
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var cmd = new SortCommand(_currentSheetId, range, sortByColOffset: 0, ascending: false);
        if (!TryExecuteCommand(cmd, "Sort"))
            return;
        UpdateViewport();
    }

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Sort keys (for example 1 asc; 2 desc):", "1 asc");
        if (input is null) return;

        if (!SortInputParser.TryParse(input, out var keys, out var error))
        {
            MessageBox.Show(error ?? "Enter sort keys such as 1 asc; 2 desc.",
                "Custom Sort", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cmd = new SortCommand(_currentSheetId, range, keys);
        if (!TryExecuteCommand(cmd, "Sort"))
            return;
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = PromptForInput("Filter: values separated by comma/semicolon, top:n, bottom:n, toppercent:n, bottompercent:n, aboveavg, belowavg, blank, nonblank, text=value, text<>value, contains:text, notcontains:text, begins:text, ends:text, between:min:max, date=yyyy-mm-dd, date<>yyyy-mm-dd, date>yyyy-mm-dd, date>=yyyy-mm-dd, date<yyyy-mm-dd, date<=yyyy-mm-dd, datebetween:start:end, >number, >=number, <number, <=number, =number, or <>number", "");
        if (value is null) return;  // user cancelled
        var filterText = value.TrimStart();
        if (filterText.StartsWith("top:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase))
        {
            if (!FilterInputParser.TryParseTopBottom(value, out var count, out var top, out var percent, out var error))
            {
                MessageBox.Show(error ?? "Enter top:n, bottom:n, toppercent:n, or bottompercent:n.",
                    "Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var topBottomCommand = percent
                ? TopBottomFilterCommand.Percent(_currentSheetId, range, filterColOffset: 0, count, top)
                : new TopBottomFilterCommand(_currentSheetId, range, filterColOffset: 0, count, top);
            if (!TryExecuteCommand(topBottomCommand, "Filter"))
                return;
            UpdateViewport();
            return;
        }

        if (FilterInputParser.TryParseAverage(value, out var aboveAverage))
        {
            var averageCommand = new AverageFilterCommand(_currentSheetId, range, filterColOffset: 0, aboveAverage);
            if (!TryExecuteCommand(averageCommand, "Filter"))
                return;
            UpdateViewport();
            return;
        }

        if (filterText.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
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
            filterText.StartsWith('='))
        {
            if (!FilterInputParser.TryParseCriterion(value, out var criterion, out var error) || criterion is null)
            {
                MessageBox.Show(error ?? "Enter a supported filter criterion.",
                    "Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var conditionCommand = new FilterConditionCommand(_currentSheetId, range, filterColOffset: 0, criterion);
            if (!TryExecuteCommand(conditionCommand, "Filter"))
                return;
            UpdateViewport();
            return;
        }

        var allowedValues = FilterInputParser.ParseAllowedValues(value);
        var cmd = new FilterCommand(_currentSheetId, range, filterColOffset: 0, allowedValues: allowedValues);
        if (!TryExecuteCommand(cmd, "Filter"))
            return;
        UpdateViewport();
    }

    private void CfRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "CF Rule");
            return;
        }

        var thresholdText = PromptForInput("Conditional Format: highlight cells greater than", "0");
        if (string.IsNullOrWhiteSpace(thresholdText)) return;

        var cf = new ConditionalFormat
        {
            AppliesTo    = range,
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = thresholdText.Trim(),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, CloneConditionalFormatForSheet(cf, sheetId))))
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
            if (!TryExecuteGroupedSheetCommand(
                    "Clear Data Validation",
                    sheetId => new ClearDataValidationCommand(sheetId, RemapRangeToSheet(range, sheetId))))
                return;

            UpdateViewport();
            return;
        }

        if (dlg.Result == null) return;

        var dv = dlg.Result;
        dv.AppliesTo = range;

        if (!TryExecuteGroupedSheetCommand(
                "Data Validation",
                sheetId => new SetDataValidationCommand(sheetId, CloneDataValidationForSheet(dv, sheetId))))
            return;
        UpdateViewport();
    }

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var cmd = new FilterCommand(_currentSheetId, range, filterColOffset: 0, allowedValues: []);
        if (!TryExecuteCommand(cmd, "Filter"))
            return;
        UpdateViewport();
    }

    // ── Group / Ungroup handlers ─────────────────────────────────────────────

    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            int newLevel = GetNextOutlineLevel(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            if (TryExecuteCommand(new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, newLevel), "Group Columns"))
                UpdateViewport();
            return;
        }

        int rowLevel = GetNextOutlineLevel(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        if (TryExecuteCommand(new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, rowLevel), "Group Rows"))
            UpdateViewport();
    }

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        IWorkbookCommand command = OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns
            ? new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, 0)
            : new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 0);

        if (TryExecuteCommand(command, "Ungroup"))
            UpdateViewport();
    }

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        var axis = SheetGrid.SelectedRange is { } range
            ? OutlineGroupingService.GetGroupingAxis(range)
            : OutlineGroupingAxis.Rows;
        IWorkbookCommand cmd = axis == OutlineGroupingAxis.Columns
            ? new CollapseColGroupCommand(_currentSheetId, 1)
            : new CollapseRowGroupCommand(_currentSheetId, 1);
        if (TryExecuteCommand(cmd, "Collapse Group"))
            UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        var axis = SheetGrid.SelectedRange is { } range
            ? OutlineGroupingService.GetGroupingAxis(range)
            : OutlineGroupingAxis.Rows;
        IWorkbookCommand cmd = axis == OutlineGroupingAxis.Columns
            ? new ExpandColGroupCommand(_currentSheetId, 1)
            : new ExpandRowGroupCommand(_currentSheetId, 1);
        if (TryExecuteCommand(cmd, "Expand Group"))
            UpdateViewport();
    }

    private static int GetNextOutlineLevel(uint start, uint end, IReadOnlyDictionary<uint, int> outlineLevels)
    {
        int maxExisting = 0;
        for (uint index = start; index <= end; index++)
        {
            if (outlineLevels.TryGetValue(index, out var level) && level > maxExisting)
                maxExisting = level;
        }

        return Math.Min(maxExisting + 1, 8);
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

    // ── Formatting toolbar handlers ───────────────────────────────────────────

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true));
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true));
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Underline: UnderlineButton.IsChecked == true));
    }

    private void StrikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Strikethrough: StrikeButton.IsChecked == true));
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignCenterBtn.IsChecked = false;
        AlignRightBtn.IsChecked  = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left));
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked  = false;
        AlignRightBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked   = false;
        AlignCenterBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right));
    }

    private void WrapTextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(WrapText: WrapTextBtn.IsChecked == true));
    }

    private void MergeCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var outcome = _commandBus.Execute(_workbook.Id, new MergeCellsCommand(_currentSheetId, range));
        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Cannot merge.", "Merge Cells",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
        UpdateViewport();
    }

    private void FontNameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (FontNameBox.SelectedItem is string name)
            ApplyStyleDiff(new StyleDiff(FontName: name));
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var text = FontSizeBox.Text;
        if (double.TryParse(text, out var size) && size > 0)
            ApplyStyleDiff(new StyleDiff(FontSize: size));
    }

    private void FontColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Font color (R,G,B e.g. 255,0,0):", "0,0,0");
        if (input is null) return;
        var parts = input.Split(',');
        if (parts.Length == 3 && byte.TryParse(parts[0].Trim(), out var r)
            && byte.TryParse(parts[1].Trim(), out var g) && byte.TryParse(parts[2].Trim(), out var b))
            ApplyStyleDiff(new StyleDiff(FontColor: new CellColor(r, g, b)));
    }

    private void FillColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Fill color (R,G,B e.g. 255,255,0):", "255,255,255");
        if (input is null) return;
        var parts = input.Split(',');
        if (parts.Length == 3 && byte.TryParse(parts[0].Trim(), out var r)
            && byte.TryParse(parts[1].Trim(), out var g) && byte.TryParse(parts[2].Trim(), out var b))
            ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(r, g, b)));
    }

    private void NumberFormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (NumberFormatBox.SelectedIndex < 0) return;
        var codes = new[] { "General", "0.00", "$#,##0.00", "0%", "yyyy-MM-dd", "HH:mm:ss", "@" };
        if (NumberFormatBox.SelectedIndex < codes.Length)
            ApplyStyleDiff(new StyleDiff(NumberFormat: codes[NumberFormatBox.SelectedIndex]));
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

    // ── Ribbon clipboard ─────────────────────────────────────────────────────

    private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(isCut: true); ExecuteClearSelection(); }
    private void CopyBtn_Click(object sender, RoutedEventArgs e)  { ExecuteCopy(); }
    private void PasteBtn_Click(object sender, RoutedEventArgs e) { ExecutePaste(); }

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

    // ── Context menu + Insert/Delete ─────────────────────────────────────────

    private void OnGridContextMenuRequested(CellAddress clickedCell, System.Windows.Point screenPos)
    {
        var actualAddr = new CellAddress(_currentSheetId, clickedCell.Row, clickedCell.Col);
        if (SheetGrid.SelectedRange is null)
            SetActiveCell(actualAddr);

        var menu = new ContextMenu();
        void AddItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem("Cut",   () => { ExecuteCopy(); ExecuteClearSelection(); });
        AddItem("Copy",  () => ExecuteCopy());
        AddItem("Paste", () => ExecutePaste());
        menu.Items.Add(new Separator());
        AddItem("Insert Row Above",    () => InsertRows(actualAddr.Row));
        AddItem("Insert Row Below",    () => InsertRows(actualAddr.Row + 1));
        AddItem("Insert Column Left",  () => InsertColumns(actualAddr.Col));
        AddItem("Insert Column Right", () => InsertColumns(actualAddr.Col + 1));
        menu.Items.Add(new Separator());
        AddItem("Delete Row(s)",    DeleteSelectedRows);
        AddItem("Delete Column(s)", DeleteSelectedColumns);
        menu.Items.Add(new Separator());
        AddItem("Format Cells...",  OpenFormatCellsDialog);
        menu.Items.Add(new Separator());
        AddItem("Clear Contents",   ExecuteClearSelection);

        menu.PlacementTarget = SheetGrid;
        menu.IsOpen = true;
    }

    private void InsertRows(uint beforeRow)
    {
        if (!TryExecuteGroupedSheetCommand("Insert Row", sheetId => new InsertRowsCommand(sheetId, beforeRow)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        if (!TryExecuteGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, beforeCol)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        uint count = range.End.Row - range.Start.Row + 1;
        if (!TryExecuteGroupedSheetCommand("Delete Row", sheetId => new DeleteRowsCommand(sheetId, range.Start.Row, count)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        uint count = range.End.Col - range.Start.Col + 1;
        if (!TryExecuteGroupedSheetCommand("Delete Column", sheetId => new DeleteColumnsCommand(sheetId, range.Start.Col, count)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private static bool IsCtrlPlus(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.Control &&
        (e.Key is Key.Add or Key.OemPlus || e.SystemKey is Key.Add or Key.OemPlus);

    private static bool IsCtrlMinus(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.Control &&
        (e.Key is Key.Subtract or Key.OemMinus || e.SystemKey is Key.Subtract or Key.OemMinus);

    private static bool IsCtrl9(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.D9 or Key.NumPad9;

    private static bool IsCtrlShift9(KeyEventArgs e) =>
        Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.D9 or Key.NumPad9;

    private static bool IsCtrl0(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.D0 or Key.NumPad0;

    private static bool IsCtrlShift0(KeyEventArgs e) =>
        Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.D0 or Key.NumPad0;

    private static bool TryGetNumberFormatShortcut(KeyEventArgs e, out NumberFormatShortcut shortcut)
    {
        shortcut = default;
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return false;

        shortcut = e.Key switch
        {
            Key.Oem3 => NumberFormatShortcut.General,
            Key.D1 => NumberFormatShortcut.Number,
            Key.D2 => NumberFormatShortcut.Time,
            Key.D3 => NumberFormatShortcut.Date,
            Key.D4 => NumberFormatShortcut.Currency,
            Key.D5 => NumberFormatShortcut.Percentage,
            Key.D6 => NumberFormatShortcut.Scientific,
            _ => default
        };

        return e.Key is Key.Oem3 or Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6;
    }

    private void ApplyNumberFormatShortcut(NumberFormatShortcut shortcut) =>
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatShortcutService.GetFormat(shortcut)));

    private void ApplyFontToggleShortcut(FontToggleShortcut shortcut, ToggleButton button)
    {
        button.IsChecked = !(button.IsChecked == true);
        ApplyStyleDiff(FontToggleShortcutService.CreateDiff(shortcut, button.IsChecked == true));
    }

    private static bool IsBoldShortcut(KeyEventArgs e) =>
        (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) != 0) ||
        ((e.Key is Key.D2 or Key.NumPad2) && Keyboard.Modifiers == ModifierKeys.Control);

    private static bool IsItalicShortcut(KeyEventArgs e) =>
        (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) != 0) ||
        ((e.Key is Key.D3 or Key.NumPad3) && Keyboard.Modifiers == ModifierKeys.Control);

    private static bool IsUnderlineShortcut(KeyEventArgs e) =>
        (e.Key == Key.U && (Keyboard.Modifiers & ModifierKeys.Control) != 0) ||
        ((e.Key is Key.D4 or Key.NumPad4) && Keyboard.Modifiers == ModifierKeys.Control);

    private static bool IsCtrlShiftAmpersand(KeyEventArgs e) =>
        Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D7;

    private static bool IsCtrlShiftUnderscore(KeyEventArgs e) =>
        Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.OemMinus;

    private void ApplyOutlineBorderShortcut()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var address = new CellAddress(_currentSheetId, r, c);
                var diff = BorderShortcutService.GetOutlineBorderDiff(range, address);
                if (!TryExecuteApplyStyle(new GridRange(address, address), diff, "Outline Border"))
                    return;
            }
        }

        UpdateViewport();
    }

    private void ExecuteKeyboardInsert()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            if (!TryExecuteGroupedSheetCommand("Insert Row", sheetId => new InsertRowsCommand(sheetId, range.Start.Row, range.RowCount)))
                return;
        }
        else if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            if (!TryExecuteGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, range.Start.Col, range.ColCount)))
                return;
        }
        else if (!TryExecuteGroupedSheetCommand(
                     "Insert Cells",
                     sheetId => new InsertCellsCommand(sheetId, RemapRangeToSheet(range, sheetId), InsertCellsShiftDirection.Down)))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteKeyboardDelete()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            if (!TryExecuteGroupedSheetCommand("Delete Row", sheetId => new DeleteRowsCommand(sheetId, range.Start.Row, range.RowCount)))
                return;
        }
        else if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            if (!TryExecuteGroupedSheetCommand("Delete Column", sheetId => new DeleteColumnsCommand(sheetId, range.Start.Col, range.ColCount)))
                return;
        }
        else if (!TryExecuteGroupedSheetCommand(
                     "Delete Cells",
                     sheetId => new DeleteCellsCommand(sheetId, RemapRangeToSheet(range, sheetId), DeleteCellsShiftDirection.Up)))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteRowsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        if (!TryExecuteGroupedSheetCommand(
                hidden ? "Hide Row" : "Unhide Row",
                sheetId => new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden)))
            return;

        UpdateViewport();
    }

    private void ExecuteColumnsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        if (!TryExecuteGroupedSheetCommand(
                hidden ? "Hide Column" : "Unhide Column",
                sheetId => new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden)))
            return;

        UpdateViewport();
    }

    private void OpenFormatCellsDialog()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var currentStyle = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        var dlg = new FormatCellsDialog(currentStyle) { Owner = this };
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
            StatusReadyText.Text = GetReadyStatusText(sheet, range.Start);
            return;
        }

        StatusReadyText.Visibility  = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Visible;
        StatusAvgText.Text   = stats.Average.HasValue ? $"Average: {FormatStatusNumber(stats.Average.Value)}" : "";
        StatusCountText.Text = $"Count: {stats.Count}";
        StatusSumText.Text   = $"Sum: {FormatStatusNumber(stats.Sum)}";
        StatusMinText.Text   = stats.Min.HasValue ? $"Min: {FormatStatusNumber(stats.Min.Value)}" : "";
        StatusMaxText.Text   = stats.Max.HasValue ? $"Max: {FormatStatusNumber(stats.Max.Value)}" : "";
    }

    private static string FormatStatusNumber(double value)
    {
        // Show integers without decimal places; use comma grouping for large numbers
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            return value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        return value.ToString("G10", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static string GetReadyStatusText(Sheet sheet, CellAddress activeCell)
    {
        var prompt = DataValidationService.GetInputPrompt(sheet, activeCell);
        if (prompt is null)
            return "Ready";

        if (prompt.Title.Length == 0)
            return prompt.Message;

        if (prompt.Message.Length == 0)
            return prompt.Title;

        return $"{prompt.Title}: {prompt.Message}";
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

    private void ExecuteCopy(bool isCut = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        var text = ClipboardSerializer.Serialize(viewport, range);
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked */ }

        // Show marching ants around the copied range
        SheetGrid.ClipboardRange = range;

        // Capture raw cells (including formulas) for paste formula adjustment
        var sheet = _workbook.GetSheet(_currentSheetId);
        var clipCells = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var addr = new CellAddress(_currentSheetId, r, c);
                var cell = sheet?.GetCell(r, c);
                clipCells.Add((addr, cell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }
        _internalClipboard = new InternalClipboard(range, clipCells);
    }

    private void ExecutePaste(PasteMode mode = PasteMode.All, PasteSpecialOptions options = default)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        // If we have an internal clipboard (copied from within this app), use it with formula adjustment
        if (_internalClipboard is { } clip)
        {
            if (options.Transpose || options.Operation != PasteSpecialOperation.None)
            {
                var sourceCells = clip.Cells
                    .Select(c => (c.Item1, mode == PasteMode.Values || options.Operation != PasteSpecialOperation.None
                        ? Cell.FromValue(c.Item2.Value)
                        : c.Item2.Clone()))
                    .ToList();
                var specialCommand = new PasteSpecialCellsCommand(
                    _currentSheetId,
                    clip.SourceRange,
                    sourceCells,
                    range.Start,
                    options);
                if (!TryExecuteCommand(specialCommand, "Paste Special", out var specialOutcome))
                    return;

                RecalculateIfAutomatic(specialOutcome.AffectedCells ?? []);
                var pastedRows = options.Transpose ? clip.SourceRange.ColCount : clip.SourceRange.RowCount;
                var pastedCols = options.Transpose ? clip.SourceRange.RowCount : clip.SourceRange.ColCount;
                var specialPastedEnd = new CellAddress(
                    _currentSheetId,
                    range.Start.Row + (uint)pastedRows - 1,
                    range.Start.Col + (uint)pastedCols - 1);
                _selectionAnchor = range.Start;
                _selectionCursor = specialPastedEnd;
                SheetGrid.SelectedRanges = null;
                SheetGrid.SelectedRange = new GridRange(range.Start, specialPastedEnd);
                SheetGrid.ClipboardRange = null;
                UpdateViewport();
                RefreshToolbar();
                return;
            }

            var edits = new List<(CellAddress, Cell)>();
            var formats = new List<(CellAddress, StyleId)>();
            int rowDelta = (int)range.Start.Row - (int)clip.SourceRange.Start.Row;
            int colDelta = (int)range.Start.Col - (int)clip.SourceRange.Start.Col;
            var pasteOp  = new Freexcel.Core.Formula.PasteOffsetOp(rowDelta, colDelta);
            var activeSheetName = _workbook.GetSheet(_currentSheetId)?.Name ?? "";

            foreach (var (sourceAddr, sourceCell) in clip.Cells)
            {
                var destAddr = new CellAddress(_currentSheetId,
                    (uint)((int)sourceAddr.Row + rowDelta),
                    (uint)((int)sourceAddr.Col + colDelta));

                var destCell = sourceCell.Clone();

                if (mode == PasteMode.Formats)
                {
                    formats.Add((destAddr, sourceCell.StyleId));
                    continue;
                }

                if (mode == PasteMode.Values)
                {
                    edits.Add((destAddr, Cell.FromValue(sourceCell.Value)));
                    continue;
                }

                if (destCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
                {
                    destCell.FormulaText =
                        Freexcel.Core.Formula.FormulaRewriter.Rewrite(
                            destCell.FormulaText, pasteOp, activeSheetName)
                        ?? destCell.FormulaText;
                }

                if (mode == PasteMode.Formulas && !destCell.HasFormula)
                    destCell = Cell.FromValue(sourceCell.Value);

                edits.Add((destAddr, destCell));
            }

            if (formats.Count > 0)
            {
                var formatCommand = new PasteFormatsCommand(_currentSheetId, formats);
                if (!TryExecuteCommand(formatCommand, "Paste Special"))
                    return;
            }

            if (edits.Count > 0)
            {
                CommandOutcome pasteOutcome;
                var pasted = mode == PasteMode.All
                    ? TryExecuteCommand(new PasteCellsCommand(_currentSheetId, edits), "Paste", out pasteOutcome)
                    : TryExecuteEditCells(edits, "Paste", out pasteOutcome);
                if (!pasted)
                    return;
                if (mode != PasteMode.Formats)
                    RecalculateIfAutomatic(pasteOutcome.AffectedCells ?? edits.Select(e => e.Item1).ToList());
            }

            var pastedRowSpan = (uint)(clip.SourceRange.RowCount - 1);
            var pastedColSpan = (uint)(clip.SourceRange.ColCount - 1);
            var pastedEnd     = new CellAddress(_currentSheetId,
                range.Start.Row + pastedRowSpan,
                range.Start.Col + pastedColSpan);
            _selectionAnchor = range.Start;
            _selectionCursor = pastedEnd;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
            SheetGrid.ClipboardRange = null;
            UpdateViewport();
            RefreshToolbar();
            return;
        }

        if (mode == PasteMode.Formats || mode == PasteMode.Formulas)
            return;

        if (mode == PasteMode.All && TryPasteClipboardImage(range.Start))
            return;

        // Fallback: external clipboard (plain text)
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var rows = ClipboardSerializer.Deserialize(text);
        var fallbackEdits = new List<(CellAddress, Cell)>();

        for (int ri = 0; ri < rows.Length; ri++)
        {
            for (int ci = 0; ci < rows[ri].Length; ci++)
            {
                var addr = new CellAddress(_currentSheetId,
                    range.Start.Row + (uint)ri,
                    range.Start.Col + (uint)ci);
                ScalarValue val = double.TryParse(rows[ri][ci],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.CurrentCulture, out var d)
                    ? new NumberValue(d)
                    : new TextValue(rows[ri][ci]);
                fallbackEdits.Add((addr, Cell.FromValue(val)));
            }
        }

        if (fallbackEdits.Count == 0) return;

        if (!TryExecuteEditCells(fallbackEdits, "Paste", out var fallbackOutcome))
            return;
        RecalculateIfAutomatic(fallbackOutcome.AffectedCells ?? fallbackEdits.Select(e => e.Item1).ToList());

        uint pastedRowSpanFallback = rows.Length > 0 ? (uint)(rows.Length - 1) : 0;
        uint pastedColSpanFallback = rows.Length > 0 && rows[0].Length > 0 ? (uint)(rows[0].Length - 1) : 0;
        var pastedEndFallback = new CellAddress(_currentSheetId,
            range.Start.Row + pastedRowSpanFallback,
            range.Start.Col + pastedColSpanFallback);
        _selectionAnchor = range.Start;
        _selectionCursor = pastedEndFallback;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEndFallback);
        SheetGrid.ClipboardRange = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private bool TryPasteClipboardImage(CellAddress anchor)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
                return false;

            var image = System.Windows.Clipboard.GetImage();
            if (image is null)
                return false;

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);

            if (!TryExecuteCommand(
                    ClipboardPictureService.CreateInsertCommand(
                        _currentSheetId,
                        anchor,
                        stream.ToArray(),
                        image.PixelWidth,
                        image.PixelHeight),
                    "Paste Picture"))
                return true;

            SheetGrid.ClipboardRange = null;
            UpdateViewport();
            RefreshToolbar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ExecuteClearSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
                edits.Add((new CellAddress(_currentSheetId, r, c), Cell.FromValue(BlankValue.Instance)));

        if (!TryExecuteEditCells(edits, "Clear"))
            return;
        UpdateViewport();
    }

    // ── Print / Export ────────────────────────────────────────────────────────

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var viewer = new System.Windows.Controls.DocumentViewer { Document = doc };
        var previewWin = new Window
        {
            Title = $"Print Preview — {_workbook.Name}",
            Width = 900, Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Content = viewer
        };
        previewWin.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export as PDF / XPS",
            Filter     = "PDF files (*.pdf)|*.pdf|XPS files (*.xps)|*.xps",
            DefaultExt = ".pdf",
            FileName   = _workbook.Name
        };
        if (saveDlg.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(saveDlg.FileName).ToLowerInvariant();
        if (ext == ".pdf")
            ExportViaPrintToPdf(saveDlg.FileName);
        else
            ExportAsXps(saveDlg.FileName);
    }

    /// <summary>
    /// Tries to export directly to PDF by routing through the "Microsoft Print to PDF"
    /// virtual printer. If the printer is unavailable, falls back to XPS and informs the user.
    /// </summary>
    private void ExportViaPrintToPdf(string pdfPath)
    {
        // Look for a PDF-capable print queue (case-insensitive)
        System.Printing.PrintQueue? pdfQueue = null;
        try
        {
            using var server = new System.Printing.LocalPrintServer();
            pdfQueue = server.GetPrintQueues()
                .FirstOrDefault(q => q.Name.Contains("PDF", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Print-spooler unavailable — fall through to XPS fallback
        }

        if (pdfQueue != null)
        {
            // The WPF PrintDialog API can target a specific queue but cannot programmatically
            // set the output file path for the Microsoft Print to PDF virtual printer through
            // the managed API alone. We fall back to XPS (which Windows can open/convert to PDF).
            var xpsPath = System.IO.Path.ChangeExtension(pdfPath, ".xps");
            ExportAsXps(xpsPath);
            MessageBox.Show(
                $"Saved as XPS: {xpsPath}\n\n" +
                "Open the file in XPS Viewer and print to any PDF printer, " +
                "or use File → Print and select 'Microsoft Print to PDF' to save directly as PDF.",
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            // No PDF printer found; just save XPS
            var xpsPath = System.IO.Path.ChangeExtension(pdfPath, ".xps");
            ExportAsXps(xpsPath);
            MessageBox.Show(
                $"No PDF printer found on this system.\n\nSaved as XPS: {xpsPath}",
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Writes the current sheet as an XPS package to <paramref name="xpsPath"/>.
    /// Uses the internal <c>XpsDocumentWriter(XpsDocument)</c> constructor (available in
    /// ReachFramework on .NET 10 / .NET Framework) to write directly to a file without
    /// showing a print dialog.
    /// </summary>
    // ── Format Painter ───────────────────────────────────────────────────────

    private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        _formatPainterStyleId = sheet?.GetCell(range.Start)?.StyleId ?? StyleId.Default;
        _formatPainterActive = true;
    }

    // Call from cell-click path: if painter active, apply stored style
    private bool TryApplyFormatPainter(CellAddress addr)
    {
        if (!_formatPainterActive) return false;
        _formatPainterActive = false;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(_formatPainterStyleId);
        var diff = StyleDiff.FromStyle(style);
        if (!TryExecuteApplyStyle(new GridRange(addr, addr), diff, "Format Painter"))
            return true;

        UpdateViewport();
        return true;
    }

    // ── Paste Special ────────────────────────────────────────────────────────

    private void PasteSpecialBtn_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var dlg = new PasteSpecialDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var options = new PasteSpecialOptions(
            Transpose: dlg.Transpose,
            Operation: dlg.Operation.ToLowerInvariant() switch
            {
                "add" => PasteSpecialOperation.Add,
                "subtract" => PasteSpecialOperation.Subtract,
                "multiply" => PasteSpecialOperation.Multiply,
                "divide" => PasteSpecialOperation.Divide,
                _ => PasteSpecialOperation.None
            });
        var keepColumnWidths = dlg.KeepColumnWidths;
        if (dlg.PastePicture)
        {
            ExecutePasteAsPicture();
            return;
        }

        if (dlg.PasteLink)
        {
            ExecutePasteLink(options.Transpose);
            if (keepColumnWidths && _internalClipboard is { } linkClip && SheetGrid.SelectedRange is { } linkTargetRange)
            {
                if (TryExecuteCommand(
                        new PasteColumnWidthsCommand(_currentSheetId, linkClip.SourceRange, linkTargetRange.Start.Col),
                        "Paste Column Widths"))
                    UpdateViewport();
            }
            return;
        }

        if (dlg.PasteValues)
            ExecutePaste(PasteMode.Values, options);
        else if (dlg.PasteFormulas)
            ExecutePaste(PasteMode.Formulas, options);
        else if (dlg.PasteFormats)
            ExecutePaste(PasteMode.Formats, options);
        else
            ExecutePaste(PasteMode.All, options);

        if (keepColumnWidths && _internalClipboard is { } clip && SheetGrid.SelectedRange is { } targetRange)
        {
            if (TryExecuteCommand(
                    new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, targetRange.Start.Col),
                    "Paste Column Widths"))
                UpdateViewport();
        }
    }

    private void ExecutePasteAsPicture()
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceCells = clip.Cells
            .Select(c => (c.Item1, FormatPictureCellText(c.Item2.Value)))
            .ToList();
        if (!TryExecuteCommand(
                new PasteRangeAsPictureCommand(_currentSheetId, clip.SourceRange, sourceCells, range.Start),
                "Paste Picture"))
            return;

        SheetGrid.ClipboardRange = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private static string FormatPictureCellText(ScalarValue value) =>
        value switch
        {
            BlankValue => "",
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            TextValue t => t.Value,
            ErrorValue e => e.Code,
            _ => value.ToString() ?? ""
        };

    private void ExecutePasteLink(bool transpose)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceSheet = _workbook.GetSheet(clip.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return;

        var linkedCells = PasteLinkService.CreateLinkedCells(
            clip.SourceRange,
            range.Start,
            sourceSheet.Name,
            transpose);
        if (!TryExecuteEditCells(linkedCells, "Paste Link", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? linkedCells.Select(c => c.Address).ToList());
        var pastedRows = transpose ? clip.SourceRange.ColCount : clip.SourceRange.RowCount;
        var pastedCols = transpose ? clip.SourceRange.RowCount : clip.SourceRange.ColCount;
        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)pastedRows - 1,
            range.Start.Col + (uint)pastedCols - 1);
        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        SheetGrid.ClipboardRange = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void InsertCurrentDateOrTime(bool insertTime)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = insertTime
            ? DateTimeEntryService.CurrentTime(DateTime.Now)
            : DateTimeEntryService.CurrentDate(DateTime.Now);
        if (!TryExecuteEditCells([(range.Start, Cell.FromValue(value))],
                insertTime ? "Insert Time" : "Insert Date",
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    // ── Font group additions ─────────────────────────────────────────────────

    private void DoubleUnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var isOn = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        ApplyStyleDiff(new StyleDiff(DoubleUnderline: isOn, Underline: isOn ? false : null));
    }

    private void IncreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        double newSize = style.FontSize switch { < 10 => style.FontSize + 1, < 24 => style.FontSize + 2, _ => style.FontSize + 4 };
        ApplyStyleDiff(new StyleDiff(FontSize: newSize));
    }

    private void DecreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        double newSize = style.FontSize switch { <= 10 => Math.Max(1, style.FontSize - 1), <= 26 => style.FontSize - 2, _ => style.FontSize - 4 };
        ApplyStyleDiff(new StyleDiff(FontSize: newSize));
    }

    // ── Border picker ────────────────────────────────────────────────────────

    private void BorderPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void BorderAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var b = new CellBorder(BorderStyle.Thin, CellColor.Black);
        ApplyStyleDiff(new StyleDiff(BorderTop: b, BorderRight: b, BorderBottom: b, BorderLeft: b));
    }
    private void BorderOutsideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyOutlineBorderShortcut();
    }
    private void BorderNoneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
    }
    private void BorderBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderBottom: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderTopMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderTop: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderLeft: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderRight: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderBottom: new CellBorder(BorderStyle.Thick, CellColor.Black)));

    // ── Alignment group additions ────────────────────────────────────────────

    private void AlignTopBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top));
    private void AlignMiddleBtn_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center));
    private void AlignBottomBtn_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom));

    private void IndentIncBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)));
    }
    private void IndentDecBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)));
    }

    private void OrientationPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientHorizMenuItem_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(TextRotation: 0));
    private void OrientAngleCCWMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: 45));
    private void OrientAngleCWMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: -45));
    private void OrientVertMenuItem_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateUpMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateDownMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: -90));

    // ── Number group additions ───────────────────────────────────────────────

    private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: "$#,##0.00"));
    private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: "0%"));
    private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: "#,##0.00"));

    private void IncDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: AddDecimalPlace(style.NumberFormat)));
    }
    private void DecDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: RemoveDecimalPlace(style.NumberFormat)));
    }

    private static string AddDecimalPlace(string fmt)
    {
        if (string.IsNullOrEmpty(fmt) || fmt == "General") return "0.0";
        var m = System.Text.RegularExpressions.Regex.Match(fmt, @"(\d*)(\.(\d*))");
        if (m.Success) return fmt.Remove(m.Index, m.Length).Insert(m.Index, m.Groups[1].Value + "." + m.Groups[3].Value + "0");
        var m2 = System.Text.RegularExpressions.Regex.Match(fmt, @"(\d+)");
        if (m2.Success) return fmt.Remove(m2.Index, m2.Length).Insert(m2.Index, m2.Value + ".0");
        return fmt + ".0";
    }
    private static string RemoveDecimalPlace(string fmt)
    {
        if (string.IsNullOrEmpty(fmt) || fmt == "General") return "0";
        var m = System.Text.RegularExpressions.Regex.Match(fmt, @"\.(\d+)");
        if (!m.Success) return fmt;
        if (m.Groups[1].Value.Length <= 1) return fmt.Remove(m.Index, m.Length);
        return fmt.Remove(m.Index + m.Length - 1, 1);
    }

    // ── Styles group ─────────────────────────────────────────────────────────

    private void CfPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CfGtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Greater Than");
    private void CfLtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Less Than");
    private void CfBetweenMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Between");
    private void CfEqMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Equal To");
    private void CfTextMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Text Contains");
    private void CfDateMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Date Occurring");
    private void CfDuplicateMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Duplicate Values");
    private void CfTop10MenuItem_Click(object sender, RoutedEventArgs e)    => ShowCfDialog("Top 10 Items");
    private void CfBottom10MenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Bottom 10 Items");
    private void CfAboveAvgMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Above Average");
    private void CfDataBarMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Data Bar");
    private void CfColorScaleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Color Scale");
    private void CfIconSetMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Icon Set");
    private void CfNewRuleMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("New Rule");
    private void CfNewFormulaRuleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Formula");
    private void CfClearRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand(
                "Clear Conditional Formatting",
                sheetId => new ClearConditionalFormatsCommand(sheetId, RemapRangeToSheet(range, sheetId))))
            return;
        UpdateViewport();
    }
    private void CfManageRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var dlg = new ManageConditionalFormatsDialog(sheet, SheetGrid.SelectedRange) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null) return;
        var newRules = dlg.ResultRules;
        if (!TryExecuteGroupedSheetCommand(
                "Manage Conditional Formatting Rules",
                sheetId =>
                {
                    var remapped = newRules
                        .Select(r => CloneConditionalFormatForSheet(r, sheetId))
                        .ToList();
                    return new ReplaceAllConditionalFormatsCommand(sheetId, remapped);
                }))
            return;
        UpdateViewport();
    }

    private void ShowCfDialog(string ruleType)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = new ConditionalFormatDialog(ruleType, range) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRule is null) return;
        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, CloneConditionalFormatForSheet(dlg.ResultRule, sheetId))))
            return;
        UpdateViewport();
    }

    private void FormatTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormatTableLightMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyTableFormat(0);
    private void FormatTableMediumMenuItem_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(1);
    private void FormatTableDarkMenuItem_Click(object sender, RoutedEventArgs e)   => ApplyTableFormat(2);

    private void ApplyTableFormat(int variant)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var (headerFill, oddFill, evenFill) = variant switch
        {
            1 => (new CellColor(31, 78, 121), new CellColor(222, 235, 247), new CellColor(255, 255, 255)),
            2 => (new CellColor(54, 54, 54),  new CellColor(68, 68, 68),    new CellColor(80, 80, 80)),
            _ => (new CellColor(31, 115, 70), new CellColor(226, 239, 218), new CellColor(255, 255, 255))
        };
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var fill = r == range.Start.Row ? headerFill : (r % 2 == 0 ? evenFill : oddFill);
            var fontColor = r == range.Start.Row ? CellColor.White : CellColor.Black;
            var bold = r == range.Start.Row;
            if (!TryExecuteApplyStyle(
                    new GridRange(
                        new CellAddress(_currentSheetId, r, range.Start.Col),
                        new CellAddress(_currentSheetId, r, range.End.Col)),
                    new StyleDiff(FillColor: fill, FontColor: fontColor, Bold: bold),
                    "Format as Table"))
                return;
        }
        UpdateViewport();
    }

    private void CellStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(198, 239, 206), FontColor: new CellColor(0, 97, 0)));
    private void CellStyleBadMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 199, 206), FontColor: new CellColor(156, 0, 6)));
    private void CellStyleNeutralMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 235, 156), FontColor: new CellColor(156, 101, 0)));
    private void CellStyleH1MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, FontSize: 16, FillColor: new CellColor(31, 115, 70), FontColor: CellColor.White));
    private void CellStyleH2MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, FontSize: 14));
    private void CellStyleNoteMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 255, 204), BorderBottom: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void CellStyleWarningMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 192, 0), FontColor: CellColor.Black, Bold: true));
    private void CellStyleTotalMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, BorderTop: new CellBorder(BorderStyle.Thin, CellColor.Black),
            BorderBottom: new CellBorder(BorderStyle.Double, CellColor.Black)));

    // ── Cells group (pickers) ────────────────────────────────────────────────

    private void InsertPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void DeletePickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormatPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void InsertCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Shift cells (right/down):", "right");
        if (input is null) return;

        var direction = input.Trim().Equals("down", StringComparison.OrdinalIgnoreCase)
            ? InsertCellsShiftDirection.Down
            : InsertCellsShiftDirection.Right;
        if (!TryExecuteCommand(new InsertCellsCommand(_currentSheetId, range, direction), "Insert Cells", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }
    private void DeleteCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Shift cells (left/up):", "left");
        if (input is null) return;

        var direction = input.Trim().Equals("up", StringComparison.OrdinalIgnoreCase)
            ? DeleteCellsShiftDirection.Up
            : DeleteCellsShiftDirection.Left;
        if (!TryExecuteCommand(new DeleteCellsCommand(_currentSheetId, range, direction), "Delete Cells", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || _workbook.Sheets.Count <= 1) { MessageBox.Show("Cannot delete the only sheet."); return; }
        if (MessageBox.Show($"Delete '{sheet.Name}'?", "Delete Sheet", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var outcome = _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(_currentSheetId));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Delete Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[0].Id;
        RecalculateWorkbook();
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Row height (pixels):", "20");
        if (input is null || !double.TryParse(input, out var h) || h <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, h)))
            return;
        UpdateViewport();
    }
    private void FormatAutoRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Row Height", sheetId => new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height: null)))
            return;
        UpdateViewport();
    }
    private void FormatColWidthMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Column width (character units):", "8");
        if (input is null || !double.TryParse(input, out var w) || w <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, w)))
            return;
        UpdateViewport();
    }
    private void FormatAutoColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Column Width", sheetId => new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width: null)))
            return;
        UpdateViewport();
    }
    private void FormatDefaultWidthMenuItem_Click(object sender, RoutedEventArgs e) { FormatColWidthMenuItem_Click(sender, e); }
    private void FormatHideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Hide Row", sheetId => new SetRowsHiddenCommand(sheetId, range.Start.Row, range.End.Row, hidden: true)))
            return;
        UpdateViewport();
    }
    private void FormatUnhideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Unhide Row", sheetId => new SetRowsHiddenCommand(sheetId, range.Start.Row, range.End.Row, hidden: false)))
            return;
        UpdateViewport();
    }
    private void FormatHideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Hide Column", sheetId => new SetColumnsHiddenCommand(sheetId, range.Start.Col, range.End.Col, hidden: true)))
            return;
        UpdateViewport();
    }
    private void FormatUnhideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Unhide Column", sheetId => new SetColumnsHiddenCommand(sheetId, range.Start.Col, range.End.Col, hidden: false)))
            return;
        UpdateViewport();
    }
    private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }
    private void FormatLockCellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(Locked: !style.Locked));
    }

    // ── Editing group (pickers) ──────────────────────────────────────────────

    private void AutoSumPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormulasAutoSumPickerBtn_Click(object sender, RoutedEventArgs e) { AutoSumPickerBtn_Click(sender, e); }

    private void InsertAutoSumFormula(string func)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var addr = range.Start;
        // Look above for a contiguous numeric range
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        uint topRow = addr.Row;
        while (topRow > 1 && sheet.GetValue(topRow - 1, addr.Col) is NumberValue) topRow--;
        if (topRow == addr.Row) // try to the left
        {
            uint leftCol = addr.Col;
            while (leftCol > 1 && sheet.GetValue(addr.Row, leftCol - 1) is NumberValue) leftCol--;
            if (leftCol < addr.Col)
            {
                var rangeRef = $"{CellAddress.NumberToColumnName(leftCol)}{addr.Row}:{CellAddress.NumberToColumnName(addr.Col - 1)}{addr.Row}";
                CommitFormulaAt(addr, $"{func}({rangeRef})");
                return;
            }
        }
        var rangeStr = topRow < addr.Row
            ? $"{CellAddress.NumberToColumnName(addr.Col)}{topRow}:{CellAddress.NumberToColumnName(addr.Col)}{addr.Row - 1}"
            : $"{CellAddress.NumberToColumnName(addr.Col)}{Math.Max(1, addr.Row - 1)}:{CellAddress.NumberToColumnName(addr.Col)}{addr.Row}";
        CommitFormulaAt(addr, $"{func}({rangeStr})");
    }

    private void CommitFormulaAt(CellAddress addr, string formula)
    {
        if (!TryExecuteEditCells([(addr, Cell.FromFormula(formula))], "AutoSum", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [addr]);
        SetActiveCell(new CellAddress(_currentSheetId, addr.Row + 1, addr.Col));
        UpdateViewport();
    }

    private void AutoSumSumMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("SUM");
    private void AutoSumAvgMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("AVERAGE");
    private void AutoSumCountMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula("COUNT");
    private void AutoSumMaxMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MAX");
    private void AutoSumMinMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MIN");
    private void AutoSumMoreMenuItem_Click(object sender, RoutedEventArgs e)  => InsertFunctionBtn_Click(sender, e);

    private void FillPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FillDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range || range.RowCount < 2) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var edits = new List<(CellAddress, Cell)>();
        for (uint c = range.Start.Col; c <= range.End.Col; c++)
        {
            var srcCell = sheet.GetCell(new CellAddress(_currentSheetId, range.Start.Row, c));
            for (uint r = range.Start.Row + 1; r <= range.End.Row; r++)
                edits.Add((new CellAddress(_currentSheetId, r, c), srcCell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
        }
        if (!TryExecuteEditCells(edits, "Fill Down", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }
    private void FillRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range || range.ColCount < 2) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var srcCell = sheet.GetCell(new CellAddress(_currentSheetId, r, range.Start.Col));
            for (uint c = range.Start.Col + 1; c <= range.End.Col; c++)
                edits.Add((new CellAddress(_currentSheetId, r, c), srcCell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
        }
        if (!TryExecuteEditCells(edits, "Fill Right", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }
    private void FillUpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range || range.RowCount < 2) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var edits = new List<(CellAddress, Cell)>();
        for (uint c = range.Start.Col; c <= range.End.Col; c++)
        {
            var srcCell = sheet.GetCell(new CellAddress(_currentSheetId, range.End.Row, c));
            for (uint r = range.Start.Row; r < range.End.Row; r++)
                edits.Add((new CellAddress(_currentSheetId, r, c), srcCell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
        }
        if (!TryExecuteEditCells(edits, "Fill Up", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }
    private void FillLeftMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range || range.ColCount < 2) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var srcCell = sheet.GetCell(new CellAddress(_currentSheetId, r, range.End.Col));
            for (uint c = range.Start.Col; c < range.End.Col; c++)
                edits.Add((new CellAddress(_currentSheetId, r, c), srcCell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
        }
        if (!TryExecuteEditCells(edits, "Fill Left", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }
    private void FillSeriesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Basic: fill a linear series starting from selected cell
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var startVal = sheet.GetValue(range.Start.Row, range.Start.Col) as NumberValue;
        if (startVal is null) { MessageBox.Show("Select a cell with a numeric value to start a series."); return; }
        var stepInput = PromptForInput("Step value:", "1");
        if (stepInput is null || !double.TryParse(stepInput, out var step)) return;
        var edits = new List<(CellAddress, Cell)>();
        double val = startVal.Value;
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (r == range.Start.Row && c == range.Start.Col) { val += step; continue; }
                edits.Add((new CellAddress(_currentSheetId, r, c), Cell.FromValue(new NumberValue(val))));
                val += step;
            }
        if (!TryExecuteEditCells(edits, "Fill Series", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }

    private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();

    private void TryFlashFill()
    {
        var range = SheetGrid.SelectedRange;
        if (range is null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        uint fillCol = range.Value.Start.Col;
        uint sourceCol = fillCol > 1 ? fillCol - 1 : fillCol + 1;
        uint startRow = range.Value.Start.Row;
        uint endRow = range.Value.End.Row;

        // If selection is a single cell, auto-extend end row to the data region
        if (startRow == endRow)
        {
            // Extend down to the last non-blank row in either the fill or source column
            uint maxRow = startRow;
            for (uint r = startRow + 1; r <= CellAddress.MaxRow; r++)
            {
                var fillVal = sheet.GetValue(r, fillCol);
                var srcVal = sheet.GetValue(r, sourceCol);
                if (fillVal is BlankValue && srcVal is BlankValue)
                    break;
                maxRow = r;
            }
            endRow = maxRow;
        }

        var cmd = new FlashFillCommand(_currentSheetId, fillCol, sourceCol, startRow, endRow);
        if (!TryExecuteCommand(cmd, "Flash Fill", out var outcome))
            return;

        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorMessage ?? "Flash Fill could not detect a pattern.",
                "Flash Fill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void SortFilterPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);
    private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);
    private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortCustomButton_Click(sender, e);
    private void FilterToggleMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);
    private void FilterClearMenuItem_Click(object sender, RoutedEventArgs e)  => ClearFilterButton_Click(sender, e);
    private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);

    private void FindSelectPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FindFindMenuItem_Click(object sender, RoutedEventArgs e)       => FindButton_Click(sender, e);
    private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)    => ReplaceButton_Click(sender, e);
    private void FindGoToMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Go To cell (e.g. B5):", "A1");
        if (input is null) return;
        try
        {
            var addr = CellAddress.Parse(input.Trim(), _currentSheetId);
            SetActiveCell(addr);
            EnsureCellVisible(addr);
        }
        catch { MessageBox.Show("Invalid cell address."); }
    }
    private void FindGoToSpecialMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = SheetGrid.SelectedRange ?? sheet.GetUsedRange() ??
            new GridRange(new CellAddress(_currentSheetId, 1, 1), new CellAddress(_currentSheetId, 1, 1));
        var input = PromptForInput("Go To Special (blanks/constants/formulas/comments/validation/visible):", "blanks");
        if (input is null) return;

        var kind = input.Trim().ToLowerInvariant() switch
        {
            "constant" or "constants" => GoToSpecialKind.Constants,
            "formula" or "formulas" => GoToSpecialKind.Formulas,
            "comment" or "comments" => GoToSpecialKind.Comments,
            "validation" or "data validation" => GoToSpecialKind.DataValidation,
            "visible" or "visible cells" => GoToSpecialKind.VisibleCellsOnly,
            _ => GoToSpecialKind.Blanks
        };

        var matches = GoToSpecialService.Find(sheet, range, kind);
        if (matches.Count == 0)
        {
            MessageBox.Show("No cells found.", "Go To Special", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var compressedRanges = SelectionRangeService.CompressAddresses(matches);
        _selectionAnchor = matches[0];
        _selectionCursor = matches[0];
        SheetGrid.SelectedRange = new GridRange(matches[0], matches[0]);
        SheetGrid.SelectedRanges = compressedRanges;
        CellAddressBox.Text = compressedRanges.Count == 1
            ? FormatRangeReference(compressedRanges[0].Start, compressedRanges[0].End)
            : $"{matches.Count} cells";
        EnsureCellVisible(matches[0]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ClearPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ClearValues();
        ClearFormats();
    }
    private void ClearFormatsMenuItem_Click(object sender, RoutedEventArgs e) => ClearFormats();
    private void ClearValuesMenuItem_Click(object sender, RoutedEventArgs e)  => ClearValues();
    private void ClearCommentsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteCommand(new ClearCommentsCommand(_currentSheetId, range), "Clear Comments"))
            return;

        UpdateViewport();
    }
    private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteCommand(new ClearHyperlinksCommand(_currentSheetId, range), "Clear Hyperlinks"))
            return;
        UpdateViewport();
    }

    private void ClearValues()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var edits = new List<(CellAddress, Cell)>();
        var sheet = _workbook.GetSheet(_currentSheetId);
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var existing = sheet?.GetCell(new CellAddress(_currentSheetId, r, c));
                var cleared = Cell.FromValue(BlankValue.Instance);
                if (existing is not null) cleared.StyleId = existing.StyleId;
                edits.Add((new CellAddress(_currentSheetId, r, c), cleared));
            }
        if (!TryExecuteEditCells(edits, "Clear Values"))
            return;
        UpdateViewport();
    }
    private void ClearFormats()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyStyleDiff(new StyleDiff(
            Bold: false, Italic: false, Underline: false, DoubleUnderline: false, Strikethrough: false,
            FontName: "Calibri", FontSize: 11, ClearFill: true, NumberFormat: "General",
            HAlign: CellHAlign.General, VAlign: CellVAlign.Bottom, WrapText: false, IndentLevel: 0,
            BorderTop: new CellBorder(BorderStyle.None),
            BorderBottom: new CellBorder(BorderStyle.None),
            BorderLeft: new CellBorder(BorderStyle.None),
            BorderRight: new CellBorder(BorderStyle.None)));
    }

    // ── Insert tab ────────────────────────────────────────────────────────────

    private void PivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "PivotTables, pivot caches, slicers, and timelines are excluded from Freexcel v1. Use Sort, Filter, Remove Duplicates, Consolidate, and formulas for supported local analysis workflows.",
            "PivotTable",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
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

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a pie or doughnut chart before changing first-slice angle.",
                "First Slice Angle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (chart.Type is not (ChartType.Pie or ChartType.Doughnut))
        {
            MessageBox.Show(
                "First-slice angle only applies to pie and doughnut charts.",
                "First Slice Angle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var nextAngle = chart.FirstSliceAngle >= 270 ? 0 : chart.FirstSliceAngle + 90;
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(FirstSliceAngle: nextAngle)),
                "First Slice Angle"))
            return;

        UpdateViewport();
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a doughnut chart before changing hole size.",
                "Doughnut Hole Size",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (chart.Type != ChartType.Doughnut)
        {
            MessageBox.Show(
                "Doughnut hole size only applies to doughnut charts.",
                "Doughnut Hole Size",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var nextSize = chart.DoughnutHoleSize switch
        {
            < 0.45 => 0.55,
            < 0.7 => 0.75,
            _ => 0.35
        };
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(DoughnutHoleSize: nextSize)),
                "Doughnut Hole Size"))
            return;

        UpdateViewport();
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a pie or doughnut chart before exploding a slice.",
                "Explode Slice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (chart.Type is not (ChartType.Pie or ChartType.Doughnut))
        {
            MessageBox.Show(
                "Exploded slices only apply to pie and doughnut charts.",
                "Explode Slice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sliceCount = ChartTypeSupport.GetDataPointCount(chart);
        if (sliceCount == 0)
        {
            MessageBox.Show(
                "Add chart data before exploding a slice.",
                "Explode Slice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var nextIndex = chart.ExplodedSliceIndex < 0
            ? 0
            : chart.ExplodedSliceIndex + 1 >= sliceCount ? -1 : chart.ExplodedSliceIndex + 1;
        var nextDistance = nextIndex < 0
            ? 0.1
            : chart.ExplodedSliceDistance >= 0.22 ? 0.1 : chart.ExplodedSliceDistance + 0.06;
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(ExplodedSliceIndex: nextIndex, ExplodedSliceDistance: nextDistance)),
                "Explode Slice"))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing data labels.",
                "Data Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(ShowDataLabels: !chart.ShowDataLabels)),
                "Data Labels"))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing data label positions.",
                "Data Label Position",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowDataLabels: true,
                        DataLabelPosition: GetNextDataLabelPosition(chart.DataLabelPosition))),
                "Data Label Position"))
            return;

        UpdateViewport();
    }

    private static ChartDataLabelPosition GetNextDataLabelPosition(ChartDataLabelPosition current) =>
        current switch
        {
            ChartDataLabelPosition.BestFit => ChartDataLabelPosition.OutsideEnd,
            ChartDataLabelPosition.OutsideEnd => ChartDataLabelPosition.InsideEnd,
            ChartDataLabelPosition.InsideEnd => ChartDataLabelPosition.Center,
            _ => ChartDataLabelPosition.BestFit
        };

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
                DataLabelNumberFormat: chart.DataLabelNumberFormat switch
                {
                    ChartDataLabelNumberFormat.General => ChartDataLabelNumberFormat.Number,
                    ChartDataLabelNumberFormat.Number => ChartDataLabelNumberFormat.Currency,
                    ChartDataLabelNumberFormat.Currency => ChartDataLabelNumberFormat.Percent,
                    _ => ChartDataLabelNumberFormat.General
                }));
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
                DataLabelFillColor: GetNextSeriesColor(chart.DataLabelFillColor)));
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Text",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelTextColor: GetNextSeriesColor(chart.DataLabelTextColor)));
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Border",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelBorderColor: GetNextSeriesColor(chart.DataLabelBorderColor),
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
                DataLabelAngle: GetNextAxisLabelAngle(chart.DataLabelAngle)));
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        const string caption = "Format Data Point Label";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing point data-label formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (GetChartSeriesCount(chart) == 0 || ChartTypeSupport.GetDataPointCount(chart) == 0)
        {
            MessageBox.Show(
                "Add chart data points before changing point data-label formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var formats = chart.PointDataLabelFormats.ToList();
        var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0 && format.PointIndex == 0);
        var current = existingIndex >= 0 ? formats[existingIndex] : new ChartPointDataLabelFormat(0, 0);
        var updated = current with
        {
            FillColor = GetNextSeriesColor(current.FillColor),
            BorderColor = GetNextSeriesColor(current.BorderColor ?? current.FillColor),
            BorderThickness = current.BorderThickness is null or >= 3 ? 0.75 : current.BorderThickness.Value + 0.75,
            TextColor = GetNextSeriesColor(current.TextColor),
            FontSize = current.FontSize is null or >= 16 ? 9 : current.FontSize.Value + 1
        };
        if (existingIndex >= 0)
            formats[existingIndex] = updated;
        else
            formats.Add(updated);

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowDataLabels: true,
                        PointDataLabelFormats: formats)),
                caption))
            return;

        UpdateViewport();
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Area Fill",
            chart => new ChartLayoutOptions(ChartAreaFillColor: GetNextSeriesColor(chart.ChartAreaFillColor)));
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Color",
            chart => new ChartLayoutOptions(ChartTitleTextColor: GetNextSeriesColor(chart.ChartTitleTextColor)));
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
            chart => new ChartLayoutOptions(AxisTitleTextColor: GetNextSeriesColor(chart.AxisTitleTextColor)));
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
            chart => new ChartLayoutOptions(PlotAreaFillColor: GetNextSeriesColor(chart.PlotAreaFillColor)));
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Border",
            chart => new ChartLayoutOptions(
                PlotAreaBorderColor: GetNextSeriesColor(chart.PlotAreaBorderColor),
                PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3 ? 1 : chart.PlotAreaBorderThickness + 0.75));
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Text",
            chart => new ChartLayoutOptions(LegendTextColor: GetNextSeriesColor(chart.LegendTextColor)));
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Fill",
            chart => new ChartLayoutOptions(LegendFillColor: GetNextSeriesColor(chart.LegendFillColor)));
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Border",
            chart => new ChartLayoutOptions(
                LegendBorderColor: GetNextSeriesColor(chart.LegendBorderColor),
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

    private static ChartDataLabelNumberFormat GetNextDataLabelNumberFormat(ChartDataLabelNumberFormat current) =>
        current switch
        {
            ChartDataLabelNumberFormat.General => ChartDataLabelNumberFormat.Number,
            ChartDataLabelNumberFormat.Number => ChartDataLabelNumberFormat.Currency,
            ChartDataLabelNumberFormat.Currency => ChartDataLabelNumberFormat.Percent,
            _ => ChartDataLabelNumberFormat.General
        };

    private void ToggleDataLabelOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing data label options.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart)),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAreaOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing chart area formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart)),
                caption))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing trendlines.",
                "Trendline",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            MessageBox.Show(
                "Linear trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                "Trendline",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(ShowLinearTrendline: !chart.ShowLinearTrendline)),
                "Trendline"))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing trendline type.",
                "Trendline Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            MessageBox.Show(
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                "Trendline Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowLinearTrendline: true,
                        TrendlineType: GetNextTrendlineType(chart.TrendlineType))),
                "Trendline Type"))
            return;

        UpdateViewport();
    }

    private static ChartTrendlineType GetNextTrendlineType(ChartTrendlineType current) =>
        current switch
        {
            ChartTrendlineType.Linear => ChartTrendlineType.Exponential,
            ChartTrendlineType.Exponential => ChartTrendlineType.Logarithmic,
            ChartTrendlineType.Logarithmic => ChartTrendlineType.Power,
            ChartTrendlineType.Power => ChartTrendlineType.MovingAverage,
            ChartTrendlineType.MovingAverage => ChartTrendlineType.Polynomial,
            _ => ChartTrendlineType.Linear
        };

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing moving-average period.",
                "Moving Average Period",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            MessageBox.Show(
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                "Moving Average Period",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowLinearTrendline: true,
                        TrendlineType: ChartTrendlineType.MovingAverage,
                        TrendlinePeriod: chart.TrendlinePeriod >= 6 ? 2 : chart.TrendlinePeriod + 1)),
                "Moving Average Period"))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing polynomial order.",
                "Polynomial Order",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            MessageBox.Show(
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                "Polynomial Order",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowLinearTrendline: true,
                        TrendlineType: ChartTrendlineType.Polynomial,
                        TrendlineOrder: chart.TrendlineOrder >= 6 ? 2 : chart.TrendlineOrder + 1)),
                "Polynomial Order"))
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
                TrendlineColor: GetNextTrendlineColor(chart.TrendlineColor)));
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

    private static CellColor GetNextTrendlineColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(217, 83, 25);
        if (current.Value.R == 217 && current.Value.G == 83 && current.Value.B == 25)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(0, 158, 115);
        return new CellColor(128, 128, 128);
    }

    private void ToggleTrendlineInfo(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing trendline information.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            MessageBox.Show(
                "Trendline information is currently supported for column, line, bar, scatter, bubble, and area charts.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart)),
                caption))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing secondary axes.",
                "Secondary Axis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type))
        {
            MessageBox.Show(
                "Secondary value axes are currently supported for column, line, area, and scatter charts.",
                "Secondary Axis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!chart.ShowSecondaryAxis && GetChartSeriesCount(chart) < 2)
        {
            MessageBox.Show(
                "Add at least two data series before adding a secondary axis.",
                "Secondary Axis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowSecondaryAxis: !chart.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes: [])),
                "Secondary Axis"))
            return;

        UpdateViewport();
    }

    private void ChartXAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisBounds(useXAxis: true);
    }

    private void ChartYAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisBounds(useXAxis: false);
    }

    private void ChartXAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLogScale(useXAxis: true);
    }

    private void ChartYAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLogScale(useXAxis: false);
    }

    private void ChartXAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisNumberFormat(useXAxis: true);
    }

    private void ChartYAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisNumberFormat(useXAxis: false);
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
        ToggleChartAxisTicks(useXAxis: true);
    }

    private void ChartYAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisTicks(useXAxis: false);
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
        ToggleChartAxisLine(useXAxis: true);
    }

    private void ChartYAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLine(useXAxis: false);
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Ticks" : "Y Axis Ticks";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis ticks.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var (major, minor) = useXAxis
            ? GetNextAxisTickState(chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle)
            : GetNextAxisTickState(chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle);
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisMajorTickStyle: major, XAxisMinorTickStyle: minor)
            : new ChartLayoutOptions(YAxisMajorTickStyle: major, YAxisMinorTickStyle: minor);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Labels" : "Y Axis Labels";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis labels.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var options = useXAxis
            ? new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels)
            : new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Label Font" : "Y Axis Label Font";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis label formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentColor = useXAxis ? chart.XAxisLabelTextColor : chart.YAxisLabelTextColor;
        var currentSize = useXAxis ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
        var nextColor = GetNextSeriesColor(currentColor);
        var nextSize = currentSize >= 14 ? 9 : currentSize + 1;
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisLabelTextColor: nextColor, XAxisLabelFontSize: nextSize)
            : new ChartLayoutOptions(YAxisLabelTextColor: nextColor, YAxisLabelFontSize: nextSize);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Label Angle" : "Y Axis Label Angle";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis label rotation.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentAngle = useXAxis ? chart.XAxisLabelAngle : chart.YAxisLabelAngle;
        var nextAngle = GetNextAxisLabelAngle(currentAngle);
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisLabelAngle: nextAngle)
            : new ChartLayoutOptions(YAxisLabelAngle: nextAngle);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Line" : "Y Axis Line";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis line formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentColor = useXAxis ? chart.XAxisLineColor : chart.YAxisLineColor;
        var currentThickness = useXAxis ? chart.XAxisLineThickness : chart.YAxisLineThickness;
        var (nextColor, nextThickness) = GetNextAxisLineState(currentColor, currentThickness);
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisLineColor: nextColor, XAxisLineThickness: nextThickness)
            : new ChartLayoutOptions(YAxisLineColor: nextColor, YAxisLineThickness: nextThickness);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private static (ChartAxisTickStyle Major, ChartAxisTickStyle Minor) GetNextAxisTickState(
        ChartAxisTickStyle currentMajor,
        ChartAxisTickStyle currentMinor)
    {
        if (currentMajor == ChartAxisTickStyle.Outside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Inside, ChartAxisTickStyle.None);
        if (currentMajor == ChartAxisTickStyle.Inside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Cross, ChartAxisTickStyle.Inside);
        if (currentMajor == ChartAxisTickStyle.Cross)
            return (ChartAxisTickStyle.None, ChartAxisTickStyle.None);
        return (ChartAxisTickStyle.Outside, ChartAxisTickStyle.None);
    }

    private static double GetNextAxisLabelAngle(double currentAngle)
    {
        if (Math.Abs(currentAngle) < 0.5)
            return -45;
        if (currentAngle <= -44.5)
            return 45;
        if (currentAngle < 89.5)
            return 90;
        return 0;
    }

    private static (CellColor Color, double Thickness) GetNextAxisLineState(CellColor? currentColor, double currentThickness)
    {
        if (currentColor is null || currentThickness < 1.5)
            return (new CellColor(89, 89, 89), 1.5);
        if (currentThickness < 2.5)
            return (new CellColor(0, 114, 178), 2.5);
        if (currentThickness < 3.5)
            return (new CellColor(213, 94, 0), 3.5);
        return (new CellColor(89, 89, 89), 1);
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Gridlines" : "Y Axis Gridlines";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis gridlines.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var (showMajor, showMinor) = useXAxis
            ? GetNextGridlineState(chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines)
            : GetNextGridlineState(chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines);
        var options = useXAxis
            ? new ChartLayoutOptions(ShowXAxisMajorGridlines: showMajor, ShowXAxisMinorGridlines: showMinor)
            : new ChartLayoutOptions(ShowYAxisMajorGridlines: showMajor, ShowYAxisMinorGridlines: showMinor);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private static (bool ShowMajor, bool ShowMinor) GetNextGridlineState(bool currentMajor, bool currentMinor)
    {
        if (!currentMajor)
            return (true, false);
        if (!currentMinor)
            return (true, true);
        return (false, false);
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Gridline Style" : "Y Gridline Style";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing gridline formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentMajorColor = useXAxis ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
        var currentMinorColor = useXAxis ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
        var currentThickness = useXAxis ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
        var nextMajorColor = GetNextSeriesColor(currentMajorColor);
        var nextMinorColor = GetNextSeriesColor(currentMinorColor ?? currentMajorColor);
        var nextThickness = currentThickness >= 3 ? 1 : currentThickness + 0.5;
        var options = useXAxis
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
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Number Format" : "Y Axis Number Format";
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis number formats.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var next = GetNextDataLabelNumberFormat(useXAxis ? chart.XAxisNumberFormat : chart.YAxisNumberFormat);
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisNumberFormat: next)
            : new ChartLayoutOptions(YAxisNumberFormat: next);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Log Scale" : "Y Log Scale";
        if (sheet is null || chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis scale.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (useXAxis && !ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
        {
            MessageBox.Show(
                "X-axis log scale is currently supported for bar, scatter, and bubble charts with value X axes.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!useXAxis && !ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
        {
            MessageBox.Show(
                "Y-axis log scale is currently supported for column, line, area, scatter, and bubble charts with value Y axes.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var enableLog = useXAxis ? !chart.XAxisLogScale : !chart.YAxisLogScale;
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisLogScale: enableLog)
            : new ChartLayoutOptions(YAxisLogScale: enableLog);

        if (enableLog && TryGetChartAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
        {
            var positiveMinimum = minimum > 0 ? minimum : 1;
            var positiveMaximum = maximum > positiveMinimum ? maximum : positiveMinimum * 10;
            options = useXAxis
                ? options with { XAxisMinimum = positiveMinimum, XAxisMaximum = positiveMaximum }
                : options with { YAxisMinimum = positiveMinimum, YAxisMaximum = positiveMaximum };
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        var caption = useXAxis ? "X Axis Bounds" : "Y Axis Bounds";
        if (sheet is null || chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing axis bounds.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var hasBounds = useXAxis
            ? chart.XAxisMinimum is not null || chart.XAxisMaximum is not null
            : chart.YAxisMinimum is not null || chart.YAxisMaximum is not null;
        if (!hasBounds &&
            (useXAxis
                ? !ChartTypeSupport.SupportsXAxisBounds(chart.Type)
                : !ChartTypeSupport.SupportsYAxisBounds(chart.Type)))
        {
            MessageBox.Show(
                "Axis bounds are currently supported for chart value axes only.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ChartLayoutOptions options;
        if (hasBounds)
        {
            options = useXAxis
                ? new ChartLayoutOptions(ClearXAxisBounds: true)
                : new ChartLayoutOptions(ClearYAxisBounds: true);
        }
        else if (TryGetChartAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
        {
            var majorUnit = Math.Max(double.Epsilon, (maximum - minimum) / 5);
            var minorUnit = Math.Max(double.Epsilon, majorUnit / 2);
            options = useXAxis
                ? new ChartLayoutOptions(XAxisMinimum: minimum, XAxisMaximum: maximum, XAxisMajorUnit: majorUnit, XAxisMinorUnit: minorUnit)
                : new ChartLayoutOptions(YAxisMinimum: minimum, YAxisMaximum: maximum, YAxisMajorUnit: majorUnit, YAxisMinorUnit: minorUnit);
        }
        else
        {
            MessageBox.Show(
                "Add numeric chart data before setting axis bounds.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(_currentSheetId, chart.Id, options),
                caption))
            return;

        UpdateViewport();
    }

    private static bool TryGetChartAxisBounds(Sheet sheet, ChartModel chart, bool useXAxis, out double minimum, out double maximum)
    {
        minimum = 0;
        maximum = 0;
        var values = new List<double>();
        var startRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (startRow > chart.DataRange.End.Row)
            return false;

        if (useXAxis)
        {
            var xColumns = ChartTypeSupport.GetXAxisValueColumns(chart);
            foreach (var xColumn in xColumns)
            {
                for (var row = startRow; row <= chart.DataRange.End.Row; row++)
                {
                    if (sheet.GetValue(row, xColumn) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }
        else
        {
            var yColumns = ChartTypeSupport.GetYAxisValueColumns(chart);
            for (var row = startRow; row <= chart.DataRange.End.Row; row++)
            {
                foreach (var col in yColumns)
                {
                    if (sheet.GetValue(row, col) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }

        if (values.Count == 0)
            return false;

        minimum = values.Min();
        maximum = values.Max();
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            minimum -= 1;
            maximum += 1;
        }

        return true;
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing secondary-axis series.",
                "Secondary Axis Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type))
        {
            MessageBox.Show(
                "Secondary value axes are currently supported for column, line, area, and scatter charts.",
                "Secondary Axis Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var seriesCount = GetChartSeriesCount(chart);
        if (seriesCount < 2)
        {
            MessageBox.Show(
                "Add at least two data series before assigning a secondary axis.",
                "Secondary Axis Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var next = GetNextSecondaryAxisSeries(chart, seriesCount);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        ShowSecondaryAxis: next.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes: next.SeriesIndexes)),
                "Secondary Axis Series"))
            return;

        UpdateViewport();
    }

    private static int GetChartSeriesCount(ChartModel chart)
    {
        return ChartTypeSupport.GetDataSeriesCount(chart);
    }

    private static (bool ShowSecondaryAxis, int[] SeriesIndexes) GetNextSecondaryAxisSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis)
            return (true, [1]);

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return (false, []);

        var current = chart.SecondaryAxisSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return (true, [current + 1]);

        return (true, []);
    }

    private static int[] GetNextComboLineSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || chart.ComboLineSeriesIndexes.Count == 0)
            return [1];

        var current = chart.ComboLineSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return [current + 1];

        return [];
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing combo chart options.",
                "Combo Chart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type))
        {
            MessageBox.Show(
                "Combo line overlays are currently supported for column and area charts.",
                "Combo Chart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!chart.UseComboLineForSecondarySeries && !ChartTypeSupport.SupportsComboLineOverlay(chart))
        {
            MessageBox.Show(
                "Add at least two data series before enabling a combo line overlay.",
                "Combo Chart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        UseComboLineForSecondarySeries: !chart.UseComboLineForSecondarySeries,
                        ComboLineSeriesIndexes: !chart.UseComboLineForSecondarySeries ? chart.ComboLineSeriesIndexes : [])),
                "Combo Chart"))
            return;

        UpdateViewport();
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing combo chart series.",
                "Combo Chart Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type))
        {
            MessageBox.Show(
                "Combo line overlays are currently supported for column and area charts.",
                "Combo Chart Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ChartTypeSupport.SupportsComboLineOverlay(chart))
        {
            MessageBox.Show(
                "Add at least two data series before choosing a combo chart line series.",
                "Combo Chart Series",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var seriesCount = GetChartSeriesCount(chart);
        var nextIndexes = GetNextComboLineSeries(chart, seriesCount);
        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(
                        UseComboLineForSecondarySeries: nextIndexes.Length > 0,
                        ComboLineSeriesIndexes: nextIndexes)),
                "Combo Chart Series"))
            return;

        UpdateViewport();
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Color",
            format => format with
            {
                FillColor = GetNextSeriesColor(format.FillColor),
                StrokeColor = GetNextSeriesColor(format.StrokeColor ?? format.FillColor)
            });
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
        if (!CanChangeSeriesMarkers("Series Marker"))
            return;

        ToggleSeriesFormat(
            "Series Marker",
            format => format with
            {
                MarkerStyle = format.MarkerStyle switch
                {
                    null => ChartMarkerStyle.Circle,
                    ChartMarkerStyle.Circle => ChartMarkerStyle.Square,
                    ChartMarkerStyle.Square => ChartMarkerStyle.Diamond,
                    ChartMarkerStyle.Diamond => ChartMarkerStyle.Triangle,
                    ChartMarkerStyle.Triangle => ChartMarkerStyle.None,
                    _ => null
                }
            });
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeSeriesMarkers("Marker Size"))
            return;

        ToggleSeriesFormat(
            "Marker Size",
            format => format with
            {
                MarkerSize = format.MarkerSize is null or >= 12 ? 5 : format.MarkerSize.Value + 2
            });
    }

    private bool CanChangeSeriesMarkers(string caption)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing series markers.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return true;

        MessageBox.Show(
            "Series marker shape and size are currently supported for line and scatter charts.",
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void ToggleSeriesFormat(string caption, Func<ChartSeriesFormat, ChartSeriesFormat> update)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var chart = sheet?.Charts.FirstOrDefault();
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a chart before changing series formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (GetChartSeriesCount(chart) == 0)
        {
            MessageBox.Show(
                "Add data series before changing series formatting.",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var formats = chart.SeriesFormats.ToList();
        var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0);
        var current = existingIndex >= 0 ? formats[existingIndex] : new ChartSeriesFormat(0);
        var updated = update(current);
        if (existingIndex >= 0)
            formats[existingIndex] = updated;
        else
            formats.Add(updated);

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    _currentSheetId,
                    chart.Id,
                    new ChartLayoutOptions(SeriesFormats: formats)),
                caption))
            return;

        UpdateViewport();
    }

    private static CellColor GetNextSeriesColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(213, 94, 0);
        if (current.Value.R == 213 && current.Value.G == 94 && current.Value.B == 0)
            return new CellColor(0, 158, 115);
        return new CellColor(0, 114, 178);
    }

    private void InsertChartOfType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        InsertChartOfType(normalized switch
        {
            "line" => ChartType.Line,
            "pie" => ChartType.Pie,
            "doughnut" or "donut" => ChartType.Doughnut,
            "bar" => ChartType.Bar,
            "stackedbar" or "stacked bar" => ChartType.StackedBar,
            "percentstackedbar" or "100% stacked bar" or "100%stackedbar" => ChartType.PercentStackedBar,
            "stackedcolumn" or "stacked column" => ChartType.StackedColumn,
            "percentstackedcolumn" or "100% stacked column" or "100%stackedcolumn" => ChartType.PercentStackedColumn,
            "scatter" => ChartType.Scatter,
            "bubble" => ChartType.Bubble,
            "area" => ChartType.Area,
            _ => ChartType.Column
        });
    }

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e)    => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e)  => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var selected = SheetGrid.SelectedRange;
        var rangeInput = PromptForInput("Data range (e.g. A1:E1):", selected?.ToString() ?? "");
        if (rangeInput is null) return;
        var targetInput = PromptForInput("Location cell (e.g. F1):", "");
        if (targetInput is null) return;

        GridRange dataRange;
        try
        {
            dataRange = GridRange.Parse(rangeInput, _currentSheetId);
        }
        catch
        {
            MessageBox.Show("Invalid data range.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CellAddress.TryParse(targetInput, _currentSheetId, out var location))
        {
            MessageBox.Show("Invalid location cell.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var kind = type switch
        {
            "column" => SparklineKind.Column,
            "winloss" => SparklineKind.WinLoss,
            _ => SparklineKind.Line
        };

        if (!TryExecuteCommand(new AddSparklineCommand(_currentSheetId, dataRange, location, kind), "Insert Sparkline"))
            return;

        SetActiveCell(location);
        EnsureCellVisible(location);
        UpdateViewport();
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildSparklineValues(Sheet sheet)
    {
        var values = new Dictionary<Guid, IReadOnlyList<double>>();
        foreach (var sparkline in sheet.Sparklines)
        {
            var series = new List<double>();
            for (var row = sparkline.DataRange.Start.Row; row <= sparkline.DataRange.End.Row; row++)
            {
                for (var col = sparkline.DataRange.Start.Col; col <= sparkline.DataRange.End.Col; col++)
                {
                    switch (sheet.GetValue(row, col))
                    {
                        case NumberValue number:
                            series.Add(number.Value);
                            break;
                        case DateTimeValue date:
                            series.Add(date.Value);
                            break;
                        case BoolValue boolean:
                            series.Add(boolean.Value ? 1 : 0);
                            break;
                    }
                }
            }

            values[sparkline.Id] = series;
        }

        return values;
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var url = PromptForInput("URL:", "https://");
        if (url is null) return;
        var label = PromptForInput("Display text (leave blank to use URL):", "");
        var text = string.IsNullOrWhiteSpace(label) ? url : label;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var cmd = new SetHyperlinkCommand(_currentSheetId, addr, url, text);
        if (!TryExecuteCommand(cmd, "Insert Link"))
            return;
        UpdateViewport();
    }

    private void InsertCommentBtn_Click(object sender, RoutedEventArgs e)    => ReviewNewCommentBtn_Click(sender, e);
    private void TextBoxBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertTextBox();
    }
    private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Insert Picture",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read picture file:\n{ex.Message}",
                "Insert Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var contentType = GetImageContentType(dialog.FileName);
        if (!TryExecuteGroupedSheetCommand(
                "Insert Picture",
                sheetId => new InsertPictureCommand(
                    sheetId,
                    new CellAddress(sheetId, range.Start.Row, range.Start.Col),
                    bytes,
                    contentType)))
            return;

        UpdateViewport();
    }

    private static string GetImageContentType(string fileName) =>
        System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "image/png"
        };

    private void PictureSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Picture Size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Picture size (width x height):", $"{(int)picture.Width}x{(int)picture.Height}");
        if (input is null) return;
        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out var width) ||
            !double.TryParse(parts[1], out var height))
        {
            MessageBox.Show("Enter size as width x height, for example 320x180.",
                "Picture Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Picture Size",
                sheetId => new ResizePictureCommand(sheetId, GetTargetPicture(sheetId)?.Id ?? Guid.Empty, width, height)))
            return;

        UpdateViewport();
    }

    private void PictureRotateBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Rotate Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Rotation degrees:", ((int)picture.RotationDegrees).ToString());
        if (input is null) return;
        if (!double.TryParse(input, out var rotation))
        {
            MessageBox.Show("Enter a numeric rotation in degrees.",
                "Rotate Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Rotate Picture",
                sheetId => new RotatePictureCommand(sheetId, GetTargetPicture(sheetId)?.Id ?? Guid.Empty, rotation)))
            return;

        UpdateViewport();
    }

    private PictureModel? GetTargetPicture(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null || sheet.Pictures.Count == 0)
            return null;

        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.Pictures.LastOrDefault(p =>
                p.Anchor.Row == range.Start.Row &&
                p.Anchor.Col == range.Start.Col);
            if (anchored is not null)
                return anchored;
        }

        return sheet.Pictures[^1];
    }

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
        var addr = SheetGrid.SelectedRange.Value.Start;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var existing = sheet?.GetCell(addr)?.Value as TextValue;
        var newText = (existing?.Value ?? "") + dlg.SelectedChar;
        if (!TryExecuteEditCells([(addr, Cell.FromValue(new TextValue(newText)))], "Insert Symbol"))
            return;
        UpdateViewport();
    }

    // ── Draw tab stubs ────────────────────────────────────────────────────────

    private void DrawRectBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Rectangle);
    private void DrawEllipseBtn_Click(object sender, RoutedEventArgs e) => InsertDrawingShape(DrawingShapeKind.Ellipse);
    private void DrawLineBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Line);
    private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();
    private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: true);
    private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: false);
    private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();
    private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();
    private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: true);
    private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);

    // ── Page Layout tab ───────────────────────────────────────────────────────

    private void InsertTextBox()
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        var text = PromptForInput("Text box text:", "");
        if (text is null) return;

        if (!TryExecuteGroupedSheetCommand(
                "Insert Text Box",
                sheetId => new AddTextBoxCommand(sheetId, new CellAddress(sheetId, anchor.Row, anchor.Col), text)))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void InsertDrawingShape(DrawingShapeKind kind)
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        if (!TryExecuteGroupedSheetCommand(
                "Insert Shape",
                sheetId => new AddDrawingShapeCommand(sheetId, new CellAddress(sheetId, anchor.Row, anchor.Col), kind)))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void ReorderSelectedDrawingShape(bool forward)
    {
        var currentShape = GetTargetDrawingShape(_currentSheetId);
        if (currentShape is null)
        {
            MessageBox.Show("No drawing shapes are available on this sheet.",
                "Draw", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var title = forward ? "Bring Forward" : "Send Backward";
        if (!TryExecuteGroupedSheetCommand(
                title,
                sheetId =>
                {
                    var target = GetTargetDrawingShape(sheetId);
                    return forward
                        ? new BringDrawingShapeForwardCommand(sheetId, target?.Id ?? Guid.Empty)
                        : new SendDrawingShapeBackwardCommand(sheetId, target?.Id ?? Guid.Empty);
                }))
            return;

        SetActiveCell(currentShape.Anchor);
        EnsureCellVisible(currentShape.Anchor);
        UpdateViewport();
    }

    private void ResizeSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Object Size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Object size (width x height):", $"{(int)target.Width}x{(int)target.Height}");
        if (input is null) return;
        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out var width) ||
            !double.TryParse(parts[1], out var height))
        {
            MessageBox.Show("Enter size as width x height, for example 160x90.",
                "Object Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Object Size",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new ResizeDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, width, height)
                        : new ResizeTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, width, height);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void RotateSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Rotate Object", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Rotation degrees:", ((int)target.RotationDegrees).ToString());
        if (input is null) return;
        if (!double.TryParse(input, out var rotation))
        {
            MessageBox.Show("Enter a numeric rotation in degrees.",
                "Rotate Object", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Rotate Object",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new RotateDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, rotation)
                        : new RotateTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, rotation);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingObjectColor(bool isFill)
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.",
                isFill ? "Object Fill" : "Object Outline",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput(
            isFill ? "Object fill color (R,G,B):" : "Object outline color (R,G,B):",
            isFill ? "31,119,180" : "68,68,68");
        if (input is null) return;
        if (!TryParseRgbColor(input, out var color))
        {
            MessageBox.Show("Enter a color as R,G,B, for example 31,119,180.",
                isFill ? "Object Fill" : "Object Outline",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteGroupedSheetCommand(
                isFill ? "Object Fill" : "Object Outline",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    if (target.Kind == DrawingObjectTargetKind.Shape)
                    {
                        return new SetDrawingShapeColorsCommand(
                            sheetId,
                            groupedTarget?.Id ?? Guid.Empty,
                            isFill ? color : groupedTarget?.FillColor,
                            isFill ? groupedTarget?.OutlineColor : color);
                    }

                    return new SetTextBoxColorsCommand(
                        sheetId,
                        groupedTarget?.Id ?? Guid.Empty,
                        isFill ? color : groupedTarget?.FillColor,
                        isFill ? groupedTarget?.OutlineColor : color);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private static bool TryParseRgbColor(string input, out CellColor color)
    {
        var parts = input.Split(',');
        if (parts.Length == 3 &&
            byte.TryParse(parts[0].Trim(), out var r) &&
            byte.TryParse(parts[1].Trim(), out var g) &&
            byte.TryParse(parts[2].Trim(), out var b))
        {
            color = new CellColor(r, g, b);
            return true;
        }

        color = default;
        return false;
    }

    private DrawingShapeModel? GetTargetDrawingShape(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null || sheet.DrawingShapes.Count == 0)
            return null;

        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.DrawingShapes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
                return anchored;
        }

        return sheet.DrawingShapes[^1];
    }

    private DrawingObjectTarget? GetTargetDrawingObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        if (preferredKind is null or DrawingObjectTargetKind.Shape && TryGetTargetShape(sheet, out var shape))
            return new DrawingObjectTarget(DrawingObjectTargetKind.Shape, shape.Id, shape.Anchor, shape.Width, shape.Height, shape.RotationDegrees, shape.FillColor, shape.OutlineColor);

        if (preferredKind is null or DrawingObjectTargetKind.TextBox && TryGetTargetTextBox(sheet, out var textBox))
            return new DrawingObjectTarget(DrawingObjectTargetKind.TextBox, textBox.Id, textBox.Anchor, textBox.Width, textBox.Height, textBox.RotationDegrees, textBox.FillColor, textBox.OutlineColor);

        return null;
    }

    private bool TryGetTargetShape(Sheet sheet, out DrawingShapeModel shape)
    {
        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.DrawingShapes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
            {
                shape = anchored;
                return true;
            }
        }

        if (sheet.DrawingShapes.Count > 0)
        {
            shape = sheet.DrawingShapes[^1];
            return true;
        }

        shape = null!;
        return false;
    }

    private bool TryGetTargetTextBox(Sheet sheet, out TextBoxModel textBox)
    {
        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.TextBoxes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
            {
                textBox = anchored;
                return true;
            }
        }

        if (sheet.TextBoxes.Count > 0)
        {
            textBox = sheet.TextBoxes[^1];
            return true;
        }

        textBox = null!;
        return false;
    }

    private enum DrawingObjectTargetKind
    {
        Shape,
        TextBox
    }

    private sealed record DrawingObjectTarget(
        DrawingObjectTargetKind Kind,
        Guid Id,
        CellAddress Anchor,
        double Width,
        double Height,
        double RotationDegrees,
        CellColor? FillColor,
        CellColor? OutlineColor);

    private enum AltTextTargetKind
    {
        Picture,
        Shape,
        TextBox
    }

    private sealed record AltTextTarget(
        AltTextTargetKind Kind,
        Guid Id,
        CellAddress Anchor,
        string? AltText);

    private void PageLayoutDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString() ?? "This command";
        MessageBox.Show(
            $"{commandName} is deferred until Freexcel has a workbook theme model. It is tracked as a documented parity gap, not a silent partial implementation.",
            commandName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BackgroundBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void BackgroundChooseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sheet Background",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dialog.FileName);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Could not read the selected image: {ex.Message}", "Sheet Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Could not read the selected image: {ex.Message}", "Sheet Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var background = new WorksheetBackgroundImage(
            bytes,
            GetImageContentType(dialog.FileName),
            Path.GetFileName(dialog.FileName));

        if (!TryExecuteGroupedSheetCommand("Sheet Background", sheetId => new SetWorksheetBackgroundCommand(sheetId, background)))
            return;

        UpdateViewport();
    }

    private void BackgroundClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Clear Sheet Background", sheetId => new ClearWorksheetBackgroundCommand(sheetId)))
            return;

        UpdateViewport();
    }

    private void PageMarginsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void MarginNormalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Normal));
    }

    private void MarginWideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Wide));
    }

    private void MarginNarrowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Narrow));
    }

    private void MarginCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var current = sheet.PageMargins;
        var defaultValue = string.Join(", ",
            current.Left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Right.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Top.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Bottom.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        var input = PromptForInput("Custom margins in inches (left, right, top, bottom):", defaultValue);
        if (input is null) return;

        if (!PageMarginInputParser.TryParse(input, out var margins, out var error))
        {
            MessageBox.Show(error, "Page Margins", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, margins));
    }

    private void PageOrientBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientPortraitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Portrait));
    }

    private void OrientLandscapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Landscape));
    }

    private void PageSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SizeLetter_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Letter));
    }

    private void SizeA4_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.A4));
    }

    private void SizeLegal_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Legal));
    }

    private void PrintAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void PrintAreaSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Print Area", sheetId => new SetPrintAreaCommand(sheetId, RemapRangeToSheet(range, sheetId))))
            return;
        RefreshStatusBar();
    }

    private void PrintAreaClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Print Area", sheetId => new ClearPrintAreaCommand(sheetId)))
            return;
        RefreshStatusBar();
    }

    private void ScaleToFitBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var current = sheet.ScaleToFit;
        var defaultValue = current.ScalePercent.HasValue
            ? current.ScalePercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{current.FitToPagesWide ?? 1}x{current.FitToPagesTall ?? 1}";
        var input = PromptForInput("Scale percent (10-400) or pages wide x tall (for example 1x1):", defaultValue);
        if (input is null) return;

        WorksheetScaleToFit scaleToFit;
        if (input.Contains('x', StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var wide) ||
                !int.TryParse(parts[1], out var tall))
            {
                MessageBox.Show("Enter fit-to-pages as width x height, for example 1x1.", "Scale to Fit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            scaleToFit = new WorksheetScaleToFit(null, wide, tall);
        }
        else if (int.TryParse(input, out var percent))
        {
            scaleToFit = new WorksheetScaleToFit(percent, null, null);
        }
        else
        {
            MessageBox.Show("Enter a scale percent or fit-to-pages value.", "Scale to Fit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Scale to Fit", sheetId => new SetScaleToFitCommand(sheetId, scaleToFit));
    }

    private void PageBreaksBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var selected = SheetGrid.SelectedRange?.Start;
        var defaultValue = selected is { } address
            ? $"row {Math.Max(2, address.Row)}"
            : "clear";
        var input = PromptForInput("Page break: row N, col N, or clear:", defaultValue);
        if (input is null) return;

        var rowBreaks = sheet.RowPageBreaks.ToList();
        var columnBreaks = sheet.ColumnPageBreaks.ToList();
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            rowBreaks.Clear();
            columnBreaks.Clear();
        }
        else if (TryParseBreakInput(trimmed, "row", out var rowBreak))
        {
            rowBreaks.Add(rowBreak);
        }
        else if (TryParseBreakInput(trimmed, "col", out var columnBreak) ||
                 TryParseBreakInput(trimmed, "column", out columnBreak))
        {
            columnBreaks.Add(columnBreak);
        }
        else
        {
            MessageBox.Show("Enter row N, col N, or clear.", "Page Breaks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Page Breaks", sheetId => new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks));
    }

    private void PrintTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var rowsDefault = sheet.PrintTitleRows is { } rows ? $"{rows.Start}:{rows.End}" : "none";
        var colsDefault = sheet.PrintTitleColumns is { } cols
            ? $"{CellAddress.NumberToColumnName(cols.Start)}:{CellAddress.NumberToColumnName(cols.End)}"
            : "none";
        var rowsInput = PromptForInput("Rows to repeat at top (for example 1:2, or none):", rowsDefault);
        if (rowsInput is null) return;
        var colsInput = PromptForInput("Columns to repeat at left (for example A:C, or none):", colsDefault);
        if (colsInput is null) return;

        if (!TryParseRepeatRows(rowsInput, out var repeatRows) ||
            !TryParseRepeatColumns(colsInput, out var repeatColumns))
        {
            MessageBox.Show("Enter row titles as 1:2 and column titles as A:C, or type none.",
                "Print Titles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Print Titles", sheetId => new SetPrintTitlesCommand(sheetId, repeatRows, repeatColumns));
    }

    private void PageSetupDialogBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var dialog = new PageSetupDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Page Setup",
                sheetId => new SetPageSetupCommand(
                    sheetId,
                    dialog.Orientation,
                    dialog.PaperSize,
                    dialog.Margins,
                    dialog.PrintGridlines,
                    dialog.PrintHeadings,
                    dialog.ScaleToFit,
                    dialog.PrintTitleRows,
                    dialog.PrintTitleColumns,
                    dialog.CenterHorizontally,
                    dialog.CenterVertically,
                    dialog.PageOrder,
                    dialog.FirstPageNumber,
                    dialog.HeaderMargin,
                    dialog.FooterMargin,
                    dialog.PrintBlackAndWhite,
                    dialog.PrintDraftQuality,
                    dialog.PrintQualityDpi,
                    dialog.PrintErrorValue,
                    dialog.PrintComments)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private static bool TryParseBreakInput(string input, string keyword, out uint value)
    {
        value = 0;
        if (!input.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return false;
        var numberText = input[keyword.Length..].Trim();
        return uint.TryParse(numberText, out value);
    }

    private static bool TryParseRepeatRows(string input, out WorksheetRepeatRange? range)
    {
        range = null;
        var normalized = input.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            normalized.Length == 0)
        {
            return true;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && uint.TryParse(parts[0], out var single))
        {
            range = new WorksheetRepeatRange(single, single);
            return single > 0;
        }
        if (parts.Length == 2 && uint.TryParse(parts[0], out var start) && uint.TryParse(parts[1], out var end))
        {
            range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
            return start > 0 && end > 0;
        }

        return false;
    }

    private static bool TryParseRepeatColumns(string input, out WorksheetRepeatRange? range)
    {
        range = null;
        var normalized = input.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            normalized.Length == 0)
        {
            return true;
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            try
            {
                var single = CellAddress.ColumnNameToNumber(parts[0]);
                range = new WorksheetRepeatRange(single, single);
                return true;
            }
            catch (FormatException) { return false; }
        }
        if (parts.Length == 2)
        {
            try
            {
                var start = CellAddress.ColumnNameToNumber(parts[0]);
                var end = CellAddress.ColumnNameToNumber(parts[1]);
                range = new WorksheetRepeatRange(Math.Min(start, end), Math.Max(start, end));
                return true;
            }
            catch (FormatException) { return false; }
        }

        return false;
    }

    private void PrintGridlinesChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            new SetPrintOptionsCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false),
            "Print Gridlines");
    }

    private void PrintHeadingsChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            new SetPrintOptionsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked),
            "Print Headings");
    }

    // ── Formulas tab ──────────────────────────────────────────────────────────

    private void InsertFunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InsertFunctionDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.SelectedFormula)) return;
        if (SheetGrid.SelectedRange is null) return;
        FormulaBar.Text = "=" + dlg.SelectedFormula;
        EnterEditMode();
    }

    private void DefineNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var name = PromptForInput("Define named range (name):", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var outcome = _commandBus.Execute(_workbook.Id, new DefineNamedRangeCommand(name.Trim(), range));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Define Name");
            return;
        }

        MessageBox.Show($"Named range '{name}' = {range} defined.", "Define Name", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UseInFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (_workbook.NamedRanges.Count == 0)
        {
            MessageBox.Show("No names are defined in this workbook.", "Use in Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        foreach (var name in _workbook.NamedRanges.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => InsertDefinedNameIntoFormula(name);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void InsertDefinedNameIntoFormula(string name)
    {
        var result = FormulaInsertionService.InsertDefinedName(FormulaBar.Text, FormulaBar.CaretIndex, name);
        FormulaBar.Text = result.Text;
        FormulaBar.CaretIndex = result.CaretIndex;
        FormulaBar.Focus();
        EnterEditMode();
    }

    private void TracePrecedentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        TracePrecedentsForCell(range.Start, "Trace Precedents");
    }

    private void TracePrecedentsForCell(CellAddress activeCell, string title)
    {
        var precedents = FormulaAuditingService.GetDirectPrecedents(_workbook, activeCell);
        if (precedents.Count == 0)
        {
            MessageBox.Show($"{FormatAuditAddress(activeCell)} has no direct precedents.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetPrecedentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormatAuditAddress(activeCell)} directly references {precedents.Count} cell(s):\n{FormatAuditAddresses(precedents)}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TraceDependentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var activeCell = range.Start;
        var dependents = FormulaAuditingService.GetDirectDependents(_workbook, activeCell);
        if (dependents.Count == 0)
        {
            MessageBox.Show($"{FormatAuditAddress(activeCell)} has no direct dependents.",
                "Trace Dependents", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetDependentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormatAuditAddress(activeCell)} is directly referenced by {dependents.Count} cell(s):\n{FormatAuditAddresses(dependents)}",
            "Trace Dependents",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private string FormatAuditAddress(CellAddress address)
    {
        var sheetName = _workbook.GetSheet(address.Sheet)?.Name ?? "Sheet";
        return $"{sheetName}!{address.ToA1()}";
    }

    private string FormatAuditAddresses(IReadOnlyList<CellAddress> addresses)
    {
        const int maxShown = 12;
        var shown = addresses.Take(maxShown).Select(FormatAuditAddress);
        var suffix = addresses.Count > maxShown ? $"\n...and {addresses.Count - maxShown} more." : "";
        return string.Join(", ", shown) + suffix;
    }

    private void RemoveArrowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_formulaTraceArrows.Count == 0)
        {
            MessageBox.Show("No auditing arrows to remove.", "Remove Arrows", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        UpdateViewport();
    }

    private void ShowFormulasBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var showFormulas = !sheet.ShowFormulas;
        if (!TryExecuteGroupedSheetCommand(
                "Show Formulas",
                sheetId => new SetWorksheetShowFormulasCommand(sheetId, showFormulas)))
            return;

        UpdateViewport();
    }

    private void ErrorCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId);
        if (issues.Count == 0)
        {
            MessageBox.Show("No errors found.", "Error Checking", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ErrorCheckingDialog(
            issues,
            address =>
            {
                NavigateToCell(address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
            },
            issue =>
            {
                if (!TryExecuteCommand(
                        new SetFormulaErrorIgnoredCommand(issue.SheetId, issue.Address, ignored: true),
                        "Ignore Error"))
                    return false;

                UpdateViewport();
                RefreshStatusBar();
                return true;
            },
            issue =>
            {
                NavigateToCell(issue.Address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
                TracePrecedentsForCell(issue.Address, "Trace Error");
            })
        {
            Owner = this
        };
        dialog.Show();
    }

    private void EvaluateFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        RecalculateWorkbook();
        var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, range.Start);
        if (summary is null)
        {
            MessageBox.Show("Select a cell that contains a formula.", "Evaluate Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EvaluateFormulaDialog(summary)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void AddWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var added = WatchWindowService.AddWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        MessageBox.Show(
            added > 0
                ? $"{added} cell{(added == 1 ? "" : "s")} added to Watch Window."
                : $"{FormatRangeReference(range.Start, range.End)} is already watched.",
            "Watch Window",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DeleteWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var removed = WatchWindowService.RemoveWatch(_workbook, range.Start);
        _watchWindowDialog?.Refresh();
        MessageBox.Show(
            removed ? $"{FormatAuditAddress(range.Start)} removed from Watch Window." : $"{FormatAuditAddress(range.Start)} is not watched.",
            "Watch Window",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void WatchWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_watchWindowDialog is null)
        {
            _watchWindowDialog = new WatchWindowDialog(
                () =>
                {
                    RecalculateWorkbook();
                    return WatchWindowService.GetEntries(_workbook);
                },
                address =>
                {
                    NavigateToCell(address);
                    RefreshSheetTabs();
                    UpdateViewport();
                    RefreshStatusBar();
                },
                address =>
                {
                    WatchWindowService.RemoveWatch(_workbook, address);
                    UpdateViewport();
                })
            {
                Owner = this
            };
            _watchWindowDialog.Closed += (_, _) => _watchWindowDialog = null;
            _watchWindowDialog.Show();
        }
        else
        {
            _watchWindowDialog.Refresh();
            if (_watchWindowDialog.WindowState == WindowState.Minimized)
                _watchWindowDialog.WindowState = WindowState.Normal;
            _watchWindowDialog.Activate();
        }
    }

    private void CalcNowBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();
        UpdateViewport();
    }
    private void CalcSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        _recalcEngine.RecalculateSheetFormulas(_workbook, _currentSheetId);
        UpdateViewport();
    }
    private void CalcOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CalcAutoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Automatic), "Calculation Options"))
            return;
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void CalcManualMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Manual), "Calculation Options");
    }

    private void FormulaLogicalBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenFormulaFunctionMenu(sender, ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA"]);
    }
    private void FormulaTextBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["CONCAT", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"]);
    private void FormulaDateBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF"]);
    private void FormulaLookupBtn_Click(object sender, RoutedEventArgs e)  => OpenFormulaFunctionMenu(sender, ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH"]);
    private void FormulaMathBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["SUM", "AVERAGE", "COUNT", "MIN", "MAX", "ROUND", "ABS", "SQRT", "MOD", "POWER", "INT", "CEILING", "FLOOR", "SIGN", "LOG", "LN", "EXP", "PI", "FACT", "RANDBETWEEN"]);
    private void FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);

    private void OpenFormulaFunctionMenu(object sender, IReadOnlyList<string> functionNames)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var menu = new ContextMenu();
        foreach (var functionName in functionNames)
        {
            var item = new MenuItem { Header = functionName };
            item.Click += (_, _) => InsertFormulaFunction(functionName);
            menu.Items.Add(item);
        }

        btn.ContextMenu = menu;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void InsertFormulaFunction(string funcName)
    {
        if (SheetGrid.SelectedRange is null) return;
        FormulaBar.Text = $"={funcName}(";
        EnterEditMode();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
    }
    private void Formula_IF_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("IF");
    private void Formula_AND_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("AND");
    private void Formula_OR_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("OR");
    private void Formula_NOT_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOT");
    private void Formula_IFS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("IFS");
    private void Formula_CONCAT_Click(object sender, RoutedEventArgs e)  => InsertFormulaFunction("CONCAT");
    private void Formula_LEFT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("LEFT");
    private void Formula_RIGHT_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("RIGHT");
    private void Formula_MID_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("MID");
    private void Formula_LEN_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("LEN");
    private void Formula_TRIM_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TRIM");
    private void Formula_TEXT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TEXT");
    private void Formula_TODAY_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("TODAY");
    private void Formula_NOW_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOW");
    private void Formula_DATE_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("DATE");
    private void Formula_YEAR_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("YEAR");
    private void Formula_MONTH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MONTH");
    private void Formula_DAY_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("DAY");
    private void Formula_VLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("VLOOKUP");
    private void Formula_HLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("HLOOKUP");
    private void Formula_INDEX_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("INDEX");
    private void Formula_MATCH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MATCH");
    private void Formula_XLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("XLOOKUP");
    private void Formula_SUM_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("SUM");
    private void Formula_ROUND_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("ROUND");
    private void Formula_ABS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("ABS");
    private void Formula_SQRT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("SQRT");

    // ── Data tab additions ────────────────────────────────────────────────────

    private void GetDataBtn_Click(object sender, RoutedEventArgs e)
    {
        var adapters = _fileAdapters
            .Where(adapter => adapter.Extension is ".csv")
            .ToList();
        if (adapters.Count == 0)
        {
            MessageBox.Show("No import adapters are available.", "Get Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filter = string.Join("|", adapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var adapter = adapters.FirstOrDefault(a => a.Extension == ext);
        if (adapter is null) return;

        try
        {
            using var stream = System.IO.File.OpenRead(dialog.FileName);
            var imported = adapter.Load(stream);
            if (imported.Sheets.Count == 0) return;

            var destination = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
            if (!TryExecuteCommand(new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0]), "Get Data", out var outcome))
                return;

            RecalculateIfAutomatic(outcome.AffectedCells ?? []);
            SetActiveCell(destination);
            EnsureCellVisible(destination);
            UpdateViewport();
            RefreshStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import data:\n{ex.Message}", "Get Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);

    private void TextToColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var delimInput = PromptForInput("Delimiter (e.g. , or ;):", ",");
        if (delimInput is null || delimInput.Length == 0) return;
        char delim = delimInput[0];
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var cellVal = sheet.GetValue(r, range.Start.Col) as TextValue;
            if (cellVal is null) continue;
            var parts = cellVal.Value.Split(delim);
            for (int i = 0; i < parts.Length; i++)
            {
                var addr = new CellAddress(_currentSheetId, r, range.Start.Col + (uint)i);
                ScalarValue val = double.TryParse(parts[i].Trim(), out var d) ? new NumberValue(d) : new TextValue(parts[i].Trim());
                edits.Add((addr, Cell.FromValue(val)));
            }
        }
        if (!TryExecuteEditCells(edits, "Text to Columns", out var outcome))
            return;
        RecalculateIfAutomatic(outcome.AffectedCells ?? edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }

    private void RemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var command = new RemoveDuplicateRowsCommand(_currentSheetId, range);
        if (!TryExecuteCommand(command, "Remove Duplicates"))
            return;

        MessageBox.Show($"Removed {command.RemovedRowCount} duplicate rows.", "Remove Duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateViewport();
    }

    private void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = SheetGrid.SelectedRange;
        var defaultSource = selected?.ToString() ?? "A1:B2";
        var sourceInput = PromptForInput("Source ranges to sum (same size, separated by comma or semicolon):", defaultSource);
        if (sourceInput is null) return;

        var ranges = new List<GridRange>();
        foreach (var part in sourceInput.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                ranges.Add(GridRange.Parse(part, _currentSheetId));
            }
            catch
            {
                MessageBox.Show($"Invalid range: {part}", "Consolidate", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var defaultDestination = selected?.Start.ToA1() ?? "A1";
        var destinationInput = PromptForInput("Destination cell:", defaultDestination);
        if (destinationInput is null) return;

        if (!CellAddress.TryParse(destinationInput, _currentSheetId, out var destination))
        {
            MessageBox.Show("Invalid destination cell.", "Consolidate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new ConsolidateCommand(ranges, destination);
        if (!TryExecuteCommand(command, "Consolidate", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        SetActiveCell(destination);
        EnsureCellVisible(destination);
        UpdateViewport();
    }

    // ── What-If Analysis ─────────────────────────────────────────────────────

    private void SubtotalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range with a header row and data rows.", "Subtotal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Group column, subtotal columns, function, options (for example 1,2+3,sum,replace pagebreak above):", "1,2,sum,replace");
        if (input is null) return;

        if (!SubtotalInputParser.TryParse(input, out var options, out var error))
        {
            MessageBox.Show(error ?? "Enter group column, subtotal columns, and optional function, for example 1,2+3,sum.",
                "Subtotal", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var subtotalCommand = new SubtotalCommand(
            _currentSheetId,
            range,
            groupByColumnOffset: options.GroupColumnOffset,
            subtotalColumnOffsets: options.SubtotalColumnOffsets,
            functionNumber: options.FunctionNumber,
            pageBreakBetweenGroups: options.PageBreakBetweenGroups,
            summaryBelowData: options.SummaryBelowData);
        IWorkbookCommand command = options.ReplaceExisting
            ? new CompositeWorkbookCommand("Subtotal", [new RemoveSubtotalRowsCommand(_currentSheetId, range), subtotalCommand])
            : subtotalCommand;
        if (!TryExecuteCommand(command, "Subtotal", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void GoalSeekBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedCell = _selectionAnchor;
        var dlg = new GoalSeekDialog(_currentSheetId, selectedCell) { Owner = this };

        if (dlg.ShowDialog() != true)
            return;

        var setCell = dlg.SetCell!.Value;
        var changingCell = dlg.ChangingCell!.Value;
        var targetValue = dlg.TargetValue;

        var result = GoalSeekService.Seek(_workbook, _recalcEngine, setCell, targetValue, changingCell);

        if (!result.Converged)
        {
            MessageBox.Show(
                $"Goal Seek could not find a solution.\nClosest value: {result.FoundValue:G10}\nActual result: {result.ActualResult:G10}",
                "Goal Seek", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Goal Seek found a solution.\nChanging cell value: {result.FoundValue:G10}",
            "Goal Seek", MessageBoxButton.OKCancel, MessageBoxImage.Information);

        if (confirm == MessageBoxResult.OK)
        {
            var cmd = new GoalSeekCommand(changingCell, result.FoundValue);
            if (TryExecuteCommand(cmd, "Goal Seek"))
                RecalculateIfAutomatic([changingCell]);
        }
    }

    // ── Review tab ────────────────────────────────────────────────────────────

    private void ScenariosBtn_Click(object sender, RoutedEventArgs e)
    {
        var action = PromptForInput("Scenario action (save/show/list/report):", _workbook.Scenarios.Count == 0 ? "save" : "show");
        if (action is null)
            return;

        switch (action.Trim().ToLowerInvariant())
        {
            case "save":
            case "add":
                SaveScenarioFromSelection();
                break;
            case "show":
            case "apply":
                ShowScenarioByName();
                break;
            case "list":
            case "manager":
                ListScenarios();
                break;
            case "report":
            case "summary":
                CreateScenarioSummaryReport();
                break;
            default:
                MessageBox.Show("Enter save, show, list, or report.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void SaveScenarioFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select the changing cells for the scenario.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var name = PromptForInput("Scenario name:", _workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {_workbook.Scenarios.Count + 1}");
        if (name is null)
            return;

        var changes = range.AllCells()
            .Select(address => new ScenarioCellValue(address, sheet.GetValue(address.Row, address.Col)))
            .ToList();
        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes), "Scenario Manager"))
            return;

        MessageBox.Show($"Scenario '{name.Trim()}' saved for {changes.Count} changing cell(s).",
            "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowScenarioByName()
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PromptForInput("Scenario name to show:", _workbook.Scenarios[0].Name);
        if (name is null)
            return;

        if (!TryExecuteCommand(new ApplyScenarioCommand(name), "Scenario Manager", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (outcome.AffectedCells?.FirstOrDefault() is { } first)
        {
            SetActiveCell(first);
            EnsureCellVisible(first);
        }

        UpdateViewport();
        RefreshStatusBar();
    }

    private void ListScenarios()
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = string.Join(Environment.NewLine,
            _workbook.Scenarios.Select(s => $"{s.Name}: {s.ChangingCells.Count} changing cell(s)"));
        MessageBox.Show(message, "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateScenarioSummaryReport()
    {
        if (!TryExecuteCommand(new ScenarioSummaryReportCommand(), "Scenario Manager"))
            return;

        var report = _workbook.Sheets.LastOrDefault();
        if (report is not null)
        {
            _currentSheetId = report.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    private void ForecastSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a two-column range with headers and at least two data rows.",
                "Forecast Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var periodsInput = PromptForInput("Forecast periods:", "3");
        if (periodsInput is null)
            return;
        if (!uint.TryParse(periodsInput.Trim(), out var periods) || periods == 0)
        {
            MessageBox.Show("Enter a positive whole number of forecast periods.",
                "Forecast Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new ForecastSheetCommand(range, periods), "Forecast Sheet"))
            return;

        var forecastSheet = _workbook.Sheets.LastOrDefault();
        if (forecastSheet is not null)
        {
            _currentSheetId = forecastSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    private void DataTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select the data table range, including the formula row and input values.",
                "Data Table", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = PromptForInput("Data table type (one/two):", "one");
        if (mode is null)
            return;
        var twoVariable = mode.Trim().Equals("two", StringComparison.OrdinalIgnoreCase) ||
                          mode.Trim().Equals("2", StringComparison.OrdinalIgnoreCase);

        var formulaDefault = new CellAddress(_currentSheetId, range.Start.Row, twoVariable ? range.Start.Col : range.Start.Col + 1).ToA1();
        var formulaInput = PromptForInput("Formula cell:", formulaDefault);
        if (formulaInput is null)
            return;
        if (!CellAddress.TryParse(formulaInput.Trim(), _currentSheetId, out var formulaCell))
        {
            MessageBox.Show("Enter a valid formula cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IWorkbookCommand command;
        if (twoVariable)
        {
            var rowInputText = PromptForInput("Row input cell:", formulaCell.ToA1());
            if (rowInputText is null)
                return;
            if (!CellAddress.TryParse(rowInputText.Trim(), _currentSheetId, out var rowInputCell))
            {
                MessageBox.Show("Enter a valid row input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var columnInputText = PromptForInput("Column input cell:", formulaCell.ToA1());
            if (columnInputText is null)
                return;
            if (!CellAddress.TryParse(columnInputText.Trim(), _currentSheetId, out var columnInputCell))
            {
                MessageBox.Show("Enter a valid column input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            command = new TwoVariableDataTableCommand(range, formulaCell, rowInputCell, columnInputCell);
        }
        else
        {
            var inputInput = PromptForInput("Column input cell:", formulaCell.ToA1());
            if (inputInput is null)
                return;
            if (!CellAddress.TryParse(inputInput.Trim(), _currentSheetId, out var inputCell))
            {
                MessageBox.Show("Enter a valid input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            command = new OneVariableDataTableCommand(range, formulaCell, inputCell);
        }

        if (!TryExecuteCommand(command, "Data Table", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = SpellCheckService.FindIssues(_workbook, _currentSheetId);
        if (issues.Count == 0)
        {
            MessageBox.Show("Spelling check is complete.", "Spell Check", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var issue = issues[0];
        SetActiveCell(issue.Address);
        EnsureCellVisible(issue.Address);
        UpdateViewport();

        var replacement = PromptForInput(
            $"Replace '{issue.Word}' in {issue.Address.ToA1()} with:",
            issue.Suggestion);
        if (replacement is null) return;

        var corrected = SpellCheckService.ApplyCorrection(issue, replacement);
        if (!TryExecuteEditCells([(issue.Address, Cell.FromValue(new TextValue(corrected)))], "Spell Check"))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void WorkbookStatisticsBtn_Click(object sender, RoutedEventArgs e)
    {
        var statistics = WorkbookStatisticsService.GetStatistics(_workbook);
        var message = string.Join(Environment.NewLine,
            $"Sheets: {statistics.WorksheetCount}",
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Comments: {statistics.CommentCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Shapes and text boxes: {statistics.ShapeCount}",
            $"Named ranges: {statistics.NamedRangeCount}");
        MessageBox.Show(message, "Workbook Statistics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AccessibilityCheckerBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = AccessibilityCheckerService.FindIssues(_workbook);
        if (issues.Count == 0)
        {
            MessageBox.Show("No accessibility issues found.", "Accessibility", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = string.Join(Environment.NewLine,
            issues.Take(20).Select(issue => $"{issue.SheetName}!{issue.Location}: {issue.Message}"));
        if (issues.Count > 20)
            message += $"{Environment.NewLine}...and {issues.Count - 20} more.";
        MessageBox.Show(message, "Accessibility", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SetAltTextBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = GetTargetAltTextObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No picture, shape, or text box found on this sheet.",
                "Alt Text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Alt text:", target.AltText ?? "");
        if (input is null)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Alt Text",
                sheetId =>
                {
                    var groupedTarget = GetTargetAltTextObject(sheetId, target.Kind);
                    return target.Kind switch
                    {
                        AltTextTargetKind.Picture => new SetPictureAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input),
                        AltTextTargetKind.Shape => new SetDrawingShapeAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input),
                        _ => new SetTextBoxAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input)
                    };
                }))
        {
            return;
        }

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
        RefreshStatusBar();
    }

    private AltTextTarget? GetTargetAltTextObject(SheetId sheetId, AltTextTargetKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        if (SheetGrid.SelectedRange is { } range)
        {
            var row = range.Start.Row;
            var col = range.Start.Col;
            if (preferredKind is null or AltTextTargetKind.Picture)
            {
                var picture = sheet.Pictures.LastOrDefault(item => item.Anchor.Row == row && item.Anchor.Col == col);
                if (picture is not null)
                    return new AltTextTarget(AltTextTargetKind.Picture, picture.Id, picture.Anchor, picture.AltText);
            }

            if (preferredKind is null or AltTextTargetKind.Shape)
            {
                var shape = sheet.DrawingShapes.LastOrDefault(item => item.Anchor.Row == row && item.Anchor.Col == col);
                if (shape is not null)
                    return new AltTextTarget(AltTextTargetKind.Shape, shape.Id, shape.Anchor, shape.AltText);
            }

            if (preferredKind is null or AltTextTargetKind.TextBox)
            {
                var textBox = sheet.TextBoxes.LastOrDefault(item => item.Anchor.Row == row && item.Anchor.Col == col);
                if (textBox is not null)
                    return new AltTextTarget(AltTextTargetKind.TextBox, textBox.Id, textBox.Anchor, textBox.AltText);
            }
        }

        if (preferredKind is null or AltTextTargetKind.Picture && sheet.Pictures.Count > 0)
        {
            var picture = sheet.Pictures[^1];
            return new AltTextTarget(AltTextTargetKind.Picture, picture.Id, picture.Anchor, picture.AltText);
        }

        if (preferredKind is null or AltTextTargetKind.Shape && sheet.DrawingShapes.Count > 0)
        {
            var shape = sheet.DrawingShapes[^1];
            return new AltTextTarget(AltTextTargetKind.Shape, shape.Id, shape.Anchor, shape.AltText);
        }

        if (preferredKind is null or AltTextTargetKind.TextBox && sheet.TextBoxes.Count > 0)
        {
            var textBox = sheet.TextBoxes[^1];
            return new AltTextTarget(AltTextTargetKind.TextBox, textBox.Id, textBox.Anchor, textBox.AltText);
        }

        return null;
    }

    private void ReviewNewCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = PromptForInput($"Add comment to {addr.ToA1()}:", "");
        if (text is null) return;
        var outcome = _commandBus.Execute(_workbook.Id, new SetCommentCommand(_currentSheetId, addr, text));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Comment");
            return;
        }

        UpdateViewport();
        MessageBox.Show($"Comment added to {addr.ToA1()}.", "Comment", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReviewDeleteCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var outcome = _commandBus.Execute(_workbook.Id, new DeleteCommentCommand(_currentSheetId, addr));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Comment");
            return;
        }

        UpdateViewport();
    }

    private void ReviewPrevCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: true);
    }

    private void ReviewNextCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: false);
    }

    private void ReviewShowCommentsBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0)
        {
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = string.Join(Environment.NewLine, sheet.Comments
            .OrderBy(c => c.Key.Row)
            .ThenBy(c => c.Key.Col)
            .Select(c => $"{c.Key.ToA1()}: {c.Value}"));
        MessageBox.Show(text, "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NavigateComment(bool previous)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0)
        {
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var comments = sheet.Comments.Keys
            .OrderBy(a => a.Row)
            .ThenBy(a => a.Col)
            .ToList();
        var current = SheetGrid.SelectedRange?.Start ?? comments[0];
        var target = previous
            ? comments.LastOrDefault(a => a.Row < current.Row || (a.Row == current.Row && a.Col < current.Col))
            : comments.FirstOrDefault(a => a.Row > current.Row || (a.Row == current.Row && a.Col > current.Col));
        if (target.Equals(default(CellAddress)))
            target = previous ? comments[^1] : comments[0];

        SetActiveCell(target);
        EnsureCellVisible(target);
        UpdateViewport();
    }

    private void ProtectSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PromptForInput("Set sheet protection password (leave blank for no password):", "");
        if (pwd is null) return;
        var outcome = _commandBus.Execute(_workbook.Id, new ProtectSheetCommand(_currentSheetId, pwd));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Protect Sheet");
            return;
        }

        MessageBox.Show("Sheet is now protected.", "Protect Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand command;
        if (_workbook.IsStructureProtected)
        {
            command = new UnprotectWorkbookCommand();
        }
        else
        {
            var pwd = PromptForInput("Set workbook structure password (leave blank for no password):", "");
            if (pwd is null) return;
            command = new ProtectWorkbookCommand(pwd);
        }

        if (!TryExecuteCommand(command, "Protect Workbook"))
            return;
        RefreshSheetTabs();
    }
    private void AllowEditRangesBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = SheetGrid.SelectedRange?.ToString() ?? "A1:A1";
        var input = PromptForInput("Range to allow editing while protected:", defaultRange);
        if (input is null) return;

        GridRange range;
        try
        {
            range = GridRange.Parse(input, _currentSheetId);
        }
        catch
        {
            MessageBox.Show("Invalid range.", "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new AllowEditRangeCommand(_currentSheetId, range), "Allow Edit Ranges"))
            return;

        MessageBox.Show($"{range} can now be edited while this sheet is protected.",
            "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => ShowExcludedShareMessage();

    private static void ShowExcludedShareMessage()
    {
        MessageBox.Show(
            "Microsoft 365 Share and cloud co-authoring are excluded from Freexcel. Save the workbook and share the file through your normal file system or source-control workflow.",
            "Share Workbook",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ── View tab ─────────────────────────────────────────────────────────────

    private void ViewGridlinesChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Gridlines",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewHeadersChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Headings",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewRulerChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Ruler",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    chk.IsChecked == true)))
            return;

        UpdateViewport();
    }

    private void ViewFormulaBarChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAppViewOptionSync) return;
        if (sender is not System.Windows.Controls.CheckBox chk || FormulaBarBorder is null) return;

        _options.ShowFormulaBar = chk.IsChecked == true;
        _options.Save();
        FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NormalViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.Normal);

    private void PageBreakPreviewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

    private void PageLayoutViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);

    private void SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        if (!TryExecuteGroupedSheetCommand("Workbook View",
                sheetId => new SetWorksheetViewModeCommand(sheetId, viewMode)))
            return;

        UpdateViewport();
    }

    private void CustomViewsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewsDialog(_workbook, _commandBus) { Owner = this };
        dialog.ShowDialog();
        if (dialog.ViewApplied)
            UpdateViewport();
    }

    private void ArrangeAllPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ArrangeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !Enum.TryParse<WorkbookWindowArrangement>(tag, out var arrangement))
            return;

        TryExecuteCommand(new SetWorkbookWindowArrangementCommand(arrangement), "Arrange Windows");
    }

    private void ViewWindowDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString() ?? "This command";
        MessageBox.Show(
            $"{commandName} is deferred until Freexcel has multi-window workbook hosting. It is tracked as a documented parity gap, not a silent partial implementation.",
            commandName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void FreezePanesPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetFreezePanes(
            (uint)Math.Max(0, (int)range.Start.Row - 1),
            (uint)Math.Max(0, (int)range.Start.Col - 1));
    }
    private void FreezeTopRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(1, 0);
    }
    private void FreezeFirstColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 1);
    }
    private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 0);
    }

    private void SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var outcome = _commandBus.Execute(
            _workbook.Id,
            new SetFreezePanesCommand(_currentSheetId, frozenRows, frozenCols));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Freeze Panes");
            return;
        }

        UpdateViewport();
    }

    private void SplitViewBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        uint? splitRow = null;
        uint? splitColumn = null;
        if (sheet.SplitRow is null && sheet.SplitColumn is null &&
            SheetGrid.SelectedRange is { } range)
        {
            splitRow = range.Start.Row > 1 ? range.Start.Row : null;
            splitColumn = range.Start.Col > 1 ? range.Start.Col : null;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, splitRow, splitColumn)))
            return;

        UpdateViewport();
    }

    private void OnSplitDividerMoved(uint? splitRow, uint? splitColumn)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var nextRow = splitRow ?? sheet.SplitRow;
        var nextColumn = splitColumn ?? sheet.SplitColumn;
        if (nextRow == sheet.SplitRow && nextColumn == sheet.SplitColumn)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, nextRow, nextColumn)))
            return;

        _splitPaneViewportOffsets.Remove(_currentSheetId);
        UpdateViewport();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaxRestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseSysBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    // Slider is 0–200 with 100 at the midpoint.
    // Below midpoint: 30%–100%; above midpoint: 100%–400%.
    private static double SliderToZoomPct(double sliderVal)
    {
        if (sliderVal <= 100.0)
            return 30.0 + (sliderVal / 100.0) * 70.0;   // 30% … 100%
        else
            return 100.0 + ((sliderVal - 100.0) / 100.0) * 300.0; // 100% … 400%
    }

    private static double ZoomPctToSlider(double zoomPct)
    {
        zoomPct = Math.Max(30.0, Math.Min(400.0, zoomPct));
        if (zoomPct <= 100.0)
            return (zoomPct - 30.0) / 70.0 * 100.0;
        else
            return 100.0 + (zoomPct - 100.0) / 300.0 * 100.0;
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 5);
    }
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 5);
    }
    private void ZoomPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ZoomPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent))
            return;

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
    }
    private void ZoomCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var current = (int)Math.Round(_zoomLevel * 100);
        var input = PromptForInput("Zoom", current.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (!Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(input, out var zoomPercent))
        {
            MessageBox.Show(
                "Enter a zoom value from 10 to 400.",
                "Zoom",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
    }
    private void Zoom100Btn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 100;
    }
    private void ZoomSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        double cols = range.ColCount, rows = range.RowCount;
        double fitPct = Math.Max(Freexcel.App.UI.ZoomLevelMapper.MinZoomPercent, Math.Min(Freexcel.App.UI.ZoomLevelMapper.MaxZoomPercent, Math.Min(
            SheetGrid.ActualWidth  / Math.Max(1, cols * 80) * 100,
            SheetGrid.ActualHeight / Math.Max(1, rows * 20) * 100)));
        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct);
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomSlider == null || SheetGrid == null || StatusZoomText == null) return;
        if (_snapInProgress || _suppressZoomSync) return;
        double sliderVal = e.NewValue;

        // Snap to 100% when near the midpoint
        if (Math.Abs(sliderVal - 100.0) < 3.0)
        {
            _snapInProgress = true;
            ZoomSlider.Value = 100.0;
            _snapInProgress = false;
            sliderVal = 100.0;
        }

        double zoomPct = Freexcel.App.UI.ZoomLevelMapper.SliderToZoomPercent(sliderVal);
        var roundedZoomPct = (int)Math.Round(zoomPct);
        if (!TryExecuteGroupedSheetCommand(
                "Zoom",
                sheetId => new SetWorksheetZoomCommand(sheetId, roundedZoomPct)))
            return;

        SyncZoomFromSheet(roundedZoomPct, updateSlider: false);
        UpdateViewport();
    }

    private void SyncZoomFromSheet(int zoomPercent, bool updateSlider = true)
    {
        zoomPercent = Math.Clamp(zoomPercent, SetWorksheetZoomCommand.MinZoomPercent, SetWorksheetZoomCommand.MaxZoomPercent);
        _zoomLevel = zoomPercent / 100.0;
        if (SheetGrid is not null)
        {
            SheetGrid.ZoomFactor = _zoomLevel;
            SheetGrid.RenderTransform = new System.Windows.Media.ScaleTransform(_zoomLevel, _zoomLevel, 0, 0);
        }
        if (StatusZoomText is not null)
            StatusZoomText.Text = $"{zoomPercent}%";

        if (!updateSlider || ZoomSlider is null)
            return;

        _suppressZoomSync = true;
        try
        {
            ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
        }
        finally
        {
            _suppressZoomSync = false;
        }
    }

    // ── QAT / title bar ──────────────────────────────────────────────────────

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();
    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();

    // ── Formula bar expand chevron ────────────────────────────────────────────

    private void FormulaBarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        _options.FormulaBarExpanded = _formulaBarExpanded;
        _options.Save();
        ApplyFormulaBarExpansion();
    }

    private void ApplyFormulaBarExpansion()
    {
        if (_formulaBarExpanded)
        {
            FormulaBar.Height       = 72;
            FormulaBar.AcceptsReturn = true;
            FormulaBarExpandBtn.Content = "▲";
        }
        else
        {
            FormulaBar.ClearValue(System.Windows.Controls.TextBox.HeightProperty);
            FormulaBar.AcceptsReturn = false;
            FormulaBarExpandBtn.Content = "▼";
        }
    }

    // ── Ribbon horizontal scroll via mouse wheel ─────────────────────────────

    private void RibbonScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.5);
        e.Handled = true;
    }

    // ── Sheet tab nav arrows ──────────────────────────────────────────────────

    private void SheetNavLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            Math.Max(0, SheetTabsScroller.HorizontalOffset - 80));
    }

    private void SheetNavRightBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            SheetTabsScroller.HorizontalOffset + 80);
    }

    // ── Sheet tab context menu ────────────────────────────────────────────────

    private void SheetCtxRename_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        RenameSheetFromTab(tab);
    }

    private void RenameSheetFromTab(SheetTabViewModel tab)
    {
        var name = PromptForInput("Rename Sheet", tab.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != tab.Name)
        {
            var outcome = _commandBus.Execute(_workbook.Id, new RenameSheetCommand(tab.Id, name));
            if (!outcome.Success)
            {
                ShowCommandError(outcome, "Rename Sheet");
                return;
            }

            RecalculateWorkbook();
            RefreshSheetTabs();
        }
    }

    private void SheetCtxInsert_Click(object sender, RoutedEventArgs e)
    {
        var name = GenerateUniqueSheetName();
        var outcome = _commandBus.Execute(_workbook.Id, new AddSheetCommand(name));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[^1].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_workbook.Sheets.Count(s => !s.IsHidden) <= 1)
        {
            MessageBox.Show("Cannot delete the only visible sheet.", "Delete Sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (MessageBox.Show($"Delete sheet \"{tab.Name}\"?", "Delete Sheet",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var outcome = _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(tab.Id));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Delete Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[0].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void ActivateAdjacentVisibleSheet(int direction)
    {
        var visibleSheets = _workbook.Sheets.Where(s => !s.IsHidden).ToList();
        if (visibleSheets.Count == 0)
            return;

        var index = visibleSheets.FindIndex(s => s.Id == _currentSheetId);
        if (index < 0)
            index = 0;
        var nextIndex = Math.Clamp(index + direction, 0, visibleSheets.Count - 1);
        _currentSheetId = visibleSheets[nextIndex].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (!TryExecuteCommand(new DuplicateSheetCommand(tab.Id), "Duplicate Sheet"))
            return;

        var sourceIndex = _workbook.Sheets.ToList().FindIndex(s => s.Id == tab.Id);
        _currentSheetId = _workbook.Sheets[Math.Min(sourceIndex + 1, _workbook.Sheets.Count - 1)].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxHide_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (!TryExecuteCommand(new SetSheetHiddenCommand(tab.Id, hidden: true), "Hide Sheet"))
            return;

        if (_currentSheetId == tab.Id)
            _currentSheetId = _workbook.Sheets.First(s => !s.IsHidden).Id;
        _groupedSheetIds.Remove(tab.Id);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)
    {
        var hiddenSheets = _workbook.Sheets.Where(s => s.IsHidden).ToList();
        if (hiddenSheets.Count == 0)
        {
            MessageBox.Show("No hidden sheets.", "Unhide Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = hiddenSheets[0].Name;
        var name = PromptForInput("Unhide sheet name:", defaultName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var sheet = hiddenSheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            MessageBox.Show("Hidden sheet not found.", "Unhide Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new SetSheetHiddenCommand(sheet.Id, hidden: false), "Unhide Sheet"))
            return;

        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxTabColor_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        var sheet = _workbook.GetSheet(tab.Id);
        var defaultValue = sheet?.TabColor is { } color
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : "#217346";
        var input = PromptForInput("Tab color (#RRGGBB or none):", defaultValue);
        if (input is null) return;

        CellColor? tabColor;
        if (input.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            tabColor = null;
        }
        else if (!TryParseHexColor(input, out tabColor))
        {
            MessageBox.Show("Enter a color as #RRGGBB, or type none.", "Tab Color",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new SetSheetTabColorCommand(tab.Id, tabColor), "Tab Color"))
            return;
        RefreshSheetTabs();
    }

    private void SheetCtxSelectAllSheets_Click(object sender, RoutedEventArgs e)
    {
        var visibleSheetIds = _workbook.Sheets.Where(s => !s.IsHidden).Select(s => s.Id).ToList();
        _groupedSheetIds.Clear();
        foreach (var id in SheetGroupSelectionService.SelectAll(visibleSheetIds))
            _groupedSheetIds.Add(id);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private void SheetCtxUngroupSheets_Click(object sender, RoutedEventArgs e)
    {
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private void SheetCtxMoveLeft_Click(object sender, RoutedEventArgs e)
    {
        MoveSheetTab(sender, -1);
    }

    private void SheetCtxMoveRight_Click(object sender, RoutedEventArgs e)
    {
        MoveSheetTab(sender, 1);
    }

    private void MoveSheetTab(object sender, int direction)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;

        var fromIndex = _workbook.Sheets.ToList().FindIndex(s => s.Id == tab.Id);
        var toIndex = fromIndex + direction;
        var outcome = _commandBus.Execute(_workbook.Id, new MoveSheetCommand(fromIndex, toIndex));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Move Sheet");
            return;
        }

        _currentSheetId = tab.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private static SheetTabViewModel? GetContextMenuTab(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            FindParentContextMenu(mi) is { PlacementTarget: System.Windows.FrameworkElement fe })
        {
            return fe.DataContext as SheetTabViewModel
                ?? (fe.Parent as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        }
        return null;
    }

    private static SheetTabViewModel? FindSheetTabViewModel(System.Windows.DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is System.Windows.FrameworkElement { DataContext: SheetTabViewModel tab })
                return tab;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static System.Windows.Controls.ContextMenu? FindParentContextMenu(System.Windows.DependencyObject item)
    {
        var current = item;
        while (current is not null)
        {
            if (current is System.Windows.Controls.ContextMenu contextMenu)
                return contextMenu;
            current = System.Windows.LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool TryParseHexColor(string text, out CellColor? color)
    {
        color = null;
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    // ── Help tab ──────────────────────────────────────────────────────────────

    private void HelpOnlineBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = "https://github.com/anthropics/claude-code/issues", UseShellExecute = true });
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Freexcel\nVersion 0.5 (Phase 5)\n\nBuilt with .NET 10, WPF, ClosedXML, OxyPlot.\nPowered by Claude Code.",
            "About Freexcel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SendFeedbackBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = "https://github.com/anthropics/claude-code/issues/new", UseShellExecute = true });
    }

    private void ExportAsXps(string xpsPath)
    {
        try
        {
            var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);

            // Open the XPS package for write
            var pkg = System.IO.Packaging.Package.Open(
                xpsPath,
                System.IO.FileMode.Create,
                System.IO.FileAccess.ReadWrite);

            using var xpsDoc = new System.Windows.Xps.Packaging.XpsDocument(pkg);

            // XpsDocumentWriter(XpsDocument) is internal in ReachFramework; create it via reflection
            var writerType = typeof(System.Windows.Xps.XpsDocumentWriter);
            var ctor = writerType.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(System.Windows.Xps.Packaging.XpsDocument)],
                null);

            if (ctor == null)
                throw new InvalidOperationException("XpsDocumentWriter(XpsDocument) constructor not found in ReachFramework.");

            var writer = (System.Windows.Xps.XpsDocumentWriter)ctor.Invoke([xpsDoc]);
            writer.Write(doc.DocumentPaginator);
            // xpsDoc closed by 'using'

            MessageBox.Show(
                $"Saved XPS file:\n{xpsPath}",
                "Export XPS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save XPS file:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

internal sealed class RecentFileViewModel
{
    public string Path { get; }
    public string FileName { get; }
    public string Directory { get; }
    public string LastOpenedText { get; }

    public RecentFileViewModel(RecentFileEntry entry)
    {
        Path = entry.Path;
        FileName = System.IO.Path.GetFileName(entry.Path);
        Directory = System.IO.Path.GetDirectoryName(entry.Path) ?? "";
        LastOpenedText = FormatDate(entry.LastOpened);
    }

    private static string FormatDate(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalHours < 1) return "Just now";
        if (diff.TotalDays < 1) return "Today at " + dt.ToString("h:mm tt");
        if (diff.TotalDays < 2) return "Yesterday at " + dt.ToString("h:mm tt");
        if (diff.TotalDays < 7) return dt.DayOfWeek + " at " + dt.ToString("h:mm tt");
        return dt.Year == DateTime.Now.Year ? dt.ToString("MMM d") : dt.ToString("MMM d, yyyy");
    }
}

internal sealed class SheetTabViewModel(SheetId id, string name, CellColor? tabColor) : System.ComponentModel.INotifyPropertyChanged
{
    public SheetId Id { get; } = id;
    public CellColor? TabColor { get; } = tabColor;
    public System.Windows.Media.Brush TabBrush => TabColor is { } color
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B))
        : System.Windows.Media.Brushes.Transparent;

    private string _name = name;
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name))); }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive))); }
    }

    private bool _isGrouped;
    public bool IsGrouped
    {
        get => _isGrouped;
        set { _isGrouped = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsGrouped))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
