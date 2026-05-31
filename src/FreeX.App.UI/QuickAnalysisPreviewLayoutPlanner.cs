using System.Windows;

using FreeX.Core.Model;

namespace FreeX.App.UI;

internal interface IQuickAnalysisPreviewRectConsumer
{
    void Accept(Rect rect);
}

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
        var rows = BuildRowMetricLookup(viewport.RowMetrics);
        var cols = BuildColMetricLookup(viewport.ColMetrics);
        var consumer = new ListRectConsumer();
        VisitDataBarPreviewRects(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            rows,
            cols,
            ref consumer);

        return consumer.ToResult();
    }

    internal static void VisitDataBarPreviewRects<TConsumer>(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        Dictionary<uint, RowMetric> rows,
        Dictionary<uint, ColMetric> cols,
        ref TConsumer consumer)
        where TConsumer : struct, IQuickAnalysisPreviewRectConsumer
    {
        var max = 0d;
        var hasNumericCell = false;
        foreach (var cell in viewport.Cells)
        {
            if (!IsCellInRange(cell, range) || !TryGetPreviewNumber(cell, out var value))
                continue;

            hasNumericCell = true;
            var positiveValue = Math.Max(0, value);
            if (positiveValue > max)
                max = positiveValue;
        }

        if (!hasNumericCell)
            return;

        foreach (var cell in viewport.Cells)
        {
            if (IsCellInRange(cell, range) &&
                TryGetPreviewNumber(cell, out var value) &&
                rows.TryGetValue(cell.Row, out var row) &&
                cols.TryGetValue(cell.Col, out var col))
            {
                consumer.Accept(CreateDataBarRect(row, col, value, max, rowHeaderWidth, columnHeaderHeight));
            }
        }
    }

    private static bool IsCellInRange(DisplayCell cell, GridRange range) =>
        cell.Row >= range.Start.Row &&
        cell.Row <= range.End.Row &&
        cell.Col >= range.Start.Col &&
        cell.Col <= range.End.Col;

    public static IReadOnlyList<Rect> CalculateCellPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var consumer = new ListRectConsumer();
        VisitCellPreviewRects(viewport, range, rowHeaderWidth, columnHeaderHeight, ref consumer);
        return consumer.ToResult();
    }

    internal static void VisitCellPreviewRects<TConsumer>(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        ref TConsumer consumer)
        where TConsumer : struct, IQuickAnalysisPreviewRectConsumer
    {
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row || row.Row > range.End.Row)
                continue;

            foreach (var col in viewport.ColMetrics)
            {
                if (col.Col < range.Start.Col || col.Col > range.End.Col)
                    continue;

                consumer.Accept(new Rect(
                    col.LeftOffset + rowHeaderWidth + 3,
                    row.TopOffset + columnHeaderHeight + 3,
                    Math.Max(0, col.Width - 6),
                    Math.Max(0, row.Height - 6)));
            }
        }
    }

    public static IReadOnlyList<Rect> CalculateSparklinePreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var consumer = new ListRectConsumer();
        VisitSparklinePreviewRects(viewport, range, rowHeaderWidth, columnHeaderHeight, ref consumer);
        return consumer.ToResult();
    }

    internal static void VisitSparklinePreviewRects<TConsumer>(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        ref TConsumer consumer)
        where TConsumer : struct, IQuickAnalysisPreviewRectConsumer
    {
        var col = FirstVisibleColumnInRange(viewport.ColMetrics, range);
        if (col is null)
            return;

        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row || row.Row > range.End.Row)
                continue;

            var height = Math.Max(4, Math.Floor(row.Height / 3));
            var width = col.Width - 12;
            if (width < 6)
                continue;

            consumer.Accept(new Rect(
                col.LeftOffset + rowHeaderWidth + 6,
                row.TopOffset + columnHeaderHeight + Math.Round((row.Height - height) / 2),
                width,
                height));
        }
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

    private static Rect CreateDataBarRect(
        RowMetric row,
        ColMetric col,
        double value,
        double max,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var fraction = max <= 0 ? 0 : Math.Clamp(Math.Max(0, value) / max, 0, 1);
        var availableWidth = Math.Max(0, col.Width - 6);
        return new Rect(
            col.LeftOffset + rowHeaderWidth + 3,
            row.TopOffset + columnHeaderHeight + 4,
            Math.Round(availableWidth * fraction, 3),
            Math.Max(0, row.Height - 8));
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

    private struct ListRectConsumer : IQuickAnalysisPreviewRectConsumer
    {
        private List<Rect>? _rects;

        public void Accept(Rect rect)
        {
            _rects ??= [];
            _rects.Add(rect);
        }

        public readonly IReadOnlyList<Rect> ToResult() => _rects is { Count: > 0 } rects ? rects : [];
    }
}
