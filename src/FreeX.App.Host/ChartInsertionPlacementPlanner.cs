using FreeX.Core.Model;

namespace FreeX.App.Host;

internal readonly record struct ChartInsertionPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

internal static class ChartInsertionPlacementPlanner
{
    public const double DefaultChartWidth = 400d;
    public const double DefaultChartHeight = 300d;
    private const double PlacementGap = 16d;
    private const double ViewportInset = 20d;

    public static ChartInsertionPlacement CreatePlacement(
        Sheet sheet,
        GridRange sourceRange,
        ViewportModel? viewport,
        double viewportWidth,
        double viewportHeight)
    {
        var sourceLeft = GetColumnLeft(sheet, sourceRange.Start.Col);
        var sourceRight = GetColumnRight(sheet, sourceRange.End.Col);
        var sourceTop = GetRowTop(sheet, sourceRange.Start.Row);
        var sourceBottom = GetRowBottom(sheet, sourceRange.End.Row);

        var visible = GetVisibleWorksheetRect(sheet, viewport, viewportWidth, viewportHeight);

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
        double viewportHeight)
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
        var left = GetColumnLeft(sheet, firstCol);
        var top = GetRowTop(sheet, firstRow);
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

    private static double GetColumnLeft(Sheet sheet, uint column)
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

        foreach (var hiddenColumn in GetHiddenColumns(sheet))
        {
            if (hiddenColumn < column)
                left -= GetRawColumnWidthPixels(sheet, hiddenColumn);
        }

        return Math.Max(0, left);
    }

    private static double GetColumnRight(Sheet sheet, uint column) =>
        GetColumnLeft(sheet, column) + GetColumnWidthPixels(sheet, column);

    private static double GetRowTop(Sheet sheet, uint row)
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

        foreach (var hiddenRow in GetHiddenRows(sheet))
        {
            if (hiddenRow < row)
                top -= GetRawRowHeight(sheet, hiddenRow);
        }

        return Math.Max(0, top);
    }

    private static double GetRowBottom(Sheet sheet, uint row) =>
        GetRowTop(sheet, row) + GetRowHeight(sheet, row);

    private static double GetDefaultColumnWidthPixels(Sheet sheet) =>
        Math.Max(1, ColumnWidthToPixels(sheet.DefaultColumnWidth));

    private static double GetColumnWidthPixels(Sheet sheet, uint column) =>
        sheet.IsColEffectivelyHidden(column)
            ? 0
            : GetRawColumnWidthPixels(sheet, column);

    private static double GetRawColumnWidthPixels(Sheet sheet, uint column) =>
        Math.Max(1, ColumnWidthToPixels(sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth)));

    private static double GetColumnWidthPixels(double width) =>
        Math.Max(1, ColumnWidthToPixels(width));

    private static double ColumnWidthToPixels(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return 0;

        return width < 1
            ? Math.Round(width * 12.0, MidpointRounding.AwayFromZero)
            : Math.Round(width * 7.0 + 5.0, MidpointRounding.AwayFromZero);
    }

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
