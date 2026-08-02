using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Split-pane divider, hit-testing, scrollbar chrome, and clipping helpers.

    private void RenderSplitDivider(DrawingContext dc)
    {
        if (Viewport?.SplitPanes is null) return;
        var layout = CalculateSplitDividerLayout(Viewport);

        if (layout.HorizontalY is { } horizontalY)
        {
            dc.DrawLine(SplitPanePen, new Point(ActualRowHeaderWidth, horizontalY), new Point(GetLogicalViewportWidth(), horizontalY));
        }

        if (layout.VerticalX is { } verticalX)
        {
            dc.DrawLine(SplitPanePen, new Point(verticalX, EffectiveColHeaderHeight), new Point(verticalX, GetLogicalViewportHeight()));
        }

        RenderSplitDividerHandles(dc, layout);
    }

    private void RenderSplitDividerHandles(DrawingContext dc, SplitDividerLayout layout)
    {
        if (layout.HorizontalY is { } horizontalY)
        {
            dc.DrawRectangle(Brushes.White, SplitDividerHandlePen, new Rect(0, horizontalY - 4, ActualRowHeaderWidth, 8));
            dc.DrawLine(SplitDividerHandlePen, new Point(8, horizontalY), new Point(ActualRowHeaderWidth - 8, horizontalY));
        }

        if (layout.VerticalX is { } verticalX)
        {
            dc.DrawRectangle(Brushes.White, SplitDividerHandlePen, new Rect(verticalX - 4, 0, 8, EffectiveColHeaderHeight));
            dc.DrawLine(SplitDividerHandlePen, new Point(verticalX, 6), new Point(verticalX, EffectiveColHeaderHeight - 6));
        }
    }

    private void RenderSplitPaneScrollbarChrome(DrawingContext dc)
    {
        if (Viewport?.SplitPanes is null)
            return;

        var chrome = CalculateSplitPaneScrollbarChrome(Viewport, GetLogicalViewportWidth(), GetLogicalViewportHeight());
        DrawSplitScrollbar(dc, chrome.HorizontalTopRight);
        DrawSplitScrollbar(dc, chrome.VerticalBottomLeft);
    }

    private static void DrawSplitScrollbar(DrawingContext dc, SplitPaneScrollbar? scrollbar)
    {
        if (scrollbar is not { } value)
            return;

        dc.DrawRectangle(SplitScrollbarTrackBrush, SplitScrollbarPen, value.Track);
        dc.DrawRectangle(SplitScrollbarThumbBrush, SplitScrollbarPen, value.Thumb);
    }

    public static SplitDividerLayout CalculateSplitDividerLayout(ViewportModel viewport)
    {
        var rowHeaderWidth = CalculateRowHeaderWidth(viewport);
        var shared = SplitPanePointerPlanner.CalculateDividerLayout(
            viewport,
            rowHeaderWidth,
            ColHeaderHeight);
        return new SplitDividerLayout(shared.HorizontalY, shared.VerticalX);
    }

    public static SplitPaneScrollbarChrome CalculateSplitPaneScrollbarChrome(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight) =>
        SplitPaneViewportChrome.CalculateScrollbarChrome(viewport, actualWidth, actualHeight);

    public static SplitPaneScrollbarHit? HitTestSplitPaneScrollbar(SplitPaneScrollbarChrome chrome, Point pos) =>
        SplitPaneViewportChrome.HitTestScrollbar(chrome, pos);

    public static SplitPaneScrollbarScrollTarget? CalculateSplitPaneScrollbarScrollTarget(
        SplitPaneScrollbarChrome chrome,
        Point pos) =>
        SplitPaneViewportChrome.CalculateScrollTarget(chrome, pos);

    public static SplitPaneScrollbarScrollTarget CalculateSplitPaneScrollbarThumbDragTarget(
        SplitPaneScrollbar scrollbar,
        Point pos,
        double pointerOffset) =>
        SplitPaneViewportChrome.CalculateThumbDragTarget(scrollbar, pos, pointerOffset);

    public static SplitPaneScrollbarScrollTarget CalculateSplitPaneScrollbarWheelTarget(
        SplitPaneScrollbar scrollbar,
        uint currentIndex,
        int notches,
        uint step = SplitPaneScrollbarLayoutPlanner.DefaultWheelScrollStep) =>
        SplitPaneViewportChrome.CalculateWheelTarget(scrollbar, currentIndex, notches, step);

    public static SplitPaneWheelTarget ResolveSplitPaneWheelTarget(
        ViewportModel viewport,
        SheetId sheetId,
        Point pos,
        double actualWidth,
        double actualHeight,
        bool requestedHorizontal)
    {
        if (viewport.SplitPanes is not null)
        {
            var chrome = CalculateSplitPaneScrollbarChrome(viewport, actualWidth, actualHeight);
            if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)
            {
                return new SplitPaneWheelTarget(
                    scrollbarHit.Region,
                    scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal);
            }
        }

        var region = viewport.SplitPanes is not null &&
            HitTestViewportCell(viewport, sheetId, pos) is not null
                ? HitTestSplitPaneRegion(viewport, pos)
                : SplitPaneRegion.BottomRight;
        return new SplitPaneWheelTarget(region, requestedHorizontal);
    }

    public static SplitPaneScrollbarScrollTarget? CalculateSplitPaneScrollbarInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        Point pos) =>
        SplitPaneViewportChrome.CalculateInteractionTarget(viewport, chrome, pos);

    public static SplitPaneScrollbarScrollTarget? CalculateSplitPaneScrollbarInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        SplitPaneScrollbarHit hit,
        Point pos) =>
        SplitPaneViewportChrome.CalculateInteractionTarget(viewport, chrome, hit, pos);

    public static IReadOnlyList<SplitPaneCellLayout> CalculateSplitPaneCellLayouts(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions = null,
        CellAddress? editingCell = null) =>
        SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions, editingCell);

    public static CellAddress? HitTestViewportCell(ViewportModel viewport, SheetId sheetId, Point pos)
    {
        var rowHeaderWidth = CalculateRowHeaderWidth(viewport);
        // Use the EFFECTIVE column-header height (bare ColHeaderHeight plus the column-outline
        // gutter, when a column outline group is active) rather than the bare constant — the
        // render path (GridView.Rendering.Headers.cs / GridView.cs's EffectiveColHeaderHeight)
        // draws the header row, and therefore row 1's real top, at this same effective height, so
        // hit-testing must match it or every click misaligns by the gutter's height once columns
        // are grouped (r49 outline-gutter fix).
        var colHeaderHeight = CalculateColumnHeaderHeight(viewport);
        if (pos.X < rowHeaderWidth || pos.Y < colHeaderHeight)
            return null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            var dividerLayout = CalculateSplitDividerLayout(viewport);
            var horizontalY = dividerLayout.HorizontalY;
            var verticalX = dividerLayout.VerticalX;
            var region = HitTestSplitPaneRegion(dividerLayout, pos);
            var topRows = splitPanes.TopRows ?? [];
            var leftColumns = splitPanes.LeftColumns ?? [];
            var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
            var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;

            var rows = region switch
            {
                SplitPaneRegion.TopLeft or SplitPaneRegion.TopRight => topRows,
                SplitPaneRegion.BottomLeft => bottomLeftRows,
                _ => viewport.RowMetrics
            };
            var cols = region switch
            {
                SplitPaneRegion.TopLeft or SplitPaneRegion.BottomLeft => leftColumns,
                SplitPaneRegion.TopRight => topRightColumns,
                _ => viewport.ColMetrics
            };
            var rowOrigin = region is SplitPaneRegion.BottomLeft or SplitPaneRegion.BottomRight && horizontalY.HasValue
                ? horizontalY.Value
                : colHeaderHeight;
            var colOrigin = region is SplitPaneRegion.TopRight or SplitPaneRegion.BottomRight && verticalX.HasValue
                ? verticalX.Value
                : rowHeaderWidth;

            return HitTestMetrics(sheetId, pos, rows, cols, rowOrigin, colOrigin);
        }

        return HitTestMetrics(sheetId, pos, viewport.RowMetrics, viewport.ColMetrics, colHeaderHeight, rowHeaderWidth);
    }

    public static SplitPaneRegion HitTestSplitPaneRegion(ViewportModel viewport, Point pos)
    {
        var dividerLayout = CalculateSplitDividerLayout(viewport);
        return HitTestSplitPaneRegion(dividerLayout, pos);
    }

    private static SplitPaneRegion HitTestSplitPaneRegion(SplitDividerLayout dividerLayout, Point pos)
    {
        var isTop = dividerLayout.HorizontalY.HasValue && pos.Y < dividerLayout.HorizontalY.Value;
        var isLeft = dividerLayout.VerticalX.HasValue && pos.X < dividerLayout.VerticalX.Value;

        return (isTop, isLeft) switch
        {
            (true, true) => SplitPaneRegion.TopLeft,
            (true, false) => SplitPaneRegion.TopRight,
            (false, true) => SplitPaneRegion.BottomLeft,
            _ => SplitPaneRegion.BottomRight
        };
    }

    public static SplitDividerHandle HitTestSplitDividerHandle(ViewportModel viewport, Point pos)
        => HitTestSplitDividerHandle(viewport, pos, double.PositiveInfinity, double.PositiveInfinity);

    public static SplitDividerHandle HitTestSplitDividerHandle(
        ViewportModel viewport,
        Point pos,
        double actualWidth,
        double actualHeight)
    {
        return SplitPanePointerPlanner.HitTestDivider(
            viewport,
            new GridPoint(pos.X, pos.Y),
            actualWidth,
            actualHeight,
            CalculateRowHeaderWidth(viewport),
            ColHeaderHeight) switch
        {
            SplitPanePointerHandle.Intersection => SplitDividerHandle.Intersection,
            SplitPanePointerHandle.Horizontal => SplitDividerHandle.Horizontal,
            SplitPanePointerHandle.Vertical => SplitDividerHandle.Vertical,
            _ => SplitDividerHandle.None,
        };
    }

    public static SplitDividerDragTarget? CalculateSplitDividerDragTarget(
        ViewportModel viewport,
        SplitDividerHandle handle,
        Point pos)
    {
        var target = SplitPanePointerPlanner.CalculateDividerDragTarget(
            viewport,
            (SplitPanePointerHandle)handle,
            new GridPoint(pos.X, pos.Y),
            CalculateRowHeaderWidth(viewport),
            ColHeaderHeight);
        return target is { } value ? new SplitDividerDragTarget(value.Row, value.Column) : null;
    }

    public static bool CanScrollSplitPaneRegion(SplitPaneRegion region, bool horizontal) =>
        SplitPanePointerPlanner.CanScroll((SplitPanePointerRegion)region, horizontal);

    // Kept as GridView-wide metric lookups for the other partials; split geometry itself lives in
    // SplitPanePointerPlanner so the WPF and Avalonia hosts share one boundary algorithm.
    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row) =>
        metrics.FirstOrDefault(metric => metric.Row == row);

    private static ColMetric? FindColMetric(IReadOnlyList<ColMetric> metrics, uint column) =>
        metrics.FirstOrDefault(metric => metric.Col == column);

    private static CellAddress? HitTestMetrics(
        SheetId sheetId,
        Point pos,
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> cols,
        double rowOrigin,
        double colOrigin)
    {
        uint? row = null;
        uint? col = null;
        foreach (var rm in rows)
        {
            var top = rm.TopOffset + rowOrigin;
            if (pos.Y < top)
                break;

            if (pos.Y < top + rm.Height)
            {
                row = rm.Row;
                break;
            }
        }

        foreach (var cm in cols)
        {
            var left = cm.LeftOffset + colOrigin;
            if (pos.X < left)
                break;

            if (pos.X < left + cm.Width)
            {
                col = cm.Col;
                break;
            }
        }

        return row.HasValue && col.HasValue
            ? new CellAddress(sheetId, row.Value, col.Value)
            : null;
    }

    public static SplitPaneClipRects CalculateSplitPaneClipRects(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight) =>
        SplitPaneClipLayoutPlanner.CalculateClipRects(viewport, actualWidth, actualHeight);
}

