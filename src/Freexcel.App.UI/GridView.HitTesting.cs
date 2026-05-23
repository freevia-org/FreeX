using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private (ResizeTarget Target, uint Index, double CurrentSize) HitTestResize(Point pos)
    {
        if (Viewport == null) return (ResizeTarget.None, 0, 0);

        if (pos.Y < EffectiveColHeaderHeight)
        {
            foreach (var col in Viewport.ColMetrics)
            {
                double rightEdge = col.LeftOffset + col.Width + ActualRowHeaderWidth;
                if (Math.Abs(pos.X - rightEdge) <= ResizeHitZone)
                    return (ResizeTarget.Column, col.Col, col.Width);
            }
        }

        if (pos.X < ActualRowHeaderWidth)
        {
            foreach (var row in Viewport.RowMetrics)
            {
                double bottomEdge = row.TopOffset + row.Height + EffectiveColHeaderHeight;
                if (Math.Abs(pos.Y - bottomEdge) <= ResizeHitZone)
                    return (ResizeTarget.Row, row.Row, row.Height);
            }
        }

        return (ResizeTarget.None, 0, 0);
    }

    private bool IsOnAutofillHandle(Point pos)
    {
        if (Viewport == null || !SelectedRange.HasValue) return false;
        var range = SelectedRange.Value;
        var endRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == range.End.Row);
        var endCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == range.End.Col);
        if (endRow == null || endCol == null) return false;

        const double handleSize = 6;
        double hx = endCol.LeftOffset + endCol.Width + ActualRowHeaderWidth - handleSize / 2;
        double hy = endRow.TopOffset + endRow.Height + EffectiveColHeaderHeight - handleSize / 2;
        return pos.X >= hx - 3 && pos.X <= hx + handleSize + 3
            && pos.Y >= hy - 3 && pos.Y <= hy + handleSize + 3;
    }

    private WorksheetPageMarginEdge? HitTestPageMarginGuide(Point pos)
    {
        if (!ShowRulers || WorksheetViewMode != WorksheetViewMode.PageLayout || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;
        var pageBounds = new Rect(
            guide.Value.Left,
            guide.Value.Top,
            Math.Max(0, guide.Value.Right - guide.Value.Left),
            Math.Max(0, guide.Value.Bottom - guide.Value.Top));
        var handles = CalculatePageMarginRulerHandles(pageBounds, PaperSize, PageOrientation, PageMargins);
        if (HitTestPageMarginRulerHandles(handles, pos, ShowRulers) is { } handleEdge)
            return handleEdge;

        if (pos.Y >= guide.Value.Top && pos.Y <= guide.Value.Bottom)
        {
            if (Math.Abs(pos.X - guide.Value.MarginLeft) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Left;
            if (Math.Abs(pos.X - guide.Value.MarginRight) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Right;
        }

        if (pos.X >= guide.Value.Left && pos.X <= guide.Value.Right)
        {
            if (Math.Abs(pos.Y - guide.Value.MarginTop) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Top;
            if (Math.Abs(pos.Y - guide.Value.MarginBottom) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Bottom;
        }

        return null;
    }

    private WorksheetPageMargins? GetPageMarginsForDraggedGuide(Point pos)
    {
        if (_marginDragEdge is not { } edge || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;

        var fraction = edge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
            ? (pos.X - guide.Value.Left) / Math.Max(1.0, guide.Value.Right - guide.Value.Left)
            : (pos.Y - guide.Value.Top) / Math.Max(1.0, guide.Value.Bottom - guide.Value.Top);

        return WorksheetPageLayout.GetMarginsFromGuideFraction(
            PaperSize,
            PageOrientation,
            PageMargins,
            edge,
            fraction);
    }

    public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton(
        IReadOnlyList<ChartModel>? charts,
        Point pos,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (charts is null)
            return null;

        foreach (var chart in charts.Where(chart => chart.IsPivotChart && chart.ShowPivotChartFieldButtons).Reverse())
        {
            var rect = new Rect(chart.Left + rowHeaderWidth, chart.Top + columnHeaderHeight, chart.Width, chart.Height);
            var topButton = new Rect(rect.Left + 6, rect.Top + 6, Math.Min(150, Math.Max(80, rect.Width - 12)), 24);
            if (chart.ShowPivotChartReportFilterButtons && topButton.Contains(pos))
                return (chart, string.IsNullOrWhiteSpace(chart.PivotTableName) ? "PivotTable" : chart.PivotTableName!);

            var bottomTop = rect.Bottom - 36;
            var axisButton = new Rect(rect.Left + 6, bottomTop, 118, 24);
            if (chart.ShowPivotChartAxisFieldButtons && axisButton.Contains(pos))
                return (chart, "Axis Fields");

            var valuesButton = new Rect(rect.Right - 120, bottomTop, 104, 24);
            if (chart.ShowPivotChartValueFieldButtons && valuesButton.Contains(pos))
                return (chart, "Values");
        }

        return null;
    }
}
