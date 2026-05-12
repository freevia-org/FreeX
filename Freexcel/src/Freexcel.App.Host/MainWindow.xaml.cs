using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;

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
    private Workbook _workbook;
    private SheetId _currentSheetId;

    public MainWindow(
        ILogger<MainWindow> logger,
        IViewportService viewportService,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        IEnumerable<IFileAdapter> fileAdapters,
        Workbook workbook)
    {
        _logger = logger;
        _viewportService = viewportService;
        _commandBus = commandBus;
        _recalcEngine = recalcEngine;
        _fileAdapters = fileAdapters;
        _workbook = workbook;
        
        InitializeComponent();

        _currentSheetId = _workbook.Sheets[0].Id;
        
        // Wire up scrollbars
        VerticalScroll.ValueChanged += Scroll_ValueChanged;
        HorizontalScroll.ValueChanged += Scroll_ValueChanged;
        
        // Wire up grid interactions
        SheetGrid.MouseDown += SheetGrid_MouseDown;
        this.KeyDown += MainWindow_KeyDown;
        
        // Initial data for testing
        SeedSampleData();
        
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;

        _logger.LogInformation("MainWindow initialized with Workbook {WorkbookId}", _workbook.Id);
    }

    private void SeedSampleData()
    {
        var sheet = _workbook.Sheets[0];
        for (uint r = 1; r <= 100; r++)
        {
            for (uint c = 1; c <= 10; c++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * c));
            }
        }
        sheet.SetCell(new CellAddress(sheet.Id, 1, 11), new TextValue("Hello Freexcel!"));
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateViewport();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewport();
    }

    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateViewport();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        var addr = _viewportService.HitTest(_workbook, _currentSheetId, pos.X, pos.Y, 1.0);
        if (addr != null)
        {
            SetActiveCell(addr.Value);
        }
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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

        if (SheetGrid.SelectedRange == null) return;
        var current = SheetGrid.SelectedRange.Value.Start;
        uint newRow = current.Row;
        uint newCol = current.Col;

        switch (e.Key)
        {
            case System.Windows.Input.Key.Up: if (newRow > 1) newRow--; break;
            case System.Windows.Input.Key.Down: newRow++; break;
            case System.Windows.Input.Key.Left: if (newCol > 1) newCol--; break;
            case System.Windows.Input.Key.Right: newCol++; break;
            case System.Windows.Input.Key.Enter: newRow++; break;
            case System.Windows.Input.Key.Tab: newCol++; break;
            default: return;
        }

        var newAddr = new CellAddress(_currentSheetId, newRow, newCol);
        SetActiveCell(newAddr);
        EnsureCellVisible(newAddr);
        e.Handled = true;
    }

    private void SetActiveCell(CellAddress addr)
    {
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = addr.ToA1();
        
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : cell?.Value?.ToString() ?? "";
    }

    private void EnsureCellVisible(CellAddress addr)
    {
        // Simple logic: if off-screen, move scrollbars
        if (addr.Row < VerticalScroll.Value) VerticalScroll.Value = addr.Row;
        if (addr.Col < HorizontalScroll.Value) HorizontalScroll.Value = addr.Col;
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitEdit();
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

            command = new EditCellsCommand(_currentSheetId, addr, value);
            _recalcEngine.ClearFormulaDependencies(addr);
        }

        _commandBus.Execute(_workbook.Id, command);
        _recalcEngine.Recalculate(_workbook, [addr]);
        UpdateViewport();
    }

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        // Estimate how many rows/cols fit in the current size
        // This is a simplification; in a real app we'd use the actual row heights
        uint rowCount = (uint)(SheetGrid.ActualHeight / 20) + 2; 
        uint colCount = (uint)(SheetGrid.ActualWidth / 64) + 2;

        var request = new ViewportRequest(
            TopRow: (uint)VerticalScroll.Value,
            LeftCol: (uint)HorizontalScroll.Value,
            RowCount: rowCount,
            ColCount: colCount
        );

        var viewport = _viewportService.GetViewport(_workbook, _currentSheetId, request);
        SheetGrid.Viewport = viewport;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };

        if (dialog.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
            var adapter = _fileAdapters.FirstOrDefault(a => a.Extension == ext);
            if (adapter == null) return;

            using var stream = dialog.OpenFile();
            _workbook = adapter.Load(stream);
            _workbook.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            _currentSheetId = _workbook.Sheets[0].Id;
            WorkbookNameText.Text = _workbook.Name;
            
            // Re-register dependencies
            foreach (var sheet in _workbook.Sheets)
            {
                _recalcEngine.Recalculate(_workbook, []); // Clear first
                foreach (var (addr, cell) in sheet.GetUsedCells())
                {
                    if (cell.HasFormula)
                    {
                        try {
                            var lexer = new Lexer("=" + cell.FormulaText);
                            var parser = new Parser(lexer.Tokenize());
                            _recalcEngine.RegisterFormulaDependencies(addr, parser.Parse(), sheet.Id, _workbook);
                        } catch { }
                    }
                }
            }

            _recalcEngine.Recalculate(_workbook, []);
            UpdateViewport();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = _workbook.Name
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
}