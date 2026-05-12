using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;
using System.Collections.Generic;
using System.Linq;

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
    private readonly System.Collections.ObjectModel.ObservableCollection<SheetTabViewModel> _sheetTabs = [];

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
        SheetTabsControl.ItemsSource = _sheetTabs;
        
        // Wire up scrollbars
        VerticalScroll.ValueChanged += Scroll_ValueChanged;
        HorizontalScroll.ValueChanged += Scroll_ValueChanged;
        
        // Wire up grid interactions
        SheetGrid.MouseDown += SheetGrid_MouseDown;
        SheetGrid.ColumnResized += OnColumnResized;
        SheetGrid.RowResized += OnRowResized;
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
        RefreshSheetTabs();
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
        const double headerSize = 30;
        if (pos.X < headerSize || pos.Y < headerSize) return;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

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
            SetActiveCell(new CellAddress(_currentSheetId, hitRow.Value, hitCol.Value));
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
        FormulaBar.Text = cell?.HasFormula == true ? "=" + cell.FormulaText : FormatCellValue(cell?.Value);
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
            RefreshSheetTabs();
        }
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