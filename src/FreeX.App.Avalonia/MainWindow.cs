using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Calc;
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
        var source = new StartupWorkbookLoader().Load(startupArguments);
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
        StartupWorkbookLoadResult source,
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

    private static Control BuildToolbar(StartupWorkbookLoadResult source, Sheet sheet, ViewportModel viewport)
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
