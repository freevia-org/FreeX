using Freexcel.Core.Model;

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
