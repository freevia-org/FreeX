using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class PageMarginGuideLayoutPlanner
{
    public static PageMarginGuideLayout? CalculateGuide(
        ViewportModel viewport,
        GridRange printArea,
        double rowHeaderWidth,
        double columnHeaderHeight,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        var top = FindRowTop(viewport, printArea.Start.Row, columnHeaderHeight);
        var bottom = FindRowBottom(viewport, printArea.End.Row, columnHeaderHeight);
        var left = FindColumnLeft(viewport, printArea.Start.Col, rowHeaderWidth);
        var right = FindColumnRight(viewport, printArea.End.Col, rowHeaderWidth);
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue)
            return null;

        var guide = WorksheetPageLayout.GetMarginGuideFractions(paperSize, orientation, margins);
        var width = right.Value - left.Value;
        var height = bottom.Value - top.Value;
        if (width <= 0 || height <= 0)
            return null;

        return new PageMarginGuideLayout(
            top.Value,
            left.Value,
            bottom.Value,
            right.Value,
            left.Value + width * guide.Left,
            left.Value + width * guide.Right,
            top.Value + height * guide.Top,
            top.Value + height * guide.Bottom);
    }

    private static double? FindRowTop(ViewportModel viewport, uint row, double columnHeaderHeight) =>
        viewport.RowMetrics.FirstOrDefault(metric => metric.Row == row) is { } metric
            ? metric.TopOffset + columnHeaderHeight
            : null;

    private static double? FindRowBottom(ViewportModel viewport, uint row, double columnHeaderHeight) =>
        viewport.RowMetrics.FirstOrDefault(metric => metric.Row == row) is { } metric
            ? metric.TopOffset + metric.Height + columnHeaderHeight
            : null;

    private static double? FindColumnLeft(ViewportModel viewport, uint column, double rowHeaderWidth) =>
        viewport.ColMetrics.FirstOrDefault(metric => metric.Col == column) is { } metric
            ? metric.LeftOffset + rowHeaderWidth
            : null;

    private static double? FindColumnRight(ViewportModel viewport, uint column, double rowHeaderWidth) =>
        viewport.ColMetrics.FirstOrDefault(metric => metric.Col == column) is { } metric
            ? metric.LeftOffset + metric.Width + rowHeaderWidth
            : null;

    public static WorksheetPageMarginEdge? HitTestGuide(
        PageMarginGuideLayout guide,
        Point pointer,
        PageMarginRulerHandles handles,
        bool showRulers,
        double guideHitZone)
    {
        if (GridView.HitTestPageMarginRulerHandles(handles, pointer, showRulers) is { } handleEdge)
            return handleEdge;

        if (pointer.Y >= guide.Top && pointer.Y <= guide.Bottom)
        {
            if (Math.Abs(pointer.X - guide.MarginLeft) <= guideHitZone)
                return WorksheetPageMarginEdge.Left;
            if (Math.Abs(pointer.X - guide.MarginRight) <= guideHitZone)
                return WorksheetPageMarginEdge.Right;
        }

        if (pointer.X >= guide.Left && pointer.X <= guide.Right)
        {
            if (Math.Abs(pointer.Y - guide.MarginTop) <= guideHitZone)
                return WorksheetPageMarginEdge.Top;
            if (Math.Abs(pointer.Y - guide.MarginBottom) <= guideHitZone)
                return WorksheetPageMarginEdge.Bottom;
        }

        return null;
    }

    public static WorksheetPageMargins CalculateDraggedMargins(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins currentMargins,
        WorksheetPageMarginEdge edge,
        PageMarginGuideLayout guide,
        Point pointer)
    {
        var fraction = edge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
            ? (pointer.X - guide.Left) / Math.Max(1.0, guide.Right - guide.Left)
            : (pointer.Y - guide.Top) / Math.Max(1.0, guide.Bottom - guide.Top);

        return WorksheetPageLayout.GetMarginsFromGuideFraction(
            paperSize,
            orientation,
            currentMargins,
            edge,
            fraction);
    }
}

public readonly record struct PageMarginGuideLayout(
    double Top,
    double Left,
    double Bottom,
    double Right,
    double MarginLeft,
    double MarginRight,
    double MarginTop,
    double MarginBottom);
