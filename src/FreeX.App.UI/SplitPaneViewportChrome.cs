using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class SplitPaneViewportChrome
{
    public static SplitPaneScrollbarChrome CalculateScrollbarChrome(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight)
    {
        var shared = SplitPanePointerPlanner.CalculateScrollbarChrome(
            viewport,
            actualWidth,
            actualHeight,
            GridView.CalculateRowHeaderWidth(viewport),
            GridView.ColHeaderHeight);
        return new SplitPaneScrollbarChrome(ToWpf(shared.HorizontalTopRight), ToWpf(shared.VerticalBottomLeft));
    }

    private static SplitPaneScrollbar? ToWpf(SplitPanePointerScrollbar? scrollbar) =>
        scrollbar is { } value
            ? new SplitPaneScrollbar(
                (SplitPaneScrollbarOrientation)value.Orientation,
                (SplitPaneRegion)value.Region,
                new Rect(value.Track.Left, value.Track.Top, value.Track.Width, value.Track.Height),
                new Rect(value.Thumb.Left, value.Thumb.Top, value.Thumb.Width, value.Thumb.Height),
                value.VisibleSpan,
                value.MaxStartIndex)
            : null;

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
        uint step = SplitPaneScrollbarLayoutPlanner.DefaultWheelScrollStep) =>
        SplitPaneScrollbarLayoutPlanner.CalculateWheelTarget(scrollbar, currentIndex, notches, step);

    public static SplitPaneScrollbarScrollTarget? CalculateInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        Point pos)
    {
        if (HitTestScrollbar(chrome, pos) is not { } hit)
            return null;

        return CalculateInteractionTarget(viewport, chrome, hit, pos);
    }

    public static SplitPaneScrollbarScrollTarget? CalculateInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        SplitPaneScrollbarHit hit,
        Point pos)
    {
        if (hit.Part == SplitPaneScrollbarPart.Thumb)
            return null;

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
