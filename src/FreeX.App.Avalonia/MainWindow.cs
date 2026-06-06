using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private const string NativeWorkbookExtension = ".fxl";
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);
    private static readonly IBrush SelectionBorder = Brush(11, 112, 116);
    private static readonly IBrush SelectionHeaderBackground = Brush(225, 244, 242);
    private static readonly IBrush SelectionHeaderForeground = Brush(13, 86, 89);

    private readonly StartupWorkbookLoadResult _source;
    private readonly IReadOnlyList<IFileAdapter> _adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
    private readonly WorkbookSaveService _saveService = new();
    private readonly WorkbookSheetSelectionService _sheetSelectionService = new();
    private readonly Workbook _workbook;
    private readonly IViewportService _viewportService = new ViewportService();
    private readonly RecalcEngine _recalcEngine = new(new DependencyGraph(), new FormulaEvaluator());
    private readonly WorkbookCellEditService _cellEditService;
    private readonly ContentControl _sheetGridHost = new();
    private readonly ContentControl _sheetTabsHost = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _cellAddressText = new();
    private readonly TextBox _formulaBox = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveAsButton = new();
    private Sheet _sheet;
    private ViewportModel _viewport = new([], [], [], null, []);
    private CellAddress _activeCell;
    private CellAddress? _formulaEditAddress;
    private string? _currentFilePath;
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private bool _isDirty;
    private bool _isSaving;

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        _source = new StartupWorkbookLoader().Load(startupArguments);
        _workbook = _source.Workbook;
        _sheet = _sheetSelectionService.EnsureActiveSheet(_workbook).Sheet;
        _activeCell = GetInitialActiveCell(_sheet);
        _currentFilePath = _source.OpenedAsTemplate ? null : _source.SourcePath;
        _currentXlsxFeatureReport = _source.FeatureReport;

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
        KeyDown += MainWindow_KeyDown;
        RefreshShell(FormatStartupStatus(_source));
    }

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

        var sheetTabs = BuildSheetTabsChrome();
        DockPanel.SetDock(sheetTabs, Dock.Bottom);
        root.Children.Add(sheetTabs);

        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _sheetGridHost,
        });

        return root;
    }

    private Control BuildSheetTabsChrome()
    {
        _sheetTabsHost.Content = BuildSheetTabs();
        return new Border
        {
            Background = Brush(249, 250, 252),
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _sheetTabsHost,
            },
        };
    }

    private Control BuildToolbar()
    {
        _titleText.FontSize = 14;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.Foreground = Brush(25, 31, 40);
        _titleText.MaxWidth = 180;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _titleText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _detailText.FontSize = 12;
        _detailText.Foreground = Brush(94, 103, 116);
        _detailText.MaxWidth = 220;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _statusText.FontSize = 12;
        _statusText.VerticalAlignment = AvaloniaVerticalAlignment.Center;

        _saveButton.Content = "Save";
        _saveButton.Padding = new Thickness(10, 4);
        _saveButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _saveButton.Click += SaveButton_Click;

        _saveAsButton.Content = "Save As";
        _saveAsButton.Padding = new Thickness(10, 4);
        _saveAsButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _saveAsButton.Click += SaveAsButton_Click;

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
                    _titleText,
                    _detailText,
                    _saveButton,
                    _saveAsButton,
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
        _sheetTabsHost.Content = BuildSheetTabs();
        _titleText.Text = CurrentDisplayName;
        _detailText.Text = $"{_sheet.Name}  |  {_viewport.RowMetrics.Count} rows x {_viewport.ColMetrics.Count} columns";
        _cellAddressText.Text = FormatCellReference(_activeCell);
        _formulaBox.Text = FormatEditText(_sheet.GetCell(_activeCell), _activeCell);
        _statusText.Text = status;
        _statusText.Foreground = ShouldUseWarningStatusColor(status)
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
        Title = $"FreeX - {CurrentDisplayName}{(_isDirty ? " *" : "")}";
        UpdateSaveButton();
    }

    private void UpdateSaveButton()
    {
        _saveButton.IsEnabled = !_isSaving && CanSaveCurrentSource(out _);
        _saveButton.Content = _isDirty ? "Save*" : "Save";
        _saveAsButton.IsEnabled = !_isSaving && StorageProvider.CanSave;
    }

    private Control BuildSheetTabs()
    {
        var selection = _sheetSelectionService.EnsureActiveSheet(_workbook);
        if (_sheet.Id != selection.Sheet.Id)
        {
            _sheet = selection.Sheet;
            _activeCell = GetInitialActiveCell(_sheet);
            _formulaEditAddress = null;
            _viewport = GetViewport();
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };

        foreach (var tab in selection.Tabs)
        {
            var button = new Button
            {
                MinWidth = 72,
                MaxWidth = 180,
                MinHeight = 28,
                Padding = new Thickness(12, 4),
                Background = tab.IsActive ? SelectionHeaderBackground : Brushes.White,
                BorderBrush = tab.IsActive ? SelectionBorder : ToolbarBorder,
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = tab.Name,
                    FontSize = 12,
                    FontWeight = tab.IsActive ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = tab.IsActive ? SelectionHeaderForeground : HeaderForeground,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                },
            };
            button.Click += (_, _) => SelectSheet(tab.Id);
            panel.Children.Add(button);
        }

        return panel;
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

    private void SelectSheet(SheetId sheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(_workbook, sheetId);
        if (_sheet.Id == selection.Sheet.Id)
            return;

        _sheet = selection.Sheet;
        _activeCell = GetInitialActiveCell(_sheet);
        _formulaEditAddress = null;
        _viewport = GetViewport();
        RefreshShell($"Selected {_sheet.Name}");
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
        _isDirty = true;
        _viewport = GetViewport();
        RefreshShell($"Edited {FormatCellReference(address)}");
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentWorkbookAsync();
    }

    private async void SaveAsButton_Click(object? sender, RoutedEventArgs e)
    {
        await SaveWorkbookAsAsync();
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S ||
            (!e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
             !e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            return;
        }

        e.Handled = true;
        await SaveCurrentWorkbookAsync();
    }

    private async Task SaveCurrentWorkbookAsync()
    {
        if (_isSaving)
            return;

        if (CanSaveCurrentSource(out var target))
        {
            await SaveWorkbookToTargetAsync(target!);
            return;
        }

        await SaveWorkbookAsAsync();
    }

    private async Task SaveWorkbookAsAsync()
    {
        if (_isSaving)
            return;

        if (!StorageProvider.CanSave)
        {
            ShowSaveIssue("Save As unavailable on this platform.");
            return;
        }

        var fileTypes = BuildSaveFileTypes();
        if (fileTypes.Count == 0)
        {
            ShowSaveIssue("No save formats are available.");
            return;
        }

        var storageFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Workbook",
            SuggestedFileName = BuildSuggestedSaveAsFileName(),
            DefaultExtension = NativeWorkbookExtension[1..],
            FileTypeChoices = fileTypes,
            SuggestedFileType = fileTypes[0],
            ShowOverwritePrompt = true,
        });

        if (storageFile is null)
            return;

        using (storageFile)
        {
            var path = storageFile.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowSaveIssue("Save As requires a local file path.");
                return;
            }

            path = EnsureSaveExtension(path);
            if (!TryResolveSaveTarget(path, out var target, out var message))
            {
                ShowSaveIssue(message);
                return;
            }

            await SaveWorkbookToTargetAsync(target!);
        }
    }

    private async Task SaveWorkbookToTargetAsync(FileSaveTarget target)
    {
        try
        {
            _isSaving = true;
            UpdateSaveButton();
            _statusText.Text = "Saving...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookSaveProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatSaveStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            await _saveService.SaveAsync(target.Path, target.Adapter, _workbook, progress);
            _isDirty = false;
            _currentFilePath = target.Path;
            _currentXlsxFeatureReport = null;
            _workbook.Name = Path.GetFileName(target.Path);
            RefreshShell($"Saved {Path.GetFileName(target.Path)}");
        }
        catch (Exception ex)
        {
            ShowSaveIssue($"Save failed: {ex.Message}");
        }
        finally
        {
            _isSaving = false;
            UpdateSaveButton();
        }
    }

    private bool CanSaveCurrentSource(out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return false;

        return TryResolveSaveTarget(_currentFilePath, out target, out _);
    }

    private bool TryResolveSaveTarget(string path, out FileSaveTarget? target, out string message)
    {
        target = null;
        if (!FileSavePlanner.TryResolveExistingPath(path, _adapters, out var resolvedTarget) ||
            resolvedTarget is null)
        {
            message = "Unsupported save format.";
            return false;
        }

        if (!CanWriteTarget(resolvedTarget.Path, out message))
            return false;

        target = resolvedTarget;
        message = "";
        return true;
    }

    private bool CanWriteTarget(string path, out string message)
    {
        if (IsXlsxPath(path) && _currentXlsxFeatureReport?.HasUnsupportedFeatures == true)
        {
            message = "Save As FreeX Workbook to avoid dropping unsupported XLSX features.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool IsXlsxPath(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<FilePickerFileType> BuildSaveFileTypes()
    {
        var formats = _adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .GroupBy(format => FileFormatResolver.NormalizeExtension(format.Extension), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var nativeIndex = formats.FindIndex(format =>
            string.Equals(
                FileFormatResolver.NormalizeExtension(format.Extension),
                NativeWorkbookExtension,
                StringComparison.OrdinalIgnoreCase));
        if (nativeIndex > 0)
        {
            var native = formats[nativeIndex];
            formats.RemoveAt(nativeIndex);
            formats.Insert(0, native);
        }

        return formats
            .Select(format =>
            {
                var extension = FileFormatResolver.NormalizeExtension(format.Extension);
                return new FilePickerFileType(format.FormatName)
                {
                    Patterns = [$"*{extension}"],
                };
            })
            .ToList();
    }

    private string BuildSuggestedSaveAsFileName()
    {
        var sourceName = string.IsNullOrWhiteSpace(_workbook.Name)
            ? CurrentDisplayName
            : _workbook.Name;
        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Workbook";

        return baseName + NativeWorkbookExtension;
    }

    private static string EnsureSaveExtension(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? path + NativeWorkbookExtension
                : path;
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }

    private void ShowSaveIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private string CurrentDisplayName =>
        string.IsNullOrWhiteSpace(_currentFilePath)
            ? _source.DisplayName
            : Path.GetFileName(_currentFilePath);

    private bool ShouldUseWarningStatusColor(string status) =>
        _source.IsFallback ||
        status.Contains("Unsupported XLSX", StringComparison.Ordinal) ||
        status.Contains("load warning", StringComparison.OrdinalIgnoreCase);

    private static string FormatStartupStatus(StartupWorkbookLoadResult source)
    {
        var status = source.Status;
        if (source.OpenedAsTemplate)
            status += " Opened as template.";
        if (source.FeatureReport?.HasUnsupportedFeatures == true)
            status += " Unsupported XLSX features detected.";
        if (source.LoadWarnings is { Count: > 0 } warnings)
            status += $" {warnings.Count} load warning{(warnings.Count == 1 ? "" : "s")}.";

        return status;
    }

    private static string FormatSaveStatus(WorkbookSaveProgressUpdate update) =>
        update.Phase switch
        {
            WorkbookSavePhase.Preparing => "Preparing save...",
            WorkbookSavePhase.Writing => "Writing file...",
            WorkbookSavePhase.Completed => "Saved",
            _ => "Saving..."
        };

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
