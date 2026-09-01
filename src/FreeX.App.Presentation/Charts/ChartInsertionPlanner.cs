using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

public readonly record struct ChartInsertionPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

public readonly record struct ChartInsertionViewport(
    ViewportModel? Viewport,
    double AvailableWidth,
    double AvailableHeight);

public readonly record struct ChartInsertionPlan(
    GridRange DataRange,
    ChartInsertionPlacement Placement,
    AddChartCommand Command);

public static class ChartInsertionPlanner
{
    public const double DefaultLeft = 20d;
    public const double DefaultTop = 20d;
    public const double DefaultChartWidth = 400d;
    public const double DefaultChartHeight = 300d;
    private const double PlacementGap = 16d;
    private const double ViewportInset = 20d;

    public static ChartInsertionPlacement DefaultPlacement { get; } =
        new(DefaultLeft, DefaultTop, DefaultChartWidth, DefaultChartHeight);

    public static ChartType? ChartTypeForRibbonCommand(string commandId) => commandId switch
    {
        "insert.column" => ChartType.Column,
        "insert.colClustered" => ChartType.Column,
        "insert.colStacked" => ChartType.StackedColumn,
        "insert.col100" => ChartType.PercentStackedColumn,
        "insert.bar" => ChartType.Bar,
        "insert.line" => ChartType.Line,
        "insert.area" => ChartType.Area,
        "insert.pie" => ChartType.Pie,
        "insert.doughnut" => ChartType.Doughnut,
        "insert.scatter" => ChartType.Scatter,
        "insert.recommended" => ChartType.Column,

        "Recommended Charts" => ChartType.Column,
        "Column Chart" => ChartType.Column,
        "Stacked Column Chart" => ChartType.StackedColumn,
        "100% Stacked Column Chart" => ChartType.PercentStackedColumn,
        "Bar Chart" => ChartType.Bar,
        "Stacked Bar Chart" => ChartType.StackedBar,
        "100% Stacked Bar Chart" => ChartType.PercentStackedBar,
        "Line Chart" => ChartType.Line,
        "Area Chart" => ChartType.Area,
        "Pie Chart" => ChartType.Pie,
        "Doughnut Chart" => ChartType.Doughnut,
        "Scatter Chart" => ChartType.Scatter,
        "Stock Chart" => ChartType.Stock,
        "Bubble Chart" => ChartType.Bubble,
        "Radar Chart" => ChartType.Radar,
        _ => null,
    };

    public static GridRange ResolveDataRange(Sheet? sheet, GridRange selectedRange) =>
        sheet is null
            ? selectedRange
            : ChartDataSourcePlanner.ResolveInsertionRange(sheet, selectedRange);

    public static ChartInsertionPlan CreateEmbeddedChartPlan(
        Sheet sheet,
        GridRange selectedRange,
        ChartType chartType,
        ChartInsertionViewport viewport,
        string? title = "Chart")
    {
        var dataRange = ResolveDataRange(sheet, selectedRange);
        var placement = CreatePlacement(
            sheet,
            dataRange,
            viewport.Viewport,
            viewport.AvailableWidth,
            viewport.AvailableHeight);
        return CreateEmbeddedChartPlan(sheet.Id, dataRange, chartType, title, placement);
    }

    public static ChartInsertionPlan CreateEmbeddedChartPlan(
        Sheet sheet,
        GridRange selectedRange,
        ChartType chartType,
        string? title = null,
        ChartInsertionPlacement? placement = null)
    {
        var dataRange = ResolveDataRange(sheet, selectedRange);
        return CreateEmbeddedChartPlan(
            sheet.Id,
            dataRange,
            chartType,
            title,
            placement ?? DefaultPlacement);
    }

    public static ChartInsertionPlan CreateEmbeddedChartPlan(
        SheetId sheetId,
        GridRange dataRange,
        ChartType chartType,
        string? title,
        ChartInsertionPlacement placement)
    {
        var command = BuildEmbeddedChartCommand(sheetId, dataRange, chartType, title, placement);
        return new ChartInsertionPlan(dataRange, placement, command);
    }

