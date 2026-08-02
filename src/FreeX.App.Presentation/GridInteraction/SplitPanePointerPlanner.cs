using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Portable pointer geometry for Window &gt; Split. WPF and Avalonia use different pointer and
/// rectangle types, but the worksheet boundary math and scrollbar hit zones must stay identical.
/// </summary>
public static class SplitPanePointerPlanner
{
    public const double DividerHitZone = 4;
    public const double ScrollbarThickness = 10;
    public const double ScrollbarMinThumbLength = 24;
    public const uint DefaultWheelScrollStep = 3;

    public static SplitPanePointerDividerLayout CalculateDividerLayout(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double metricScale = 1)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        var scale = metricScale > 0 ? metricScale : 1;
        double? horizontalY = null;
        double? verticalX = null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            if (splitPanes.Row is { } splitRow)
            {
                var pinnedRows = splitPanes.TopRows ?? [];
                horizontalY = pinnedRows.Count > 0
                    ? columnHeaderHeight + SumRowHeights(pinnedRows) * scale
                    : FindRowMetric(viewport.RowMetrics, splitRow)?.TopOffset * scale + columnHeaderHeight;
            }

            if (splitPanes.Column is { } splitColumn)
            {
                var pinnedColumns = splitPanes.LeftColumns ?? [];
                verticalX = pinnedColumns.Count > 0
                    ? rowHeaderWidth + SumColumnWidths(pinnedColumns) * scale
                    : FindColumnMetric(viewport.ColMetrics, splitColumn)?.LeftOffset * scale + rowHeaderWidth;
            }
        }

        return new SplitPanePointerDividerLayout(horizontalY, verticalX);
    }

    public static SplitPanePointerHandle HitTestDivider(
        ViewportModel viewport,
        GridPoint position,
        double actualWidth,
        double actualHeight,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double metricScale = 1)
    {
        var layout = CalculateDividerLayout(viewport, rowHeaderWidth, columnHeaderHeight, metricScale);
        var hitZone = DividerHitZone * (metricScale > 0 ? metricScale : 1);
        var onHorizontal = layout.HorizontalY is { } horizontalY &&
            position.X >= 0 && position.X <= actualWidth &&
            Math.Abs(position.Y - horizontalY) <= hitZone;
        var onVertical = layout.VerticalX is { } verticalX &&
            position.Y >= 0 && position.Y <= actualHeight &&
            Math.Abs(position.X - verticalX) <= hitZone;

        return (onHorizontal, onVertical) switch
        {
            (true, true) => SplitPanePointerHandle.Intersection,
            (true, false) => SplitPanePointerHandle.Horizontal,
            (false, true) => SplitPanePointerHandle.Vertical,
            _ => SplitPanePointerHandle.None,
        };
    }

    public static SplitPanePointerDividerDragTarget? CalculateDividerDragTarget(
        ViewportModel viewport,
        SplitPanePointerHandle handle,
        GridPoint position,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double metricScale = 1)
    {
        if (handle == SplitPanePointerHandle.None || viewport.SplitPanes is not { } splitPanes)
            return null;

        var scale = metricScale > 0 ? metricScale : 1;
        uint? row = handle is SplitPanePointerHandle.Horizontal or SplitPanePointerHandle.Intersection
            ? FindSplitRow(splitPanes.TopRows ?? [], viewport.RowMetrics, position.Y, columnHeaderHeight, scale)
            : null;
        uint? column = handle is SplitPanePointerHandle.Vertical or SplitPanePointerHandle.Intersection
            ? FindSplitColumn(splitPanes.LeftColumns ?? [], viewport.ColMetrics, position.X, rowHeaderWidth, scale)
            : null;

        return new SplitPanePointerDividerDragTarget(row, column);
    }

    public static SplitPanePointerScrollbarChrome CalculateScrollbarChrome(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double metricScale = 1)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return new SplitPanePointerScrollbarChrome(null, null);

        var scale = metricScale > 0 ? metricScale : 1;
        var divider = CalculateDividerLayout(viewport, rowHeaderWidth, columnHeaderHeight, scale);
        var thickness = ScrollbarThickness * scale;
        SplitPanePointerScrollbar? horizontal = null;
        SplitPanePointerScrollbar? vertical = null;
        var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;

        if (divider.HorizontalY is { } horizontalY &&
            divider.VerticalX is { } verticalX &&
            topRightColumns.Count > 0 && actualWidth > verticalX)
        {
            var track = new GridRect(
                verticalX,
                Math.Max(columnHeaderHeight, horizontalY - thickness),
                Math.Max(0, actualWidth - verticalX),
                thickness);
            var visibleSpan = Math.Max(1, topRightColumns.Count);
            var maxStartIndex = Math.Max(1, CellAddress.MaxCol - (uint)visibleSpan + 1);
            horizontal = new SplitPanePointerScrollbar(
                SplitPanePointerScrollbarOrientation.Horizontal,
                SplitPanePointerRegion.TopRight,
                track,
                CalculateThumb(track, true, topRightColumns[0].Col, visibleSpan, CellAddress.MaxCol),
                visibleSpan,
                maxStartIndex);
        }

        if (divider.HorizontalY is { } bottomY &&
            divider.VerticalX is { } leftX &&
            bottomLeftRows.Count > 0 && actualHeight > bottomY)
        {
            var track = new GridRect(
                Math.Max(rowHeaderWidth, leftX - thickness),
                bottomY,
                thickness,
                Math.Max(0, actualHeight - bottomY));
            var visibleSpan = Math.Max(1, bottomLeftRows.Count);
            var maxStartIndex = Math.Max(1, CellAddress.MaxRow - (uint)visibleSpan + 1);
            vertical = new SplitPanePointerScrollbar(
                SplitPanePointerScrollbarOrientation.Vertical,
                SplitPanePointerRegion.BottomLeft,
                track,
                CalculateThumb(track, false, bottomLeftRows[0].Row, visibleSpan, CellAddress.MaxRow),
                visibleSpan,
                maxStartIndex);
        }

        return new SplitPanePointerScrollbarChrome(horizontal, vertical);
    }

    public static SplitPanePointerScrollbarHit? HitTestScrollbar(
        SplitPanePointerScrollbarChrome chrome,
        GridPoint position)
    {
        if (chrome.HorizontalTopRight is { } horizontal && IsInside(horizontal.Track, position))
            return new SplitPanePointerScrollbarHit(
                IsInside(horizontal.Thumb, position)
                    ? SplitPanePointerScrollbarPart.Thumb
                    : SplitPanePointerScrollbarPart.Track,
                horizontal.Orientation,
                horizontal.Region);

        if (chrome.VerticalBottomLeft is { } vertical && IsInside(vertical.Track, position))
            return new SplitPanePointerScrollbarHit(
                IsInside(vertical.Thumb, position)
                    ? SplitPanePointerScrollbarPart.Thumb
                    : SplitPanePointerScrollbarPart.Track,
                vertical.Orientation,
                vertical.Region);

        return null;
    }

    public static SplitPanePointerScrollTarget CalculateThumbDragTarget(
        SplitPanePointerScrollbar scrollbar,
        GridPoint position,
        double pointerOffset)
    {
        var trackPosition = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? position.X
            : position.Y;
        var trackStart = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Track.Left
            : scrollbar.Track.Top;
        var trackLength = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Track.Width
            : scrollbar.Track.Height;
        var thumbLength = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Width
            : scrollbar.Thumb.Height;
        var available = Math.Max(1, trackLength - 2 - thumbLength);
        var ratio = Math.Clamp((trackPosition - pointerOffset - trackStart - 1) / available, 0, 1);
        var index = 1 + (long)Math.Round(ratio * (scrollbar.MaxStartIndex - 1));
        return new SplitPanePointerScrollTarget(
            scrollbar.Region,
            scrollbar.Orientation,
            ClampStartIndex(scrollbar.MaxStartIndex, index));
    }

    public static SplitPanePointerScrollTarget CalculateTrackTarget(
        SplitPanePointerScrollbar scrollbar,
        GridPoint position)
    {
        var trackPosition = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? position.X
            : position.Y;
        var trackStart = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Track.Left
            : scrollbar.Track.Top;
        var trackLength = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Track.Width
            : scrollbar.Track.Height;
        var thumbLength = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Width
            : scrollbar.Thumb.Height;
        var available = Math.Max(1, trackLength - 2 - thumbLength);
        var ratio = Math.Clamp((trackPosition - trackStart - 1) / available, 0, 1);
        var index = 1 + (long)Math.Round(ratio * (scrollbar.MaxStartIndex - 1));
        return new SplitPanePointerScrollTarget(
            scrollbar.Region,
            scrollbar.Orientation,
            ClampStartIndex(scrollbar.MaxStartIndex, index));
    }

    /// <summary>
    /// Returns the page-step target used when the scrollbar track is clicked outside its thumb.
    /// This mirrors the WPF split-pane scrollbar contract; a track click advances by one visible
    /// pane rather than jumping the thumb directly under the pointer.
    /// </summary>
    public static SplitPanePointerScrollTarget CalculatePageTarget(
        SplitPanePointerScrollbar scrollbar,
        uint currentIndex,
        GridPoint position)
    {
        var pointer = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? position.X
            : position.Y;
        var thumbStart = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Left
            : scrollbar.Thumb.Top;
        var thumbEnd = scrollbar.Orientation == SplitPanePointerScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Right
            : scrollbar.Thumb.Bottom;
        var direction = pointer < thumbStart ? -1L : pointer > thumbEnd ? 1L : 0L;
        var next = (long)Math.Max(1, currentIndex) + direction * Math.Max(1, scrollbar.VisibleSpan);
        return new SplitPanePointerScrollTarget(
            scrollbar.Region,
            scrollbar.Orientation,
            ClampStartIndex(scrollbar.MaxStartIndex, next));
    }

    public static SplitPanePointerScrollTarget CalculateWheelTarget(
        SplitPanePointerScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = DefaultWheelScrollStep)
    {
        var next = (long)Math.Max(1, currentIndex) - (long)notches * step;
        return new SplitPanePointerScrollTarget(
            scrollbar.Region,
            scrollbar.Orientation,
            ClampStartIndex(scrollbar.MaxStartIndex, next));
    }

    public static SplitPanePointerWheelTarget ResolveWheelTarget(
        ViewportModel viewport,
        GridPoint position,
        double actualWidth,
        double actualHeight,
        double rowHeaderWidth,
        double columnHeaderHeight,
        bool requestedHorizontal,
        double metricScale = 1)
    {
        var chrome = CalculateScrollbarChrome(
            viewport,
            actualWidth,
            actualHeight,
            rowHeaderWidth,
            columnHeaderHeight,
            metricScale);
        if (HitTestScrollbar(chrome, position) is { } scrollbarHit)
        {
            return new SplitPanePointerWheelTarget(
                scrollbarHit.Region,
                scrollbarHit.Orientation == SplitPanePointerScrollbarOrientation.Horizontal);
        }

        var region = viewport.SplitPanes is not null &&
            position.X >= rowHeaderWidth && position.Y >= columnHeaderHeight
                ? HitTestRegion(CalculateDividerLayout(viewport, rowHeaderWidth, columnHeaderHeight, metricScale), position)
                : SplitPanePointerRegion.BottomRight;
        return new SplitPanePointerWheelTarget(region, requestedHorizontal);
    }

    public static bool CanScroll(SplitPanePointerRegion region, bool horizontal) =>
        horizontal
            ? region is SplitPanePointerRegion.TopRight or SplitPanePointerRegion.BottomRight
            : region is SplitPanePointerRegion.BottomLeft or SplitPanePointerRegion.BottomRight;

    private static GridRect CalculateThumb(
        GridRect track,
        bool horizontal,
        uint firstVisibleIndex,
        int visibleCount,
        uint maxIndex)
    {
        var trackLength = horizontal ? track.Width : track.Height;
        var effectiveMaxIndex = Math.Max(1, maxIndex);
        var effectiveVisibleCount = Math.Min(effectiveMaxIndex, (uint)Math.Max(1, visibleCount));
        var thumbLength = Math.Min(
            trackLength,
            Math.Max(ScrollbarMinThumbLength, trackLength * effectiveVisibleCount / effectiveMaxIndex));
        var available = Math.Max(0, trackLength - 2 - thumbLength);
        var maxStartIndex = effectiveMaxIndex - effectiveVisibleCount + 1;
        var clamped = Math.Min(maxStartIndex, Math.Max(1, firstVisibleIndex));
        var ratio = maxStartIndex <= 1 ? 0 : (double)(clamped - 1) / (maxStartIndex - 1);
        if (horizontal)
            return new GridRect(track.Left + 1 + available * ratio, track.Top + 1, thumbLength, Math.Max(0, track.Height - 2));
        return new GridRect(track.Left + 1, track.Top + 1 + available * ratio, Math.Max(0, track.Width - 2), thumbLength);
    }

    private static SplitPanePointerRegion HitTestRegion(SplitPanePointerDividerLayout layout, GridPoint position)
    {
        var isTop = layout.HorizontalY.HasValue && position.Y < layout.HorizontalY.Value;
        var isLeft = layout.VerticalX.HasValue && position.X < layout.VerticalX.Value;
        return (isTop, isLeft) switch
        {
            (true, true) => SplitPanePointerRegion.TopLeft,
            (true, false) => SplitPanePointerRegion.TopRight,
            (false, true) => SplitPanePointerRegion.BottomLeft,
            _ => SplitPanePointerRegion.BottomRight,
        };
    }

    private static uint? FindSplitRow(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<RowMetric> mainRows,
        double y,
        double headerHeight,
        double scale)
    {
        var topHeight = SumRowHeights(topRows) * scale;
        if (y < headerHeight)
            return null;
        if (y <= headerHeight + topHeight)
        {
            foreach (var row in topRows)
            {
                var bottom = headerHeight + (row.TopOffset + row.Height) * scale;
                if (y <= bottom)
                    return IncrementWithinLimit(row.Row, CellAddress.MaxRow);
            }
        }
        foreach (var row in mainRows)
        {
            var top = headerHeight + topHeight + row.TopOffset * scale;
            if (y < top)
                break;
            if (y <= top + row.Height * scale)
                return row.Row;
        }
        return null;
    }

    private static uint? FindSplitColumn(
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<ColMetric> mainColumns,
        double x,
        double headerWidth,
        double scale)
    {
        var leftWidth = SumColumnWidths(leftColumns) * scale;
        if (x < headerWidth)
            return null;
        if (x <= headerWidth + leftWidth)
        {
            foreach (var column in leftColumns)
            {
                var right = headerWidth + (column.LeftOffset + column.Width) * scale;
                if (x <= right)
                    return IncrementWithinLimit(column.Col, CellAddress.MaxCol);
            }
        }
        foreach (var column in mainColumns)
        {
            var left = headerWidth + leftWidth + column.LeftOffset * scale;
            if (x < left)
                break;
            if (x <= left + column.Width * scale)
                return column.Col;
        }
        return null;
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row) =>
        metrics.FirstOrDefault(metric => metric.Row == row);

    private static ColMetric? FindColumnMetric(IReadOnlyList<ColMetric> metrics, uint column) =>
        metrics.FirstOrDefault(metric => metric.Col == column);

    private static double SumRowHeights(IReadOnlyList<RowMetric> rows) =>
        rows.Sum(row => row.Height);

    private static double SumColumnWidths(IReadOnlyList<ColMetric> columns) =>
        columns.Sum(column => column.Width);

    private static bool IsInside(GridRect rect, GridPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static uint IncrementWithinLimit(uint value, uint limit) => value >= limit ? limit : value + 1;

    private static uint ClampStartIndex(uint maxStartIndex, long index) =>
        (uint)Math.Max(1, Math.Min(maxStartIndex, index));
}

