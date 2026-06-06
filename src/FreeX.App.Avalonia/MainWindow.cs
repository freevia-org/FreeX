using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed class MainWindow : Window
{
    private const double HeaderColumnWidth = 58;
    private const double HeaderRowHeight = 28;
    private const double ViewportHeight = 880;
    private const double ViewportWidth = 1440;
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);
    private static readonly IBrush SelectionBorder = Brush(11, 112, 116);
    private static readonly IBrush SelectionHeaderBackground = Brush(225, 244, 242);
    private static readonly IBrush SelectionHeaderForeground = Brush(13, 86, 89);

    private readonly StartupWorkbookLoadResult _source;
    private readonly Workbook _workbook;
    private readonly Sheet _sheet;
    private readonly IViewportService _viewportService = new ViewportService();
    private readonly RecalcEngine _recalcEngine = new(new DependencyGraph(), new FormulaEvaluator());
    private readonly WorkbookCellEditService _cellEditService;
    private readonly ContentControl _sheetGridHost = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _cellAddressText = new();
    private readonly TextBox _formulaBox = new();
    private ViewportModel _viewport = new([], [], [], null, []);
    private CellAddress _activeCell;
    private CellAddress? _formulaEditAddress;

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        _source = new StartupWorkbookLoader().Load(startupArguments);
        _workbook = _source.Workbook;
        _sheet = EnsureSheet(_workbook);
        _activeCell = GetInitialActiveCell(_sheet);

        var commandBus = new CommandBus(
            _ => new WorkbookCommandContext(_workbook),
            (workbookId, ctx) => XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(ctx.Workbook, out _));
        _cellEditService = new WorkbookCellEditService(commandBus, _recalcEngine);
        _recalcEngine.RebuildFormulaDependencies(_workbook);
        _viewport = GetViewport();

        Title = $"FreeX - {_source.DisplayName}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = WindowBackground;
        Content = BuildContent();
        RefreshShell(_source.Status);
    }

    private static Sheet EnsureSheet(Workbook workbook) =>
        workbook.Sheets.Count == 0
            ? workbook.AddSheet("Sheet1")
            : workbook.Sheets[Math.Clamp(workbook.ActiveSheetIndex ?? 0, 0, workbook.Sheets.Count - 1)];

    private static CellAddress GetInitialActiveCell(Sheet sheet) =>
        new(sheet.Id, Math.Max(1, sheet.ActiveRow ?? 1), Math.Max(1, sheet.ActiveCol ?? 1));

    private ViewportModel GetViewport() =>
        _viewportService.GetViewport(
            _workbook,
            _sheet.Id,
            new ViewportRequest(
                _sheet.ViewTopRow ?? 1,
                _sheet.ViewLeftCol ?? 1,
                AvailableHeight: ViewportHeight,
                AvailableWidth: ViewportWidth,
                IncludeObjects: false));

    private Control BuildContent()
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _sheetGridHost,
        });

        return root;
    }

    private Control BuildToolbar()
    {
        var title = new TextBlock
        {
            Text = _source.DisplayName,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(25, 31, 40),
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

        _detailText.FontSize = 12;
        _detailText.Foreground = Brush(94, 103, 116);
        _detailText.MaxWidth = 220;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _statusText.FontSize = 12;
        _statusText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _cellAddressText.Width = 72;
        _cellAddressText.FontSize = 12;
        _cellAddressText.FontWeight = FontWeight.SemiBold;
        _cellAddressText.Foreground = Brush(28, 38, 48);
        _cellAddressText.TextAlignment = TextAlignment.Center;
        _cellAddressText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _formulaBox.MinWidth = 320;
        _formulaBox.FontSize = 12;
        _formulaBox.Padding = new Thickness(8, 4);
        _formulaBox.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _formulaBox.GotFocus += (_, _) => _formulaEditAddress = _activeCell;
        _formulaBox.KeyDown += FormulaBox_KeyDown;

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    title,
                    _detailText,
                    _cellAddressText,
                    _formulaBox,
                    _statusText,
                },
            },
        };
    }

    private void RefreshShell(string status)
    {
        _sheetGridHost.Content = BuildSheetGrid();
        _detailText.Text = $"{_sheet.Name}  |  {_viewport.RowMetrics.Count} rows x {_viewport.ColMetrics.Count} columns";
        _cellAddressText.Text = FormatCellReference(_activeCell);
        _formulaBox.Text = FormatEditText(_sheet.GetCell(_activeCell), _activeCell);
        _statusText.Text = status;
        _statusText.Foreground = _source.IsFallback && string.Equals(status, _source.Status, StringComparison.Ordinal)
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
    }

    private Control BuildSheetGrid()
    {
        var cellsByAddress = _viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        var grid = new AvaloniaGrid
        {
            Background = Brushes.White,
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderColumnWidth) });
        foreach (var metric in _viewport.ColMetrics)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(54, metric.Width)) });

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        foreach (var metric in _viewport.RowMetrics)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(22, metric.Height)) });

        AddGridChild(grid, CreateHeaderCell(""), 0, 0);
        for (var colIndex = 0; colIndex < _viewport.ColMetrics.Count; colIndex++)
        {
            var col = _viewport.ColMetrics[colIndex].Col;
            var selected = col == _activeCell.Col;
            AddGridChild(grid, CreateHeaderCell(CellAddress.NumberToColumnName(col), selected), 0, colIndex + 1);
        }

        for (var rowIndex = 0; rowIndex < _viewport.RowMetrics.Count; rowIndex++)
        {
            var row = _viewport.RowMetrics[rowIndex].Row;
            var selectedRow = row == _activeCell.Row;
            AddGridChild(grid, CreateHeaderCell(row.ToString(), selectedRow), rowIndex + 1, 0);

            for (var colIndex = 0; colIndex < _viewport.ColMetrics.Count; colIndex++)
            {
                var col = _viewport.ColMetrics[colIndex].Col;
                cellsByAddress.TryGetValue((row, col), out var cell);
                AddGridChild(grid, CreateCell(cell, row, col), rowIndex + 1, colIndex + 1);
            }
        }

        return grid;
    }

    private Border CreateHeaderCell(string text, bool selected = false) =>
        CreateCellBorder(
            text,
            selected ? SelectionHeaderBackground : HeaderBackground,
            selected ? SelectionHeaderForeground : HeaderForeground,
            TextAlignment.Center,
            FontWeight.SemiBold,
            selected: false);

    private Border CreateCell(DisplayCell cell, uint row, uint col)
    {
        var hasCell = cell.Row != 0 && cell.Col != 0;
        var selected = row == _activeCell.Row && col == _activeCell.Col;
        var address = new CellAddress(_sheet.Id, row, col);

        if (!hasCell)
            return CreateInteractiveCellBorder(
                "",
                Brushes.White,
                Brushes.Black,
                TextAlignment.Left,
                FontWeight.Normal,
                selected,
                address);

        var style = cell.Style;
        var background = style?.ResolveFillColor(_workbook.Theme) is { } fillColor
            ? Brush(fillColor)
            : Brushes.White;
        var foreground = style is null
            ? Brushes.Black
            : Brush(style.ResolveFontColor(_workbook.Theme));
        var alignment = cell.RawValue is NumberValue or DateTimeValue
            ? TextAlignment.Right
            : TextAlignment.Left;
        var weight = style?.Bold == true ? FontWeight.SemiBold : FontWeight.Normal;

        return CreateInteractiveCellBorder(
            cell.DisplayText,
            background,
            foreground,
            alignment,
            weight,
            selected,
            address);
    }

    private Border CreateInteractiveCellBorder(
        string text,
        IBrush background,
        IBrush foreground,
        TextAlignment textAlignment,
        FontWeight fontWeight,
        bool selected,
        CellAddress address)
    {
        var border = CreateCellBorder(text, background, foreground, textAlignment, fontWeight, selected);
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, args) =>
        {
            SelectCell(address);
            args.Handled = true;
        };
        border.DoubleTapped += (_, args) =>
        {
            BeginFormulaEdit(address);
            args.Handled = true;
        };
        return border;
    }

    private static Border CreateCellBorder(
        string text,
        IBrush background,
        IBrush foreground,
        TextAlignment textAlignment,
        FontWeight fontWeight,
        bool selected)
    {
        return new Border
        {
            Background = background,
            BorderBrush = selected ? SelectionBorder : GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = fontWeight,
                Foreground = foreground,
                TextAlignment = textAlignment,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            },
        };
    }

    private void SelectCell(CellAddress address)
    {
        _activeCell = address;
        _sheet.ActiveRow = address.Row;
        _sheet.ActiveCol = address.Col;
        _formulaEditAddress = null;
        RefreshShell("Ready");
    }

    private void BeginFormulaEdit(CellAddress address)
    {
        _activeCell = address;
        _formulaEditAddress = address;
        RefreshShell("Ready");
        _formulaBox.Focus();
        _formulaBox.CaretIndex = _formulaBox.Text?.Length ?? 0;
        _formulaBox.SelectionStart = _formulaBox.CaretIndex;
        _formulaBox.SelectionEnd = _formulaBox.CaretIndex;
    }

    private void FormulaBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFormulaBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _formulaEditAddress = null;
            RefreshShell("Ready");
            e.Handled = true;
        }
    }

    private void CommitFormulaBox()
    {
        var address = _formulaEditAddress ?? _activeCell;
        var result = _cellEditService.CommitCellText(
            _workbook,
            _sheet.Id,
            address,
            _formulaBox.Text ?? "",
            useR1C1ReferenceStyle: false);

        if (!result.Success)
        {
            _statusText.Text = result.ErrorMessage ?? "Edit failed";
            _statusText.Foreground = Brush(143, 74, 18);
            return;
        }

        _activeCell = address;
        _formulaEditAddress = null;
        _viewport = GetViewport();
        RefreshShell($"Edited {FormatCellReference(address)}");
    }

    private static string FormatCellReference(CellAddress address) =>
        CellAddress.NumberToColumnName(address.Col) + address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatEditText(Cell? cell, CellAddress address)
    {
        if (cell?.HasFormula == true && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        return FormatScalarValue(cell?.Value);
    }

    private static string FormatScalarValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => ""
    };

    private static void AddGridChild(AvaloniaGrid grid, Control control, int row, int column)
    {
        AvaloniaGrid.SetRow(control, row);
        AvaloniaGrid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static IBrush Brush(byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromRgb(red, green, blue));

    private static IBrush Brush(CellColor color) =>
        Brush(color.R, color.G, color.B);
}
