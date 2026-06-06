using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed class MainWindow : Window
{
    private const double HeaderColumnWidth = 58;
    private const double HeaderRowHeight = 28;
    private const double InitialViewportHeight = 880;
    private const double InitialViewportWidth = 1440;
    private const string NativeWorkbookExtension = ".fxl";
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);
    private static readonly IBrush SelectionBorder = Brush(11, 112, 116);
    private static readonly IBrush SelectionHeaderBackground = Brush(225, 244, 242);
    private static readonly IBrush SelectionHeaderForeground = Brush(13, 86, 89);

    private readonly WorkbookSessionFactory _sessionFactory = new();
    private readonly WorkbookOpenService _openService = new();
    private readonly WorkbookSaveService _saveService = new();
    private readonly ContentControl _sheetGridHost = new();
    private readonly ContentControl _sheetTabsHost = new();
    private readonly ScrollViewer _sheetScrollViewer = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _cellAddressText = new();
    private readonly TextBox _formulaBox = new();
    private readonly Button _openButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveAsButton = new();
    private WorkbookSession _session;
    private bool _isOpening;
    private bool _isSaving;

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        var source = new StartupWorkbookLoader().Load(startupArguments);
        _session = _sessionFactory.Create(source, InitialViewportHeight, InitialViewportWidth);

        Title = $"FreeX - {_session.DisplayName}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = WindowBackground;
        Content = BuildContent();
        KeyDown += MainWindow_KeyDown;
        RefreshShell(_session.StartupStatus);
    }

    private Control BuildContent()
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var sheetTabs = BuildSheetTabsChrome();
        DockPanel.SetDock(sheetTabs, Dock.Bottom);
        root.Children.Add(sheetTabs);

        _sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.Content = _sheetGridHost;
        _sheetScrollViewer.SizeChanged += SheetScrollViewer_SizeChanged;
        _sheetScrollViewer.PointerWheelChanged += SheetScrollViewer_PointerWheelChanged;
        root.Children.Add(_sheetScrollViewer);

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

        _openButton.Content = "Open";
        _openButton.Padding = new Thickness(10, 4);
        _openButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _openButton.Click += OpenButton_Click;

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
        _formulaBox.GotFocus += (_, _) => _session.BeginFormulaEdit(_session.ActiveCell);
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
                    _openButton,
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
        var preserveFormulaEdit = _formulaBox.IsFocused && _session.FormulaEditAddress is not null;
        var formulaText = _formulaBox.Text;
        var formulaCaretIndex = _formulaBox.CaretIndex;
        var formulaSelectionStart = _formulaBox.SelectionStart;
        var formulaSelectionEnd = _formulaBox.SelectionEnd;

        _sheetGridHost.Content = BuildSheetGrid();
        _sheetTabsHost.Content = BuildSheetTabs();
        _titleText.Text = _session.DisplayName;
        _detailText.Text = $"{_session.ActiveSheet.Name}  |  {_session.Viewport.RowMetrics.Count} rows x {_session.Viewport.ColMetrics.Count} columns";
        _cellAddressText.Text = FormatCellReference(_session.ActiveCell);
        _formulaBox.Text = preserveFormulaEdit
            ? formulaText
            : FormatEditText(_session.ActiveSheet.GetCell(_session.ActiveCell), _session.ActiveCell);
        if (preserveFormulaEdit)
        {
            _formulaBox.CaretIndex = Math.Min(formulaCaretIndex, _formulaBox.Text?.Length ?? 0);
            _formulaBox.SelectionStart = Math.Min(formulaSelectionStart, _formulaBox.Text?.Length ?? 0);
            _formulaBox.SelectionEnd = Math.Min(formulaSelectionEnd, _formulaBox.Text?.Length ?? 0);
        }

        _statusText.Text = status;
        _statusText.Foreground = ShouldUseWarningStatusColor(status)
            ? Brush(143, 74, 18)
            : Brush(67, 113, 83);
        Title = $"FreeX - {_session.DisplayName}{(_session.IsDirty ? " *" : "")}";
        UpdateSaveButton();
    }

    private void UpdateSaveButton()
    {
        _openButton.IsEnabled = !_isOpening && !_isSaving && StorageProvider.CanOpen;
        _saveButton.IsEnabled = !_isOpening && !_isSaving && _session.CanSaveCurrentSource(out _);
        _saveButton.Content = _session.IsDirty ? "Save*" : "Save";
        _saveAsButton.IsEnabled = !_isOpening && !_isSaving && StorageProvider.CanSave;
    }

    private Control BuildSheetTabs()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
        };

        foreach (var tab in _session.SheetTabs)
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
        var viewport = _session.Viewport;
        var cellsByAddress = viewport.Cells.ToDictionary(cell => (cell.Row, cell.Col));
        var grid = new AvaloniaGrid
        {
            Background = Brushes.White,
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderColumnWidth) });
        foreach (var metric in viewport.ColMetrics)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(54, metric.Width)) });

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        foreach (var metric in viewport.RowMetrics)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(22, metric.Height)) });

        AddGridChild(grid, CreateHeaderCell(""), 0, 0);
        for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
        {
            var col = viewport.ColMetrics[colIndex].Col;
            var selected = col == _session.ActiveCell.Col;
            AddGridChild(grid, CreateHeaderCell(CellAddress.NumberToColumnName(col), selected), 0, colIndex + 1);
        }

        for (var rowIndex = 0; rowIndex < viewport.RowMetrics.Count; rowIndex++)
        {
            var row = viewport.RowMetrics[rowIndex].Row;
            var selectedRow = row == _session.ActiveCell.Row;
            AddGridChild(grid, CreateHeaderCell(row.ToString(), selectedRow), rowIndex + 1, 0);

            for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
            {
                var col = viewport.ColMetrics[colIndex].Col;
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
        var selected = row == _session.ActiveCell.Row && col == _session.ActiveCell.Col;
        var address = new CellAddress(_session.ActiveSheet.Id, row, col);

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
        var background = style?.ResolveFillColor(_session.Workbook.Theme) is { } fillColor
            ? Brush(fillColor)
            : Brushes.White;
        var foreground = style is null
            ? Brushes.Black
            : Brush(style.ResolveFontColor(_session.Workbook.Theme));
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
        _session.SelectCell(address);
        RefreshShell("Ready");
    }

    private void SelectSheet(SheetId sheetId)
    {
        if (!_session.SelectSheet(sheetId))
            return;

        RefreshShell($"Selected {_session.ActiveSheet.Name}");
    }

    private void BeginFormulaEdit(CellAddress address)
    {
        _session.BeginFormulaEdit(address);
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
            _session.CancelFormulaEdit();
            RefreshShell("Ready");
            e.Handled = true;
        }
    }

    private void CommitFormulaBox()
    {
        var address = _session.FormulaEditAddress ?? _session.ActiveCell;
        var result = _session.CommitCellText(_formulaBox.Text ?? "");

        if (!result.Success)
        {
            _statusText.Text = result.ErrorMessage ?? "Edit failed";
            _statusText.Foreground = Brush(143, 74, 18);
            return;
        }

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

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        await OpenWorkbookAsync();
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            if (_formulaBox.IsFocused)
                return;

            NavigateActiveCell(e);
            return;
        }

        if (e.Key == Key.S)
        {
            e.Handled = true;
            await SaveCurrentWorkbookAsync();
        }
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            await OpenWorkbookAsync();
        }
    }

    private void NavigateActiveCell(KeyEventArgs e)
    {
        var pageRows = Math.Max(1, _session.Viewport.RowMetrics.Count - 1);
        var pageCols = Math.Max(1, _session.Viewport.ColMetrics.Count - 1);
        var handled = true;
        switch (e.Key)
        {
            case Key.Up:
                _session.MoveActiveCell(-1, 0);
                break;
            case Key.Down:
                _session.MoveActiveCell(1, 0);
                break;
            case Key.Left:
                _session.MoveActiveCell(0, -1);
                break;
            case Key.Right:
                _session.MoveActiveCell(0, 1);
                break;
            case Key.PageUp:
                _session.MoveActiveCell(-pageRows, 0);
                break;
            case Key.PageDown:
                _session.MoveActiveCell(pageRows, 0);
                break;
            case Key.Home:
                _session.MoveActiveCell(0, 1 - checked((int)_session.ActiveCell.Col));
                break;
            case Key.End:
                _session.MoveActiveCell(0, pageCols);
                break;
            default:
                handled = false;
                break;
        }

        if (!handled)
            return;

        e.Handled = true;
        RefreshShell("Ready");
    }

    private void SheetScrollViewer_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_formulaBox.IsFocused)
            return;

        var vertical = e.Delta.Y;
        var horizontal = e.Delta.X;
        var rowDelta = 0;
        var colDelta = 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            Math.Abs(horizontal) > Math.Abs(vertical))
        {
            var scroll = Math.Abs(horizontal) > 0 ? horizontal : vertical;
            colDelta = scroll < 0 ? 1 : -1;
        }
        else if (Math.Abs(vertical) > 0)
        {
            rowDelta = vertical < 0 ? 1 : -1;
        }

        if (rowDelta == 0 && colDelta == 0)
            return;

        if (_session.PanViewport(rowDelta * 3, colDelta * 3))
            RefreshShell("Ready");

        e.Handled = true;
    }

    private void SheetScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!TryGetSheetViewportSize(out var viewportHeight, out var viewportWidth))
            return;

        if (_session.UpdateViewportSize(viewportHeight, viewportWidth))
            RefreshShell(string.IsNullOrWhiteSpace(_statusText.Text) ? "Ready" : _statusText.Text);
    }

    private async Task OpenWorkbookAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (_session.IsDirty)
        {
            ShowOpenIssue("Save changes before opening another workbook.");
            return;
        }

        if (!StorageProvider.CanOpen)
        {
            ShowOpenIssue("Open unavailable on this platform.");
            return;
        }

        var fileTypes = BuildOpenFileTypes();
        if (fileTypes.Count == 0)
        {
            ShowOpenIssue("No open formats are available.");
            return;
        }

        var storageFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Workbook",
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
        });

        var storageFile = storageFiles.FirstOrDefault();
        if (storageFile is null)
            return;

        using (storageFile)
        {
            var path = storageFile.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowOpenIssue("Open requires a local file path.");
                return;
            }

            if (!_session.TryResolveOpenTarget(path, out var target, out var message))
            {
                ShowOpenIssue(message);
                return;
            }

            await OpenWorkbookFromTargetAsync(target!);
        }
    }

    private async Task OpenWorkbookFromTargetAsync(WorkbookOpenTarget target)
    {
        try
        {
            _isOpening = true;
            UpdateSaveButton();
            _statusText.Text = "Opening...";
            _statusText.Foreground = Brush(67, 113, 83);
            var progress = new Progress<WorkbookOpenProgressUpdate>(
                update =>
                {
                    _statusText.Text = FormatOpenStatus(update);
                    _statusText.Foreground = Brush(67, 113, 83);
                });

            var result = await _openService.LoadAsync(
                target.Path,
                target.Adapter,
                target.Extension,
                target.Format,
                progress);
            var (viewportHeight, viewportWidth) = GetCurrentSheetViewportSize();
            _session = _sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth);
            RefreshShell(_session.StartupStatus);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or WorkbookTooLargeException)
        {
            ShowOpenIssue($"Open failed: {ex.Message}");
        }
        finally
        {
            _isOpening = false;
            UpdateSaveButton();
        }
    }

    private async Task SaveCurrentWorkbookAsync()
    {
        if (_isSaving)
            return;

        if (_session.CanSaveCurrentSource(out var target))
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
            SuggestedFileName = _session.BuildSuggestedSaveAsFileName(NativeWorkbookExtension),
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

            path = WorkbookSession.EnsureSaveExtension(path, NativeWorkbookExtension);
            if (!_session.TryResolveSaveTarget(path, out var target, out var message))
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

            await _saveService.SaveAsync(target.Path, target.Adapter, _session.Workbook, progress);
            _session.MarkSaved(target.Path);
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

    private IReadOnlyList<FilePickerFileType> BuildSaveFileTypes()
    {
        var formats = _session.SaveFormats.ToList();

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

    private IReadOnlyList<FilePickerFileType> BuildOpenFileTypes()
    {
        var formats = _session.OpenFormats.ToList();
        var patterns = formats
            .Select(format => FileFormatResolver.NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}")
            .ToList();
        if (patterns.Count == 0)
            return [];

        var fileTypes = new List<FilePickerFileType>
        {
            new("All supported workbooks")
            {
                Patterns = patterns,
            },
        };

        fileTypes.AddRange(formats.Select(format =>
        {
            var extension = FileFormatResolver.NormalizeExtension(format.Extension);
            return new FilePickerFileType(format.FormatName)
            {
                Patterns = [$"*{extension}"],
            };
        }));

        return fileTypes;
    }

    private void ShowSaveIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private void ShowOpenIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
    }

    private (double Height, double Width) GetCurrentSheetViewportSize()
    {
        return TryGetSheetViewportSize(out var viewportHeight, out var viewportWidth)
            ? (viewportHeight, viewportWidth)
            : (_session.ViewportHeight, _session.ViewportWidth);
    }

    private bool TryGetSheetViewportSize(out double viewportHeight, out double viewportWidth)
    {
        var bounds = _sheetScrollViewer.Bounds;
        if (bounds.Height <= HeaderRowHeight || bounds.Width <= HeaderColumnWidth)
        {
            viewportHeight = 0;
            viewportWidth = 0;
            return false;
        }

        viewportHeight = bounds.Height - HeaderRowHeight;
        viewportWidth = bounds.Width - HeaderColumnWidth;
        return true;
    }

    private bool ShouldUseWarningStatusColor(string status) =>
        _session.IsFallback ||
        status.Contains("Unsupported XLSX", StringComparison.Ordinal) ||
        status.Contains("load warning", StringComparison.OrdinalIgnoreCase);

    private static string FormatSaveStatus(WorkbookSaveProgressUpdate update) =>
        update.Phase switch
        {
            WorkbookSavePhase.Preparing => "Preparing save...",
            WorkbookSavePhase.Writing => "Writing file...",
            WorkbookSavePhase.Completed => "Saved",
            _ => "Saving..."
        };

    private static string FormatOpenStatus(WorkbookOpenProgressUpdate update) =>
        update.Phase switch
        {
            WorkbookOpenPhase.Reading => "Reading file...",
            WorkbookOpenPhase.Inspecting => "Inspecting workbook...",
            WorkbookOpenPhase.Parsing => "Opening workbook...",
            WorkbookOpenPhase.Calculating => "Calculating workbook...",
            _ => "Opening..."
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
