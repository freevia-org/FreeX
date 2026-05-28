using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private (ResizeTarget Target, uint Index, double CurrentSize) HitTestResize(Point pos)
    {
        var hit = GridResizeHitPlanner.HitTest(
            Viewport,
            pos,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ResizeHitZone);
        var target = hit.Target switch
        {
            GridResizeHitTarget.Column => ResizeTarget.Column,
            GridResizeHitTarget.Row => ResizeTarget.Row,
            _ => ResizeTarget.None
        };
        return (target, hit.Index, hit.CurrentSize);
    }

    private bool IsOnAutofillHandle(Point pos)
        => GridAutofillPlanner.IsOnHandle(
            Viewport,
            SelectedRange,
            pos,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);

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
        return PageMarginGuideLayoutPlanner.HitTestGuide(
            guide.Value,
            pos,
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

        return PageMarginGuideLayoutPlanner.CalculateDraggedMargins(
            PaperSize,
            PageOrientation,
            PageMargins,
            edge,
            guide.Value,
            pos);
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
}
