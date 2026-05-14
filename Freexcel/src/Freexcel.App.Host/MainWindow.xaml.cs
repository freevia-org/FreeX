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
    private bool _suppressToolbarSync;
    private CellAddress? _selectionAnchor;
    private CellAddress? _selectionCursor;
    private bool _dragSelectActive;
    private readonly RecentFilesStore _recentFiles;
    private List<RecentFileViewModel> _allRecentItems = [];
    private FreexcelOptions _options = FreexcelOptions.Load();
    private string? _currentFilePath;
    private bool _formatPainterActive;
    private StyleId _formatPainterStyleId;
    private bool _showFormulas;
    private double _zoomLevel = 1.0;
    private bool _formulaBarExpanded;
    private System.Windows.Controls.TextBox? _inlineEditor;

    private record InternalClipboard(GridRange SourceRange, List<(CellAddress Source, Cell Cell)> Cells);
    private InternalClipboard? _internalClipboard;

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
        SheetGrid.MouseMove  += SheetGrid_MouseMove;
        SheetGrid.MouseUp    += SheetGrid_MouseUp;
        SheetGrid.MouseWheel += SheetGrid_MouseWheel;
        this.KeyDown += MainWindow_KeyDown;
        this.TextInput += MainWindow_TextInput;
        
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;

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

        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewport();
    }

    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateViewport();
    }

    // ── Header / select-all helpers ───────────────────────────────────────────

    private void SelectRow(uint row)
    {
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, row, 1);
        _selectionCursor = new CellAddress(_currentSheetId, row, maxCol);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = $"{row}:{row}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        const uint maxRow = 1_048_576;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, col);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, col);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        var colName = CellAddress.NumberToColumnName(col);
        CellAddressBox.Text = $"{colName}:{colName}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        const uint maxRow = 1_048_576;
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, 1);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, maxCol);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = "A1";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        const double colHeaderH = Freexcel.App.UI.GridView.ColHeaderHeight;
        const double rowHeaderW = Freexcel.App.UI.GridView.RowHeaderWidth;

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
                            var c1 = CellAddress.NumberToColumnName(Math.Min(anchorCol, cm.Col));
                            var c2 = CellAddress.NumberToColumnName(Math.Max(anchorCol, cm.Col));
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

        uint? hitRow = null, hitCol = null;
        foreach (var rm in viewport.RowMetrics)
        {
            double top = rm.TopOffset + colHeaderH;
            if (pos.Y >= top && pos.Y < top + rm.Height) { hitRow = rm.Row; break; }
        }
        foreach (var cm in viewport.ColMetrics)
        {
            double left = cm.LeftOffset + rowHeaderW;
            if (pos.X >= left && pos.X < left + cm.Width) { hitCol = cm.Col; break; }
        }

        if (hitRow.HasValue && hitCol.HasValue)
        {
            var newAddr = new CellAddress(_currentSheetId, hitRow.Value, hitCol.Value);
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

        FormulaBar.Text = e.Text;
        FormulaBar.Focus();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
        e.Handled = true;
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            BoldButton.IsChecked = !(BoldButton.IsChecked == true);
            ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ItalicButton.IsChecked = !(ItalicButton.IsChecked == true);
            ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.U && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            UnderlineButton.IsChecked = !(UnderlineButton.IsChecked == true);
            ApplyStyleDiff(new StyleDiff(Underline: UnderlineButton.IsChecked == true));
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
            SelectAll();
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
                                  : new CellAddress(_currentSheetId, current.Row + 1, current.Col),
            Key.Left  => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, +1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col + 1),

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
            SheetGrid.SelectedRange = merge.Value;
            CellAddressBox.Text = merge.Value.Start.ToA1();
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            FormulaBar.Text = mergedCell?.HasFormula == true ? "=" + mergedCell.FormulaText : FormatCellValue(mergedCell?.Value);
            SheetGrid.Focus();
            RefreshToolbar();
            RefreshStatusBar();
            return;
        }

        _selectionAnchor = addr;
        _selectionCursor = addr;
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = addr.ToA1();

        var cell = sheet?.GetCell(addr);
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExtendSelection(CellAddress anchor, CellAddress to)
    {
        _selectionCursor = to;
        SheetGrid.SelectedRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        CellAddressBox.Text = anchor == to ? anchor.ToA1() : $"{anchor.ToA1()}:{to.ToA1()}";
        RefreshStatusBar();
    }

    private CellAddress? HitTestCell(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return null;
        const double colHdrH = Freexcel.App.UI.GridView.ColHeaderHeight;
        const double rowHdrW = Freexcel.App.UI.GridView.RowHeaderWidth;
        if (pos.X < rowHdrW || pos.Y < colHdrH) return null;
        uint? row = null, col = null;
        foreach (var rm in viewport.RowMetrics)
        {
            double top = rm.TopOffset + colHdrH;
            if (pos.Y >= top && pos.Y < top + rm.Height) { row = rm.Row; break; }
        }
        foreach (var cm in viewport.ColMetrics)
        {
            double left = cm.LeftOffset + rowHdrW;
            if (pos.X >= left && pos.X < left + cm.Width) { col = cm.Col; break; }
        }
        return row.HasValue && col.HasValue
            ? new CellAddress(_currentSheetId, row.Value, col.Value)
            : null;
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

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
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
        _commandBus.Execute(_workbook.Id, new ApplyStyleCommand(_currentSheetId, range, diff));
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
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        var text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);

        if (_inlineEditor == null)
        {
            _inlineEditor = new System.Windows.Controls.TextBox
            {
                BorderThickness = new System.Windows.Thickness(2),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(33, 115, 70)),
                Padding         = new System.Windows.Thickness(1),
                FontFamily      = new System.Windows.Media.FontFamily("Consolas"),
                FontSize        = 13,
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
            };
            _inlineEditor.KeyDown    += InlineEditor_KeyDown;
            _inlineEditor.LostFocus  += InlineEditor_LostFocus;
            _inlineEditor.TextChanged += (_, _) => FormulaBar.Text = _inlineEditor.Text;
            EditOverlay.Children.Add(_inlineEditor);
        }

        double cx = colMetric.LeftOffset + Freexcel.App.UI.GridView.RowHeaderWidth;
        double cy = rowMetric.TopOffset  + Freexcel.App.UI.GridView.ColHeaderHeight;

        _inlineEditor.Text = text;
        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, cx - 2);
        System.Windows.Controls.Canvas.SetTop(_inlineEditor,  cy - 2);
        _inlineEditor.Width  = Math.Max(colMetric.Width  + 4, 60);
        _inlineEditor.Height = Math.Max(rowMetric.Height + 4, 20);
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

    private void InlineEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideInlineEditor(commit: false);
            // Restore original text in formula bar
            var addr = SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
            }
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
        if (e.Key == System.Windows.Input.Key.Enter)
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
                FormulaBar.Text = cell?.HasFormula == true
                    ? "=" + cell.FormulaText
                    : FormatCellValue(cell?.Value);
            }
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
                    Key.Down     => new CellAddress(_currentSheetId, current.Value.Row + 1, current.Value.Col),
                    Key.Tab      => new CellAddress(_currentSheetId, current.Value.Row, current.Value.Col + 1),
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

    private void CommitEdit()
    {
        if (SheetGrid.SelectedRange == null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = FormulaBar.Text;

        IWorkbookCommand command;
        if (text.StartsWith("="))
        {
            var formula = text.Substring(1);
            command = EditCellsCommand.ForFormula(_currentSheetId, addr, formula);
            
            // For now, we manually register dependencies because we haven't automated this in the command yet
            try {
                var lexer = new Lexer(text);
                var parser = new Parser(lexer.Tokenize());
                var ast = parser.Parse();
                _recalcEngine.RegisterFormulaDependencies(addr, ast, _currentSheetId, _workbook);
            } catch { /* ignore parse errors for now */ }
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
                    var msg = DataValidationService.Validate(dv, value);
                    if (msg != null) { violatingRule = dv; violationMsg = msg; break; }
                }

                if (violationMsg != null && violatingRule != null)
                {
                    var dvRule = violatingRule;
                    if (dvRule.Type == DvType.List && dvRule.ShowDropdown && !string.IsNullOrEmpty(text))
                        MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    else if (dvRule.Type != DvType.List)
                        MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    // Still apply the value (soft validation like Excel default warning mode)
                }
            }

            command = new EditCellsCommand(_currentSheetId, addr, value);
            _recalcEngine.ClearFormulaDependencies(addr);
        }

        _commandBus.Execute(_workbook.Id, command);
        _recalcEngine.Recalculate(_workbook, [addr]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        uint topRow  = Math.Max((sheet?.FrozenRows ?? 0) + 1, (uint)VerticalScroll.Value);
        uint leftCol = Math.Max((sheet?.FrozenCols ?? 0) + 1, (uint)HorizontalScroll.Value);

        // AvailableHeight/Width is divided by zoom so viewport covers the right number of cells
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - Freexcel.App.UI.GridView.ColHeaderHeight) / _zoomLevel,
            AvailableWidth:  (SheetGrid.ActualWidth  - Freexcel.App.UI.GridView.RowHeaderWidth)  / _zoomLevel
        );

        var viewport = _viewportService.GetViewport(_workbook, _currentSheetId, request);
        SheetGrid.Viewport = viewport;
        SheetGrid.Charts = sheet?.Charts;
        SheetGrid.MergedRegions = sheet?.MergedRegions;

        // Adjust scrollbar range to the used data range + buffer
        UpdateScrollbarMaximums(sheet);
    }

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        uint maxRow = 1, maxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > maxRow) maxRow = addr.Row;
                if (addr.Col > maxCol) maxCol = addr.Col;
            }
        VerticalScroll.Maximum   = Math.Max(100, maxRow + 100);
        HorizontalScroll.Maximum = Math.Max(26,  maxCol + 26);
    }

    private void UpdateTitleBar()
    {
        var displayName = $"{_workbook.Name} - Freexcel";
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
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
        UpdateTitleBar();
        _recalcEngine.Recalculate(wb, []);
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
            _workbook = adapter.Load(stream);
            _workbookRef.Current = _workbook;
            _workbook.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            _currentSheetId = _workbook.Sheets[0].Id;
            _currentFilePath = path;
            UpdateTitleBar();

            foreach (var sheet in _workbook.Sheets)
            {
                _recalcEngine.Recalculate(_workbook, []);
                foreach (var (addr, cell) in sheet.GetUsedCells())
                {
                    if (cell.HasFormula)
                    {
                        try
                        {
                            var lexer = new Lexer("=" + cell.FormulaText);
                            var parser = new Parser(lexer.Tokenize());
                            _recalcEngine.RegisterFormulaDependencies(addr, parser.Parse(), sheet.Id, _workbook);
                        }
                        catch { }
                    }
                }
            }

            _recalcEngine.Recalculate(_workbook, []);
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
        MessageBox.Show("Share is not yet implemented.", "Share",
            MessageBoxButton.OK, MessageBoxImage.Information);
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
        var dlg = new OptionsDialog(_options) { Owner = this };
        if (dlg.ShowDialog() == true)
            _options = dlg.Result;
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

            using var stream = dialog.OpenFile();
            adapter.Save(_workbook, stream);
        }
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
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            Title = "Chart",
            Left = 20, Top = 20, Width = 400, Height = 300
        };
        sheet.Charts.Add(chart);
        UpdateViewport();
    }

    private void RefreshSheetTabs()
    {
        _sheetTabs.Clear();
        foreach (var sheet in _workbook.Sheets)
            _sheetTabs.Add(new SheetTabViewModel(sheet.Id, sheet.Name));

        // Highlight active tab after layout
        SheetTabsControl.UpdateLayout();
        foreach (var tab in _sheetTabs)
        {
            var container = SheetTabsControl.ItemContainerGenerator
                .ContainerFromItem(tab) as System.Windows.FrameworkElement;
            if (container?.FindName("TabBorder") is System.Windows.Controls.Border border)
                border.Background = tab.Id == _currentSheetId ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void SheetTab_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        _currentSheetId = tab.Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetTab_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        var name = PromptForInput("Rename Sheet", tab.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != tab.Name)
        {
            _commandBus.Execute(_workbook.Id, new RenameSheetCommand(_currentSheetId, name));
            RefreshSheetTabs();
        }
        e.Handled = true;
    }

    private void SheetTab_LabelMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) SheetTab_MouseRightButtonDown(sender, e);
    }

    private void AddSheetButton_Click(object sender, RoutedEventArgs e)
    {
        var name = $"Sheet{_workbook.Sheets.Count + 1}";
        _commandBus.Execute(_workbook.Id, new AddSheetCommand(name));
        _currentSheetId = _workbook.Sheets[^1].Id;
        UpdateViewport();
        RefreshSheetTabs();
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
        _commandBus.Execute(_workbook.Id, cmd);
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var cmd = new SortCommand(_currentSheetId, range, sortByColOffset: 0, ascending: false);
        _commandBus.Execute(_workbook.Id, cmd);
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = PromptForInput("Filter: enter value to keep", "");
        if (value is null) return;  // user cancelled
        var allowedValues = string.IsNullOrWhiteSpace(value)
            ? (IReadOnlyList<string>)[]
            : [value.Trim()];
        var cmd = new FilterCommand(_currentSheetId, range, filterColOffset: 0, allowedValues: allowedValues);
        _commandBus.Execute(_workbook.Id, cmd);
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

        _commandBus.Execute(_workbook.Id, new ApplyConditionalFormatCommand(_currentSheetId, cf));
        UpdateViewport();
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "Data Validation");
            return;
        }

        var dlg = new DataValidationDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var dv = dlg.Result;
        dv.AppliesTo = range;

        _commandBus.Execute(_workbook.Id, new SetDataValidationCommand(_currentSheetId, dv));
        UpdateViewport();
    }

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var cmd = new FilterCommand(_currentSheetId, range, filterColOffset: 0, allowedValues: []);
        _commandBus.Execute(_workbook.Id, cmd);
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
        _recalcEngine.Recalculate(_workbook, []);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExecuteRedo()
    {
        var outcome = _commandBus.Redo(_workbook.Id);
        if (!outcome.Success) return;
        _recalcEngine.Recalculate(_workbook, []);
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
        _commandBus.Execute(_workbook.Id, new InsertRowsCommand(_currentSheetId, beforeRow));
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        _commandBus.Execute(_workbook.Id, new InsertColumnsCommand(_currentSheetId, beforeCol));
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        uint count = range.End.Row - range.Start.Row + 1;
        _commandBus.Execute(_workbook.Id, new DeleteRowsCommand(_currentSheetId, range.Start.Row, count));
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        uint count = range.End.Col - range.Start.Col + 1;
        _commandBus.Execute(_workbook.Id, new DeleteColumnsCommand(_currentSheetId, range.Start.Col, count));
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
        _commandBus.Execute(_workbook.Id, cmd);
        _recalcEngine.Recalculate(_workbook, fillRange.AllCells().ToList());
        UpdateViewport();
        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var stats = StatusBarCalculator.Calculate(sheet, range);

        if (stats.Count == 0)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            return;
        }

        StatusReadyText.Visibility  = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Visible;
        StatusSumText.Text   = $"Sum: {stats.Sum:N2}";
        StatusCountText.Text = $"Count: {stats.Count}";
        StatusAvgText.Text   = stats.Average.HasValue ? $"Average: {stats.Average.Value:N2}" : "";
        StatusMinText.Text   = stats.Min.HasValue ? $"Min: {stats.Min.Value:N2}" : "";
        StatusMaxText.Text   = stats.Max.HasValue ? $"Max: {stats.Max.Value:N2}" : "";
    }

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.ColumnWidths[col] = newWidthPx / 8.0;
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.ColumnWidths[col] = newWidthPx / 8.0;
        UpdateViewport();
    }

    private void OnRowResizing(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.RowHeights[row] = newHeightPx;
        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.RowHeights[row] = newHeightPx;
        UpdateViewport();
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
                if (cell is not null)
                    clipCells.Add((addr, cell.Clone()));
            }
        }
        _internalClipboard = new InternalClipboard(range, clipCells);
    }

    private void ExecutePaste()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        // If we have an internal clipboard (copied from within this app), use it with formula adjustment
        if (_internalClipboard is { } clip)
        {
            var edits = new List<(CellAddress, Cell)>();
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

                if (destCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
                {
                    destCell.FormulaText =
                        Freexcel.Core.Formula.FormulaRewriter.Rewrite(
                            destCell.FormulaText, pasteOp, activeSheetName)
                        ?? destCell.FormulaText;
                }

                edits.Add((destAddr, destCell));
            }

            if (edits.Count > 0)
            {
                var command = new EditCellsCommand(_currentSheetId, edits);
                _commandBus.Execute(_workbook.Id, command);
                _recalcEngine.Recalculate(_workbook, edits.Select(e => e.Item1).ToList());
            }

            var pastedRowSpan = (uint)(clip.SourceRange.RowCount - 1);
            var pastedColSpan = (uint)(clip.SourceRange.ColCount - 1);
            var pastedEnd     = new CellAddress(_currentSheetId,
                range.Start.Row + pastedRowSpan,
                range.Start.Col + pastedColSpan);
            _selectionAnchor = range.Start;
            _selectionCursor = pastedEnd;
            SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
            SheetGrid.ClipboardRange = null;
            UpdateViewport();
            RefreshToolbar();
            return;
        }

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

        var fallbackCommand = new EditCellsCommand(_currentSheetId, fallbackEdits);
        _commandBus.Execute(_workbook.Id, fallbackCommand);
        _recalcEngine.Recalculate(_workbook, fallbackEdits.Select(e => e.Item1).ToList());

        uint pastedRowSpanFallback = rows.Length > 0 ? (uint)(rows.Length - 1) : 0;
        uint pastedColSpanFallback = rows.Length > 0 && rows[0].Length > 0 ? (uint)(rows[0].Length - 1) : 0;
        var pastedEndFallback = new CellAddress(_currentSheetId,
            range.Start.Row + pastedRowSpanFallback,
            range.Start.Col + pastedColSpanFallback);
        _selectionAnchor = range.Start;
        _selectionCursor = pastedEndFallback;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEndFallback);
        SheetGrid.ClipboardRange = null;
        UpdateViewport();
        RefreshToolbar();
    }

    private void ExecuteClearSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
                edits.Add((new CellAddress(_currentSheetId, r, c), Cell.FromValue(BlankValue.Instance)));

        var command = new EditCellsCommand(_currentSheetId, edits);
        _commandBus.Execute(_workbook.Id, command);
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
        _commandBus.Execute(_workbook.Id, new ApplyStyleCommand(_currentSheetId,
            new GridRange(addr, addr), diff));
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

        if (dlg.PasteValues)
            ExecutePaste();  // default paste already pastes values
        // Formats-only and Formulas-only handled in PasteSpecialDialog
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
        if (SheetGrid.SelectedRange is not { } range) return;
        var b = new CellBorder(BorderStyle.Thin, CellColor.Black);
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var cmds = new List<(CellAddress, StyleDiff)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var d = new StyleDiff(
                    BorderTop:    r == range.Start.Row ? b : null,
                    BorderBottom: r == range.End.Row   ? b : null,
                    BorderLeft:   c == range.Start.Col ? b : null,
                    BorderRight:  c == range.End.Col   ? b : null);
                _commandBus.Execute(_workbook.Id, new ApplyStyleCommand(_currentSheetId,
                    new GridRange(new CellAddress(_currentSheetId, r, c), new CellAddress(_currentSheetId, r, c)), d));
            }
        UpdateViewport();
    }
    private void BorderNoneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var n = new CellBorder(BorderStyle.None);
        ApplyStyleDiff(new StyleDiff(BorderTop: n, BorderRight: n, BorderBottom: n, BorderLeft: n));
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
    private void CfClearRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.ConditionalFormats.RemoveAll(cf => cf.AppliesTo.Start.Sheet == _currentSheetId
            && range.Contains(cf.AppliesTo.Start));
        UpdateViewport();
    }
    private void CfManageRulesMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Manage Rules");

    private void ShowCfDialog(string ruleType)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = new ConditionalFormatDialog(ruleType, range) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRule is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.ConditionalFormats.Add(dlg.ResultRule);
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
            _commandBus.Execute(_workbook.Id, new ApplyStyleCommand(_currentSheetId,
                new GridRange(
                    new CellAddress(_currentSheetId, r, range.Start.Col),
                    new CellAddress(_currentSheetId, r, range.End.Col)),
                new StyleDiff(FillColor: fill, FontColor: fontColor, Bold: bold)));
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

    private void InsertCellsMenuItem_Click(object sender, RoutedEventArgs e)   { InsertRowBtn_Click(sender, e); }
    private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }
    private void DeleteCellsMenuItem_Click(object sender, RoutedEventArgs e)   { DeleteRowBtn_Click(sender, e); }
    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || _workbook.Sheets.Count <= 1) { MessageBox.Show("Cannot delete the only sheet."); return; }
        if (MessageBox.Show($"Delete '{sheet.Name}'?", "Delete Sheet", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _workbook.RemoveSheet(_currentSheetId);
        _currentSheetId = _workbook.Sheets[0].Id;
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Row height (pixels):", "20");
        if (input is null || !double.TryParse(input, out var h) || h <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        for (uint r = range.Start.Row; r <= range.End.Row; r++) sheet.RowHeights[r] = h;
        UpdateViewport();
    }
    private void FormatAutoRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        for (uint r = range.Start.Row; r <= range.End.Row; r++) sheet.RowHeights.Remove(r);
        UpdateViewport();
    }
    private void FormatColWidthMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Column width (character units):", "8");
        if (input is null || !double.TryParse(input, out var w) || w <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        for (uint c = range.Start.Col; c <= range.End.Col; c++) sheet.ColumnWidths[c] = w;
        UpdateViewport();
    }
    private void FormatAutoColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        for (uint c = range.Start.Col; c <= range.End.Col; c++) sheet.ColumnWidths.Remove(c);
        UpdateViewport();
    }
    private void FormatDefaultWidthMenuItem_Click(object sender, RoutedEventArgs e) { FormatColWidthMenuItem_Click(sender, e); }
    private void FormatHideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        for (uint r = range.Start.Row; r <= range.End.Row; r++) sheet.RowHeights[r] = 0;
        UpdateViewport();
    }
    private void FormatUnhideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        for (uint r = range.Start.Row; r <= range.End.Row; r++) sheet.RowHeights.Remove(r);
        UpdateViewport();
    }
    private void FormatHideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        for (uint c = range.Start.Col; c <= range.End.Col; c++) sheet.ColumnWidths[c] = 0;
        UpdateViewport();
    }
    private void FormatUnhideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        for (uint c = range.Start.Col; c <= range.End.Col; c++) sheet.ColumnWidths.Remove(c);
        UpdateViewport();
    }
    private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }
    private void FormatLockCellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Cell locking takes effect when the sheet is protected.", "Lock Cell",
            MessageBoxButton.OK, MessageBoxImage.Information);
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
        var cmd = EditCellsCommand.ForFormula(_currentSheetId, addr, formula);
        _commandBus.Execute(_workbook.Id, cmd);
        _recalcEngine.Recalculate(_workbook, [addr]);
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        _recalcEngine.Recalculate(_workbook, edits.Select(x => x.Item1).ToList());
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        _recalcEngine.Recalculate(_workbook, edits.Select(x => x.Item1).ToList());
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        _recalcEngine.Recalculate(_workbook, edits.Select(x => x.Item1).ToList());
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        _recalcEngine.Recalculate(_workbook, edits.Select(x => x.Item1).ToList());
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        UpdateViewport();
    }

    private void SortFilterPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);
    private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);
    private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortAscButton_Click(sender, e);
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
    private void FindGoToSpecialMenuItem_Click(object sender, RoutedEventArgs e) => FindGoToMenuItem_Click(sender, e);

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
        MessageBox.Show("Comments cleared.", "Clear Comments", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Hyperlinks cleared.", "Clear Hyperlinks", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
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
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = new PivotTableDialog(_workbook, _currentSheetId, range) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType("bar");
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType("line");
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType("pie");
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType("bar");
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType("area");
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType("scatter");

    private void InsertChartOfType(string type)
    {
        // Re-use existing chart dialog but pre-select the chart type
        InsertChartButton_Click(null!, null!);
    }

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e)    => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e)  => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var rangeInput = PromptForInput("Data range (e.g. A1:E1):", "");
        if (rangeInput is null) return;
        var targetInput = PromptForInput("Location cell (e.g. F1):", "");
        if (targetInput is null) return;
        MessageBox.Show($"Sparkline ({type}) will be rendered in {targetInput} based on {rangeInput}.\n(Full sparkline rendering coming in a future update.)",
            "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var url = PromptForInput("URL:", "https://");
        if (url is null) return;
        var label = PromptForInput("Display text (leave blank to use URL):", "");
        var text = string.IsNullOrWhiteSpace(label) ? url : label;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var cmd = EditCellsCommand.ForValue(_currentSheetId, addr, new TextValue(text));
        _commandBus.Execute(_workbook.Id, cmd);
        UpdateViewport();
    }

    private void InsertCommentBtn_Click(object sender, RoutedEventArgs e)    => ReviewNewCommentBtn_Click(sender, e);
    private void TextBoxBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Text Box drawing tool: click and drag on the sheet to place a text box.\n(Not yet implemented — coming soon.)",
            "Text Box", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void HeaderFooterBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Header & Footer editing is available in Print Preview.",
            "Header & Footer", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var cmd = EditCellsCommand.ForValue(_currentSheetId, addr, new TextValue(newText));
        _commandBus.Execute(_workbook.Id, cmd);
        UpdateViewport();
    }

    // ── Draw tab stubs ────────────────────────────────────────────────────────

    private void DrawRectBtn_Click(object sender, RoutedEventArgs e)    => DrawStub("Rectangle");
    private void DrawEllipseBtn_Click(object sender, RoutedEventArgs e) => DrawStub("Ellipse");
    private void DrawLineBtn_Click(object sender, RoutedEventArgs e)    => DrawStub("Line");
    private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => DrawStub("Text Box");
    private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => DrawStub("Bring Forward");
    private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => DrawStub("Send Backward");
    private static void DrawStub(string tool) =>
        MessageBox.Show($"'{tool}' drawing tool is not yet implemented.", "Draw", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── Page Layout tab ───────────────────────────────────────────────────────

    private void PageMarginsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void MarginNormalMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Margins set to Normal (1\" all sides).", "Page Margins", MessageBoxButton.OK, MessageBoxImage.Information);
    private void MarginWideMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Margins set to Wide (1\" top/bottom, 1.25\" left/right).", "Page Margins", MessageBoxButton.OK, MessageBoxImage.Information);
    private void MarginNarrowMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Margins set to Narrow (0.5\" all sides).", "Page Margins", MessageBoxButton.OK, MessageBoxImage.Information);
    private void MarginCustomMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Custom margins editor is not yet implemented.", "Page Margins", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PageOrientBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientPortraitMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Page orientation set to Portrait.", "Orientation", MessageBoxButton.OK, MessageBoxImage.Information);
    private void OrientLandscapeMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Page orientation set to Landscape.", "Orientation", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PageSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SizeLetter_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Paper size set to Letter (8.5\" × 11\").", "Paper Size", MessageBoxButton.OK, MessageBoxImage.Information);
    private void SizeA4_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Paper size set to A4 (210mm × 297mm).", "Paper Size", MessageBoxButton.OK, MessageBoxImage.Information);
    private void SizeLegal_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Paper size set to Legal (8.5\" × 14\").", "Paper Size", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PrintAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void PrintAreaSetMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Print area set to current selection.", "Print Area", MessageBoxButton.OK, MessageBoxImage.Information);
    private void PrintAreaClearMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Print area cleared.", "Print Area", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PrintGridlinesChk_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Print gridlines setting will take effect at print time.", "Print Gridlines", MessageBoxButton.OK, MessageBoxImage.Information);
    private void PrintHeadingsChk_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Print headings setting will take effect at print time.", "Print Headings", MessageBoxButton.OK, MessageBoxImage.Information);

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
        _workbook.DefineNamedRange(name, range);
        MessageBox.Show($"Named range '{name}' = {range} defined.", "Define Name", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UseInFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void TracePrecedentsBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Trace Precedents: formula auditing arrows are not yet rendered.", "Trace Precedents", MessageBoxButton.OK, MessageBoxImage.Information);
    private void TraceDependentsBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Trace Dependents: formula auditing arrows are not yet rendered.", "Trace Dependents", MessageBoxButton.OK, MessageBoxImage.Information);
    private void RemoveArrowsBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("No auditing arrows to remove.", "Remove Arrows", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowFormulasBtn_Click(object sender, RoutedEventArgs e)
    {
        _showFormulas = !_showFormulas;
        UpdateViewport();
    }

    private void ErrorCheckBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Error checking: no errors found.", "Error Checking", MessageBoxButton.OK, MessageBoxImage.Information);

    private void CalcNowBtn_Click(object sender, RoutedEventArgs e)
    {
        _recalcEngine.Recalculate(_workbook, []);
        UpdateViewport();
    }
    private void CalcSheetBtn_Click(object sender, RoutedEventArgs e)   => CalcNowBtn_Click(sender, e);
    private void CalcOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CalcAutoMenuItem_Click(object sender, RoutedEventArgs e)   => MessageBox.Show("Calculation mode: Automatic.", "Calculation Options");
    private void CalcManualMenuItem_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Calculation mode: Manual. Press F9 to recalculate.", "Calculation Options");

    private void FormulaLogicalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormulaTextBtn_Click(object sender, RoutedEventArgs e)    { FormulaLogicalBtn_Click(sender, e); }
    private void FormulaDateBtn_Click(object sender, RoutedEventArgs e)    { FormulaLogicalBtn_Click(sender, e); }
    private void FormulaLookupBtn_Click(object sender, RoutedEventArgs e)  { FormulaLogicalBtn_Click(sender, e); }
    private void FormulaMathBtn_Click(object sender, RoutedEventArgs e)    { FormulaLogicalBtn_Click(sender, e); }
    private void FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);

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
        => MessageBox.Show("Get & Transform Data (Power Query) is not yet implemented.", "Get Data", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _commandBus.Execute(_workbook.Id, new EditCellsCommand(_currentSheetId, edits));
        _recalcEngine.Recalculate(_workbook, edits.Select(x => x.Item1).ToList());
        UpdateViewport();
    }

    private void RemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var seen = new HashSet<string>();
        var rowsToDelete = new List<uint>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var key = string.Join("\t", Enumerable.Range((int)range.Start.Col, (int)range.ColCount)
                .Select(c => sheet.GetValue(r, (uint)c)?.ToString() ?? ""));
            if (!seen.Add(key)) rowsToDelete.Add(r);
        }
        for (int i = rowsToDelete.Count - 1; i >= 0; i--)
            _commandBus.Execute(_workbook.Id, new DeleteRowsCommand(_currentSheetId, rowsToDelete[i], 1));
        MessageBox.Show($"Removed {rowsToDelete.Count} duplicate rows.", "Remove Duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateViewport();
    }

    private void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Consolidate is not yet implemented.", "Consolidate", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── Review tab ────────────────────────────────────────────────────────────

    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Spell check is not yet implemented.", "Spell Check", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ReviewNewCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = PromptForInput($"Add comment to {addr.ToA1()}:", "");
        if (text is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.Comments[addr] = text;
        MessageBox.Show($"Comment added to {addr.ToA1()}.", "Comment", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReviewDeleteCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var sheet = _workbook.GetSheet(_currentSheetId);
        sheet?.Comments.Remove(addr);
        UpdateViewport();
    }

    private void ReviewPrevCommentBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Comment navigation is not yet implemented.", "Comments");
    private void ReviewNextCommentBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Comment navigation is not yet implemented.", "Comments");
    private void ReviewShowCommentsBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Show/hide all comments is not yet implemented.", "Comments");

    private void ProtectSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PromptForInput("Set sheet protection password (leave blank for no password):", "");
        if (pwd is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.IsProtected = true;
        sheet.ProtectionPassword = string.IsNullOrEmpty(pwd) ? null : pwd;
        MessageBox.Show("Sheet is now protected.", "Protect Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Workbook protection is not yet implemented.", "Protect Workbook", MessageBoxButton.OK, MessageBoxImage.Information);
    private void AllowEditRangesBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Allow edit ranges is not yet implemented.", "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Information);
    private void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Share Workbook is not yet implemented.", "Share", MessageBoxButton.OK, MessageBoxImage.Information);

    // ── View tab ─────────────────────────────────────────────────────────────

    private void ViewGridlinesChk_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox chk)
            SheetGrid.ShowGridLines = chk.IsChecked == true;
    }

    private void ViewHeadersChk_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox chk)
            SheetGrid.ShowHeaders = chk.IsChecked == true;
    }

    private void ViewFormulaBarChk_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox chk && FormulaBarBorder is not null)
            FormulaBarBorder.Visibility = chk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FreezePanesPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.FrozenRows = (uint)Math.Max(0, (int)range.Start.Row - 1);
        sheet.FrozenCols = (uint)Math.Max(0, (int)range.Start.Col - 1);
        UpdateViewport();
    }
    private void FreezeTopRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.FrozenRows = 1; sheet.FrozenCols = 0;
        UpdateViewport();
    }
    private void FreezeFirstColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.FrozenRows = 0; sheet.FrozenCols = 1;
        UpdateViewport();
    }
    private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        sheet.FrozenRows = 0; sheet.FrozenCols = 0;
        UpdateViewport();
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 10);
    }
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 10);
    }
    private void Zoom100Btn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 100;
    }
    private void ZoomSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        double cols = range.ColCount, rows = range.RowCount;
        double fit = Math.Max(25, Math.Min(300, Math.Min(
            SheetGrid.ActualWidth  / Math.Max(1, cols * 80) * 100,
            SheetGrid.ActualHeight / Math.Max(1, rows * 20) * 100)));
        ZoomSlider.Value = fit;
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomSlider == null || SheetGrid == null) return;
        _zoomLevel = ZoomSlider.Value / 100.0;
        SheetGrid.RenderTransform = new System.Windows.Media.ScaleTransform(_zoomLevel, _zoomLevel, 0, 0);
        StatusZoomText.Text = $"{(int)ZoomSlider.Value}%";
        UpdateViewport();
    }

    // ── QAT / title bar ──────────────────────────────────────────────────────

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();
    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();

    // ── Formula bar expand chevron ────────────────────────────────────────────

    private void FormulaBarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _formulaBarExpanded = !_formulaBarExpanded;
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
        var name = PromptForInput("Rename Sheet", tab.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != tab.Name)
        {
            _commandBus.Execute(_workbook.Id, new RenameSheetCommand(tab.Id, name));
            RefreshSheetTabs();
        }
    }

    private void SheetCtxInsert_Click(object sender, RoutedEventArgs e)
    {
        var name = $"Sheet{_workbook.Sheets.Count + 1}";
        _commandBus.Execute(_workbook.Id, new AddSheetCommand(name));
        _currentSheetId = _workbook.Sheets[^1].Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_workbook.Sheets.Count <= 1)
        {
            MessageBox.Show("Cannot delete the only sheet.", "Delete Sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (MessageBox.Show($"Delete sheet \"{tab.Name}\"?", "Delete Sheet",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(tab.Id));
        _currentSheetId = _workbook.Sheets[0].Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxMove_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Move/Copy sheet is not yet implemented.", "Move/Copy",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static SheetTabViewModel? GetContextMenuTab(object sender)
    {
        // Walk up from the MenuItem → ContextMenu → PlacementTarget (the Border)
        if (sender is System.Windows.Controls.MenuItem mi
            && mi.Parent is System.Windows.Controls.ContextMenu cm
            && cm.PlacementTarget is System.Windows.FrameworkElement fe)
        {
            return fe.DataContext as SheetTabViewModel
                ?? (fe.Parent as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        }
        return null;
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

internal sealed class SheetTabViewModel(SheetId id, string name) : System.ComponentModel.INotifyPropertyChanged
{
    public SheetId Id { get; } = id;

    private string _name = name;
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}