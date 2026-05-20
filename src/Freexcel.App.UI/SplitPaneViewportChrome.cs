using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class SplitPaneViewportChrome
{
    private const double SplitScrollbarThickness = 10;
    private const double SplitScrollbarMinThumb = 24;

    public static SplitPaneScrollbarChrome CalculateScrollbarChrome(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return new SplitPaneScrollbarChrome(null, null);

        var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
        SplitPaneScrollbar? horizontalTopRight = null;
        SplitPaneScrollbar? verticalBottomLeft = null;
        var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;

        if (dividerLayout.HorizontalY is { } horizontalY &&
            dividerLayout.VerticalX is { } verticalX &&
            topRightColumns.Count > 0)
        {
            var track = new Rect(
                verticalX,
                Math.Max(GridView.ColHeaderHeight, horizontalY - SplitScrollbarThickness),
                Math.Max(0, actualWidth - verticalX),
                SplitScrollbarThickness);
            horizontalTopRight = new SplitPaneScrollbar(
                SplitPaneScrollbarOrientation.Horizontal,
                SplitPaneRegion.TopRight,
                track,
                CalculateHorizontalThumb(track, topRightColumns[0].Col, topRightColumns.Count, CellAddress.MaxCol),
                Math.Max(1, topRightColumns.Count),
                Math.Max(1, CellAddress.MaxCol - (uint)Math.Max(1, topRightColumns.Count) + 1));
        }

        if (dividerLayout.HorizontalY is { } bottomY &&
            dividerLayout.VerticalX is { } leftX &&
            bottomLeftRows.Count > 0)
        {
            var track = new Rect(
                Math.Max(GridView.CalculateRowHeaderWidth(viewport), leftX - SplitScrollbarThickness),
                bottomY,
                SplitScrollbarThickness,
                Math.Max(0, actualHeight - bottomY));
            verticalBottomLeft = new SplitPaneScrollbar(
                SplitPaneScrollbarOrientation.Vertical,
                SplitPaneRegion.BottomLeft,
                track,
                CalculateVerticalThumb(track, bottomLeftRows[0].Row, bottomLeftRows.Count, CellAddress.MaxRow),
                Math.Max(1, bottomLeftRows.Count),
                Math.Max(1, CellAddress.MaxRow - (uint)Math.Max(1, bottomLeftRows.Count) + 1));
        }

        return new SplitPaneScrollbarChrome(horizontalTopRight, verticalBottomLeft);
    }

    public static SplitPaneScrollbarHit? HitTestScrollbar(SplitPaneScrollbarChrome chrome, Point pos)
    {
        if (HitTestScrollbar(chrome.HorizontalTopRight, pos) is { } horizontalHit)
            return horizontalHit;

        return HitTestScrollbar(chrome.VerticalBottomLeft, pos);
    }

    public static SplitPaneScrollbarScrollTarget? CalculateScrollTarget(
        SplitPaneScrollbarChrome chrome,
        Point pos)
    {
        if (CalculateScrollTarget(chrome.HorizontalTopRight, pos) is { } horizontal)
            return horizontal;

        return CalculateScrollTarget(chrome.VerticalBottomLeft, pos);
    }

    public static SplitPaneScrollbarScrollTarget CalculateThumbDragTarget(
        SplitPaneScrollbar scrollbar,
        Point pos,
        double pointerOffset)
    {
        var trackStart = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Track.Left + 1
            : scrollbar.Track.Top + 1;
        var trackLength = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Track.Width
            : scrollbar.Track.Height;
        var thumbLength = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Width
            : scrollbar.Thumb.Height;
        var available = Math.Max(1, trackLength - thumbLength - 2);
        var position = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? pos.X
            : pos.Y;
        var ratio = Math.Max(0, Math.Min(1, (position - pointerOffset - trackStart) / available));
        var index = (uint)Math.Max(1, Math.Min(scrollbar.MaxStartIndex, 1 + Math.Round(ratio * (scrollbar.MaxStartIndex - 1))));
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, index);
    }

    public static SplitPaneScrollbarScrollTarget CalculateWheelTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = 3)
    {
        var next = (long)Math.Max(1, currentIndex) - (long)notches * step;
        var clamped = (uint)Math.Max(1, Math.Min(scrollbar.MaxStartIndex, next));
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, clamped);
    }

    public static SplitPaneScrollbarScrollTarget? CalculateInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        Point pos)
    {
        var hit = HitTestScrollbar(chrome, pos);
        if (hit is null)
            return null;

        if (hit.Part == SplitPaneScrollbarPart.Thumb)
            return CalculateScrollTarget(chrome, pos);

        if (viewport.SplitPanes is not { } splitPanes)
            return null;

        if (hit is { Region: SplitPaneRegion.TopRight, Orientation: SplitPaneScrollbarOrientation.Horizontal } &&
            chrome.HorizontalTopRight is { } horizontal)
        {
            var columns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
            if (columns.Count == 0)
                return null;

            var current = columns[0].Col;
            var page = (uint)Math.Max(1, columns.Count);
            var beforeThumb = pos.X < horizontal.Thumb.Left;
            var next = beforeThumb
                ? current > page ? current - page : 1
                : Math.Min(CellAddress.MaxCol, current + page);
            return new SplitPaneScrollbarScrollTarget(SplitPaneRegion.TopRight, SplitPaneScrollbarOrientation.Horizontal, next);
        }

        if (hit is { Region: SplitPaneRegion.BottomLeft, Orientation: SplitPaneScrollbarOrientation.Vertical } &&
            chrome.VerticalBottomLeft is { } vertical)
        {
            var rows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
            if (rows.Count == 0)
                return null;

            var current = rows[0].Row;
            var page = (uint)Math.Max(1, rows.Count);
            var beforeThumb = pos.Y < vertical.Thumb.Top;
            var next = beforeThumb
                ? current > page ? current - page : 1
                : Math.Min(CellAddress.MaxRow, current + page);
            return new SplitPaneScrollbarScrollTarget(SplitPaneRegion.BottomLeft, SplitPaneScrollbarOrientation.Vertical, next);
        }

        return null;
    }

    private static Rect CalculateHorizontalThumb(Rect track, uint firstColumn, int visibleColumns, uint maxColumn)
    {
        var trackWidth = Math.Max(0, track.Width - 2);
        var thumbWidth = Math.Min(trackWidth, Math.Max(SplitScrollbarMinThumb, trackWidth * Math.Max(1, visibleColumns) / maxColumn));
        var available = Math.Max(0, track.Width - thumbWidth - 2);
        var maxStartColumn = Math.Max(1, maxColumn - (uint)Math.Max(1, visibleColumns) + 1);
        var ratio = maxStartColumn <= 1 ? 0 : (double)(Math.Max(1, firstColumn) - 1) / (maxStartColumn - 1);
        return new Rect(track.X + 1 + available * ratio, track.Y + 1, thumbWidth, Math.Max(0, track.Height - 2));
    }

    private static Rect CalculateVerticalThumb(Rect track, uint firstRow, int visibleRows, uint maxRow)
    {
        var trackHeight = Math.Max(0, track.Height - 2);
        var thumbHeight = Math.Min(trackHeight, Math.Max(SplitScrollbarMinThumb, trackHeight * Math.Max(1, visibleRows) / maxRow));
        var available = Math.Max(0, track.Height - thumbHeight - 2);
        var maxStartRow = Math.Max(1, maxRow - (uint)Math.Max(1, visibleRows) + 1);
        var ratio = maxStartRow <= 1 ? 0 : (double)(Math.Max(1, firstRow) - 1) / (maxStartRow - 1);
        return new Rect(track.X + 1, track.Y + 1 + available * ratio, Math.Max(0, track.Width - 2), thumbHeight);
    }

    private static SplitPaneScrollbarScrollTarget? CalculateScrollTarget(
        SplitPaneScrollbar? scrollbar,
        Point pos)
    {
        if (scrollbar is null || !scrollbar.Track.Contains(pos))
            return null;

        var trackStart = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Track.Left + 1
            : scrollbar.Track.Top + 1;
        var trackLength = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Track.Width
            : scrollbar.Track.Height;
        var thumbLength = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? scrollbar.Thumb.Width
            : scrollbar.Thumb.Height;
        var available = Math.Max(1, trackLength - thumbLength - 2);
        var position = scrollbar.Orientation == SplitPaneScrollbarOrientation.Horizontal
            ? pos.X
            : pos.Y;
        var ratio = Math.Max(0, Math.Min(1, (position - trackStart) / available));
        var index = (uint)Math.Max(1, Math.Min(scrollbar.MaxStartIndex, 1 + Math.Round(ratio * (scrollbar.MaxStartIndex - 1))));
        return new SplitPaneScrollbarScrollTarget(scrollbar.Region, scrollbar.Orientation, index);
    }

    private static SplitPaneScrollbarHit? HitTestScrollbar(SplitPaneScrollbar? scrollbar, Point pos)
    {
        if (scrollbar is null || !scrollbar.Track.Contains(pos))
            return null;

        var part = scrollbar.Thumb.Contains(pos)
            ? SplitPaneScrollbarPart.Thumb
            : SplitPaneScrollbarPart.Track;
        return new SplitPaneScrollbarHit(part, scrollbar.Orientation, scrollbar.Region);
    }
}
