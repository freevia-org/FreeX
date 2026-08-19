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
    // Stronger tint applied to the active cell's own row/column header within a
    // multi-cell selection, so the active cell stays visually locatable (matches Excel).
    // Mutable (not readonly): part of the HC-reactive chrome palette -- see
    // GridView.cs's ApplyHighContrastChromePalette.
    private static Brush ActiveHeaderHighlightBrush = MakeBrush(151, 181, 135);
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
        double VisibleBottom,
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
        double VisibleBottom,
        bool UseR1C1ReferenceStyle,
        string CultureName,
        double PixelsPerDip,
        CellAddress? ActiveCell);

    private readonly record struct HeaderSelectionInterval(uint Start, uint End);

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;
        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = FindLastRowMetricAtOrBefore(Viewport.RowMetrics, fp.Rows);
            double y = columnHeaderHeight + (lastFrozenRow != null ? lastFrozenRow.TopOffset + lastFrozenRow.Height : 0);
            dc.DrawLine(FreezePen, new Point(0, y), new Point(GetLogicalViewportWidth(), y));
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = FindLastColMetricAtOrBefore(Viewport.ColMetrics, fp.Cols);
            double x = rowHeaderWidth + (lastFrozenCol != null ? lastFrozenCol.LeftOffset + lastFrozenCol.Width : 0);
            dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, GetLogicalViewportHeight()));
        }
    }

    // The exact frozen-boundary row/column can be absent from RowMetrics/ColMetrics
    // (e.g. hidden without being a merge anchor, so BuildRowMetrics/BuildColMetrics
    // drops it entirely). Fall back to the nearest preceding entry so the freeze
    // divider still draws at the frozen block's actual visible extent, matching
    // Excel, instead of vanishing outright.
    internal static RowMetric? FindLastRowMetricAtOrBefore(IReadOnlyList<RowMetric> metrics, uint row)
    {
        RowMetric? result = null;
        foreach (var metric in metrics)
        {
            if (metric.Row > row)
                break;
            result = metric;
        }

        return result;
    }

    internal static ColMetric? FindLastColMetricAtOrBefore(IReadOnlyList<ColMetric> metrics, uint column)
    {
        ColMetric? result = null;
        foreach (var metric in metrics)
        {
            if (metric.Col > column)
                break;
            result = metric;
        }

        return result;
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
        var rowOutlineWidth = CalculateRowOutlineGutterWidth(viewport);
        var columnOutlineHeight = CalculateColumnOutlineGutterHeight(viewport);
        var visibleBottom = GetRenderVisibleBottom();

        RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);
        RenderSelectedHeaderLayer(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);
    }

    private void RenderHeaderBaseLayer(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        var key = new HeaderBaseLayerCacheKey(
            viewport,
            rowHeaderWidth,
            columnHeaderHeight,
            visibleBottom,
            UseR1C1ReferenceStyle,
            CultureInfo.CurrentCulture.Name,
            pixelsPerDip);
        if (_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key)
        {
            dc.DrawDrawing(cached);
            return;
        }

        var rebuilt = BuildHeaderBaseLayerCache(viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);
        _headerBaseLayerCache = rebuilt;
        _headerBaseLayerCacheKey = key;
        dc.DrawDrawing(rebuilt);
    }

    private DrawingGroup BuildHeaderBaseLayerCache(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);

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
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
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
            visibleBottom,
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
                rowOutlineWidth,
                columnOutlineHeight,
                visibleBottom,
                pixelsPerDip);
            _selectedHeaderLayerCache = rebuilt;
            _selectedHeaderLayerCacheKey = key;
            RememberSelectedHeaderLayerRenderKey(key);
            dc.DrawDrawing(rebuilt);
            return;
        }

        RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);
        RememberSelectedHeaderLayerRenderKey(key);
    }

    private SelectedHeaderLayerCacheKey CreateSelectedHeaderLayerCacheKey(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double visibleBottom,
        double pixelsPerDip) =>
        new(
            viewport,
            selectedRanges,
            selectedRanges?.Count ?? 0,
            selectedRanges is { Count: > 0 } ? CalculateGridRangeListSignature(selectedRanges) : 0,
            selRange,
            rowHeaderWidth,
            columnHeaderHeight,
            visibleBottom,
            UseR1C1ReferenceStyle,
            CultureInfo.CurrentCulture.Name,
            pixelsPerDip,
            ActiveCell ?? selRange?.Start);

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
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderSelectedHeaders(groupContext, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);

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
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, rowHeaderWidth, columnHeaderHeight));

        DrawOutlineHeaderSurfaces(dc, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight);

        // View>Split: the header gutter must show Excel's two stacked header blocks -- the
        // pinned TopRows/LeftColumns bands' own labels (at the un-shifted origin, exactly like
        // RenderSplitPaneCells draws their cell content) PLUS the main (BottomRight) pane's
        // labels, shifted past the divider so they stay aligned with that pane's now
        // divider-relative cell positions (see RenderCells in GridView.Rendering.cs). Route
        // through the same divider-layout helper the hit-test and cell renderer both use.
        var mainColumnOriginX = rowHeaderWidth;
        var mainRowOriginY = columnHeaderHeight;
        if (viewport.SplitPanes is { } splitPanes)
        {
            var dividerLayout = CalculateSplitDividerLayout(viewport);
            if (dividerLayout.VerticalX is { } verticalX)
                mainColumnOriginX = verticalX;
            if (dividerLayout.HorizontalY is { } horizontalY)
                mainRowOriginY = horizontalY;

            foreach (var col in splitPanes.LeftColumns ?? [])
                DrawColumnHeader(dc, col, rowHeaderWidth, columnOutlineHeight, HeaderBackgroundBrush, pixelsPerDip);

            foreach (var row in splitPanes.TopRows ?? [])
                DrawRowHeader(dc, row, rowHeaderWidth, rowOutlineWidth, columnHeaderHeight, visibleBottom, HeaderBackgroundBrush, pixelsPerDip);
        }

        foreach (var col in viewport.ColMetrics)
            DrawColumnHeader(dc, col, mainColumnOriginX, columnOutlineHeight, HeaderBackgroundBrush, pixelsPerDip);

        foreach (var row in viewport.RowMetrics)
            DrawRowHeader(dc, row, rowHeaderWidth, rowOutlineWidth, mainRowOriginY, visibleBottom, HeaderBackgroundBrush, pixelsPerDip);

        DrawOutlineGroups(dc, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip);
    }

    private static void DrawOutlineHeaderSurfaces(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight)
    {
        if (columnOutlineHeight > 0)
        {
            foreach (var column in viewport.ColMetrics)
            {
                dc.DrawRectangle(
                    HeaderBackgroundBrush,
                    GridPen,
                    new Rect(rowHeaderWidth + column.LeftOffset, 0, column.Width, columnOutlineHeight));
            }
        }

        if (rowOutlineWidth > 0)
        {
            foreach (var row in viewport.RowMetrics)
            {
                dc.DrawRectangle(
                    HeaderBackgroundBrush,
                    GridPen,
                    new Rect(0, columnHeaderHeight + row.TopOffset, rowOutlineWidth, row.Height));
            }
        }
    }

    private void RenderSelectedHeaders(
        DrawingContext dc,
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        if (selectedRanges is not { Count: > 0 } && selRange is null)
            return;

        if (TryRenderSingleCellSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, visibleBottom, pixelsPerDip))
            return;

        var columnIntervals = BuildColumnHeaderSelectionIntervals(selectedRanges, selRange);
        var rowIntervals = BuildRowHeaderSelectionIntervals(selectedRanges, selRange);
        var columnIntervalIndex = 0;
        var rowIntervalIndex = 0;
        var activeCell = ActiveCell ?? selRange?.Start;

        foreach (var col in viewport.ColMetrics)
        {
            if (IsHeaderSelected(col.Col, columnIntervals, ref columnIntervalIndex))
            {
                var brush = activeCell is { } active && active.Col == col.Col ? ActiveHeaderHighlightBrush : HeaderHighlightBrush;
                DrawColumnHeader(dc, col, rowHeaderWidth, columnOutlineHeight, brush, pixelsPerDip);
            }
        }

        foreach (var row in viewport.RowMetrics)
        {
            if (IsHeaderSelected(row.Row, rowIntervals, ref rowIntervalIndex))
            {
                var brush = activeCell is { } active && active.Row == row.Row ? ActiveHeaderHighlightBrush : HeaderHighlightBrush;
                DrawRowHeader(dc, row, rowHeaderWidth, rowOutlineWidth, columnHeaderHeight, visibleBottom, brush, pixelsPerDip);
            }
        }
    }

    private bool TryRenderSingleCellSelectedHeaders(
        DrawingContext dc,
        ViewportModel viewport,
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? selectedRange,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        if (!TryGetSingleCellSelectedHeaderRange(selectedRanges, selectedRange, out var range))
            return false;

        var lookups = GetRenderMetricLookups(viewport);
        if (lookups.Columns.TryGetValue(range.Start.Col, out var column))
            DrawColumnHeader(dc, column, rowHeaderWidth, CalculateColumnOutlineGutterHeight(viewport), HeaderHighlightBrush, pixelsPerDip);
        if (lookups.Rows.TryGetValue(range.Start.Row, out var row))
            DrawRowHeader(dc, row, rowHeaderWidth, CalculateRowOutlineGutterWidth(viewport), columnHeaderHeight, visibleBottom, HeaderHighlightBrush, pixelsPerDip);

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
        double columnOutlineHeight,
        Brush background,
        double pixelsPerDip)
    {
        var rect = new Rect(col.LeftOffset + rowHeaderWidth, columnOutlineHeight, col.Width, ColHeaderHeight);
        dc.DrawRectangle(background, GridPen, rect);
        if (!ShouldDrawColumnHeaderText(rect))
            return;

        var textValue = FormatColumnHeader(col.Col, UseR1C1ReferenceStyle);
        var text = GetDefaultHeaderFormattedText(textValue, 11, pixelsPerDip);

        DrawHeaderText(dc, textValue, text, 11, pixelsPerDip, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private void DrawRowHeader(
        DrawingContext dc,
        RowMetric row,
        double rowHeaderWidth,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double visibleBottom,
        Brush background,
        double pixelsPerDip)
    {
        var rect = new Rect(rowOutlineWidth, row.TopOffset + columnHeaderHeight, Math.Max(0, rowHeaderWidth - rowOutlineWidth), row.Height);
        dc.DrawRectangle(background, GridPen, rect);
        if (!ShouldDrawRowHeaderText(rect, visibleBottom))
            return;

        var textValue = FormatRowHeader(row.Row);
        var text = GetDefaultHeaderFormattedText(textValue, 11, pixelsPerDip);

        DrawHeaderText(dc, textValue, text, 11, pixelsPerDip, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private void DrawOutlineGroups(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        if (rowOutlineWidth > 0)
        {
            DrawRowOutlineLevelButtons(dc, viewport.RowOutlineGroups, rowOutlineWidth, columnHeaderHeight, columnOutlineHeight, pixelsPerDip);
            DrawRowOutlineGroups(dc, viewport, rowOutlineWidth, columnHeaderHeight, visibleBottom, pixelsPerDip);
        }

        if (columnOutlineHeight > 0)
        {
            DrawColumnOutlineLevelButtons(dc, viewport.ColumnOutlineGroups, rowHeaderWidth, rowOutlineWidth, columnOutlineHeight, pixelsPerDip);
            DrawColumnOutlineGroups(dc, viewport, rowHeaderWidth, columnOutlineHeight, pixelsPerDip);
        }
    }

    private void DrawRowOutlineGroups(
        DrawingContext dc,
        ViewportModel viewport,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        var groups = viewport.RowOutlineGroups;
        if (groups is not { Count: > 0 })
            return;

        var lookups = GetRenderMetricLookups(viewport);
        foreach (var group in groups)
        {
            var centerX = GetRowOutlineLevelCenter(rowOutlineWidth, group.Level);
            if (TryGetRowOutlineSpan(viewport.RowMetrics, group, columnHeaderHeight, visibleBottom, out var top, out var bottom))
                DrawRowOutlineBracket(dc, centerX, top, bottom);

            if (lookups.Rows.TryGetValue(group.ToggleIndex, out var toggleRow))
            {
                var center = new Point(centerX, columnHeaderHeight + toggleRow.TopOffset + toggleRow.Height / 2);
                DrawOutlineToggleButton(dc, center, group.IsCollapsed, pixelsPerDip);
            }
        }
    }

    private void DrawColumnOutlineGroups(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnOutlineHeight,
        double pixelsPerDip)
    {
        var groups = viewport.ColumnOutlineGroups;
        if (groups is not { Count: > 0 })
            return;

        var lookups = GetRenderMetricLookups(viewport);
        foreach (var group in groups)
        {
            var centerY = GetColumnOutlineLevelCenter(columnOutlineHeight, group.Level);
            if (TryGetColumnOutlineSpan(viewport.ColMetrics, group, rowHeaderWidth, out var left, out var right))
                DrawColumnOutlineBracket(dc, centerY, left, right);

            if (lookups.Columns.TryGetValue(group.ToggleIndex, out var toggleColumn))
            {
                var center = new Point(rowHeaderWidth + toggleColumn.LeftOffset + toggleColumn.Width / 2, centerY);
                DrawOutlineToggleButton(dc, center, group.IsCollapsed, pixelsPerDip);
            }
        }
    }

    private void DrawRowOutlineLevelButtons(
        DrawingContext dc,
        IReadOnlyList<OutlineGroupRange>? groups,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double columnOutlineHeight,
        double pixelsPerDip)
    {
        var maxLevel = GetMaxOutlineLevel(groups);
        if (maxLevel <= 0)
            return;

        var top = columnOutlineHeight > 0
            ? Math.Max(1, columnOutlineHeight - OutlineButtonSize - 2)
            : Math.Max(1, (columnHeaderHeight - OutlineButtonSize) / 2);
        for (var level = 1; level <= maxLevel; level++)
        {
            var center = new Point(GetRowOutlineLevelCenter(rowOutlineWidth, level), top + OutlineButtonSize / 2);
            DrawOutlineLevelButton(dc, center, level, pixelsPerDip);
        }
    }

    private void DrawColumnOutlineLevelButtons(
        DrawingContext dc,
        IReadOnlyList<OutlineGroupRange>? groups,
        double rowHeaderWidth,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double pixelsPerDip)
    {
        var maxLevel = GetMaxOutlineLevel(groups);
        if (maxLevel <= 0)
            return;

        var left = rowOutlineWidth > 0
            ? Math.Max(1, rowOutlineWidth - OutlineButtonSize - 2)
            : Math.Max(1, (rowHeaderWidth - OutlineButtonSize) / 2);
        for (var level = 1; level <= maxLevel; level++)
        {
            var center = new Point(left + OutlineButtonSize / 2, GetColumnOutlineLevelCenter(columnOutlineHeight, level));
            DrawOutlineLevelButton(dc, center, level, pixelsPerDip);
        }
    }

    private static void DrawRowOutlineBracket(DrawingContext dc, double x, double top, double bottom)
    {
        if (bottom <= top)
            return;

        var tickEnd = x + 5;
        var topY = top + 3;
        var bottomY = bottom - 3;
        dc.DrawLine(OutlineGlyphPen, new Point(x, topY), new Point(x, bottomY));
        dc.DrawLine(OutlineGlyphPen, new Point(x, topY), new Point(tickEnd, topY));
        dc.DrawLine(OutlineGlyphPen, new Point(x, bottomY), new Point(tickEnd, bottomY));
    }

    private static void DrawColumnOutlineBracket(DrawingContext dc, double y, double left, double right)
    {
        if (right <= left)
            return;

        var tickEnd = y + 5;
        var leftX = left + 3;
        var rightX = right - 3;
        dc.DrawLine(OutlineGlyphPen, new Point(leftX, y), new Point(rightX, y));
        dc.DrawLine(OutlineGlyphPen, new Point(leftX, y), new Point(leftX, tickEnd));
        dc.DrawLine(OutlineGlyphPen, new Point(rightX, y), new Point(rightX, tickEnd));
    }

    private void DrawOutlineLevelButton(DrawingContext dc, Point center, int level, double pixelsPerDip)
    {
        var rect = CreateOutlineButtonRect(center);
        dc.DrawRectangle(OutlineButtonBrush, OutlineButtonPen, rect);
        var textValue = level.ToString(CultureInfo.InvariantCulture);
        var text = GetDefaultHeaderFormattedText(textValue, 9, pixelsPerDip);
        DrawHeaderText(dc, textValue, text, 9, pixelsPerDip, new Point(
            rect.Left + (rect.Width - text.Width) / 2,
            rect.Top + (rect.Height - text.Height) / 2));
    }

    private static void DrawOutlineToggleButton(DrawingContext dc, Point center, bool isCollapsed, double pixelsPerDip)
    {
        var rect = CreateOutlineButtonRect(center);
        dc.DrawRectangle(OutlineButtonBrush, OutlineButtonPen, rect);

        var midY = rect.Top + rect.Height / 2;
        dc.DrawLine(OutlineGlyphPen, new Point(rect.Left + 3, midY), new Point(rect.Right - 3, midY));
        if (isCollapsed)
        {
            var midX = rect.Left + rect.Width / 2;
            dc.DrawLine(OutlineGlyphPen, new Point(midX, rect.Top + 3), new Point(midX, rect.Bottom - 3));
        }
    }

    private static bool TryGetRowOutlineSpan(
        IReadOnlyList<RowMetric> rows,
        OutlineGroupRange group,
        double columnHeaderHeight,
        double visibleBottom,
        out double top,
        out double bottom)
    {
        top = 0;
        bottom = 0;
        var found = false;
        foreach (var row in rows)
        {
            if (row.Row < group.Start)
                continue;
            if (row.Row > group.End)
                break;

            var rowTop = columnHeaderHeight + row.TopOffset;
            var rowBottom = rowTop + row.Height;
            if (rowTop >= visibleBottom)
                break;

            if (!found)
            {
                top = rowTop;
                found = true;
            }

            bottom = Math.Min(rowBottom, visibleBottom);
        }

        return found && bottom > top;
    }

    private static bool TryGetColumnOutlineSpan(
        IReadOnlyList<ColMetric> columns,
        OutlineGroupRange group,
        double rowHeaderWidth,
        out double left,
        out double right)
    {
        left = 0;
        right = 0;
        var found = false;
        foreach (var column in columns)
        {
            if (column.Col < group.Start)
                continue;
            if (column.Col > group.End)
                break;

            var columnLeft = rowHeaderWidth + column.LeftOffset;
            if (!found)
            {
                left = columnLeft;
                found = true;
            }

            right = columnLeft + column.Width;
        }

        return found && right > left;
    }

    internal static double CalculateRowOutlineGutterWidth(ViewportModel? viewport)
    {
        var maxLevel = GetMaxOutlineLevel(viewport?.RowOutlineGroups);
        return maxLevel <= 0 ? 0 : OutlineGutterPadding * 2 + maxLevel * OutlineLevelPitch;
    }

    internal static double CalculateColumnOutlineGutterHeight(ViewportModel? viewport)
    {
        var maxLevel = GetMaxOutlineLevel(viewport?.ColumnOutlineGroups);
        return maxLevel <= 0 ? 0 : OutlineGutterPadding * 2 + maxLevel * OutlineLevelPitch;
    }

    private static int GetMaxOutlineLevel(IReadOnlyList<OutlineGroupRange>? groups)
    {
        if (groups is not { Count: > 0 })
            return 0;

        var maxLevel = 0;
        foreach (var group in groups)
        {
            if (group.Level > maxLevel)
                maxLevel = group.Level;
        }

        return maxLevel;
    }

    private static double GetRowOutlineLevelCenter(double rowOutlineWidth, int level) =>
        OutlineGutterPadding + (Math.Max(1, level) - 0.5) * OutlineLevelPitch;

    private static double GetColumnOutlineLevelCenter(double columnOutlineHeight, int level) =>
        OutlineGutterPadding + (Math.Max(1, level) - 0.5) * OutlineLevelPitch;

    private static Rect CreateOutlineButtonRect(Point center) =>
        new(
            center.X - OutlineButtonSize / 2,
            center.Y - OutlineButtonSize / 2,
            OutlineButtonSize,
            OutlineButtonSize);

    internal static bool TryHitTestOutlineGroupToggle(
        ViewportModel? viewport,
        Point position,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out GridOutlineGroupToggleRequest request)
    {
        request = default;
        if (viewport is null)
            return false;

        var rowOutlineWidth = CalculateRowOutlineGutterWidth(viewport);
        if (rowOutlineWidth > 0 &&
            position.X <= rowOutlineWidth &&
            TryHitTestRowOutlineGroupToggle(viewport, position, rowOutlineWidth, columnHeaderHeight, out request))
        {
            return true;
        }

        var columnOutlineHeight = CalculateColumnOutlineGutterHeight(viewport);
        return columnOutlineHeight > 0 &&
            position.Y <= columnOutlineHeight &&
            TryHitTestColumnOutlineGroupToggle(viewport, position, rowHeaderWidth, columnOutlineHeight, out request);
    }

    private static bool TryHitTestRowOutlineGroupToggle(
        ViewportModel viewport,
        Point position,
        double rowOutlineWidth,
        double columnHeaderHeight,
        out GridOutlineGroupToggleRequest request)
    {
        request = default;
        var groups = viewport.RowOutlineGroups;
        if (groups is not { Count: > 0 })
            return false;

        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            var group = groups[groupIndex];
            var row = FindRowMetric(viewport.RowMetrics, group.ToggleIndex);
            if (row is null)
                continue;

            var center = new Point(
                GetRowOutlineLevelCenter(rowOutlineWidth, group.Level),
                columnHeaderHeight + row.TopOffset + row.Height / 2);
            if (!CreateOutlineButtonRect(center).Contains(position))
                continue;

            request = new GridOutlineGroupToggleRequest(
                GridOutlineGroupAxis.Rows,
                group.Level,
                group.Start,
                group.End,
                Collapse: !group.IsCollapsed);
            return true;
        }

        return false;
    }

    private static bool TryHitTestColumnOutlineGroupToggle(
        ViewportModel viewport,
        Point position,
        double rowHeaderWidth,
        double columnOutlineHeight,
        out GridOutlineGroupToggleRequest request)
    {
        request = default;
        var groups = viewport.ColumnOutlineGroups;
        if (groups is not { Count: > 0 })
            return false;

        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            var group = groups[groupIndex];
            var column = FindColMetric(viewport.ColMetrics, group.ToggleIndex);
            if (column is null)
                continue;

            var center = new Point(
                rowHeaderWidth + column.LeftOffset + column.Width / 2,
                GetColumnOutlineLevelCenter(columnOutlineHeight, group.Level));
            if (!CreateOutlineButtonRect(center).Contains(position))
                continue;

            request = new GridOutlineGroupToggleRequest(
                GridOutlineGroupAxis.Columns,
                group.Level,
                group.Start,
                group.End,
                Collapse: !group.IsCollapsed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hit-tests the numbered "Show Outline Level N" gutter buttons drawn by
    /// <see cref="DrawRowOutlineLevelButtons"/>/<see cref="DrawColumnOutlineLevelButtons"/>. This
    /// mirrors <see cref="TryHitTestOutlineGroupToggle"/> for the per-group +/- toggle boxes; the
    /// two hit tests are deliberately separate because the level buttons live at fixed positions
    /// per axis (one row/column of boxes near the corner) rather than one box per outline group.
    /// </summary>
    internal static bool TryHitTestOutlineLevelButton(
        ViewportModel? viewport,
        Point position,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out GridOutlineLevelButtonRequest request)
    {
        request = default;
        if (viewport is null)
            return false;

        var rowOutlineWidth = CalculateRowOutlineGutterWidth(viewport);
        var columnOutlineHeight = CalculateColumnOutlineGutterHeight(viewport);

        if (rowOutlineWidth > 0 &&
            position.X <= rowOutlineWidth &&
            TryHitTestRowOutlineLevelButton(
                viewport.RowOutlineGroups, position, rowOutlineWidth, columnHeaderHeight, columnOutlineHeight, out var rowLevel))
        {
            request = new GridOutlineLevelButtonRequest(GridOutlineGroupAxis.Rows, rowLevel);
            return true;
        }

        if (columnOutlineHeight > 0 &&
            position.Y <= columnOutlineHeight &&
            TryHitTestColumnOutlineLevelButton(
                viewport.ColumnOutlineGroups, position, rowHeaderWidth, rowOutlineWidth, columnOutlineHeight, out var columnLevel))
        {
            request = new GridOutlineLevelButtonRequest(GridOutlineGroupAxis.Columns, columnLevel);
            return true;
        }

        return false;
    }

    private static bool TryHitTestRowOutlineLevelButton(
        IReadOnlyList<OutlineGroupRange>? groups,
        Point position,
        double rowOutlineWidth,
        double columnHeaderHeight,
        double columnOutlineHeight,
        out int level)
    {
        level = 0;
        var maxLevel = GetMaxOutlineLevel(groups);
        if (maxLevel <= 0)
            return false;

        var top = columnOutlineHeight > 0
            ? Math.Max(1, columnOutlineHeight - OutlineButtonSize - 2)
            : Math.Max(1, (columnHeaderHeight - OutlineButtonSize) / 2);
        for (var candidateLevel = 1; candidateLevel <= maxLevel; candidateLevel++)
        {
            var center = new Point(GetRowOutlineLevelCenter(rowOutlineWidth, candidateLevel), top + OutlineButtonSize / 2);
            if (!CreateOutlineButtonRect(center).Contains(position))
                continue;

            level = candidateLevel;
            return true;
        }

        return false;
    }

    private static bool TryHitTestColumnOutlineLevelButton(
        IReadOnlyList<OutlineGroupRange>? groups,
        Point position,
        double rowHeaderWidth,
        double rowOutlineWidth,
        double columnOutlineHeight,
        out int level)
    {
        level = 0;
        var maxLevel = GetMaxOutlineLevel(groups);
        if (maxLevel <= 0)
            return false;

        var left = rowOutlineWidth > 0
            ? Math.Max(1, rowOutlineWidth - OutlineButtonSize - 2)
            : Math.Max(1, (rowHeaderWidth - OutlineButtonSize) / 2);
        for (var candidateLevel = 1; candidateLevel <= maxLevel; candidateLevel++)
        {
            var center = new Point(left + OutlineButtonSize / 2, GetColumnOutlineLevelCenter(columnOutlineHeight, candidateLevel));
            if (!CreateOutlineButtonRect(center).Contains(position))
                continue;

            level = candidateLevel;
            return true;
        }

        return false;
    }

    internal static bool ShouldDrawRowHeaderText(Rect rowHeaderRect, double visibleBottom) =>
        rowHeaderRect.Height > 0 && rowHeaderRect.Bottom <= visibleBottom;

    // A hidden merge-anchor column is still kept in ColMetrics with Width=0 so the
    // merge's value/style stay reachable for cell rendering; its header slot must
    // stay visually empty (matching Excel), not draw the column letter centered on
    // a zero-width rect where it bleeds into the neighboring header cell.
    internal static bool ShouldDrawColumnHeaderText(Rect columnHeaderRect) =>
        columnHeaderRect.Width > 0;

    private double GetRenderVisibleBottom()
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        return ActualHeight / zoom;
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
