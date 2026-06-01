using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();
    private DrawingGroup? _headerBaseLayerCache;
    private HeaderBaseLayerCacheKey _headerBaseLayerCacheKey;

    private readonly record struct HeaderBaseLayerCacheKey(
        ViewportModel Viewport,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        bool UseR1C1ReferenceStyle,
        string CultureName,
        double PixelsPerDip);

    private readonly record struct HeaderSelectionInterval(uint Start, uint End);

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = FindRowMetric(Viewport.RowMetrics, fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + columnHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = FindColMetric(Viewport.ColMetrics, fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + rowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var viewport = Viewport!;
        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;

        RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);
        RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);
    }

    private void RenderHeaderBaseLayer(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        var key = new HeaderBaseLayerCacheKey(
            viewport,
            rowHeaderWidth,
            columnHeaderHeight,
            UseR1C1ReferenceStyle,
            CultureInfo.CurrentCulture.Name,
            pixelsPerDip);
        if (_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key)
        {
            dc.DrawDrawing(cached);
            return;
        }

        var rebuilt = BuildHeaderBaseLayerCache(viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);
        _headerBaseLayerCache = rebuilt;
        _headerBaseLayerCacheKey = key;
        dc.DrawDrawing(rebuilt);
    }

    private DrawingGroup BuildHeaderBaseLayerCache(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);

        if (group.CanFreeze)
            group.Freeze();

        return group;
    }

    private void RenderHeaderBase(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        foreach (var col in viewport.ColMetrics)
            DrawColumnHeader(dc, col, rowHeaderWidth, columnHeaderHeight, HeaderBackgroundBrush, pixelsPerDip);

        foreach (var row in viewport.RowMetrics)
            DrawRowHeader(dc, row, rowHeaderWidth, columnHeaderHeight, HeaderBackgroundBrush, pixelsPerDip);

        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, rowHeaderWidth, columnHeaderHeight));
    }

    private void RenderSelectedHeaders(
        DrawingContext dc,
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        if (selectedRanges is not { Count: > 0 } && selRange is null)
            return;

        var columnIntervals = BuildColumnHeaderSelectionIntervals(selectedRanges, selRange);
        var rowIntervals = BuildRowHeaderSelectionIntervals(selectedRanges, selRange);
        var columnIntervalIndex = 0;
        var rowIntervalIndex = 0;

        foreach (var col in viewport.ColMetrics)
        {
            if (IsHeaderSelected(col.Col, columnIntervals, ref columnIntervalIndex))
                DrawColumnHeader(dc, col, rowHeaderWidth, columnHeaderHeight, HeaderHighlightBrush, pixelsPerDip);
        }

        foreach (var row in viewport.RowMetrics)
        {
            if (IsHeaderSelected(row.Row, rowIntervals, ref rowIntervalIndex))
                DrawRowHeader(dc, row, rowHeaderWidth, columnHeaderHeight, HeaderHighlightBrush, pixelsPerDip);
        }
    }

    private void DrawColumnHeader(
        DrawingContext dc,
        ColMetric col,
        double rowHeaderWidth,
        double columnHeaderHeight,
        Brush background,
        double pixelsPerDip)
    {
        var rect = new Rect(col.LeftOffset + rowHeaderWidth, 0, col.Width, columnHeaderHeight);
        dc.DrawRectangle(background, GridPen, rect);

        var text = GetDefaultFormattedText(
            FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
            11,
            pixelsPerDip);

        dc.DrawText(text, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private void DrawRowHeader(
        DrawingContext dc,
        RowMetric row,
        double rowHeaderWidth,
        double columnHeaderHeight,
        Brush background,
        double pixelsPerDip)
    {
        var rect = new Rect(0, row.TopOffset + columnHeaderHeight, rowHeaderWidth, row.Height);
        dc.DrawRectangle(background, GridPen, rect);

        var text = GetDefaultFormattedText(
            row.Row.ToString(CultureInfo.InvariantCulture),
            11,
            pixelsPerDip);

        dc.DrawText(text, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private static IReadOnlyList<HeaderSelectionInterval> BuildColumnHeaderSelectionIntervals(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange) =>
        BuildHeaderSelectionIntervals(selectedRanges, selectedRange, static range => new HeaderSelectionInterval(range.Start.Col, range.End.Col));

    private static IReadOnlyList<HeaderSelectionInterval> BuildRowHeaderSelectionIntervals(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange) =>
        BuildHeaderSelectionIntervals(selectedRanges, selectedRange, static range => new HeaderSelectionInterval(range.Start.Row, range.End.Row));

    private static IReadOnlyList<HeaderSelectionInterval> BuildHeaderSelectionIntervals(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange,
        Func<GridRange, HeaderSelectionInterval> selector)
    {
        if (selectedRanges is { Count: > 0 })
        {
            if (selectedRanges.Count == 1)
                return [selector(selectedRanges[0])];

            var intervals = new List<HeaderSelectionInterval>(selectedRanges.Count);
            foreach (var range in selectedRanges)
                intervals.Add(selector(range));

            intervals.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            return intervals;
        }

        return selectedRange.HasValue
            ? [selector(selectedRange.Value)]
            : [];
    }

    private static bool IsHeaderSelected(
        uint index,
        IReadOnlyList<HeaderSelectionInterval> intervals,
        ref int intervalIndex)
    {
        while (intervalIndex < intervals.Count && index > intervals[intervalIndex].End)
            intervalIndex++;

        return intervalIndex < intervals.Count &&
            index >= intervals[intervalIndex].Start &&
            index <= intervals[intervalIndex].End;
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : ColumnHeaderCache.GetOrAdd(column, static col => CellAddress.NumberToColumnName(col));
}