    public static AddChartCommand BuildEmbeddedChartCommand(
        SheetId sheetId,
        GridRange dataRange,
        ChartType chartType,
        string? title,
        ChartInsertionPlacement placement) =>
        new(
            sheetId,
            dataRange,
            chartType,
            title,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height);

    public static AddChartCommand BuildEmbeddedChartCommand(
        Sheet sheet,
        GridRange selectedRange,
        ChartType chartType,
        string? title = null,
        ChartInsertionPlacement? placement = null) =>
        CreateEmbeddedChartPlan(sheet, selectedRange, chartType, title, placement).Command;

    public static AddChartSheetCommand BuildChartSheetCommand(
        Sheet? sheet,
        SheetId sheetId,
        GridRange selectedRange,
        ChartType chartType,
        string title) =>
        BuildChartSheetCommand(sheetId, ResolveDataRange(sheet, selectedRange), chartType, title);

    public static AddChartSheetCommand BuildChartSheetCommand(
        SheetId sheetId,
        GridRange dataRange,
        ChartType chartType,
        string title) =>
        new(sheetId, dataRange, chartType, title);

    public static ChartInsertionPlacement CreatePlacement(
        Sheet sheet,
        GridRange sourceRange,
        ViewportModel? viewport,
        double viewportWidth,
        double viewportHeight)
    {
        var hasVisibleViewport = viewport is { RowMetrics.Count: > 0, ColMetrics.Count: > 0 } &&
                                 viewportWidth > 0 &&
                                 viewportHeight > 0;
        var hiddenColumns = sourceRange.Start.Col > 1 ||
                            sourceRange.End.Col > 1 ||
                            hasVisibleViewport && viewport!.ColMetrics[0].Col > 1
            ? GetHiddenColumns(sheet)
            : null;
        var hiddenRows = sourceRange.Start.Row > 1 ||
                         sourceRange.End.Row > 1 ||
                         hasVisibleViewport && viewport!.RowMetrics[0].Row > 1
            ? GetHiddenRows(sheet)
            : null;
        var sourceLeft = GetColumnLeft(sheet, sourceRange.Start.Col, hiddenColumns);
        var sourceRight = GetColumnRight(sheet, sourceRange.End.Col, hiddenColumns);
        var sourceTop = GetRowTop(sheet, sourceRange.Start.Row, hiddenRows);
        var sourceBottom = GetRowBottom(sheet, sourceRange.End.Row, hiddenRows);

        var visible = GetVisibleWorksheetRect(
            sheet,
            viewport,
            viewportWidth,
            viewportHeight,
            hiddenColumns,
            hiddenRows);

        var left = sourceRight + PlacementGap;
        var top = sourceTop;
        if (visible is { } visibleRect)
        {
            if (left + DefaultChartWidth > visibleRect.Right - ViewportInset)
                left = sourceLeft;

            if (top + DefaultChartHeight > visibleRect.Bottom - ViewportInset)
                top = sourceBottom + PlacementGap;

            left = ClampStart(left, visibleRect.Left, visibleRect.Right, DefaultChartWidth);
            top = ClampStart(top, visibleRect.Top, visibleRect.Bottom, DefaultChartHeight);
        }

        return new ChartInsertionPlacement(
            Math.Max(0, left),
            Math.Max(0, top),
            DefaultChartWidth,
            DefaultChartHeight);
    }

    private static WorksheetRect? GetVisibleWorksheetRect(
        Sheet sheet,
        ViewportModel? viewport,
        double viewportWidth,
        double viewportHeight,
        IReadOnlySet<uint>? hiddenColumns,
        IReadOnlySet<uint>? hiddenRows)
    {
        if (viewport is null ||
            viewport.RowMetrics.Count == 0 ||
            viewport.ColMetrics.Count == 0 ||
            viewportWidth <= 0 ||
            viewportHeight <= 0)
        {
            return null;
        }

        var firstRow = viewport.RowMetrics[0].Row;
        var firstCol = viewport.ColMetrics[0].Col;
        var left = GetColumnLeft(sheet, firstCol, hiddenColumns);
        var top = GetRowTop(sheet, firstRow, hiddenRows);
        return new WorksheetRect(
            left,
            top,
            left + viewportWidth,
            top + viewportHeight);
    }

