using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed class MainWindow : Window
{
    private const double HeaderColumnWidth = 58;
    private const double HeaderRowHeight = 28;
    private static readonly IBrush WindowBackground = Brush(246, 247, 249);
    private static readonly IBrush HeaderBackground = Brush(241, 243, 246);
    private static readonly IBrush HeaderForeground = Brush(73, 80, 93);
    private static readonly IBrush GridLine = Brush(218, 222, 228);
    private static readonly IBrush ToolbarBorder = Brush(218, 222, 228);

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        var source = WorkbookLoader.Load(startupArguments);
        var workbook = source.Workbook;
        var sheet = EnsureSheet(workbook);
        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(
                sheet.ViewTopRow ?? 1,
                sheet.ViewLeftCol ?? 1,
                AvailableHeight: 880,
                AvailableWidth: 1440,
                IncludeObjects: false));

        Title = $"FreeX - {source.DisplayName}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        Background = WindowBackground;
        Content = BuildContent(source, sheet, viewport, workbook.Theme);
    }

    private static Sheet EnsureSheet(Workbook workbook) =>
        workbook.Sheets.Count == 0
            ? workbook.AddSheet("Sheet1")
            : workbook.Sheets[Math.Clamp(workbook.ActiveSheetIndex ?? 0, 0, workbook.Sheets.Count - 1)];

    private static Control BuildContent(
        WorkbookLoadResult source,
        Sheet sheet,
        ViewportModel viewport,
        WorkbookTheme theme)
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar(source, sheet, viewport);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildSheetGrid(viewport, theme),
        });

        return root;
    }

    private static Control BuildToolbar(WorkbookLoadResult source, Sheet sheet, ViewportModel viewport)
    {
        var title = new TextBlock
        {
            Text = source.DisplayName,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(25, 31, 40),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        var detail = new TextBlock
        {
            Text = $"{sheet.Name}  |  {viewport.RowMetrics.Count} rows x {viewport.ColMetrics.Count} columns",
            FontSize = 12,
            Foreground = Brush(94, 103, 116),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        var status = new TextBlock
        {
            Text = source.Status,
            FontSize = 12,
            Foreground = source.IsFallback ? Brush(143, 74, 18) : Brush(67, 113, 83),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 18,
                Children =
                {
                    title,
                    detail,
                    status,
                },
            },
        };
    }

    private static Control BuildSheetGrid(ViewportModel viewport, WorkbookTheme theme)
    {
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
            AddGridChild(grid, CreateHeaderCell(CellAddress.NumberToColumnName(col)), 0, colIndex + 1);
        }

        for (var rowIndex = 0; rowIndex < viewport.RowMetrics.Count; rowIndex++)
        {
            var row = viewport.RowMetrics[rowIndex].Row;
            AddGridChild(grid, CreateHeaderCell(row.ToString()), rowIndex + 1, 0);

            for (var colIndex = 0; colIndex < viewport.ColMetrics.Count; colIndex++)
            {
                var col = viewport.ColMetrics[colIndex].Col;
                cellsByAddress.TryGetValue((row, col), out var cell);
                AddGridChild(grid, CreateCell(cell, theme), rowIndex + 1, colIndex + 1);
            }
        }

        return grid;
    }

    private static Border CreateHeaderCell(string text) =>
        CreateCellBorder(text, HeaderBackground, HeaderForeground, TextAlignment.Center, FontWeight.SemiBold);

    private static Border CreateCell(DisplayCell cell, WorkbookTheme theme)
    {
        var hasCell = cell.Row != 0 && cell.Col != 0;
        if (!hasCell)
            return CreateCellBorder("", Brushes.White, Brushes.Black, TextAlignment.Left, FontWeight.Normal);

        var style = cell.Style;
        var background = style?.ResolveFillColor(theme) is { } fillColor
            ? Brush(fillColor)
            : Brushes.White;
        var foreground = style is null
            ? Brushes.Black
            : Brush(style.ResolveFontColor(theme));
        var alignment = cell.RawValue is NumberValue or DateTimeValue
            ? TextAlignment.Right
            : TextAlignment.Left;
        var weight = style?.Bold == true ? FontWeight.SemiBold : FontWeight.Normal;

        return CreateCellBorder(cell.DisplayText, background, foreground, alignment, weight);
    }

    private static Border CreateCellBorder(
        string text,
        IBrush background,
        IBrush foreground,
        TextAlignment textAlignment,
        FontWeight fontWeight)
    {
        return new Border
        {
            Background = background,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(0, 0, 1, 1),
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

internal sealed record WorkbookLoadResult(
    Workbook Workbook,
    string DisplayName,
    string Status,
    bool IsFallback);

internal static class WorkbookLoader
{
    public static WorkbookLoadResult Load(IReadOnlyList<string> startupArguments)
    {
        var filePath = startupArguments.FirstOrDefault(argument =>
            !string.IsNullOrWhiteSpace(argument) &&
            File.Exists(argument));

        if (filePath is null)
            return SampleWorkbookFactory.Create("Showing sample workbook.", isFallback: false);

        var extension = Path.GetExtension(filePath);
        var adapters = CreateOpenAdapters();
        var adapter = FileFormatResolver.FindOpenAdapter(adapters, extension, out _);
        if (adapter is null)
            return SampleWorkbookFactory.Create($"Unsupported file type: {extension}.", isFallback: true);

        try
        {
            using var stream = File.OpenRead(filePath);
            var workbook = adapter.Load(stream);
            workbook.Name = Path.GetFileName(filePath);
            return new WorkbookLoadResult(workbook, workbook.Name, $"Opened {extension}.", IsFallback: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            return SampleWorkbookFactory.Create($"Open failed: {ex.Message}", isFallback: true);
        }
    }

    private static IReadOnlyList<IFileAdapter> CreateOpenAdapters() =>
    [
        new XlsxFileAdapter(),
        new LegacyXlsFileAdapter(),
        new CsvFileAdapter(),
        new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'),
        new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'),
        new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'),
        new SpreadsheetXmlFileAdapter(),
        new NativeJsonAdapter(),
    ];
}

internal static class SampleWorkbookFactory
{
    public static WorkbookLoadResult Create(string status, bool isFallback)
    {
        var workbook = new Workbook("macOS Preview Workbook");
        var sheet = workbook.AddSheet("Port Plan");
        workbook.ActiveSheetIndex = 0;
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.ColumnWidths[1] = 22;
        sheet.ColumnWidths[2] = 18;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 34;
        sheet.ColumnWidths[5] = 18;

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = CellColor.FromArgb(232, 238, 247),
            FontColor = CellColor.FromArgb(25, 31, 40),
        });
        var greenStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = CellColor.FromArgb(226, 242, 232),
            FontColor = CellColor.FromArgb(25, 92, 52),
        });
        var amberStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = CellColor.FromArgb(255, 242, 214),
            FontColor = CellColor.FromArgb(119, 73, 10),
        });

        Set(sheet, 1, 1, "Area", headerStyle);
        Set(sheet, 1, 2, "Windows", headerStyle);
        Set(sheet, 1, 3, "macOS", headerStyle);
        Set(sheet, 1, 4, "Next port task", headerStyle);
        Set(sheet, 1, 5, "Priority", headerStyle);

        Set(sheet, 2, 1, "Core model", null);
        Set(sheet, 2, 2, "Shipping", greenStyle);
        Set(sheet, 2, 3, "Portable", greenStyle);
        Set(sheet, 2, 4, "Keep WPF/Win32 references out of Core.*", null);
        Set(sheet, 2, 5, 1, null);

        Set(sheet, 3, 1, "Formula/calc", null);
        Set(sheet, 3, 2, "Shipping", greenStyle);
        Set(sheet, 3, 3, "Portable", greenStyle);
        Set(sheet, 3, 4, "Run the default test lane on macOS runners", null);
        Set(sheet, 3, 5, 1, null);

        Set(sheet, 4, 1, "Workbook IO", null);
        Set(sheet, 4, 2, "Shipping", greenStyle);
        Set(sheet, 4, 3, "Preview", amberStyle);
        Set(sheet, 4, 4, "Load XLSX/CSV/FXL through shared adapters", null);
        Set(sheet, 4, 5, 2, null);

        Set(sheet, 5, 1, "App host", null);
        Set(sheet, 5, 2, "WPF", amberStyle);
        Set(sheet, 5, 3, "Avalonia shell", amberStyle);
        Set(sheet, 5, 4, "Extract reusable app services from WPF host", null);
        Set(sheet, 5, 5, 2, null);

        Set(sheet, 6, 1, "Grid", null);
        Set(sheet, 6, 2, "WPF renderer", greenStyle);
        Set(sheet, 6, 3, "Read-only viewport", amberStyle);
        Set(sheet, 6, 4, "Add selection, editing, frozen panes, and virtualization", null);
        Set(sheet, 6, 5, 1, null);

        Set(sheet, 7, 1, "Packaging", null);
        Set(sheet, 7, 2, "MSIX/EXE", greenStyle);
        Set(sheet, 7, 3, ".app artifact", amberStyle);
        Set(sheet, 7, 4, "Add Developer ID signing and notarization later", null);
        Set(sheet, 7, 5, 3, null);

        return new WorkbookLoadResult(workbook, workbook.Name, status, isFallback);
    }

    private static void Set(Sheet sheet, uint row, uint col, string value, StyleId? styleId) =>
        SetCell(sheet, row, col, new TextValue(value), styleId);

    private static void Set(Sheet sheet, uint row, uint col, double value, StyleId? styleId) =>
        SetCell(sheet, row, col, new NumberValue(value), styleId);

    private static void SetCell(Sheet sheet, uint row, uint col, ScalarValue value, StyleId? styleId)
    {
        var cell = Cell.FromValue(value);
        if (styleId is { } id)
            cell.StyleId = id;

        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }
}
