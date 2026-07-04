using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// One contiguous run of rows or columns that belong to a single page along one axis, identified by
/// its first and last 1-based sheet index. Title (repeat) rows/columns that are reprinted on every
/// page are folded into the segment, so the segment spans the page's whole printed extent.
/// </summary>
public readonly record struct PageAxisSegment(uint Start, uint End);

/// <summary>
/// The per-axis page capacity used to slice a print range into pages: how many rows fit on one page
/// and how many columns fit on one page once paper size, margins, orientation, and the active
/// scale-to-fit setting have been applied.
/// </summary>
public readonly record struct PageCapacity(uint RowsPerPage, uint ColumnsPerPage);

/// <summary>
/// Renderer-facing worksheet pagination plan. The neutral planner owns the page capacity and row/
/// column slicing; renderers consume the row and column plans and keep only platform drawing code.
/// </summary>
public sealed record PagePaginationPlan(
    IReadOnlyList<PrintPageRowPlan> RowPlans,
    IReadOnlyList<PrintPageColumnPlan> ColumnPlans,
    PageCapacity Capacity,
    double EffectiveScalePercent)
{
    public int RowPageCount => RowPlans.Count;
    public int ColumnPageCount => ColumnPlans.Count;
    public int PageCount => RowPlans.Count * ColumnPlans.Count;
}

/// <summary>
/// The result of slicing a print range into pages: the row and column segments along each axis (the
/// cross product of which forms the page grid), plus the effective scale percent that a fit-to-pages
/// request resolves to. <see cref="EffectiveScalePercent"/> is the explicit scale when one is set,
/// otherwise the smaller of the horizontal/vertical shrink ratios implied by the fit-to request, or
/// 100 when neither is set.
/// </summary>
public sealed record PagePaginationResult(
    IReadOnlyList<PageAxisSegment> RowSegments,
    IReadOnlyList<PageAxisSegment> ColumnSegments,
    double EffectiveScalePercent)
{
    public int RowPageCount => RowSegments.Count;
    public int ColumnPageCount => ColumnSegments.Count;
    public int PageCount => RowSegments.Count * ColumnSegments.Count;
}

/// <summary>
/// Pure pagination math shared by the desktop hosts and print/export. Given a print range, the active
/// page setup (paper size, margins, orientation, scale or fit-to N-wide × M-tall), the repeat
/// rows/columns, and any manual page breaks, it computes the page grid and the effective print scale.
///
/// The capacity math mirrors the source desktop layout: a nominal printable area is derived in pixels
/// from the paper size minus margins (at 96 dpi), divided by a nominal row height / minimum column
/// width to get a baseline rows/columns-per-page, then adjusted for the explicit scale percent or the
/// fit-to-pages request. Page slicing itself is delegated to <see cref="PrintLayoutPlanner"/> so this
/// layer never diverges from the export planner's row/column page plans.
/// </summary>
public static class PagePaginationPlanner
{
    /// <summary>Drawing surface resolution the source layout assumes for the printable area, in dots per inch.</summary>
    public const double Dpi = 96.0;

    /// <summary>
    /// Fallback printed column width in pixels used when no column-width information is available for
    /// a column (e.g. when calling the overload that does not receive sheet sizing data).
    /// </summary>
    public const double MinimumPrintColumnWidth = 40.0;

    /// <summary>
    /// Fallback printed row height in pixels used when no row-height information is available for a row
    /// (e.g. when calling the overload that does not receive sheet sizing data).
    /// </summary>
    public const double NominalRowHeight = 20.0;

    private const int MinScalePercent = 10;
    private const int MaxScalePercent = 400;

