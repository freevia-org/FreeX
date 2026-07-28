using System.Windows;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private (ResizeTarget Target, uint Index, double CurrentSize, bool IsCollapsedBoundary) HitTestResize(Point pos)
    {
        var hit = GridResizeHitPlanner.HitTest(
            Viewport,
            new GridPoint(pos.X, pos.Y),
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ResizeHitZone,
            HiddenRows,
            HiddenColumns);
        var target = hit.Target switch
        {
            GridResizeHitTarget.Column => ResizeTarget.Column,
            GridResizeHitTarget.Row => ResizeTarget.Row,
            _ => ResizeTarget.None
        };
        return (target, hit.Index, hit.CurrentSize, hit.IsCollapsedBoundary);
    }

    private bool IsOnAutofillHandle(Point pos)
        => EnableFillHandleAndCellDragAndDrop && GridAutofillPlanner.IsOnHandle(
            Viewport,
            SelectedRange,
            new GridPoint(pos.X, pos.Y),
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);

    /// <summary>Pixel tolerance for grabbing a manual page-break line in Page Break Preview, mirroring
    /// <c>PageMarginGuideHitZone</c>'s tolerance for margin guides.</summary>
    private const double PageBreakLineHitZone = 4;

    /// <summary>
    /// Hit-tests a manual (row or column) page-break line in Page Break Preview view, so the caller can
    /// offer a drag affordance (move the break to a different row/column, or drag it off the print-area
    /// edge to remove it) the same way <see cref="HitTestPageMarginGuide"/> does for margin guides.
    /// Returns null outside Page Break Preview, when there is no viewport, or when the pointer is not
    /// within <see cref="PageBreakLineHitZone"/> pixels of a manual break line.
    /// </summary>
    internal PageBreakLineHit? HitTestPageBreakLine(Point pos)
    {
        if (WorksheetViewMode != WorksheetViewMode.PageBreakPreview || Viewport is not { } viewport)
            return null;

        var logicalWidth = GetLogicalViewportWidth();
        var logicalHeight = GetLogicalViewportHeight();

        if (pos.X < ActualRowHeaderWidth || pos.Y < EffectiveColHeaderHeight ||
            pos.X > logicalWidth || pos.Y > logicalHeight)
            return null;

        if (RowPageBreaks is { Count: > 0 } rowPageBreaks)
        {
            var rowBreakLookup = GetPageBreakLookup(rowPageBreaks, ref _rowPageBreakLookupCache);
            foreach (var metric in viewport.RowMetrics)
            {
                if (!rowBreakLookup.Contains(metric.Row))
                    continue;

                var y = metric.TopOffset + EffectiveColHeaderHeight;
                if (Math.Abs(pos.Y - y) <= PageBreakLineHitZone)
                    return new PageBreakLineHit(PageBreakLineOrientation.Row, metric.Row);
            }
        }

        if (ColumnPageBreaks is { Count: > 0 } columnPageBreaks)
        {
            var columnBreakLookup = GetPageBreakLookup(columnPageBreaks, ref _columnPageBreakLookupCache);
            foreach (var metric in viewport.ColMetrics)
            {
                if (!columnBreakLookup.Contains(metric.Col))
                    continue;

                var x = metric.LeftOffset + ActualRowHeaderWidth;
                if (Math.Abs(pos.X - x) <= PageBreakLineHitZone)
                    return new PageBreakLineHit(PageBreakLineOrientation.Column, metric.Col);
            }
        }

        return null;
    }

    /// <summary>
    /// Given a page-break line drag that started at <paramref name="orientation"/>, computes where
    /// dropping the pointer at <paramref name="pos"/> should place the break: the row/column of the
    /// nearest gridline under the pointer, or <c>null</c> when the pointer is outside the grid/print
    /// area (matching <see cref="HitTestPageBreakLine"/>'s bounds check) -- Excel removes a page break
    /// dragged off the print area the same way. Takes the header size and logical (zoom-adjusted)
    /// viewport extent explicitly so callers pass the same coordinate space the render path uses,
    /// mirroring <see cref="HitTestSplitDividerHandle(ViewportModel, Point, double, double)"/>.
    /// </summary>
    internal static uint? CalculatePageBreakLineDragTarget(
        ViewportModel viewport,
        PageBreakLineOrientation orientation,
        Point pos,
        double rowHeaderWidth,
        double colHeaderHeight,
        double logicalWidth,
        double logicalHeight)
    {
        if (pos.X < rowHeaderWidth || pos.Y < colHeaderHeight ||
            pos.X > logicalWidth || pos.Y > logicalHeight)
            return null;

        if (orientation == PageBreakLineOrientation.Row)
        {
            uint? closestRow = null;
            var closestDistance = double.MaxValue;
            foreach (var metric in viewport.RowMetrics)
            {
                var y = metric.TopOffset + colHeaderHeight;
                var distance = Math.Abs(pos.Y - y);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestRow = metric.Row;
                }
            }
            return closestRow;
        }

        uint? closestCol = null;
        var closestColDistance = double.MaxValue;
        foreach (var metric in viewport.ColMetrics)
        {
            var x = metric.LeftOffset + rowHeaderWidth;
            var distance = Math.Abs(pos.X - x);
            if (distance < closestColDistance)
            {
                closestColDistance = distance;
                closestCol = metric.Col;
            }
        }
        return closestCol;
    }

    private WorksheetPageMarginEdge? HitTestPageMarginGuide(Point pos)
    {
        if (!ShowRulers || WorksheetViewMode != WorksheetViewMode.PageLayout || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;
        var pageBounds = new LayoutRect(
            guide.Value.Left,
            guide.Value.Top,
            Math.Max(0, guide.Value.Right - guide.Value.Left),
            Math.Max(0, guide.Value.Bottom - guide.Value.Top));
        var handles = FreeX.App.Presentation.PageLayout.PageMarginRulerLayoutPlanner.CalculateHandles(
            pageBounds, PaperSize, PageOrientation, PageMargins);
        return FreeX.App.Presentation.PageLayout.PageMarginGuideLayoutPlanner.HitTestGuide(
            guide.Value,
            ToLayoutPoint(pos),
            handles,
            ShowRulers,
            PageMarginGuideHitZone);
    }

    private WorksheetPageMargins? GetPageMarginsForDraggedGuide(Point pos)
    {
        if (_marginDragEdge is not { } edge || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;

        return FreeX.App.Presentation.PageLayout.PageMarginGuideLayoutPlanner.CalculateDraggedMargins(
            PaperSize,
            PageOrientation,
            PageMargins,
            edge,
            guide.Value,
            ToLayoutPoint(pos));
    }

    public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton(
        IReadOnlyList<ChartModel>? charts,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (charts is null)
            return null;

        for (var i = charts.Count - 1; i >= 0; i--)
        {
            var chart = charts[i];
            if (!chart.IsPivotChart || !chart.ShowPivotChartFieldButtons)
                continue;

            var rect = new Rect(chart.Left + rowHeaderWidth, chart.Top + columnHeaderHeight, chart.Width, chart.Height);
            if (!ContainsInclusive(rect, pos))
                continue;

            var topButton = new Rect(rect.Left + 6, rect.Top + 6, Math.Min(150, Math.Max(80, rect.Width - 12)), 24);
            if (chart.ShowPivotChartReportFilterButtons && ContainsInclusive(topButton, pos))
                return (chart, string.IsNullOrWhiteSpace(chart.PivotTableName) ? "PivotTable" : chart.PivotTableName!);

            var bottomTop = rect.Bottom - 36;
            var axisButton = new Rect(rect.Left + 6, bottomTop, 118, 24);
            if (chart.ShowPivotChartAxisFieldButtons && ContainsInclusive(axisButton, pos))
                return (chart, "Axis Fields");

            var valuesButton = new Rect(rect.Right - 120, bottomTop, 104, 24);
            if (chart.ShowPivotChartValueFieldButtons && ContainsInclusive(valuesButton, pos))
                return (chart, "Values");
        }

        return null;
    }

    public static (ChartModel Chart, int PointIndex)? HitTestWaterfallChartPoint(
        IReadOnlyList<ChartModel>? charts,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (charts is null)
            return null;

        for (var i = charts.Count - 1; i >= 0; i--)
        {
            var chart = charts[i];
            if (!chart.IsVisible || chart.Type != ChartType.Waterfall || chart.Width <= 0 || chart.Height <= 0)
                continue;

            var pointCount = ChartTypeSupport.GetDataPointCount(chart);
            if (pointCount <= 0)
                continue;

            var rect = new Rect(chart.Left + rowHeaderWidth, chart.Top + columnHeaderHeight, chart.Width, chart.Height);
            if (!ContainsInclusive(rect, pos))
                continue;

            var relativeX = Math.Clamp((pos.X - rect.Left) / rect.Width, 0, 1);
            var pointIndex = Math.Clamp((int)Math.Floor(relativeX * pointCount), 0, pointCount - 1);
            return (chart, pointIndex);
        }

        return null;
    }
}

/// <summary>Which axis a hit-tested manual page-break line runs along.</summary>
public enum PageBreakLineOrientation
{
    Row,
    Column
}

/// <summary>
/// The manual page-break line under the pointer in Page Break Preview view: <see cref="Orientation"/>
/// says whether it is a horizontal row break or vertical column break, and <see cref="Index"/> is the
/// zero-based row/column the break falls before (matching <see cref="GridView.RowPageBreaks"/> /
/// <see cref="GridView.ColumnPageBreaks"/> entries).
/// </summary>
internal readonly record struct PageBreakLineHit(PageBreakLineOrientation Orientation, uint Index);
