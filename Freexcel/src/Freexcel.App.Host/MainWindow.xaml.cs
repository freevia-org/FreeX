using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
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
        SheetGrid.ColumnResized += OnColumnResized;
        SheetGrid.RowResized += OnRowResized;
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
        var fonts = new[] { "Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia" };
        FontNameBox.ItemsSource = fonts;
        FontNameBox.SelectedItem = "Calibri";

        var sizes = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        FontSizeBox.ItemsSource = sizes;
        FontSizeBox.SelectedItem = "11";

        var formats = new[] { "General", "Number (0.00)", "Currency ($#,##0.00)", "Percentage (0%)", "Date (yyyy-MM-dd)", "Time (HH:mm:ss)", "Text (@)" };
        NumberFormatBox.ItemsSource = formats;
        NumberFormatBox.SelectedIndex = 0;

        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
        ShowStartScreen();
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
        const double headerSize = 30;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        // ── Header area ───────────────────────────────────────────────────────
        if (pos.X < headerSize || pos.Y < headerSize)
        {
            // Top-left corner: select all
            if (pos.X < headerSize && pos.Y < headerSize)
            {
                SelectAll();
                return;
            }
            // Column header: select entire column
            if (pos.Y < headerSize)
            {
                foreach (var cm in viewport.ColMetrics)
                {
                    double left = cm.LeftOffset + headerSize;
                    if (pos.X >= left && pos.X < left + cm.Width)
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                        {
                            // Extend column selection from anchor column to this column
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
                double top = rm.TopOffset + headerSize;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    {
                        // Extend row selection from anchor row to this row
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
            double top = rm.TopOffset + headerSize;
            if (pos.Y >= top && pos.Y < top + rm.Height) { hitRow = rm.Row; break; }
        }
        foreach (var cm in viewport.ColMetrics)
        {
            double left = cm.LeftOffset + headerSize;
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
            ExecuteCopy();
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
        _selectionAnchor = addr;
        _selectionCursor = addr;
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = addr.ToA1();

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
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
        const double headerSize = 30;
        if (pos.X < headerSize || pos.Y < headerSize) return null;
        uint? row = null, col = null;
        foreach (var rm in viewport.RowMetrics)
        {
            double top = rm.TopOffset + headerSize;
            if (pos.Y >= top && pos.Y < top + rm.Height) { row = rm.Row; break; }
        }
        foreach (var cm in viewport.ColMetrics)
        {
            double left = cm.LeftOffset + headerSize;
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
        // e.Delta: +120 per notch scrolled toward user (up), -120 away (down)
        int notches = e.Delta / 120;
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
        FormulaBar.Focus();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
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
                var violationMsg = DataValidationService
                    .GetApplicable(sheet, addr)
                    .Select(dv => DataValidationService.Validate(dv, value))
                    .FirstOrDefault(msg => msg != null);

                if (violationMsg != null)
                {
                    var dvRule = DataValidationService.GetApplicable(sheet, addr).First();
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
        uint topRow  = Math.Max((sheet?.FrozenRows  ?? 0) + 1, (uint)VerticalScroll.Value);
        uint leftCol = Math.Max((sheet?.FrozenCols  ?? 0) + 1, (uint)HorizontalScroll.Value);

        const double headerSize = 30;
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: SheetGrid.ActualHeight - headerSize,
            AvailableWidth: SheetGrid.ActualWidth - headerSize
        );

        var viewport = _viewportService.GetViewport(_workbook, _currentSheetId, request);
        SheetGrid.Viewport = viewport;
        SheetGrid.Charts = sheet?.Charts;
        SheetGrid.MergedRegions = sheet?.MergedRegions;
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
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left));
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
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

    private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(); ExecuteClearSelection(); }
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
        AddItem("Copy",  ExecuteCopy);
        AddItem("Paste", ExecutePaste);
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

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.ColumnWidths[col] = newWidthPx / 8.0;  // px → character units
        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        sheet.RowHeights[row] = newHeightPx;  // already px
        UpdateViewport();
    }

    private void ExecuteCopy()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        var text = ClipboardSerializer.Serialize(viewport, range);
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked */ }
    }

    private void ExecutePaste()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var rows = ClipboardSerializer.Deserialize(text);
        var edits = new List<(CellAddress, Cell)>();

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
                edits.Add((addr, Cell.FromValue(val)));
            }
        }

        if (edits.Count == 0) return;

        var command = new EditCellsCommand(_currentSheetId, edits);
        _commandBus.Execute(_workbook.Id, command);
        _recalcEngine.Recalculate(_workbook, edits.Select(e => e.Item1).ToList());
        UpdateViewport();
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