    /// <summary>
    /// Computes the baseline rows/columns that fit on one page, using the actual per-row heights and
    /// per-column widths from the sheet model. The average row height across the print range and the
    /// average column width (in pixels) across the print range are used to estimate how many items fit
    /// on one page. The printable body height is the paper height minus page margins minus the
    /// header/footer margin reservations (PR5 fix); the printable body width is the paper width minus
    /// page margins. Falls back to <see cref="NominalRowHeight"/> or <see cref="MinimumPrintColumnWidth"/>
    /// when a row/column has no recorded size.
    /// </summary>
    /// <param name="printRange">The range of rows and columns being printed.</param>
    /// <param name="scaleToFit">Explicit scale percent or fit-to-pages request (or neither).</param>
    /// <param name="printTitleRows">Rows repeated on every page, or null.</param>
    /// <param name="printTitleColumns">Columns repeated on every page, or null.</param>
    /// <param name="paperSize">Paper size for page dimension lookup.</param>
    /// <param name="orientation">Portrait or landscape.</param>
    /// <param name="margins">Page margins in inches.</param>
    /// <param name="rowHeights">Per-row height overrides in pixels (1-based row → pixels). May be empty.</param>
    /// <param name="defaultRowHeight">Default row height in pixels, used for rows absent from <paramref name="rowHeights"/>.</param>
    /// <param name="columnWidths">Per-column width overrides in characters (1-based col → characters). May be empty.</param>
    /// <param name="defaultColumnWidth">Default column width in characters, used for columns absent from <paramref name="columnWidths"/>.</param>
    /// <param name="headerMarginInches">Distance from page top to header, in inches (PR5: subtracted from body height).</param>
    /// <param name="footerMarginInches">Distance from page bottom to footer, in inches (PR5: subtracted from body height).</param>
    public static PageCapacity CalculatePageCapacity(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyDictionary<uint, double> rowHeights,
        double defaultRowHeight,
        IReadOnlyDictionary<uint, double> columnWidths,
        double defaultColumnWidth,
        double headerMarginInches,
        double footerMarginInches)
    {
        var pageSize = WorksheetPageLayout.GetPageSizeInches(paperSize, orientation);

        // PR5: subtract header + footer margin reservations from the body height.
        var headerFooterReservedPx = Math.Max(0.0, headerMarginInches + footerMarginInches) * Dpi;
        var printableWidth = Math.Max(1.0, (pageSize.Width - margins.Left - margins.Right) * Dpi);
        var printableHeight = Math.Max(1.0, (pageSize.Height - margins.Top - margins.Bottom) * Dpi - headerFooterReservedPx);

        // PR1: compute average row height from actual per-row sizes across the print range.
        var effectiveRowHeight = AverageRowHeightPixels(
            printRange.Start.Row, printRange.End.Row,
            rowHeights, defaultRowHeight);

        // PR1: compute average column width in pixels from actual per-column character widths.
        var effectiveColWidth = AverageColumnWidthPixels(
            printRange.Start.Col, printRange.End.Col,
            columnWidths, defaultColumnWidth);

        var rowsPerPage = Math.Max(1u, (uint)Math.Floor(printableHeight / effectiveRowHeight));
        var columnsPerPage = Math.Max(1u, (uint)Math.Floor(printableWidth / effectiveColWidth));

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

        return new PageCapacity(rowsPerPage, columnsPerPage);
    }

    /// <summary>
    /// Computes the baseline rows/columns that fit on one page from paper size, margins, and
    /// orientation, then applies the scale-to-fit setting (explicit percent or fit-to-pages) per axis.
    /// Uses fixed fallback constants (<see cref="NominalRowHeight"/>, <see cref="MinimumPrintColumnWidth"/>)
    /// for row height and column width. Prefer the overload that accepts sheet row/column sizing for
    /// accurate pagination of sheets with custom row heights or column widths.
    /// </summary>
    public static PageCapacity CalculatePageCapacity(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        return CalculatePageCapacity(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins,
            rowHeights: new Dictionary<uint, double>(),
            defaultRowHeight: NominalRowHeight,
            columnWidths: new Dictionary<uint, double>(),
            defaultColumnWidth: ColumnWidthPixelMapper.PixelsToColumnWidth(MinimumPrintColumnWidth),
            headerMarginInches: 0.0,
            footerMarginInches: 0.0);
    }

