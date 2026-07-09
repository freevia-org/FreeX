using FreeX.App.Presentation.Charts;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>Which of a page rectangle's four edges fall inside the visible region.</summary>
public readonly record struct PageBreakPreviewPageEdges(
    bool Top,
    bool Bottom,
    bool Left,
    bool Right);

/// <summary>
/// A single page in the page-break preview: its 1-based page number (in the active page order), the
/// pixel rectangle it occupies inside the visible region, and which of its edges are on-screen so a
/// renderer can avoid drawing a border the page clips off.
/// </summary>
public sealed record PageBreakPreviewPageLayout(
    int PageNumber,
    LayoutRect Bounds,
    PageBreakPreviewPageEdges VisibleEdges);

/// <summary>An automatic page-break line in pixel space, drawn dashed by the desktop hosts.</summary>
public sealed record PageBreakPreviewBreakLine(LayoutPoint Start, LayoutPoint End);

/// <summary>
/// The full page-break-preview overlay geometry for the visible region: rectangles masking the area
/// outside the print range, the visible pages, and the automatic (non-manual) break lines.
/// </summary>
public sealed record PageBreakPreviewLayout(
    IReadOnlyList<LayoutRect> OutsidePrintAreaMasks,
    IReadOnlyList<PageBreakPreviewPageLayout> Pages,
    IReadOnlyList<PageBreakPreviewBreakLine> AutomaticBreakLines);

/// <summary>
/// Pure page-break-preview / page-layout geometry shared by the desktop hosts. Given the visible
/// row/column metrics, the print range, manual page breaks, and the active page setup, it computes the
/// page grid in pixel space, the dimmed masks outside the print range, and the automatic break lines.
/// Pagination itself is delegated to <see cref="PagePaginationPlanner"/> so page slicing stays in one
/// place. Callers should pass the sheet's IsRowEffectivelyHidden/IsColEffectivelyHidden predicates (see
/// <see cref="PrintPreviewPaginationContext"/>) so the overlay's page count and break lines match the
/// real print output, which also excludes hidden/filtered rows and columns.
/// </summary>
public static class PageBreakPreviewLayoutPlanner
{
    public static PageBreakPreviewLayout Calculate(
        ViewportModel viewport,
        GridRange? printArea,
        IReadOnlyCollection<uint>? rowPageBreaks,
        IReadOnlyCollection<uint>? columnPageBreaks,
        WorksheetPageOrder pageOrder,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double actualWidth,
        double actualHeight,
        IReadOnlyDictionary<uint, double>? rowHeights = null,
        double defaultRowHeight = PagePaginationPlanner.NominalRowHeight,
        IReadOnlyDictionary<uint, double>? columnWidths = null,
        double defaultColumnWidth = 0.0,
        double headerMarginInches = 0.0,
        double footerMarginInches = 0.0,
        Func<uint, bool>? isRowHidden = null,
        Func<uint, bool>? isColumnHidden = null)
    {
        if (printArea is not { } range ||
            viewport.RowMetrics.Count == 0 ||
            viewport.ColMetrics.Count == 0)
        {
            return new PageBreakPreviewLayout([], [], []);
        }

        var gridBounds = LayoutRect.FromCorners(
            rowHeaderWidth,
            columnHeaderHeight,
            Math.Max(rowHeaderWidth, actualWidth),
            Math.Max(columnHeaderHeight, actualHeight));
        if (gridBounds.Width <= 0 || gridBounds.Height <= 0)
            return new PageBreakPreviewLayout([], [], []);

        if (!TryCalculateVisibleRangeBounds(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                actualWidth,
                actualHeight,
                out var printBounds,
                out _))
        {
            return new PageBreakPreviewLayout([], [], []);
        }

        var effectiveRowHeights = rowHeights ?? new Dictionary<uint, double>();
        var effectiveColumnWidths = columnWidths ?? new Dictionary<uint, double>();
        var effectiveDefaultColumnWidth = defaultColumnWidth > 0
            ? defaultColumnWidth
            : ColumnWidthPixelMapper.PixelsToColumnWidth(PagePaginationPlanner.MinimumPrintColumnWidth);
        var pagination = PagePaginationPlanner.Paginate(
            range,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins,
            effectiveRowHeights,
            defaultRowHeight,
            effectiveColumnWidths,
            effectiveDefaultColumnWidth,
            headerMarginInches,
            footerMarginInches,
            rowPageBreaks,
            columnPageBreaks,
            isRowHidden,
            isColumnHidden);

        var pages = BuildVisiblePages(
            viewport,
            range,
            pagination.RowSegments,
            pagination.ColumnSegments,
            pageOrder,
            rowHeaderWidth,
            columnHeaderHeight,
            actualWidth,
            actualHeight);
        var automaticBreaks = BuildAutomaticBreakLines(
            viewport,
            pagination.RowSegments,
            pagination.ColumnSegments,
            rowPageBreaks,
            columnPageBreaks,
            printBounds,
            rowHeaderWidth,
            columnHeaderHeight);

        return new PageBreakPreviewLayout(
            BuildOutsideMasks(gridBounds, printBounds),
            pages,
            automaticBreaks);
    }

