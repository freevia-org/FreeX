using System.Windows;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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
        var numericCells = viewport.Cells
            .Where(cell => cell.Row >= range.Start.Row
                && cell.Row <= range.End.Row
                && cell.Col >= range.Start.Col
                && cell.Col <= range.End.Col
                && TryGetPreviewNumber(cell, out _))
            .ToList();
        if (numericCells.Count == 0)
            return [];

        var max = numericCells
            .Select(cell => TryGetPreviewNumber(cell, out var value) ? Math.Max(0, value) : 0)
            .DefaultIfEmpty(0)
            .Max();

        var rows = viewport.RowMetrics.ToDictionary(row => row.Row);
        var cols = viewport.ColMetrics.ToDictionary(col => col.Col);
        var rects = new List<Rect>();
        foreach (var cell in numericCells)
        {
            if (!rows.TryGetValue(cell.Row, out var row) || !cols.TryGetValue(cell.Col, out var col))
                continue;

            TryGetPreviewNumber(cell, out var value);
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
        var rows = viewport.RowMetrics
            .Where(row => row.Row >= range.Start.Row && row.Row <= range.End.Row)
            .ToList();
        var cols = viewport.ColMetrics
            .Where(col => col.Col >= range.Start.Col && col.Col <= range.End.Col)
            .ToList();
        if (rows.Count == 0 || cols.Count == 0)
            return [];

        var rects = new List<Rect>(rows.Count * cols.Count);
        foreach (var row in rows)
        {
            foreach (var col in cols)
            {
                rects.Add(new Rect(
                    col.LeftOffset + rowHeaderWidth + 3,
                    row.TopOffset + columnHeaderHeight + 3,
                    Math.Max(0, col.Width - 6),
                    Math.Max(0, row.Height - 6)));
            }
        }

        return rects;
    }

    public static IReadOnlyList<Rect> CalculateSparklinePreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var rows = viewport.RowMetrics
            .Where(row => row.Row >= range.Start.Row && row.Row <= range.End.Row)
            .ToList();
        var col = viewport.ColMetrics.FirstOrDefault(col => col.Col >= range.Start.Col && col.Col <= range.End.Col);
        if (rows.Count == 0 || col is null)
            return [];

        var rects = new List<Rect>(rows.Count);
        foreach (var row in rows)
        {
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
