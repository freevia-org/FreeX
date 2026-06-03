using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private const int HeaderTextDrawingCacheLimit = 4096;
    private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();
    private static readonly ConcurrentDictionary<uint, string> RowHeaderCache = new();
    private readonly Dictionary<HeaderTextDrawingKey, DrawingGroup> _headerTextDrawingCache = new();
    private DrawingGroup? _headerBaseLayerCache;
    private HeaderBaseLayerCacheKey _headerBaseLayerCacheKey;
    private DrawingGroup? _selectedHeaderLayerCache;
    private SelectedHeaderLayerCacheKey _selectedHeaderLayerCacheKey;
    private SelectedHeaderLayerCacheKey _lastSelectedHeaderLayerRenderKey;
    private bool _hasLastSelectedHeaderLayerRenderKey;

    private readonly record struct HeaderBaseLayerCacheKey(
        ViewportModel Viewport,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        bool UseR1C1ReferenceStyle,
        string CultureName,
        double PixelsPerDip);

    private readonly record struct HeaderTextDrawingKey(
        string Text,
        string CultureName,
        double FontSize,
        double PixelsPerDip,
        double X,
        double Y);

    private readonly record struct SelectedHeaderLayerCacheKey(
        ViewportModel Viewport,
        IReadOnlyList<GridRange>? SelectedRanges,
        int SelectedRangeCount,
        long SelectedRangeSignature,
        GridRange? SelectedRange,
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
        RenderSelectedHeaderLayer(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);
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

    private void RenderSelectedHeaderLayer(
        DrawingContext dc,
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        if (selectedRanges is not { Count: > 0 } && selRange is null)
        {
            ClearSelectedHeaderLayerCache();
            return;
        }

        var key = CreateSelectedHeaderLayerCacheKey(
            viewport,
            selectedRanges,
            selRange,
            rowHeaderWidth,
            columnHeaderHeight,
            pixelsPerDip);
        if (_selectedHeaderLayerCache is { } cached && _selectedHeaderLayerCacheKey == key)
        {
            dc.DrawDrawing(cached);
            return;
        }

        if (_selectedHeaderLayerCache is not null)
            ClearSelectedHeaderLayerCache();

        if (ShouldBuildSelectedHeaderLayerCache(key))
        {
            var rebuilt = BuildSelectedHeaderLayerCache(
                viewport,
                selectedRanges,
                selRange,
                rowHeaderWidth,
                columnHeaderHeight,
                pixelsPerDip);
            _selectedHeaderLayerCache = rebuilt;
            _selectedHeaderLayerCacheKey = key;
            RememberSelectedHeaderLayerRenderKey(key);
            dc.DrawDrawing(rebuilt);
            return;
        }

        RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);
        RememberSelectedHeaderLayerRenderKey(key);
    }

    private SelectedHeaderLayerCacheKey CreateSelectedHeaderLayerCacheKey(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip) =>
        new(
            viewport,
            selectedRanges,
            selectedRanges?.Count ?? 0,
            selectedRanges is { Count: > 0 } ? CalculateGridRangeListSignature(selectedRanges) : 0,
            selRange,
            rowHeaderWidth,
            columnHeaderHeight,
            UseR1C1ReferenceStyle,
            CultureInfo.CurrentCulture.Name,
            pixelsPerDip);

    private bool ShouldBuildSelectedHeaderLayerCache(SelectedHeaderLayerCacheKey key) =>
        _hasLastSelectedHeaderLayerRenderKey && _lastSelectedHeaderLayerRenderKey == key;

    private void RememberSelectedHeaderLayerRenderKey(SelectedHeaderLayerCacheKey key)
    {
        _lastSelectedHeaderLayerRenderKey = key;
        _hasLastSelectedHeaderLayerRenderKey = true;
    }

    private DrawingGroup BuildSelectedHeaderLayerCache(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderSelectedHeaders(groupContext, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);

        if (group.CanFreeze)
            group.Freeze();

        return group;
    }

    private void ClearSelectedHeaderLayerCache()
    {
        _selectedHeaderLayerCache = null;
        _hasLastSelectedHeaderLayerRenderKey = false;
    }

    private static long CalculateGridRangeListSignature(IReadOnlyList<GridRange> ranges)
    {
        unchecked
        {
            var signature = 17L;
            foreach (var range in ranges)
            {
                signature = signature * 31 + range.Start.Row;
                signature = signature * 31 + range.Start.Col;
                signature = signature * 31 + range.End.Row;
                signature = signature * 31 + range.End.Col;
            }

            return signature;
        }
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

        if (TryRenderSingleCellSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip))
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

    private bool TryRenderSingleCellSelectedHeaders(
        DrawingContext dc,
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double pixelsPerDip)
    {
        if (!TryGetSingleCellSelectedHeaderRange(selectedRanges, selectedRange, out var range))
            return false;

        var lookups = GetRenderMetricLookups(viewport);
        if (lookups.Columns.TryGetValue(range.Start.Col, out var column))
            DrawColumnHeader(dc, column, rowHeaderWidth, columnHeaderHeight, HeaderHighlightBrush, pixelsPerDip);
        if (lookups.Rows.TryGetValue(range.Start.Row, out var row))
            DrawRowHeader(dc, row, rowHeaderWidth, columnHeaderHeight, HeaderHighlightBrush, pixelsPerDip);

        return true;
    }

    private static bool TryGetSingleCellSelectedHeaderRange(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange,
        out GridRange range)
    {
        if (selectedRanges is { Count: > 0 })
        {
            range = selectedRanges.Count == 1 ? selectedRanges[0] : default;
            return selectedRanges.Count == 1 && IsSingleCellRange(range);
        }

        range = selectedRange.GetValueOrDefault();
        return selectedRange.HasValue && IsSingleCellRange(range);
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

        var textValue = FormatColumnHeader(col.Col, UseR1C1ReferenceStyle);
        var text = GetDefaultFormattedText(textValue, 11, pixelsPerDip);

        DrawHeaderText(dc, textValue, text, 11, pixelsPerDip, new Point(
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

        var textValue = FormatRowHeader(row.Row);
        var text = GetDefaultFormattedText(textValue, 11, pixelsPerDip);

        DrawHeaderText(dc, textValue, text, 11, pixelsPerDip, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private void DrawHeaderText(
        DrawingContext dc,
        string text,
        FormattedText formattedText,
        double fontSize,
        double pixelsPerDip,
        Point origin)
    {
        var key = new HeaderTextDrawingKey(
            text,
            CultureInfo.CurrentCulture.Name,
            fontSize,
            pixelsPerDip,
            origin.X,
            origin.Y);
        if (!_headerTextDrawingCache.TryGetValue(key, out var drawing))
        {
            if (_headerTextDrawingCache.Count >= HeaderTextDrawingCacheLimit)
                _headerTextDrawingCache.Clear();

            var group = new DrawingGroup();
            using (var groupContext = group.Open())
                groupContext.DrawText(formattedText, origin);

            if (group.CanFreeze)
                group.Freeze();

            drawing = group;
            _headerTextDrawingCache.Add(key, drawing);
        }

        dc.DrawDrawing(drawing);
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

    internal static string FormatRowHeader(uint row) =>
        RowHeaderCache.GetOrAdd(row, static rowNumber => rowNumber.ToString(CultureInfo.InvariantCulture));
}