    /// <summary>
    /// The font size for the "Page N" watermark drawn behind each preview page: a fraction of the
    /// page's shorter side, clamped to a legible range.
    /// </summary>
    public static double CalculateWatermarkFontSize(LayoutRect pageBounds)
    {
        var size = Math.Min(pageBounds.Width, pageBounds.Height) * 0.18;
        return Math.Clamp(size, 24.0, 96.0);
    }

    private static IReadOnlyList<PageBreakPreviewPageLayout> BuildVisiblePages(
        ViewportModel viewport,
        GridRange printArea,
        IReadOnlyList<PageAxisSegment> rowSegments,
        IReadOnlyList<PageAxisSegment> columnSegments,
        WorksheetPageOrder pageOrder,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double actualWidth,
        double actualHeight)
    {
        var pages = new List<PageBreakPreviewPageLayout>(rowSegments.Count * columnSegments.Count);
        foreach (var page in PrintPageGridPlanner.BuildVisualIndexes(rowSegments.Count, columnSegments.Count, pageOrder))
        {
            var rowSegment = rowSegments[page.RowPageIndex];
            var columnSegment = columnSegments[page.ColumnPageIndex];
            var pageRange = new GridRange(
                new CellAddress(printArea.Start.Sheet, rowSegment.Start, columnSegment.Start),
                new CellAddress(printArea.Start.Sheet, rowSegment.End, columnSegment.End));

            if (!TryCalculateVisibleRangeBounds(
                    viewport,
                    pageRange,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    actualWidth,
                    actualHeight,
                    out var pageBounds,
                    out var visibleEdges))
            {
                continue;
            }

            pages.Add(new PageBreakPreviewPageLayout(page.SheetPageNumber, pageBounds, visibleEdges));
        }

        return pages;
    }

    private static IReadOnlyList<LayoutRect> BuildOutsideMasks(LayoutRect gridBounds, LayoutRect printBounds)
    {
        var masks = new List<LayoutRect>(4);
        AddMask(masks, new LayoutRect(gridBounds.Left, gridBounds.Top, gridBounds.Width, printBounds.Top - gridBounds.Top));
        AddMask(masks, new LayoutRect(gridBounds.Left, printBounds.Bottom, gridBounds.Width, gridBounds.Bottom - printBounds.Bottom));
        AddMask(masks, new LayoutRect(gridBounds.Left, printBounds.Top, printBounds.Left - gridBounds.Left, printBounds.Height));
        AddMask(masks, new LayoutRect(printBounds.Right, printBounds.Top, gridBounds.Right - printBounds.Right, printBounds.Height));
        return masks;
    }

    private static IReadOnlyList<PageBreakPreviewBreakLine> BuildAutomaticBreakLines(
        ViewportModel viewport,
        IReadOnlyList<PageAxisSegment> rowSegments,
        IReadOnlyList<PageAxisSegment> columnSegments,
        IReadOnlyCollection<uint>? manualRowBreaks,
        IReadOnlyCollection<uint>? manualColumnBreaks,
        LayoutRect printBounds,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var lines = new List<PageBreakPreviewBreakLine>();
        var manualRows = manualRowBreaks is { Count: > 0 } ? manualRowBreaks.ToHashSet() : null;
        for (var i = 1; i < rowSegments.Count; i++)
        {
            var row = rowSegments[i].Start;
            if (manualRows?.Contains(row) == true ||
                !TryFindRowTop(viewport.RowMetrics, row, out var rowTop))
            {
                continue;
            }

            var y = rowTop + columnHeaderHeight;
            if (y > printBounds.Top && y < printBounds.Bottom)
                lines.Add(new PageBreakPreviewBreakLine(new LayoutPoint(printBounds.Left, y), new LayoutPoint(printBounds.Right, y)));
        }

        var manualColumns = manualColumnBreaks is { Count: > 0 } ? manualColumnBreaks.ToHashSet() : null;
        for (var i = 1; i < columnSegments.Count; i++)
        {
            var column = columnSegments[i].Start;
            if (manualColumns?.Contains(column) == true ||
                !TryFindColumnLeft(viewport.ColMetrics, column, out var columnLeft))
            {
                continue;
            }

            var x = columnLeft + rowHeaderWidth;
            if (x > printBounds.Left && x < printBounds.Right)
                lines.Add(new PageBreakPreviewBreakLine(new LayoutPoint(x, printBounds.Top), new LayoutPoint(x, printBounds.Bottom)));
        }

        return lines;
    }