    private static double ClampStart(double value, double visibleStart, double visibleEnd, double extent)
    {
        if (!double.IsFinite(value) || visibleEnd <= visibleStart)
            return Math.Max(0, value);

        var min = visibleStart + ViewportInset;
        var max = visibleEnd - extent - ViewportInset;
        if (max < min)
            max = visibleStart + ViewportInset;

        return Math.Clamp(value, min, max);
    }

    private static double GetColumnLeft(
        Sheet sheet,
        uint column,
        IReadOnlySet<uint>? hiddenColumns)
    {
        if (column <= 1)
            return 0;

        var before = column - 1;
        var defaultWidth = GetDefaultColumnWidthPixels(sheet);
        var left = before * defaultWidth;
        foreach (var (index, width) in sheet.ColumnWidths)
        {
            if (index < column)
                left += GetColumnWidthPixels(width) - defaultWidth;
        }

        if (hiddenColumns is not null)
        {
            foreach (var hiddenColumn in hiddenColumns)
            {
                if (hiddenColumn < column)
                    left -= GetRawColumnWidthPixels(sheet, hiddenColumn);
            }
        }

        return Math.Max(0, left);
    }

    private static double GetColumnRight(
        Sheet sheet,
        uint column,
        IReadOnlySet<uint>? hiddenColumns) =>
        GetColumnLeft(sheet, column, hiddenColumns) + GetColumnWidthPixels(sheet, column);

    private static double GetRowTop(
        Sheet sheet,
        uint row,
        IReadOnlySet<uint>? hiddenRows)
    {
        if (row <= 1)
            return 0;

        var before = row - 1;
        var defaultHeight = GetDefaultRowHeight(sheet);
        var top = before * defaultHeight;
        foreach (var (index, height) in sheet.RowHeights)
        {
            if (index < row)
                top += GetRowHeight(height) - defaultHeight;
        }

        if (hiddenRows is not null)
        {
            foreach (var hiddenRow in hiddenRows)
            {
                if (hiddenRow < row)
                    top -= GetRawRowHeight(sheet, hiddenRow);
            }
        }

        return Math.Max(0, top);
    }

    private static double GetRowBottom(
        Sheet sheet,
        uint row,
        IReadOnlySet<uint>? hiddenRows) =>
        GetRowTop(sheet, row, hiddenRows) + GetRowHeight(sheet, row);

    private static double GetDefaultColumnWidthPixels(Sheet sheet) =>
        GetColumnWidthPixels(sheet.DefaultColumnWidth);

    private static double GetColumnWidthPixels(Sheet sheet, uint column) =>
        sheet.IsColEffectivelyHidden(column)
            ? 0
            : GetRawColumnWidthPixels(sheet, column);

    private static double GetRawColumnWidthPixels(Sheet sheet, uint column) =>
        GetColumnWidthPixels(sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth));

    private static double GetColumnWidthPixels(double width) =>
        Math.Max(1, ColumnWidthPixelMapper.ColumnWidthToPixels(width));

    private static double GetDefaultRowHeight(Sheet sheet) =>
        GetRowHeight(sheet.DefaultRowHeight);

    private static double GetRowHeight(Sheet sheet, uint row) =>
        sheet.IsRowEffectivelyHidden(row)
            ? 0
            : GetRawRowHeight(sheet, row);

    private static double GetRawRowHeight(Sheet sheet, uint row) =>
        GetRowHeight(sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight));

    private static double GetRowHeight(double height) =>
        double.IsFinite(height) && height > 0 ? height : 1;

    private static HashSet<uint> GetHiddenColumns(Sheet sheet)
    {
        var hidden = new HashSet<uint>(sheet.HiddenCols);
        foreach (var column in sheet.GroupHiddenCols)
            hidden.Add(column);
        return hidden;
    }

    private static HashSet<uint> GetHiddenRows(Sheet sheet)
    {
        var hidden = new HashSet<uint>(sheet.HiddenRows);
        foreach (var row in sheet.FilterHiddenRows)
            hidden.Add(row);
        foreach (var row in sheet.GroupHiddenRows)
            hidden.Add(row);
        return hidden;
    }

    private readonly record struct WorksheetRect(double Left, double Top, double Right, double Bottom);
}
