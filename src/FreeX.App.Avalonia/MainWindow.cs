using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private const double MinimumDisplayedColumnWidth = 54;
    private const double MinimumDisplayedRowHeight = 22;
    private const string NativeWorkbookExtension = ".fxl";
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);
    private static readonly IBrush SelectionBorder = Brush(11, 112, 116);
    private static readonly IBrush SelectionHeaderBackground = Brush(225, 244, 242);
    private static readonly IBrush SelectionHeaderForeground = Brush(13, 86, 89);
    private static readonly IBrush DrawingObjectBoundsFill = Brush(42, 11, 112, 116);
    private static readonly IBrush DrawingObjectBoundsBorder = Brush(11, 112, 116);
    private static readonly IBrush DrawingObjectBoundsForeground = Brush(5, 67, 69);

    private readonly WorkbookSessionFactory _sessionFactory = new();
    private readonly WorkbookOpenService _openService = new();
    private readonly WorkbookSaveService _saveService = new();
    private readonly ContentControl _sheetGridHost = new();
    private readonly ContentControl _sheetTabsHost = new();
    private readonly ScrollViewer _sheetScrollViewer = new();
    private readonly ScrollBar _verticalWorksheetScrollBar = new();
    private readonly ScrollBar _horizontalWorksheetScrollBar = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _cellAddressText = new();
    private readonly TextBox _formulaBox = new();
    private readonly Button _openButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveAsButton = new();
    private readonly Button _undoButton = new();
    private readonly Button _redoButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _pasteButton = new();
    private readonly NativeMenuItem _openMenuItem = new();
    private readonly NativeMenuItem _saveMenuItem = new();
    private readonly NativeMenuItem _saveAsMenuItem = new();
    private readonly NativeMenuItem _undoMenuItem = new();
    private readonly NativeMenuItem _redoMenuItem = new();
    private readonly NativeMenuItem _copyMenuItem = new();
    private readonly NativeMenuItem _pasteMenuItem = new();
    private readonly NativeMenuItem _quitMenuItem = new();
    private NativeMenu? _nativeMenu;
    private WorkbookSession _session;
    private string? _formulaBoxEditOriginalText;
    private bool _isOpening;
    private bool _isSaving;
    private bool _isUpdatingWorksheetScrollBars;

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        var source = new StartupWorkbookLoader().Load(startupArguments);
        _session = _sessionFactory.Create(source, InitialViewportHeight, InitialViewportWidth, includeObjects: true);

        Title = $"FreeX - {_session.DisplayName}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = WindowBackground;
        Content = BuildContent();
        ConfigureNativeMenu();
        ConfigureWorkbookDropTarget();
        KeyDown += MainWindow_KeyDown;
        TextInput += MainWindow_TextInput;
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

        root.Children.Add(BuildWorksheetViewportChrome());

        return root;
    }

    private Control BuildWorksheetViewportChrome()
    {
        _sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sheetScrollViewer.Content = _sheetGridHost;
        _sheetScrollViewer.SizeChanged += SheetScrollViewer_SizeChanged;
        _sheetScrollViewer.PointerWheelChanged += SheetScrollViewer_PointerWheelChanged;

        _verticalWorksheetScrollBar.Orientation = Orientation.Vertical;
        _verticalWorksheetScrollBar.Width = 16;
        _verticalWorksheetScrollBar.AllowAutoHide = false;
        _verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;

        _horizontalWorksheetScrollBar.Orientation = Orientation.Horizontal;
        _horizontalWorksheetScrollBar.Height = 16;
        _horizontalWorksheetScrollBar.AllowAutoHide = false;
        _horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;

        var chrome = new AvaloniaGrid
        {
            Background = Brushes.White,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        AddGridChild(chrome, _sheetScrollViewer, 0, 0);
        AddGridChild(chrome, _verticalWorksheetScrollBar, 0, 1);
        AddGridChild(chrome, _horizontalWorksheetScrollBar, 1, 0);
        AddGridChild(
            chrome,
            new Border
            {
                Width = 16,
                Height = 16,
                Background = HeaderBackground,
                BorderBrush = ToolbarBorder,
                BorderThickness = new Thickness(1, 1, 0, 0),
            },
            1,
            1);

        return chrome;
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

    private void ConfigureNativeMenu()
    {
        _openMenuItem.Header = "Open...";
        _openMenuItem.Gesture = new KeyGesture(Key.O, KeyModifiers.Meta);
        _openMenuItem.Click += async (_, _) => await OpenWorkbookAsync();

        _saveMenuItem.Header = "Save";
        _saveMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta);
        _saveMenuItem.Click += async (_, _) => await SaveCurrentWorkbookAsync();

        _saveAsMenuItem.Header = "Save As...";
        _saveAsMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift);
        _saveAsMenuItem.Click += async (_, _) => await SaveWorkbookAsAsync();

        _undoMenuItem.Header = "Undo";
        _undoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta);
        _undoMenuItem.Click += (_, _) => UndoLastEdit();

        _redoMenuItem.Header = "Redo";
        _redoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);
        _redoMenuItem.Click += (_, _) => RedoLastEdit();

        _copyMenuItem.Header = "Copy";
        _copyMenuItem.Gesture = new KeyGesture(Key.C, KeyModifiers.Meta);
        _copyMenuItem.Click += async (_, _) => await CopyActiveCellToClipboardAsync();

        _pasteMenuItem.Header = "Paste";
        _pasteMenuItem.Gesture = new KeyGesture(Key.V, KeyModifiers.Meta);
        _pasteMenuItem.Click += async (_, _) => await PasteClipboardTextAsync();

        _quitMenuItem.Header = "Quit FreeX";
        _quitMenuItem.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);
        _quitMenuItem.Click += (_, _) => TryQuitApplication();

        var fileMenu = new NativeMenu();
        fileMenu.Items.Add(_openMenuItem);
        fileMenu.Items.Add(_saveMenuItem);
        fileMenu.Items.Add(_saveAsMenuItem);
        fileMenu.Items.Add(new NativeMenuItemSeparator());
        fileMenu.Items.Add(_quitMenuItem);

        var editMenu = new NativeMenu();
        editMenu.Items.Add(_undoMenuItem);
        editMenu.Items.Add(_redoMenuItem);
        editMenu.Items.Add(new NativeMenuItemSeparator());
        editMenu.Items.Add(_copyMenuItem);
        editMenu.Items.Add(_pasteMenuItem);

        _nativeMenu = new NativeMenu();
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "File",
            Menu = fileMenu,
        });
        _nativeMenu.Items.Add(new NativeMenuItem
        {
            Header = "Edit",
            Menu = editMenu,
        });
        _nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();

        NativeMenu.SetMenu(this, _nativeMenu);
    }

    private void ConfigureWorkbookDropTarget()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, MainWindow_DragOver);
        DragDrop.AddDropHandler(this, MainWindow_Drop);
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

        _undoButton.Content = "Undo";
        _undoButton.Padding = new Thickness(10, 4);
        _undoButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _undoButton.Click += UndoButton_Click;

        _redoButton.Content = "Redo";
        _redoButton.Padding = new Thickness(10, 4);
        _redoButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _redoButton.Click += RedoButton_Click;

        _copyButton.Content = "Copy";
        _copyButton.Padding = new Thickness(10, 4);
        _copyButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _copyButton.Click += CopyButton_Click;

        _pasteButton.Content = "Paste";
        _pasteButton.Padding = new Thickness(10, 4);
        _pasteButton.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _pasteButton.Click += PasteButton_Click;

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
        _formulaBox.GotFocus += FormulaBox_GotFocus;
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
                    _undoButton,
                    _redoButton,
                    _copyButton,
                    _pasteButton,
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
        UpdateViewportScrollBars();
        UpdateSaveButton();
    }

    private void UpdateViewportScrollBars()
    {
        var state = WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport);
        _isUpdatingWorksheetScrollBars = true;
        try
        {
            ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);
            ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);
        }
        finally
        {
            _isUpdatingWorksheetScrollBars = false;
        }
    }

    private static void ApplyWorksheetScrollAxis(ScrollBar scrollBar, WorkbookViewportScrollAxis axis)
    {
        scrollBar.Minimum = axis.Minimum;
        scrollBar.Maximum = axis.Maximum;
        scrollBar.ViewportSize = axis.ViewportSize;
        scrollBar.SmallChange = axis.SmallChange;
        scrollBar.LargeChange = axis.LargeChange;
        scrollBar.Value = Math.Clamp(axis.Value, axis.Minimum, axis.Maximum);
        scrollBar.IsEnabled = axis.IsEnabled;
    }

    private void UpdateSaveButton()
    {
        var isIdle = !_isOpening && !_isSaving;
        _openButton.IsEnabled = isIdle && StorageProvider.CanOpen;
        _saveButton.IsEnabled = isIdle && _session.CanSaveCurrentSource(out _);
        _saveButton.Content = _session.IsDirty ? "Save*" : "Save";
        _saveAsButton.IsEnabled = isIdle && StorageProvider.CanSave;
        _undoButton.IsEnabled = isIdle && _session.CanUndo;
        _redoButton.IsEnabled = isIdle && _session.CanRedo;
        _copyButton.IsEnabled = isIdle;
        _pasteButton.IsEnabled = isIdle;

        _openMenuItem.IsEnabled = _openButton.IsEnabled;
        _saveMenuItem.IsEnabled = _saveButton.IsEnabled;
        _saveAsMenuItem.IsEnabled = _saveAsButton.IsEnabled;
        _undoMenuItem.IsEnabled = _undoButton.IsEnabled;
        _redoMenuItem.IsEnabled = _redoButton.IsEnabled;
        _copyMenuItem.IsEnabled = _copyButton.IsEnabled;
        _pasteMenuItem.IsEnabled = _pasteButton.IsEnabled;
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDisplayedColumnWidth(metric)) });

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        foreach (var metric in viewport.RowMetrics)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GetDisplayedRowHeight(metric)) });

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

        var overlay = BuildDrawingObjectOverlay(viewport);
        if (overlay.Children.Count == 0)
            return grid;

        return new AvaloniaGrid
        {
            ClipToBounds = true,
            Children =
            {
                grid,
                overlay,
            },
        };
    }

    private Canvas BuildDrawingObjectOverlay(ViewportModel viewport)
    {
        var overlay = new Canvas
        {
            Width = CalculateDisplayedGridWidth(viewport),
            Height = CalculateDisplayedGridHeight(viewport),
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        if (viewport.DrawingObjects is not { Count: > 0 })
            return overlay;

        foreach (var drawingObject in viewport.DrawingObjects)
        {
            if (!TryGetDisplayedDrawingObjectBounds(
                    viewport,
                    drawingObject,
                    out var left,
                    out var top,
                    out var width,
                    out var height))
            {
                continue;
            }

            var marker = CreateDrawingObjectBoundsMarker(drawingObject, width, height);
            Canvas.SetLeft(marker, left);
            Canvas.SetTop(marker, top);
            overlay.Children.Add(marker);
        }

        return overlay;
    }

    private static Border CreateDrawingObjectBoundsMarker(
        DrawingObjectBounds drawingObject,
        double width,
        double height)
    {
        var marker = new Border
        {
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Background = DrawingObjectBoundsFill,
            BorderBrush = DrawingObjectBoundsBorder,
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            IsHitTestVisible = false,
        };

        if (width >= 56 && height >= 24)
        {
            marker.Child = new TextBlock
            {
                Text = drawingObject.DisplayName,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = DrawingObjectBoundsForeground,
                Margin = new Thickness(6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
        }

        if (Math.Abs(drawingObject.RotationDegrees % 360) > 0.0001)
        {
            marker.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            marker.RenderTransform = new RotateTransform(drawingObject.RotationDegrees);
        }

        return marker;
    }

    private static bool TryGetDisplayedDrawingObjectBounds(
        ViewportModel viewport,
        DrawingObjectBounds drawingObject,
        out double left,
        out double top,
        out double width,
        out double height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;
        if (!TryGetDisplayedColumnLeft(viewport.ColMetrics, drawingObject.AnchorCol, out var columnLeft) ||
            !TryGetDisplayedRowTop(viewport.RowMetrics, drawingObject.AnchorRow, out var rowTop))
        {
            return false;
        }

        left = HeaderColumnWidth + columnLeft;
        top = HeaderRowHeight + rowTop;
        width = Math.Max(1, drawingObject.Width);
        height = Math.Max(1, drawingObject.Height);
        return true;
    }

    private static bool TryGetDisplayedColumnLeft(
        IReadOnlyList<ColMetric> columns,
        uint column,
        out double left)
    {
        left = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var metric = columns[i];
            if (metric.Col == column)
                return true;
            left += GetDisplayedColumnWidth(metric);
        }

        left = 0;
        return false;
    }

    private static bool TryGetDisplayedRowTop(
        IReadOnlyList<RowMetric> rows,
        uint row,
        out double top)
    {
        top = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var metric = rows[i];
            if (metric.Row == row)
                return true;
            top += GetDisplayedRowHeight(metric);
        }

        top = 0;
        return false;
    }

    private static double CalculateDisplayedGridWidth(ViewportModel viewport)
    {
        var width = HeaderColumnWidth;
        foreach (var metric in viewport.ColMetrics)
            width += GetDisplayedColumnWidth(metric);

        return width;
    }

    private static double CalculateDisplayedGridHeight(ViewportModel viewport)
    {
        var height = HeaderRowHeight;
        foreach (var metric in viewport.RowMetrics)
            height += GetDisplayedRowHeight(metric);

        return height;
    }

    private static double GetDisplayedColumnWidth(ColMetric metric) =>
        Math.Max(MinimumDisplayedColumnWidth, metric.Width);

    private static double GetDisplayedRowHeight(RowMetric metric) =>
        Math.Max(MinimumDisplayedRowHeight, metric.Height);

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
        if (!TryCommitPendingFormulaEdit())
            return;

        _session.SelectCell(address);
        RefreshShell("Ready");
    }

    private void SelectSheet(SheetId sheetId)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        if (!_session.SelectSheet(sheetId))
            return;

        RefreshShell($"Selected {_session.ActiveSheet.Name}");
    }

    private void BeginFormulaEdit(CellAddress address, string? initialText = null)
    {
        _session.BeginFormulaEdit(address);
        RefreshShell("Ready");
        var originalText = _formulaBox.Text ?? "";
        if (initialText is not null)
            _formulaBox.Text = initialText;

        _formulaBox.Focus();
        _formulaBoxEditOriginalText = originalText;
        MoveFormulaBoxCaretToEnd();
    }

    private void FormulaBox_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_session.FormulaEditAddress is not null)
            return;

        _session.BeginFormulaEdit(_session.ActiveCell);
        _formulaBoxEditOriginalText = _formulaBox.Text ?? "";
    }

    private void FormulaBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFormulaBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            var colDelta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            if (CommitFormulaBox())
            {
                _session.MoveActiveCell(0, colDelta);
                RefreshShell("Ready");
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _session.CancelFormulaEdit();
            _formulaBoxEditOriginalText = null;
            RefreshShell("Ready");
            e.Handled = true;
        }
    }

    private bool CommitFormulaBox()
    {
        var address = _session.FormulaEditAddress ?? _session.ActiveCell;
        var result = _session.CommitCellText(_formulaBox.Text ?? "");

        if (!result.Success)
        {
            _statusText.Text = result.ErrorMessage ?? "Edit failed";
            _statusText.Foreground = Brush(143, 74, 18);
            return false;
        }

        _formulaBoxEditOriginalText = null;
        RefreshShell($"Edited {FormatCellReference(address)}");
        return true;
    }

    private bool TryCommitPendingFormulaEdit()
    {
        if (_session.FormulaEditAddress is null)
            return true;

        if (!HasPendingFormulaEditText())
        {
            _session.CancelFormulaEdit();
            _formulaBoxEditOriginalText = null;
            return true;
        }

        return CommitFormulaBox();
    }

    private bool HasPendingFormulaEditText() =>
        !string.Equals(
            _formulaBox.Text ?? "",
            _formulaBoxEditOriginalText ?? "",
            StringComparison.Ordinal);

    private void MoveFormulaBoxCaretToEnd()
    {
        _formulaBox.CaretIndex = _formulaBox.Text?.Length ?? 0;
        _formulaBox.SelectionStart = _formulaBox.CaretIndex;
        _formulaBox.SelectionEnd = _formulaBox.CaretIndex;
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

    private void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        UndoLastEdit();
    }

    private void RedoButton_Click(object? sender, RoutedEventArgs e)
    {
        RedoLastEdit();
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        await CopyActiveCellToClipboardAsync();
    }

    private async void PasteButton_Click(object? sender, RoutedEventArgs e)
    {
        await PasteClipboardTextAsync();
    }

    private void UndoLastEdit()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ApplyEditHistoryResult(_session.UndoLastEdit(), "Undid last edit");
    }

    private void RedoLastEdit()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ApplyEditHistoryResult(_session.RedoLastEdit(), "Redid last edit");
    }

    private void ApplyEditHistoryResult(WorkbookCellEditResult result, string successStatus)
    {
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Edit history unavailable.");
            return;
        }

        RefreshShell(successStatus);
    }

    private async Task CopyActiveCellToClipboardAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var cellReference = FormatCellReference(_session.ActiveCell);
        await clipboard.SetTextAsync(_session.CopyActiveCellText());
        RefreshShell($"Copied {cellReference}");
    }

    private async Task PasteClipboardTextAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowEditIssue("Clipboard unavailable on this platform.");
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            ShowEditIssue("Clipboard does not contain text.");
            return;
        }

        var destination = _session.ActiveCell;
        var result = _session.PasteExternalTextAtActiveCell(text);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Paste failed.");
            return;
        }

        RefreshShell($"Pasted at {FormatCellReference(destination)}");
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TrySelectDroppedWorkbookPath(e, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TrySelectDroppedWorkbookPath(e, out var path, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        await OpenWorkbookPathAsync(path!);
    }

    public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)
    {
        if (!TrySelectOpenableLocalWorkbookPath(files, out var path, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        await OpenWorkbookPathAsync(path!);
    }

    internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()
    {
        var hasNativeFileMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "File", StringComparison.Ordinal) &&
            item.Menu is not null) == true;
        var hasNativeEditMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), "Edit", StringComparison.Ordinal) &&
            item.Menu is not null) == true;

        return new MacOsLaunchSmokeSnapshot(
            WindowShown: IsVisible,
            WindowTitle: Title ?? "",
            DisplayName: _session.DisplayName,
            ActiveSheetName: _session.ActiveSheet.Name,
            ViewportRowCount: _session.Viewport.RowMetrics.Count,
            ViewportColumnCount: _session.Viewport.ColMetrics.Count,
            OpenedSourcePath: _session.CurrentFilePath,
            IsOpening: _isOpening,
            HasNativeFileMenu: hasNativeFileMenu,
            HasNativeEditMenu: hasNativeEditMenu,
            HasNativeOpenMenuItem: HasNativeMenuItem(_openMenuItem, "Open..."),
            HasNativeSaveMenuItem: HasNativeMenuItem(_saveMenuItem, "Save"),
            HasNativeSaveAsMenuItem: HasNativeMenuItem(_saveAsMenuItem, "Save As..."),
            HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, "Undo"),
            HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, "Redo"),
            HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, "Copy"),
            HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, "Paste"),
            HasNativeQuitMenuItem: HasNativeMenuItem(_quitMenuItem, "Quit FreeX"));
    }

    private static bool HasNativeMenuItem(NativeMenuItem item, string expectedHeader) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal) &&
        item.Gesture is not null;

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

        if (_formulaBox.IsFocused && e.Key is Key.Z or Key.Y or Key.C or Key.V)
            return;

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            RedoLastEdit();
        }
        else if (e.Key == Key.Z)
        {
            e.Handled = true;
            UndoLastEdit();
        }
        else if (e.Key == Key.Y)
        {
            e.Handled = true;
            RedoLastEdit();
        }
        else if (e.Key == Key.C)
        {
            e.Handled = true;
            await CopyActiveCellToClipboardAsync();
        }
        else if (e.Key == Key.V)
        {
            e.Handled = true;
            await PasteClipboardTextAsync();
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            await SaveWorkbookAsAsync();
        }
        else if (e.Key == Key.S)
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

    private void TryQuitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown(0);
            return;
        }

        Close();
    }

    private void NavigateActiveCell(KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            BeginFormulaEdit(_session.ActiveCell);
            return;
        }

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

    private void MainWindow_TextInput(object? sender, TextInputEventArgs e)
    {
        if (_formulaBox.IsFocused || string.IsNullOrEmpty(e.Text))
            return;

        foreach (var character in e.Text)
        {
            if (char.IsControl(character))
                return;
        }

        BeginFormulaEdit(_session.ActiveCell, e.Text);
        e.Handled = true;
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

    private void WorksheetScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingWorksheetScrollBars)
            return;

        var (topRow, leftCol) = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
            _session.ActiveSheet,
            _verticalWorksheetScrollBar.Value,
            _horizontalWorksheetScrollBar.Value);
        if (_session.SetViewportOrigin(topRow, leftCol))
            RefreshShell("Ready");
    }

    private async Task OpenWorkbookAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
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

            await OpenWorkbookPathAsync(path);
        }
    }

    private async Task OpenWorkbookPathAsync(string path)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsDirty)
        {
            ShowOpenIssue("Save changes before opening another workbook.");
            return;
        }

        if (!_session.TryResolveOpenTarget(path, out var target, out var message))
        {
            ShowOpenIssue(message);
            return;
        }

        await OpenWorkbookFromTargetAsync(target!);
    }

    private bool TrySelectDroppedWorkbookPath(DragEventArgs e, out string? path, out string message)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            path = null;
            message = "Drop a supported local workbook file.";
            return false;
        }

        return TrySelectOpenableLocalWorkbookPath(files, out path, out message);
    }

    private bool TrySelectOpenableLocalWorkbookPath(IEnumerable<IStorageItem> files, out string? path, out string message)
    {
        path = null;
        if (_isOpening || _isSaving)
        {
            message = "Open is busy.";
            return false;
        }

        if (!TryCommitPendingFormulaEdit())
        {
            message = "Finish the current cell edit before opening another workbook.";
            return false;
        }

        if (_session.IsDirty)
        {
            message = "Save changes before opening another workbook.";
            return false;
        }

        var sawLocalPath = false;
        var sawFileCandidate = false;
        var unsupportedMessage = "Drop a supported workbook file.";
        foreach (var file in files)
        {
            var candidate = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            sawLocalPath = true;
            if (Directory.Exists(candidate))
                continue;
            if (!File.Exists(candidate))
                continue;

            sawFileCandidate = true;
            if (_session.TryResolveOpenTarget(candidate, out _, out unsupportedMessage))
            {
                path = candidate;
                message = "";
                return true;
            }
        }

        message = sawFileCandidate
            ? unsupportedMessage
            : sawLocalPath
                ? "Drop a supported workbook file."
                : "Open requires a local file path.";
        return false;
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
            _session = _sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth, includeObjects: true);
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

        if (!TryCommitPendingFormulaEdit())
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

        if (!TryCommitPendingFormulaEdit())
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

    private void ShowEditIssue(string message)
    {
        _statusText.Text = message;
        _statusText.Foreground = Brush(143, 74, 18);
        UpdateSaveButton();
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

    private static IBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));

    private static IBrush Brush(CellColor color) =>
        Brush(color.R, color.G, color.B);
}