    private static void AddMask(List<LayoutRect> masks, LayoutRect rect)
    {
        if (rect.Width > 0 && rect.Height > 0)
            masks.Add(rect);
    }

    private static bool TryCalculateVisibleRangeBounds(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double actualWidth,
        double actualHeight,
        out LayoutRect bounds,
        out PageBreakPreviewPageEdges visibleEdges)
    {
        bounds = default;
        visibleEdges = default;
        var rows = viewport.RowMetrics;
        var columns = viewport.ColMetrics;
        if (rows.Count == 0 || columns.Count == 0)
            return false;
        if (range.End.Row < rows[0].Row || range.Start.Row > rows[^1].Row)
            return false;
        if (range.End.Col < columns[0].Col || range.Start.Col > columns[^1].Col)
            return false;

        var isTopEdgeVisible = TryFindRowTop(rows, range.Start.Row, out var rowTop);
        var isBottomEdgeVisible = TryFindRowBottom(rows, range.End.Row, out var rowBottom);
        var isLeftEdgeVisible = TryFindColumnLeft(columns, range.Start.Col, out var columnLeft);
        var isRightEdgeVisible = TryFindColumnRight(columns, range.End.Col, out var columnRight);

        var top = isTopEdgeVisible
            ? rowTop + columnHeaderHeight
            : columnHeaderHeight;
        var bottom = isBottomEdgeVisible
            ? rowBottom + columnHeaderHeight
            : actualHeight;
        var left = isLeftEdgeVisible
            ? columnLeft + rowHeaderWidth
            : rowHeaderWidth;
        var right = isRightEdgeVisible
            ? columnRight + rowHeaderWidth
            : actualWidth;

        bounds = LayoutRect.FromCorners(
            Math.Max(rowHeaderWidth, left),
            Math.Max(columnHeaderHeight, top),
            Math.Min(actualWidth, right),
            Math.Min(actualHeight, bottom));
        visibleEdges = new PageBreakPreviewPageEdges(
            isTopEdgeVisible,
            isBottomEdgeVisible,
            isLeftEdgeVisible,
            isRightEdgeVisible);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryFindRowTop(IReadOnlyList<RowMetric> rows, uint row, out double top)
    {
        foreach (var metric in rows)
        {
            if (metric.Row == row)
            {
                top = metric.TopOffset;
                return true;
            }

            if (metric.Row > row)
                break;
        }

        top = 0;
        return false;
    }

    private static bool TryFindRowBottom(IReadOnlyList<RowMetric> rows, uint row, out double bottom)
    {
        foreach (var metric in rows)
        {
            if (metric.Row == row)
            {
                bottom = metric.TopOffset + metric.Height;
                return true;
            }

            if (metric.Row > row)
                break;
        }

        bottom = 0;
        return false;
    }

    private static bool TryFindColumnLeft(IReadOnlyList<ColMetric> columns, uint column, out double left)
    {
        foreach (var metric in columns)
        {
            if (metric.Col == column)
            {
                left = metric.LeftOffset;
                return true;
            }

            if (metric.Col > column)
                break;
        }

        left = 0;
        return false;
    }

    private static bool TryFindColumnRight(IReadOnlyList<ColMetric> columns, uint column, out double right)
    {
        foreach (var metric in columns)
        {
            if (metric.Col == column)
            {
                right = metric.LeftOffset + metric.Width;
                return true;
            }

            if (metric.Col > column)
                break;
        }

        right = 0;
        return false;
    }
}
