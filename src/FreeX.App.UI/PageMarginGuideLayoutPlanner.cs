using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

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
        if (!TryFindRowMetrics(viewport.RowMetrics, printArea.Start.Row, printArea.End.Row, out var topRow, out var bottomRow) ||
            !TryFindColumnMetrics(viewport.ColMetrics, printArea.Start.Col, printArea.End.Col, out var leftColumn, out var rightColumn))
            return null;

        var guide = WorksheetPageLayout.GetMarginGuideFractions(paperSize, orientation, margins);
        var top = topRow.TopOffset + columnHeaderHeight;
        var bottom = bottomRow.TopOffset + bottomRow.Height + columnHeaderHeight;
        var left = leftColumn.LeftOffset + rowHeaderWidth;
        var right = rightColumn.LeftOffset + rightColumn.Width + rowHeaderWidth;
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
            return null;

        return new PageMarginGuideLayout(
            top,
            left,
            bottom,
            right,
            left + width * guide.Left,
            left + width * guide.Right,
            top + height * guide.Top,
            top + height * guide.Bottom);
    }

    private static bool TryFindRowMetrics(
        IReadOnlyList<RowMetric> metrics,
        uint topRow,
        uint bottomRow,
        out RowMetric topMetric,
        out RowMetric bottomMetric)
    {
        RowMetric? foundTop = null;
        RowMetric? foundBottom = null;

        foreach (var metric in metrics)
        {
            if (metric.Row > bottomRow)
                break;

            if (foundTop is null && metric.Row == topRow)
                foundTop = metric;

            if (foundBottom is null && metric.Row == bottomRow)
                foundBottom = metric;

            if (foundTop is not null && foundBottom is not null)
            {
                topMetric = foundTop;
                bottomMetric = foundBottom;
                return true;
            }
        }

        topMetric = null!;
        bottomMetric = null!;
        return false;
    }

    private static bool TryFindColumnMetrics(
        IReadOnlyList<ColMetric> metrics,
        uint leftColumn,
        uint rightColumn,
        out ColMetric leftMetric,
        out ColMetric rightMetric)
    {
        ColMetric? foundLeft = null;
        ColMetric? foundRight = null;

        foreach (var metric in metrics)
        {
            if (metric.Col > rightColumn)
                break;

            if (foundLeft is null && metric.Col == leftColumn)
                foundLeft = metric;

            if (foundRight is null && metric.Col == rightColumn)
                foundRight = metric;

            if (foundLeft is not null && foundRight is not null)
            {
                leftMetric = foundLeft;
                rightMetric = foundRight;
                return true;
            }
        }

        leftMetric = null!;
        rightMetric = null!;
        return false;
    }

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