    /// <summary>
    /// Slices a print range into page segments along both axes using actual sheet row heights and
    /// column widths, and reports the effective scale. Title rows/columns are reprinted on every page;
    /// manual breaks force a new page; the explicit scale percent (when set) wins, otherwise a
    /// fit-to-pages request resolves to the shrink ratio that makes the body fit the requested page count.
    /// </summary>
    public static PagePaginationPlan BuildPlan(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyDictionary<uint, double> rowHeights,
        double defaultRowHeight,
        IReadOnlyDictionary<uint, double> columnWidths,
        double defaultColumnWidth,
        double headerMarginInches,
        double footerMarginInches,
        IReadOnlyCollection<uint>? rowPageBreaks = null,
        IReadOnlyCollection<uint>? columnPageBreaks = null,
        Func<uint, bool>? isRowHidden = null,
        Func<uint, bool>? isColumnHidden = null)
    {
        var capacity = CalculatePageCapacity(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins,
            rowHeights,
            defaultRowHeight,
            columnWidths,
            defaultColumnWidth,
            headerMarginInches,
            footerMarginInches);

        var rowPlans = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            printTitleRows,
            capacity.RowsPerPage,
            rowPageBreaks,
            isRowHidden);
        var columnPlans = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            printTitleColumns,
            capacity.ColumnsPerPage,
            columnPageBreaks,
            isColumnHidden);

        var effectiveScale = CalculateEffectiveScalePercent(scaleToFit, rowPlans.Count, columnPlans.Count);
        return new PagePaginationPlan(rowPlans, columnPlans, capacity, effectiveScale);
    }

    /// <summary>
    /// Slices a print range into page segments along both axes and reports the effective scale.
    /// Title rows/columns are reprinted on every page; manual breaks force a new page; the explicit
    /// scale percent (when set) wins, otherwise a fit-to-pages request resolves to the shrink ratio
    /// that makes the body fit the requested page count. Uses fixed fallback constants for row height
    /// and column width. Prefer the overload that accepts sheet row/column sizing.
    /// </summary>
    public static PagePaginationPlan BuildPlan(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyCollection<uint>? rowPageBreaks = null,
        IReadOnlyCollection<uint>? columnPageBreaks = null)
    {
        var capacity = CalculatePageCapacity(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins);

        var rowPlans = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            printTitleRows,
            capacity.RowsPerPage,
            rowPageBreaks);
        var columnPlans = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            printTitleColumns,
            capacity.ColumnsPerPage,
            columnPageBreaks);

        var effectiveScale = CalculateEffectiveScalePercent(scaleToFit, rowPlans.Count, columnPlans.Count);
        return new PagePaginationPlan(rowPlans, columnPlans, capacity, effectiveScale);
    }

    /// <summary>
    /// Slices a print range into page segments along both axes using actual sheet row heights and
    /// column widths, and reports the effective scale.
    /// </summary>
    public static PagePaginationResult Paginate(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyDictionary<uint, double> rowHeights,
        double defaultRowHeight,
        IReadOnlyDictionary<uint, double> columnWidths,
        double defaultColumnWidth,
        double headerMarginInches,
        double footerMarginInches,
        IReadOnlyCollection<uint>? rowPageBreaks = null,
        IReadOnlyCollection<uint>? columnPageBreaks = null)
    {
        var plan = BuildPlan(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins,
            rowHeights,
            defaultRowHeight,
            columnWidths,
            defaultColumnWidth,
            headerMarginInches,
            footerMarginInches,
            rowPageBreaks,
            columnPageBreaks);

        return new PagePaginationResult(
            BuildSegments(plan.RowPlans),
            BuildSegments(plan.ColumnPlans),
            plan.EffectiveScalePercent);
    }

    /// <summary>
    /// Slices a print range into page segments along both axes and reports the effective scale.
    /// Title rows/columns are reprinted on every page; manual breaks force a new page; the explicit
    /// scale percent (when set) wins, otherwise a fit-to-pages request resolves to the shrink ratio
    /// that makes the body fit the requested page count.
    /// </summary>
    public static PagePaginationResult Paginate(
        GridRange printRange,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins,
        IReadOnlyCollection<uint>? rowPageBreaks = null,
        IReadOnlyCollection<uint>? columnPageBreaks = null)
    {
        var plan = BuildPlan(
            printRange,
            scaleToFit,
            printTitleRows,
            printTitleColumns,
            paperSize,
            orientation,
            margins,
            rowPageBreaks,
            columnPageBreaks);

        return new PagePaginationResult(
            BuildSegments(plan.RowPlans),
            BuildSegments(plan.ColumnPlans),
            plan.EffectiveScalePercent);
    }

    /// <summary>
    /// Resolves the scale-to-fit setting to a single scale percent. An explicit percent (clamped to
    /// the supported range) wins. Otherwise a fit-to N-wide × M-tall request resolves to
    /// <c>100 × min(wide / actualColumnPages, tall / actualRowPages)</c> — the same shrink the source
    /// layout applies so the sheet collapses onto the requested page count. With neither set the scale
    /// is 100.
    /// </summary>
    public static double CalculateEffectiveScalePercent(
        WorksheetScaleToFit scaleToFit,
        int actualRowPages,
        int actualColumnPages)
    {
        if (scaleToFit.ScalePercent is { } percent)
            return Math.Clamp(percent, MinScalePercent, MaxScalePercent);

        var ratio = 1.0;
        if (scaleToFit.FitToPagesWide is { } wide and >= 1 && actualColumnPages > wide)
            ratio = Math.Min(ratio, wide / (double)actualColumnPages);
        if (scaleToFit.FitToPagesTall is { } tall and >= 1 && actualRowPages > tall)
            ratio = Math.Min(ratio, tall / (double)actualRowPages);

        return 100.0 * ratio;
    }

    internal static uint ApplyScaleToFitCapacity(
        uint baseItemsPerPage,
        uint start,
        uint end,
        WorksheetRepeatRange? repeat,
        uint maxItem,
        int? scalePercent,
        int? fitToPages)
    {
        if (scalePercent is { } percent and >= MinScalePercent and <= MaxScalePercent)
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

    private static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageRowPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyRows, static plan => plan.TitleRows);

    private static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageColumnPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyColumns, static plan => plan.TitleColumns);

    private static IReadOnlyList<PageAxisSegment> BuildSegments<TPlan>(
        IReadOnlyList<TPlan> plans,
        Func<TPlan, IReadOnlyList<uint>> getBodyIndexes,
        Func<TPlan, IReadOnlyList<uint>> getTitleIndexes)
    {
        var segments = new List<PageAxisSegment>(plans.Count);
        foreach (var plan in plans)
        {
            var indexes = getBodyIndexes(plan);
            if (indexes.Count == 0)
                indexes = getTitleIndexes(plan);
            if (indexes.Count == 0)
                continue;

            segments.Add(new PageAxisSegment(indexes[0], indexes[^1]));
        }

        return segments;
    }

    /// <summary>
    /// Returns the average row height in pixels across the rows [startRow, endRow]. Each row's height
    /// is taken from <paramref name="rowHeights"/> when present; rows absent from the dictionary use
    /// <paramref name="defaultRowHeight"/>. Falls back to <see cref="NominalRowHeight"/> when
    /// <paramref name="defaultRowHeight"/> is not positive.
    /// </summary>
    public static double AverageRowHeightPixels(
        uint startRow,
        uint endRow,
        IReadOnlyDictionary<uint, double> rowHeights,
        double defaultRowHeight)
    {
        var fallback = defaultRowHeight > 0 ? defaultRowHeight : NominalRowHeight;
        if (endRow < startRow)
            return fallback;

        var total = 0.0;
        var count = endRow - startRow + 1;
        for (var row = startRow; row <= endRow; row++)
            total += rowHeights.TryGetValue(row, out var h) && h > 0 ? h : fallback;

        return total / count;
    }

    /// <summary>
    /// Returns the average column width in pixels across the columns [startCol, endCol]. Each column's
    /// character-unit width is taken from <paramref name="columnWidths"/> when present; columns absent
    /// from the dictionary use <paramref name="defaultColumnWidth"/>. The character-unit width is
    /// converted to pixels via <see cref="ColumnWidthPixelMapper.ColumnWidthToPixels"/>. Falls back to
    /// <see cref="MinimumPrintColumnWidth"/> when the computed pixel width would be zero or negative.
    /// </summary>
    public static double AverageColumnWidthPixels(
        uint startCol,
        uint endCol,
        IReadOnlyDictionary<uint, double> columnWidths,
        double defaultColumnWidth)
    {
        var fallbackChars = defaultColumnWidth > 0 ? defaultColumnWidth : ColumnWidthPixelMapper.PixelsToColumnWidth(MinimumPrintColumnWidth);
        var fallbackPx = Math.Max(MinimumPrintColumnWidth, ColumnWidthPixelMapper.ColumnWidthToPixels(fallbackChars));
        if (endCol < startCol)
            return fallbackPx;

        var total = 0.0;
        var count = endCol - startCol + 1;
        for (var col = startCol; col <= endCol; col++)
        {
            var chars = columnWidths.TryGetValue(col, out var w) && w > 0 ? w : fallbackChars;
            var px = ColumnWidthPixelMapper.ColumnWidthToPixels(chars);
            total += Math.Max(MinimumPrintColumnWidth, px);
        }

        return total / count;
    }
}
