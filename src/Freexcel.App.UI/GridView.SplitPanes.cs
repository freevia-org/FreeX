using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    // Split-pane divider, hit-testing, scrollbar chrome, and clipping helpers.

    private void RenderSplitDivider(DrawingContext dc)
    {
        if (Viewport == null) return;
        var layout = CalculateSplitDividerLayout(Viewport);

        if (layout.HorizontalY is { } horizontalY)
        {
            dc.DrawLine(SplitPanePen, new Point(ActualRowHeaderWidth, horizontalY), new Point(ActualWidth, horizontalY));
        }

        if (layout.VerticalX is { } verticalX)
        {
            dc.DrawLine(SplitPanePen, new Point(verticalX, EffectiveColHeaderHeight), new Point(verticalX, ActualHeight));
        }

        RenderSplitDividerHandles(dc, layout);
    }

    private void RenderSplitDividerHandles(DrawingContext dc, SplitDividerLayout layout)
    {
        var brush = MakeBrush(112, 112, 112);
        var pen = new Pen(brush, 1);
        pen.Freeze();

        if (layout.HorizontalY is { } horizontalY)
        {
            dc.DrawRectangle(Brushes.White, pen, new Rect(0, horizontalY - 4, ActualRowHeaderWidth, 8));
            dc.DrawLine(pen, new Point(8, horizontalY), new Point(ActualRowHeaderWidth - 8, horizontalY));
        }

        if (layout.VerticalX is { } verticalX)
        {
            dc.DrawRectangle(Brushes.White, pen, new Rect(verticalX - 4, 0, 8, EffectiveColHeaderHeight));
            dc.DrawLine(pen, new Point(verticalX, 6), new Point(verticalX, EffectiveColHeaderHeight - 6));
        }
    }

    private void RenderSplitPaneScrollbarChrome(DrawingContext dc)
    {
        if (Viewport?.SplitPanes is null)
            return;

        var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
        DrawSplitScrollbar(dc, chrome.HorizontalTopRight);
        DrawSplitScrollbar(dc, chrome.VerticalBottomLeft);
    }

    private static void DrawSplitScrollbar(DrawingContext dc, SplitPaneScrollbar? scrollbar)
    {
        if (scrollbar is null)
            return;

        dc.DrawRectangle(SplitScrollbarTrackBrush, SplitScrollbarPen, scrollbar.Track);
        dc.DrawRectangle(SplitScrollbarThumbBrush, SplitScrollbarPen, scrollbar.Thumb);
    }

    public static SplitDividerLayout CalculateSplitDividerLayout(ViewportModel viewport)
    {
        double? horizontalY = null;
        double? verticalX = null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            if (splitPanes.Row is { } splitRow)
            {
                var pinnedRows = splitPanes.TopRows ?? [];
                horizontalY = pinnedRows.Count > 0
                    ? ColHeaderHeight + pinnedRows.Sum(row => row.Height)
                    : viewport.RowMetrics.FirstOrDefault(row => row.Row == splitRow)?.TopOffset + ColHeaderHeight;
            }

            if (splitPanes.Column is { } splitColumn)
            {
                var pinnedColumns = splitPanes.LeftColumns ?? [];
                verticalX = pinnedColumns.Count > 0
                    ? CalculateRowHeaderWidth(viewport) + pinnedColumns.Sum(column => column.Width)
                    : viewport.ColMetrics.FirstOrDefault(column => column.Col == splitColumn)?.LeftOffset + CalculateRowHeaderWidth(viewport);
            }
        }

        return new SplitDividerLayout(horizontalY, verticalX);
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
        uint step = 3) =>
        SplitPaneViewportChrome.CalculateWheelTarget(scrollbar, currentIndex, notches, step);

    public static SplitPaneScrollbarScrollTarget? CalculateSplitPaneScrollbarInteractionTarget(
        ViewportModel viewport,
        SplitPaneScrollbarChrome chrome,
        Point pos) =>
        SplitPaneViewportChrome.CalculateInteractionTarget(viewport, chrome, pos);

    public static IReadOnlyList<SplitPaneCellLayout> CalculateSplitPaneCellLayouts(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions = null) =>
        SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions);

    public static CellAddress? HitTestViewportCell(ViewportModel viewport, SheetId sheetId, Point pos)
    {
        if (pos.X < CalculateRowHeaderWidth(viewport) || pos.Y < ColHeaderHeight)
            return null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            var dividerLayout = CalculateSplitDividerLayout(viewport);
            var horizontalY = dividerLayout.HorizontalY;
            var verticalX = dividerLayout.VerticalX;
            var region = HitTestSplitPaneRegion(viewport, pos);
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
                : ColHeaderHeight;
            var colOrigin = region is SplitPaneRegion.TopRight or SplitPaneRegion.BottomRight && verticalX.HasValue
                ? verticalX.Value
                : CalculateRowHeaderWidth(viewport);

            return HitTestMetrics(sheetId, pos, rows, cols, rowOrigin, colOrigin);
        }

        return HitTestMetrics(sheetId, pos, viewport.RowMetrics, viewport.ColMetrics, ColHeaderHeight, CalculateRowHeaderWidth(viewport));
    }

    public static SplitPaneRegion HitTestSplitPaneRegion(ViewportModel viewport, Point pos)
    {
        var dividerLayout = CalculateSplitDividerLayout(viewport);
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
    {
        var dividerLayout = CalculateSplitDividerLayout(viewport);
        var onHorizontal = dividerLayout.HorizontalY is { } horizontalY &&
            pos.X >= 0 &&
            Math.Abs(pos.Y - horizontalY) <= SplitDividerHitZone;
        var onVertical = dividerLayout.VerticalX is { } verticalX &&
            pos.Y >= 0 &&
            Math.Abs(pos.X - verticalX) <= SplitDividerHitZone;

        return (onHorizontal, onVertical) switch
        {
            (true, true) => SplitDividerHandle.Intersection,
            (true, false) => SplitDividerHandle.Horizontal,
            (false, true) => SplitDividerHandle.Vertical,
            _ => SplitDividerHandle.None
        };
    }

    public static SplitDividerDragTarget? CalculateSplitDividerDragTarget(
        ViewportModel viewport,
        SplitDividerHandle handle,
        Point pos)
    {
        if (handle == SplitDividerHandle.None)
            return null;

        var splitPanes = viewport.SplitPanes;
        if (splitPanes is null)
            return null;

        uint? row = handle is SplitDividerHandle.Horizontal or SplitDividerHandle.Intersection
            ? FindSplitRow(splitPanes.TopRows ?? [], viewport.RowMetrics, pos.Y)
            : null;
        uint? column = handle is SplitDividerHandle.Vertical or SplitDividerHandle.Intersection
            ? FindSplitColumn(splitPanes.LeftColumns ?? [], viewport.ColMetrics, pos.X, CalculateRowHeaderWidth(viewport))
            : null;

        return new SplitDividerDragTarget(row, column);
    }

    private static uint? FindSplitRow(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<RowMetric> mainRows,
        double y)
    {
        var topHeight = topRows.Sum(row => row.Height);
        if (y < ColHeaderHeight)
            return null;

        if (y <= ColHeaderHeight + topHeight)
        {
            foreach (var row in topRows)
            {
                var bottom = ColHeaderHeight + row.TopOffset + row.Height;
                if (y <= bottom)
                    return Math.Min(CellAddress.MaxRow, row.Row + 1);
            }
        }

        foreach (var row in mainRows)
        {
            var top = ColHeaderHeight + topHeight + row.TopOffset;
            if (y >= top && y <= top + row.Height)
                return row.Row;
        }

        return null;
    }

    private static uint? FindSplitColumn(
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<ColMetric> mainColumns,
        double x,
        double ActualRowHeaderWidth)
    {
        var leftWidth = leftColumns.Sum(column => column.Width);
        if (x < ActualRowHeaderWidth)
            return null;

        if (x <= ActualRowHeaderWidth + leftWidth)
        {
            foreach (var column in leftColumns)
            {
                var right = ActualRowHeaderWidth + column.LeftOffset + column.Width;
                if (x <= right)
                    return Math.Min(CellAddress.MaxCol, column.Col + 1);
            }
        }

        foreach (var column in mainColumns)
        {
            var left = ActualRowHeaderWidth + leftWidth + column.LeftOffset;
            if (x >= left && x <= left + column.Width)
                return column.Col;
        }

        return null;
    }

    public static bool CanScrollSplitPaneRegion(SplitPaneRegion region, bool horizontal) =>
        horizontal
            ? region is SplitPaneRegion.TopRight or SplitPaneRegion.BottomRight
            : region is SplitPaneRegion.BottomLeft or SplitPaneRegion.BottomRight;

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
            if (pos.Y >= top && pos.Y < top + rm.Height)
            {
                row = rm.Row;
                break;
            }
        }

        foreach (var cm in cols)
        {
            var left = cm.LeftOffset + colOrigin;
            if (pos.X >= left && pos.X < left + cm.Width)
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
        double actualHeight)
    {
        var layout = CalculateSplitDividerLayout(viewport);
        var horizontalY = layout.HorizontalY ?? actualHeight;
        var verticalX = layout.VerticalX ?? actualWidth;
        var top = ColHeaderHeight;
        var left = CalculateRowHeaderWidth(viewport);
        var right = Math.Max(verticalX, actualWidth);
        var bottom = Math.Max(horizontalY, actualHeight);

        return new SplitPaneClipRects(
            new Rect(left, top, Math.Max(0, verticalX - left), Math.Max(0, horizontalY - top)),
            new Rect(verticalX, top, Math.Max(0, right - verticalX), Math.Max(0, horizontalY - top)),
            new Rect(left, horizontalY, Math.Max(0, verticalX - left), Math.Max(0, bottom - horizontalY)),
            new Rect(verticalX, horizontalY, Math.Max(0, right - verticalX), Math.Max(0, bottom - horizontalY)));
    }

    private static Rect GetSplitPaneClipRectForCell(
        ViewportModel viewport,
        DisplayCell cell,
        SplitPaneClipRects clips)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return clips.BottomRight;

        var isTop = (splitPanes.TopRows ?? []).Any(row => row.Row == cell.Row);
        var isLeft = (splitPanes.LeftColumns ?? []).Any(column => column.Col == cell.Col);
        return (isTop, isLeft) switch
        {
            (true, true) => clips.TopLeft,
            (true, false) => clips.TopRight,
            (false, true) => clips.BottomLeft,
            _ => clips.BottomRight
        };
    }
}

public sealed record SplitDividerLayout(double? HorizontalY, double? VerticalX);
public sealed record SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect);
public sealed record SplitDividerDragTarget(uint? Row, uint? Column);
public sealed record SplitPaneScrollbarChrome(
    SplitPaneScrollbar? HorizontalTopRight,
    SplitPaneScrollbar? VerticalBottomLeft);
public sealed record SplitPaneScrollbar(
    SplitPaneScrollbarOrientation Orientation,
    SplitPaneRegion Region,
    Rect Track,
    Rect Thumb,
    int VisibleSpan,
    uint MaxStartIndex);
public sealed record SplitPaneScrollbarHit(
    SplitPaneScrollbarPart Part,
    SplitPaneScrollbarOrientation Orientation,
    SplitPaneRegion Region);
public sealed record SplitPaneScrollbarScrollTarget(
    SplitPaneRegion Region,
    SplitPaneScrollbarOrientation Orientation,
    uint Index);
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
