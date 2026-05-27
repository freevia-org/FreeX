using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class SplitPaneViewportChrome
{
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
                Math.Max(GridView.ColHeaderHeight, horizontalY - SplitPaneScrollbarLayoutPlanner.Thickness),
                Math.Max(0, actualWidth - verticalX),
                SplitPaneScrollbarLayoutPlanner.Thickness);
            horizontalTopRight = new SplitPaneScrollbar(
                SplitPaneScrollbarOrientation.Horizontal,
                SplitPaneRegion.TopRight,
                track,
                SplitPaneScrollbarLayoutPlanner.CalculateThumb(
                    SplitPaneScrollbarOrientation.Horizontal,
                    track,
                    topRightColumns[0].Col,
                    topRightColumns.Count,
                    CellAddress.MaxCol),
                Math.Max(1, topRightColumns.Count),
                Math.Max(1, CellAddress.MaxCol - (uint)Math.Max(1, topRightColumns.Count) + 1));
        }

        if (dividerLayout.HorizontalY is { } bottomY &&
            dividerLayout.VerticalX is { } leftX &&
            bottomLeftRows.Count > 0)
        {
            var track = new Rect(
                Math.Max(GridView.CalculateRowHeaderWidth(viewport), leftX - SplitPaneScrollbarLayoutPlanner.Thickness),
                bottomY,
                SplitPaneScrollbarLayoutPlanner.Thickness,
                Math.Max(0, actualHeight - bottomY));
            verticalBottomLeft = new SplitPaneScrollbar(
                SplitPaneScrollbarOrientation.Vertical,
                SplitPaneRegion.BottomLeft,
                track,
                SplitPaneScrollbarLayoutPlanner.CalculateThumb(
                    SplitPaneScrollbarOrientation.Vertical,
                    track,
                    bottomLeftRows[0].Row,
                    bottomLeftRows.Count,
                    CellAddress.MaxRow),
                Math.Max(1, bottomLeftRows.Count),
                Math.Max(1, CellAddress.MaxRow - (uint)Math.Max(1, bottomLeftRows.Count) + 1));
        }

        return new SplitPaneScrollbarChrome(horizontalTopRight, verticalBottomLeft);
    }

    public static SplitPaneScrollbarHit? HitTestScrollbar(SplitPaneScrollbarChrome chrome, Point pos)
    {
        if (SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(chrome.HorizontalTopRight, pos) is { } horizontalHit)
            return horizontalHit;

        return SplitPaneScrollbarLayoutPlanner.HitTestScrollbar(chrome.VerticalBottomLeft, pos);
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
        double pointerOffset) =>
        SplitPaneScrollbarLayoutPlanner.CalculateThumbDragTarget(scrollbar, pos, pointerOffset);

    public static SplitPaneScrollbarScrollTarget CalculateWheelTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = 3) =>
        SplitPaneScrollbarLayoutPlanner.CalculateWheelTarget(scrollbar, currentIndex, notches, step);

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
            return SplitPaneScrollbarLayoutPlanner.CalculatePageTarget(horizontal, current, pos);
        }

        if (hit is { Region: SplitPaneRegion.BottomLeft, Orientation: SplitPaneScrollbarOrientation.Vertical } &&
            chrome.VerticalBottomLeft is { } vertical)
        {
            var rows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
            if (rows.Count == 0)
                return null;

            var current = rows[0].Row;
            return SplitPaneScrollbarLayoutPlanner.CalculatePageTarget(vertical, current, pos);
        }

        return null;
    }

    private static SplitPaneScrollbarScrollTarget? CalculateScrollTarget(
        SplitPaneScrollbar? scrollbar,
        Point pos)
    {
        return SplitPaneScrollbarLayoutPlanner.CalculateScrollTarget(scrollbar, pos);
    }
}
