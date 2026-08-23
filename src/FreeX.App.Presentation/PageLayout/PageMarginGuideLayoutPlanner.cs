using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// The Page Layout view margin guide in pixel space: the outer print-range rectangle
/// (<see cref="Top"/>/<see cref="Left"/>/<see cref="Bottom"/>/<see cref="Right"/>) plus the four
/// draggable margin lines positioned inside it by the paper-relative margin fractions.
/// </summary>
public readonly record struct PageMarginGuideLayout(
    double Top,
    double Left,
    double Bottom,
    double Right,
    double MarginLeft,
    double MarginRight,
    double MarginTop,
    double MarginBottom);

/// <summary>
/// Pure margin-guide geometry for the Page Layout view, shared by the desktop hosts. Maps the print
/// range to its pixel rectangle, places the four margin lines inside it using the paper-relative
/// fractions from <see cref="WorksheetPageLayout"/>, and provides hit-testing plus drag-to-margin math.
/// </summary>
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
        if (!ViewportMetricEndpointLookup.TryFindRows(viewport.RowMetrics, printArea.Start.Row, printArea.End.Row, out var topRow, out var bottomRow) ||
            !ViewportMetricEndpointLookup.TryFindColumns(viewport.ColMetrics, printArea.Start.Col, printArea.End.Col, out var leftColumn, out var rightColumn))
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

    /// <summary>
    /// Returns the margin edge under <paramref name="pointer"/>, preferring a ruler handle (when rulers
    /// are shown) and falling back to the in-grid guide lines within <paramref name="guideHitZone"/>
    /// pixels. Returns null when the pointer is not on a margin control.
    /// </summary>
    public static WorksheetPageMarginEdge? HitTestGuide(
        PageMarginGuideLayout guide,
        LayoutPoint pointer,
        PageMarginRulerHandles handles,
        bool showRulers,
        double guideHitZone)
    {
        if (PageMarginRulerLayoutPlanner.HitTestHandles(handles, pointer, showRulers) is { } handleEdge)
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

    /// <summary>
    /// Converts a pointer position on a dragged margin line into the new margins, by turning the
    /// pointer's offset within the guide rectangle into a paper-relative fraction and feeding it to
    /// <see cref="WorksheetPageLayout.GetMarginsFromGuideFraction"/>.
    /// </summary>
    public static WorksheetPageMargins CalculateDraggedMargins(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins currentMargins,
        WorksheetPageMarginEdge edge,
        PageMarginGuideLayout guide,
        LayoutPoint pointer)
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