public readonly record struct SplitPanePointerDividerLayout(double? HorizontalY, double? VerticalX);
public readonly record struct SplitPanePointerDividerDragTarget(uint? Row, uint? Column);
public readonly record struct SplitPanePointerScrollbarChrome(
    SplitPanePointerScrollbar? HorizontalTopRight,
    SplitPanePointerScrollbar? VerticalBottomLeft);
public readonly record struct SplitPanePointerScrollbar(
    SplitPanePointerScrollbarOrientation Orientation,
    SplitPanePointerRegion Region,
    GridRect Track,
    GridRect Thumb,
    int VisibleSpan,
    uint MaxStartIndex);
public readonly record struct SplitPanePointerScrollbarHit(
    SplitPanePointerScrollbarPart Part,
    SplitPanePointerScrollbarOrientation Orientation,
    SplitPanePointerRegion Region);
public readonly record struct SplitPanePointerScrollTarget(
    SplitPanePointerRegion Region,
    SplitPanePointerScrollbarOrientation Orientation,
    uint Index);
public readonly record struct SplitPanePointerWheelTarget(SplitPanePointerRegion Region, bool Horizontal);

public enum SplitPanePointerHandle
{
    None,
    Horizontal,
    Vertical,
    Intersection,
}

public enum SplitPanePointerScrollbarPart
{
    Track,
    Thumb,
}

public enum SplitPanePointerScrollbarOrientation
{
    Horizontal,
    Vertical,
}

public enum SplitPanePointerRegion
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
