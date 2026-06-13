using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public sealed record PageBreakPreviewPageLayout(
    int PageNumber,
    Rect Bounds,
    PageBreakPreviewPageEdges VisibleEdges);

public readonly record struct PageBreakPreviewPageEdges(
    bool Top,
    bool Bottom,
    bool Left,
    bool Right);

public sealed record PageBreakPreviewBreakLine(
    Point Start,
    Point End);

public sealed record PageBreakPreviewLayout(
    IReadOnlyList<Rect> OutsidePrintAreaMasks,
    IReadOnlyList<PageBreakPreviewPageLayout> Pages,
    IReadOnlyList<PageBreakPreviewBreakLine> AutomaticBreakLines);

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
        double actualHeight)
    {
        if (printArea is not { } range ||
            viewport.RowMetrics.Count == 0 ||
            viewport.ColMetrics.Count == 0)
        {
            return new PageBreakPreviewLayout([], [], []);
        }

        var gridBounds = new Rect(
            rowHeaderWidth,
            columnHeaderHeight,
            Math.Max(0, actualWidth - rowHeaderWidth),
            Math.Max(0, actualHeight - columnHeaderHeight));
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

        var (rowsPerPage, columnsPerPage) = CalculatePageCapacity(
            range,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins);
        var rowSegments = BuildSegments(PrintLayoutPlanner.BuildRowPlans(
            range,
            printTitleRows,
            rowsPerPage,
            rowPageBreaks));
        var columnSegments = BuildSegments(PrintLayoutPlanner.BuildColumnPlans(
            range,
            printTitleColumns,
            columnsPerPage,
            columnPageBreaks));
        var pages = BuildVisiblePages(
            viewport,
            range,
            rowSegments,
            columnSegments,
            pageOrder,
            rowHeaderWidth,
            columnHeaderHeight,
            actualWidth,
            actualHeight);
        var automaticBreaks = BuildAutomaticBreakLines(
            viewport,
            rowSegments,
            columnSegments,
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

    public static double CalculateWatermarkFontSize(Rect pageBounds)
    {
        var size = Math.Min(pageBounds.Width, pageBounds.Height) * 0.18;
        return Math.Clamp(size, 24.0, 96.0);
    }

    private static IReadOnlyList<PageBreakPreviewPageLayout> BuildVisiblePages(
        ViewportModel viewport,
        GridRange printArea,
        IReadOnlyList<AxisSegment> rowSegments,
        IReadOnlyList<AxisSegment> columnSegments,
        WorksheetPageOrder pageOrder,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double actualWidth,
        double actualHeight)
    {
        var pages = new List<PageBreakPreviewPageLayout>(rowSegments.Count * columnSegments.Count);
        for (var rowIndex = 0; rowIndex < rowSegments.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columnSegments.Count; columnIndex++)
            {
                var pageRange = new GridRange(
                    new CellAddress(printArea.Start.Sheet, rowSegments[rowIndex].Start, columnSegments[columnIndex].Start),
                    new CellAddress(printArea.Start.Sheet, rowSegments[rowIndex].End, columnSegments[columnIndex].End));

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

                var pageNumber = pageOrder == WorksheetPageOrder.OverThenDown
                    ? (rowIndex * columnSegments.Count) + columnIndex + 1
                    : (columnIndex * rowSegments.Count) + rowIndex + 1;
                pages.Add(new PageBreakPreviewPageLayout(pageNumber, pageBounds, visibleEdges));
            }
        }

        return pages;
    }

    private static IReadOnlyList<AxisSegment> BuildSegments(IReadOnlyList<PrintPageRowPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyRows, static plan => plan.TitleRows);

    private static IReadOnlyList<AxisSegment> BuildSegments(IReadOnlyList<PrintPageColumnPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyColumns, static plan => plan.TitleColumns);

    private static IReadOnlyList<AxisSegment> BuildSegments<TPlan>(
        IReadOnlyList<TPlan> plans,
        Func<TPlan, IReadOnlyList<uint>> getBodyIndexes,
        Func<TPlan, IReadOnlyList<uint>> getTitleIndexes)
    {
        var segments = new List<AxisSegment>(plans.Count);
        foreach (var plan in plans)
        {
            var indexes = getBodyIndexes(plan);
            if (indexes.Count == 0)
                indexes = getTitleIndexes(plan);
            if (indexes.Count == 0)
                continue;

            segments.Add(new AxisSegment(indexes[0], indexes[^1]));
        }

        return segments;
    }

    private static IReadOnlyList<Rect> BuildOutsideMasks(Rect gridBounds, Rect printBounds)
    {
        var masks = new List<Rect>(4);
        AddMask(masks, new Rect(gridBounds.Left, gridBounds.Top, gridBounds.Width, printBounds.Top - gridBounds.Top));
        AddMask(masks, new Rect(gridBounds.Left, printBounds.Bottom, gridBounds.Width, gridBounds.Bottom - printBounds.Bottom));
        AddMask(masks, new Rect(gridBounds.Left, printBounds.Top, printBounds.Left - gridBounds.Left, printBounds.Height));
        AddMask(masks, new Rect(printBounds.Right, printBounds.Top, gridBounds.Right - printBounds.Right, printBounds.Height));
        return masks;
    }

    private static IReadOnlyList<PageBreakPreviewBreakLine> BuildAutomaticBreakLines(
        ViewportModel viewport,
        IReadOnlyList<AxisSegment> rowSegments,
        IReadOnlyList<AxisSegment> columnSegments,
        IReadOnlyCollection<uint>? manualRowBreaks,
        IReadOnlyCollection<uint>? manualColumnBreaks,
        Rect printBounds,
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
                lines.Add(new PageBreakPreviewBreakLine(new Point(printBounds.Left, y), new Point(printBounds.Right, y)));
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
                lines.Add(new PageBreakPreviewBreakLine(new Point(x, printBounds.Top), new Point(x, printBounds.Bottom)));
        }

        return lines;
    }

    private static (uint RowsPerPage, uint ColumnsPerPage) CalculatePageCapacity(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        const double dpi = 96.0;
        const double minimumPrintColumnWidth = 40.0;
        const double rowHeight = 20.0;

        var pageSize = WorksheetPageLayout.GetPageSizeInches(paperSize, orientation);
        var printableWidth = Math.Max(1.0, (pageSize.Width - margins.Left - margins.Right) * dpi);
        var printableHeight = Math.Max(1.0, (pageSize.Height - margins.Top - margins.Bottom) * dpi);
        var rowsPerPage = Math.Max(1u, (uint)Math.Floor(printableHeight / rowHeight));
        var columnsPerPage = Math.Max(1u, (uint)Math.Floor(printableWidth / minimumPrintColumnWidth));

        rowsPerPage = ApplyScaleToFitCapacity(
            rowsPerPage,
            printRange.Start.Row,
            printRange.End.Row,
            printTitleRows,
            CellAddress.MaxRow,
            scaleToFit.ScalePercent,
            scaleToFit.FitToPagesTall);
        columnsPerPage = ApplyScaleToFitCapacity(
            columnsPerPage,
            printRange.Start.Col,
            printRange.End.Col,
            printTitleColumns,
            CellAddress.MaxCol,
            scaleToFit.ScalePercent,
            scaleToFit.FitToPagesWide);

        return (rowsPerPage, columnsPerPage);
    }

    private static uint ApplyScaleToFitCapacity(
        uint baseItemsPerPage,
        uint start,
        uint end,
        WorksheetRepeatRange? repeat,
        uint maxItem,
        int? scalePercent,
        int? fitToPages)
    {
        if (scalePercent is { } percent and >= 10 and <= 400)
            return Math.Max(1, (uint)Math.Floor(baseItemsPerPage * (100d / percent)));

        if (fitToPages is not { } pageCount || pageCount < 1)
            return baseItemsPerPage;

        var titleCount = CountRepeatItems(repeat, maxItem);
        var bodyCount = CountBodyItems(start, end, repeat);
        if (bodyCount == 0)
            return Math.Max(1, titleCount);

        var bodyItemsPerPage = (uint)Math.Ceiling(bodyCount / (double)pageCount);
        return Math.Max(1, bodyItemsPerPage + titleCount);
    }

    private static uint CountRepeatItems(WorksheetRepeatRange? repeat, uint maxItem)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0;

        return Math.Min(range.End, maxItem) - range.Start + 1;
    }

    private static uint CountBodyItems(uint start, uint end, WorksheetRepeatRange? repeat)
    {
        if (end < start)
            return 0;

        var count = end - start + 1;
        if (repeat is not { } range || range.End < start || range.Start > end)
            return count;

        var overlapStart = Math.Max(start, range.Start);
        var overlapEnd = Math.Min(end, range.End);
        return overlapEnd >= overlapStart
            ? count - (overlapEnd - overlapStart + 1)
            : count;
    }

    private static void AddMask(List<Rect> masks, Rect rect)
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
        out Rect bounds,
        out PageBreakPreviewPageEdges visibleEdges)
    {
        bounds = Rect.Empty;
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

        bounds = new Rect(
            new Point(Math.Max(rowHeaderWidth, left), Math.Max(columnHeaderHeight, top)),
            new Point(Math.Min(actualWidth, right), Math.Min(actualHeight, bottom)));
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

    private readonly record struct AxisSegment(uint Start, uint End);
}