public readonly record struct SplitDividerLayout(double? HorizontalY, double? VerticalX);
public readonly record struct SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect, SplitPaneRegion Region);
public sealed record SplitDividerDragTarget(uint? Row, uint? Column);
public readonly record struct SplitPaneScrollbarChrome(
    SplitPaneScrollbar? HorizontalTopRight,
    SplitPaneScrollbar? VerticalBottomLeft);
public readonly record struct SplitPaneScrollbar(
    SplitPaneScrollbarOrientation Orientation,
    SplitPaneRegion Region,
    Rect Track,
    Rect Thumb,
    int VisibleSpan,
    uint MaxStartIndex);
public readonly record struct SplitPaneScrollbarHit(
    SplitPaneScrollbarPart Part,
    SplitPaneScrollbarOrientation Orientation,
    SplitPaneRegion Region);
public readonly record struct SplitPaneScrollbarScrollTarget(
    SplitPaneRegion Region,
    SplitPaneScrollbarOrientation Orientation,
    uint Index);
public sealed record SplitPaneWheelTarget(SplitPaneRegion Region, bool Horizontal);
public sealed record SplitPaneClipRects(
    Rect TopLeft,
    Rect TopRight,
    Rect BottomLeft,
    Rect BottomRight);
public enum SplitPaneScrollbarPart
{
    Track,
    Thumb
}
public enum SplitPaneScrollbarOrientation
{
    Horizontal,
    Vertical
}
public enum SplitDividerHandle
{
    None,
    Horizontal,
    Vertical,
    Intersection
}
public enum SplitPaneRegion
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
