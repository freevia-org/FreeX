using System.Windows;

using FreeX.Core.Model;

namespace FreeX.App.UI;

internal static class QuickAnalysisPreviewLayoutPlanner
{
    public static Rect? CalculatePreviewRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static IReadOnlyList<Rect> CalculateDataBarPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var numericCells = new List<(DisplayCell Cell, double Value)>();
        var max = 0d;
        foreach (var cell in viewport.Cells)
        {
            if (cell.Row < range.Start.Row ||
                cell.Row > range.End.Row ||
                cell.Col < range.Start.Col ||
                cell.Col > range.End.Col ||
                !TryGetPreviewNumber(cell, out var value))
                continue;

            numericCells.Add((cell, value));
            max = Math.Max(max, Math.Max(0, value));
        }

        if (numericCells.Count == 0)
            return [];

        var rows = BuildRowMetricLookup(viewport.RowMetrics);
        var cols = BuildColMetricLookup(viewport.ColMetrics);
        var rects = new List<Rect>();
        foreach (var (cell, value) in numericCells)
        {
            if (!rows.TryGetValue(cell.Row, out var row) || !cols.TryGetValue(cell.Col, out var col))
                continue;

            var fraction = max <= 0 ? 0 : Math.Clamp(Math.Max(0, value) / max, 0, 1);
            var availableWidth = Math.Max(0, col.Width - 6);
            rects.Add(new Rect(
                col.LeftOffset + rowHeaderWidth + 3,
                row.TopOffset + columnHeaderHeight + 4,
                Math.Round(availableWidth * fraction, 3),
                Math.Max(0, row.Height - 8)));
        }

        return rects;
    }

    public static IReadOnlyList<Rect> CalculateCellPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var rects = new List<Rect>();
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row || row.Row > range.End.Row)
                continue;

            foreach (var col in viewport.ColMetrics)
            {
                if (col.Col < range.Start.Col || col.Col > range.End.Col)
                    continue;

                rects.Add(new Rect(
                    col.LeftOffset + rowHeaderWidth + 3,
                    row.TopOffset + columnHeaderHeight + 3,
                    Math.Max(0, col.Width - 6),
                    Math.Max(0, row.Height - 6)));
            }
        }

        return rects.Count == 0 ? [] : rects;
    }

    public static IReadOnlyList<Rect> CalculateSparklinePreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var col = FirstVisibleColumnInRange(viewport.ColMetrics, range);
        if (col is null)
            return [];

        var rects = new List<Rect>();
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row || row.Row > range.End.Row)
                continue;

            var height = Math.Max(4, Math.Floor(row.Height / 3));
            var width = col.Width - 12;
            if (width < 6)
                continue;

            rects.Add(new Rect(
                col.LeftOffset + rowHeaderWidth + 6,
                row.TopOffset + columnHeaderHeight + Math.Round((row.Height - height) / 2),
                width,
                height));
        }

        return rects;
    }

    private static ColMetric? FirstVisibleColumnInRange(
        IReadOnlyList<ColMetric> columns,
        GridRange range)
    {
        foreach (var col in columns)
        {
            if (col.Col >= range.Start.Col && col.Col <= range.End.Col)
                return col;
        }

        return null;
    }

    private static Dictionary<uint, RowMetric> BuildRowMetricLookup(IReadOnlyList<RowMetric> metrics)
    {
        var lookup = new Dictionary<uint, RowMetric>(metrics.Count);
        foreach (var metric in metrics)
            lookup[metric.Row] = metric;

        return lookup;
    }

    private static Dictionary<uint, ColMetric> BuildColMetricLookup(IReadOnlyList<ColMetric> metrics)
    {
        var lookup = new Dictionary<uint, ColMetric>(metrics.Count);
        foreach (var metric in metrics)
            lookup[metric.Col] = metric;

        return lookup;
    }

    private static bool TryGetPreviewNumber(DisplayCell cell, out double value)
    {
        switch (cell.RawValue)
        {
            case NumberValue number:
                value = number.Value;
                return true;
            case DateTimeValue dateTime:
                value = dateTime.Value;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
