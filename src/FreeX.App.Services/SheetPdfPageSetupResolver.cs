using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Converts a sheet's OOXML page-setup fields into PDF-native geometry (points at 72 dpi) and into the
/// <see cref="PortablePdfDocumentOptions"/> the PDF content builder consumes. Also derives the
/// <see cref="WorkbookExportPrintPageCapacity"/> (rows/columns per page) from the same page setup so
/// <see cref="WorkbookExportPrintPlanner"/> can paginate correctly for each sheet.
///
/// <para>
/// All dimension math converts inches → PDF points (1 inch = 72 pt). Paper dimensions come from
/// <see cref="WorksheetPageLayout.GetPageSizeInches"/>, which already handles the landscape swap.
/// Margin math is inset-from-paper: the content body rect is further reduced by header/footer band
/// reservations (<see cref="Sheet.HeaderMargin"/> / <see cref="Sheet.FooterMargin"/>).
/// </para>
///
/// <para>
/// Row/column pagination capacity mirrors the shared <c>PagePaginationPlanner</c> math at 96 dpi so
/// the row-page / column-page counts computed here match what the print-preview planner would produce.
/// The per-sheet capacity is fed into <see cref="WorkbookExportPrintPlanner"/> so each sheet breaks
/// correctly rather than using the hardcoded 28 × 8 fallback.
/// </para>
/// </summary>
public static class SheetPdfPageSetupResolver
{
    /// <summary>Points per inch in PDF user space.</summary>
    public const double PdfPointsPerInch = 72.0;

    /// <summary>
    /// Layout DPI assumed by the shared pagination planner (96 dpi screen pixels). Row heights and
    /// column widths are stored in screen pixels / character units at this resolution.
    /// </summary>
    private const double LayoutDpi = 96.0;

    /// <summary>
    /// Fallback row height in pixels used when a row has no recorded height (matches
    /// <c>PagePaginationPlanner.NominalRowHeight</c>).
    /// </summary>
    private const double NominalRowHeightPx = 20.0;

    /// <summary>
    /// Minimum column width in pixels used as a floor for columns with no recorded width (matches
    /// <c>PagePaginationPlanner.MinimumPrintColumnWidth</c>).
    /// </summary>
    private const double MinimumColumnWidthPx = 40.0;

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns per-page PDF dimensions (points) and margin/header-footer geometry for a sheet,
    /// driven by its page setup (paper size, orientation, margins, header/footer margins).
    /// The returned <see cref="PortablePdfDocumentOptions"/> uses <c>null</c> for
    /// <see cref="PortablePdfDocumentOptions.HeaderHeightPoints"/> / row/column width constraints;
    /// those fields retain their defaults so callers that do not need them are unaffected.
    /// </summary>
    public static PortablePdfDocumentOptions ResolveOptions(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var (pageWidthPt, pageHeightPt, marginLeftPt, marginRightPt, marginTopPt, marginBottomPt,
             headerBandPt, footerBandPt) = ComputePdfGeometry(sheet);

        _ = headerBandPt;  // acknowledged; header/footer band placement is handled by builder
        _ = footerBandPt;

        // The "margin" stored in PortablePdfDocumentOptions is used as a uniform inset for all
        // four sides in the old builder. We derive the true per-side values here; the new
        // page-setup-aware builder uses the individual values directly. For compat we pass the
        // smallest of the four margins so the fallback builder doesn't over-inset.
        var uniformMarginPt = Math.Min(Math.Min(marginLeftPt, marginRightPt),
                                        Math.Min(marginTopPt, marginBottomPt));

        // The header height is the top margin minus the top body margin, i.e. the gap from the
        // header text to the grid. For the PDF options struct we compute the combined top+header
        // reservation so the grid starts at the right Y.
        var headerHeightPt = marginTopPt - Math.Max(0, sheet.HeaderMargin * PdfPointsPerInch);
        var effectiveHeaderHeight = Math.Max(0, headerHeightPt);

        return new PortablePdfDocumentOptions(
            PageWidthPoints: pageWidthPt,
            PageHeightPoints: pageHeightPt,
            MarginPoints: uniformMarginPt,
            HeaderHeightPoints: effectiveHeaderHeight);
    }

    /// <summary>
    /// Derives the row/column page capacity for a sheet from its page setup (paper, orientation,
    /// margins, scale-to-fit, actual row heights + column widths). The capacity is passed to
    /// <see cref="WorkbookExportPrintPlanner"/> so it slices the sheet into the correct number of pages.
    /// </summary>
    public static WorkbookExportPrintPageCapacity ResolveCapacity(Sheet sheet, GridRange printRange) =>
        ResolveCapacityDetail(sheet, printRange).Capacity;

    /// <summary>
    /// R96-services-print-pagination-exact: the average-row-height/column-width-derived
    /// <see cref="WorkbookExportPrintPageCapacity"/> above is a fixed items-per-page COUNT -- correct
    /// only when every row/column in the print range is the same size. <see cref="PrintLayoutPlanner"/>
    /// slices pages by that fixed count (no size accumulation), so a range mixing a few oversized rows
    /// (wrapped text, picture anchors) with many short ones gets the wrong page break: the average-based
    /// count places far more or fewer rows on a page than actually fit.
    ///
    /// This method instead computes extra "manual" break points from the real ACCUMULATED per-row
    /// height / per-column width -- breaking a page once the running total would exceed the printable
    /// body size -- mirroring <c>PagePaginationPlanner.BuildPlan</c>'s R18-print-pagination-exact-3 fix
    /// for the WPF print path. The returned capacity is deliberately unbounded (large enough that
    /// <see cref="PrintLayoutPlanner"/>'s own count-based slicing never forces an additional break of its
    /// own) so the accumulated break points -- merged with the sheet's real manual page breaks -- are the
    /// only thing that decides where pages split.
    /// </summary>
    public static (WorkbookExportPrintPageCapacity Capacity, IReadOnlyCollection<uint> RowBreaks, IReadOnlyCollection<uint> ColumnBreaks)
        ResolvePagination(Sheet sheet, GridRange printRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var detail = ResolveCapacityDetail(sheet, printRange);

        double RowSize(uint row) => ResolveRowHeightPixels(sheet, row);
        double ColumnSize(uint col) => ResolveColumnWidthPixels(sheet, col);

        var rowScale = ComputeScaleFraction(detail.BaseRowsPerPage, detail.Capacity.RowsPerPage);
        var columnScale = ComputeScaleFraction(detail.BaseColumnsPerPage, detail.Capacity.ColumnsPerPage);

        var rowTitleSize = ComputeRepeatRangeSize(sheet.PrintTitleRows, CellAddress.MaxRow, sheet.IsRowEffectivelyHidden, RowSize);
        var columnTitleSize = ComputeRepeatRangeSize(sheet.PrintTitleColumns, CellAddress.MaxCol, sheet.IsColEffectivelyHidden, ColumnSize);

        var rowBodyBudget = detail.PrintableHeightPx / rowScale - rowTitleSize;
        var columnBodyBudget = detail.PrintableWidthPx / columnScale - columnTitleSize;

        var accumulatedRowBreaks = ComputeAccumulationBreakPoints(
            printRange.Start.Row, printRange.End.Row, sheet.PrintTitleRows, sheet.IsRowEffectivelyHidden, RowSize, rowBodyBudget);
        var accumulatedColumnBreaks = ComputeAccumulationBreakPoints(
            printRange.Start.Col, printRange.End.Col, sheet.PrintTitleColumns, sheet.IsColEffectivelyHidden, ColumnSize, columnBodyBudget);

        var rowBreaks = MergeBreaks(sheet.RowPageBreaks, accumulatedRowBreaks);
        var columnBreaks = MergeBreaks(sheet.ColumnPageBreaks, accumulatedColumnBreaks);

        var unboundedCapacity = new WorkbookExportPrintPageCapacity(
            UnboundedAxisCapacity(printRange.Start.Row, printRange.End.Row),
            UnboundedAxisCapacity(printRange.Start.Col, printRange.End.Col));

        return (unboundedCapacity, rowBreaks, columnBreaks);
    }

    /// <summary>
    /// Internal detail behind <see cref="ResolveCapacity"/>: also exposes the printable body size
    /// (pixels) and the pre-scale-to-fit ("base") per-page item counts, so
    /// <see cref="ResolvePagination"/> can derive the real uniform shrink factor implied by the
    /// resolved capacity and accumulate real row/column sizes against it, instead of re-deriving the
    /// scale from scratch.
    /// </summary>
    private readonly record struct PageCapacityDetail(
        WorkbookExportPrintPageCapacity Capacity,
        double PrintableWidthPx,
        double PrintableHeightPx,
        uint BaseRowsPerPage,
        uint BaseColumnsPerPage);

    private static PageCapacityDetail ResolveCapacityDetail(Sheet sheet, GridRange printRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        // Compute the printable body area in pixels at layout DPI (matching the shared planner).
        var (_, _, marginLeftPt, marginRightPt, marginTopPt, marginBottomPt,
             headerBandPt, footerBandPt) = ComputePdfGeometry(sheet);

        // Convert PDF-points margins to layout-pixels (96 dpi).
        var marginLeftPx  = marginLeftPt  * (LayoutDpi / PdfPointsPerInch);
        var marginRightPx = marginRightPt * (LayoutDpi / PdfPointsPerInch);
        var marginTopPx   = marginTopPt   * (LayoutDpi / PdfPointsPerInch);
        var marginBottomPx = marginBottomPt * (LayoutDpi / PdfPointsPerInch);
        var headerBandPx  = headerBandPt  * (LayoutDpi / PdfPointsPerInch);
        var footerBandPx  = footerBandPt  * (LayoutDpi / PdfPointsPerInch);

        var (pageWidthPt, pageHeightPt, _, _, _, _, _, _) = ComputePdfGeometry(sheet);
        var pageWidthPx  = pageWidthPt  * (LayoutDpi / PdfPointsPerInch);
        var pageHeightPx = pageHeightPt * (LayoutDpi / PdfPointsPerInch);

        // R96-services-pagesetup-header-band-1: the header/footer margin is the distance from the
        // page edge to the header/footer band, which sits WITHIN the top/bottom margin band as long
        // as it doesn't exceed it -- Excel's own guide-line model, and the same model
        // PagePaginationPlanner.CalculatePageCapacityDetail already uses for the WPF print path
        // (R88-services-page-setup-margins-5-1). The body only shrinks further when a header/footer
        // margin is larger than its corresponding top/bottom margin. The previous formula reserved
        // the header/footer band ADDITIONALLY on top of the top/bottom margin, so the PDF pagination
        // capacity (rows/cols per page) disagreed with both Excel and the WPF print path -- and with
        // WorkbookPdfContentBuilder.BuildPageWithPageSetup's own actual content rect below, which
        // already only insets by the plain margins (mT/mB) and discards headerBandPt/footerBandPt.
        var bodyTopPx    = Math.Max(marginTopPx, headerBandPx);
        var bodyBottomPx = Math.Max(marginBottomPx, footerBandPx);

        var printableWidthPx  = Math.Max(1.0, pageWidthPx  - marginLeftPx - marginRightPx);
        var printableHeightPx = Math.Max(1.0, pageHeightPx - bodyTopPx - bodyBottomPx);

        // Average row height across the print range.
        var avgRowHeightPx = AverageRowHeightPx(
            sheet, printRange.Start.Row, printRange.End.Row);

        // Average column width across the print range (in pixels).
        var avgColWidthPx = AverageColumnWidthPx(
            sheet, printRange.Start.Col, printRange.End.Col);

        var baseRowsPerPage   = Math.Max(1u, (uint)Math.Floor(printableHeightPx / avgRowHeightPx));
        var baseColsPerPage   = Math.Max(1u, (uint)Math.Floor(printableWidthPx  / avgColWidthPx));

        // Captured before scale-to-fit mutates baseRowsPerPage/baseColsPerPage below: the pre-scale
        // ("natural") per-page counts ResolvePagination needs to derive the real uniform shrink
        // fraction the resolved capacity implies.
        var preScaleRowsPerPage = baseRowsPerPage;
        var preScaleColsPerPage = baseColsPerPage;

        // Apply scale-to-fit (explicit percent or fit-to-pages).
        var scaleToFit = sheet.ScaleToFit;
        if (scaleToFit.ScalePercent is { } pct && pct is >= 10 and <= 400)
        {
            baseRowsPerPage = Math.Max(1u, (uint)Math.Floor(baseRowsPerPage * (100d / pct)));
            baseColsPerPage = Math.Max(1u, (uint)Math.Floor(baseColsPerPage * (100d / pct)));
        }
        else
        {
            // fit-to-pages: enough rows/cols per page so that the total page count matches the
            // requested wide × tall.
            //
            // R20-print-area-page-setup-2: mirrors PagePaginationPlanner's R18 uniform-scale fix.
            // Excel derives ONE uniform scale from whichever axis carries an explicit fit-to-pages
            // request and applies that SAME scale to the other (free) axis. When only the column
            // axis is constrained (e.g. "Fit to 1 page wide by [auto] tall"), the row axis must
            // shrink by the same ratio too, instead of staying at its unscaled natural capacity --
            // which used to over-paginate the free axis (e.g. 1x3 pages instead of Excel's
            // uniformly-shrunk 1x1).
            var naturalRowsPerPage = baseRowsPerPage;
            var naturalColsPerPage = baseColsPerPage;
            var wideConstrained = scaleToFit.FitToPagesWide is >= 1;
            var tallConstrained = scaleToFit.FitToPagesTall is >= 1;

            if (wideConstrained && !tallConstrained)
            {
                var bodyCols = CountBodyItems(printRange.Start.Col, printRange.End.Col, sheet.PrintTitleColumns);
                if (bodyCols > 0)
                {
                    var titleCols = CountRepeatItems(sheet.PrintTitleColumns, CellAddress.MaxCol);
                    var wide = scaleToFit.FitToPagesWide!.Value;
                    var bodyColsPerPage = Math.Max(1u, (uint)Math.Ceiling(bodyCols / (double)wide));
                    baseColsPerPage = Math.Max(1u, bodyColsPerPage + titleCols);
                    var uniformScale = ComputeScaleFraction(naturalColsPerPage, baseColsPerPage);
                    baseRowsPerPage = ApplyUniformScaleToFreeAxis(naturalRowsPerPage, uniformScale);
                }
            }
            else if (tallConstrained && !wideConstrained)
            {
                var bodyRows = CountBodyItems(printRange.Start.Row, printRange.End.Row, sheet.PrintTitleRows);
                if (bodyRows > 0)
                {
                    var titleRows = CountRepeatItems(sheet.PrintTitleRows, CellAddress.MaxRow);
                    var tall = scaleToFit.FitToPagesTall!.Value;
                    var bodyRowsPerPage = Math.Max(1u, (uint)Math.Ceiling(bodyRows / (double)tall));
                    baseRowsPerPage = Math.Max(1u, bodyRowsPerPage + titleRows);
                    var uniformScale = ComputeScaleFraction(naturalRowsPerPage, baseRowsPerPage);
                    baseColsPerPage = ApplyUniformScaleToFreeAxis(naturalColsPerPage, uniformScale);
                }
            }
            else if (wideConstrained && tallConstrained)
            {
                // R100-services-print-scale-uniform-both-axes: mirrors PagePaginationPlanner's
                // "wideConstrained && tallConstrained" branch (R20-print-area-page-setup-3). When
                // BOTH FitToPagesWide and FitToPagesTall are explicitly set, Excel derives ONE
                // uniform scale -- the smaller (more aggressive shrink) of the two per-axis scales
                // that would independently satisfy each axis's own explicit page-count target -- and
                // applies that SAME scale to BOTH axes. Resolving each axis to its own exact page
                // count independently (the old behavior here) produces a non-uniform scale Excel's
                // rendering model can never actually apply, over-paginating whichever axis needed
                // less shrink (e.g. "2 wide x 5 tall" over a range that already fits in 5 row-pages
                // at 100% used to still force exactly 5 row-pages even though the column-driven
                // shrink alone would have collapsed it to ~2).
                var colsIfWideOnly = naturalColsPerPage;
                var bodyCols = CountBodyItems(printRange.Start.Col, printRange.End.Col, sheet.PrintTitleColumns);
                if (bodyCols > 0)
                {
                    var titleCols = CountRepeatItems(sheet.PrintTitleColumns, CellAddress.MaxCol);
                    var bodyColsPerPage = Math.Max(1u, (uint)Math.Ceiling(bodyCols / (double)scaleToFit.FitToPagesWide!.Value));
                    colsIfWideOnly = Math.Max(1u, bodyColsPerPage + titleCols);
                }

                var rowsIfTallOnly = naturalRowsPerPage;
                var bodyRows = CountBodyItems(printRange.Start.Row, printRange.End.Row, sheet.PrintTitleRows);
                if (bodyRows > 0)
                {
                    var titleRows = CountRepeatItems(sheet.PrintTitleRows, CellAddress.MaxRow);
                    var bodyRowsPerPage = Math.Max(1u, (uint)Math.Ceiling(bodyRows / (double)scaleToFit.FitToPagesTall!.Value));
                    rowsIfTallOnly = Math.Max(1u, bodyRowsPerPage + titleRows);
                }

                var widthScale = ComputeScaleFraction(naturalColsPerPage, colsIfWideOnly);
                var heightScale = ComputeScaleFraction(naturalRowsPerPage, rowsIfTallOnly);
                var uniformScale = Math.Min(widthScale, heightScale);

                baseColsPerPage = ApplyUniformScaleToFreeAxis(naturalColsPerPage, uniformScale);
                baseRowsPerPage = ApplyUniformScaleToFreeAxis(naturalRowsPerPage, uniformScale);
            }
            else
            {
                // Neither axis constrained: baseRowsPerPage/baseColsPerPage stay at their natural
                // (unscaled) values.
            }
        }

        return new PageCapacityDetail(
            new WorkbookExportPrintPageCapacity(baseRowsPerPage, baseColsPerPage),
            printableWidthPx,
            printableHeightPx,
            preScaleRowsPerPage,
            preScaleColsPerPage);
    }

    /// <summary>
    /// Returns the effective PDF page size (width, height) in points for a sheet, honoring the
    /// paper-size code and orientation.
    /// </summary>
    public static (double WidthPoints, double HeightPoints) ResolvePageSizePoints(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var size = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        return (size.Width * PdfPointsPerInch, size.Height * PdfPointsPerInch);
    }

    // -----------------------------------------------------------------------
    // Internal geometry helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes all PDF-points geometry for a sheet's page setup in one pass.
    /// Returns (pageW, pageH, marginL, marginR, marginT, marginB, headerBand, footerBand).
    /// headerBand and footerBand are the additional space reserved inside the margins for the
    /// header and footer text bands (= margin − header/footer margin edge).
    /// </summary>
    internal static (
        double PageWidthPt,
        double PageHeightPt,
        double MarginLeftPt,
        double MarginRightPt,
        double MarginTopPt,
        double MarginBottomPt,
        double HeaderBandPt,
        double FooterBandPt) ComputePdfGeometry(Sheet sheet)
    {
        var size = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        var pageW = size.Width  * PdfPointsPerInch;
        var pageH = size.Height * PdfPointsPerInch;
        var margins = sheet.PageMargins;
        var mL = margins.Left   * PdfPointsPerInch;
        var mR = margins.Right  * PdfPointsPerInch;
        var mT = margins.Top    * PdfPointsPerInch;
        var mB = margins.Bottom * PdfPointsPerInch;

        // The header band is the distance from the top of the page to the start of the cell grid.
        // Excel defines HeaderMargin as the distance from the top edge of the paper to the header
        // text; the cell grid starts at the Top margin. So the header band height (above the grid)
        // is max(0, TopMargin - HeaderMargin).
        var headerEdgePt = sheet.HeaderMargin * PdfPointsPerInch;
        var footerEdgePt = sheet.FooterMargin * PdfPointsPerInch;
        var headerBand   = Math.Max(0.0, headerEdgePt);          // reservation for header text area
        var footerBand   = Math.Max(0.0, footerEdgePt);          // reservation for footer text area

        return (pageW, pageH, mL, mR, mT, mB, headerBand, footerBand);
    }

    private static double AverageRowHeightPx(Sheet sheet, uint startRow, uint endRow)
    {
        var fallback = sheet.DefaultRowHeight > 0 ? sheet.DefaultRowHeight : NominalRowHeightPx;
        if (endRow < startRow) return fallback;

        var total = 0.0;
        var count = endRow - startRow + 1;
        for (var row = startRow; row <= endRow; row++)
            total += sheet.RowHeights.TryGetValue(row, out var h) && h > 0 ? h : fallback;

        return total / count;
    }

    private static double AverageColumnWidthPx(Sheet sheet, uint startCol, uint endCol)
    {
        var fallbackChars = sheet.DefaultColumnWidth > 0 ? sheet.DefaultColumnWidth : 8.43;
        var fallbackPx = Math.Max(MinimumColumnWidthPx, ColumnWidthPixelMapper.ColumnWidthToPixels(fallbackChars));
        if (endCol < startCol) return fallbackPx;

        var total = 0.0;
        var count = endCol - startCol + 1;
        for (var col = startCol; col <= endCol; col++)
        {
            var chars = sheet.ColumnWidths.TryGetValue(col, out var w) && w > 0 ? w : fallbackChars;
            var px = ColumnWidthPixelMapper.ColumnWidthToPixels(chars);
            total += Math.Max(MinimumColumnWidthPx, px);
        }

        return total / count;
    }

    /// <summary>Resolves a single row's real height in pixels, the same way <see cref="AverageRowHeightPx"/> does per row.</summary>
    private static double ResolveRowHeightPixels(Sheet sheet, uint row)
    {
        var fallback = sheet.DefaultRowHeight > 0 ? sheet.DefaultRowHeight : NominalRowHeightPx;
        return sheet.RowHeights.TryGetValue(row, out var h) && h > 0 ? h : fallback;
    }

    /// <summary>Resolves a single column's real width in pixels, the same way <see cref="AverageColumnWidthPx"/> does per column.</summary>
    private static double ResolveColumnWidthPixels(Sheet sheet, uint col)
    {
        var fallbackChars = sheet.DefaultColumnWidth > 0 ? sheet.DefaultColumnWidth : 8.43;
        var chars = sheet.ColumnWidths.TryGetValue(col, out var w) && w > 0 ? w : fallbackChars;
        return Math.Max(MinimumColumnWidthPx, ColumnWidthPixelMapper.ColumnWidthToPixels(chars));
    }

    private static bool IsWithinRepeatRange(WorksheetRepeatRange? repeatRange, uint value) =>
        repeatRange is { } range && value >= range.Start && value <= range.End;

    /// <summary>
    /// Sums the real (visible, non-hidden) size of the rows/columns in <paramref name="repeat"/>
    /// (clipped to <paramref name="maxItem"/>) -- the title rows/columns that are reprinted on every
    /// page and so must be reserved out of each page's body budget. Mirrors
    /// <c>PagePaginationPlanner.ComputeRepeatRangeSize</c>.
    /// </summary>
    private static double ComputeRepeatRangeSize(
        WorksheetRepeatRange? repeat,
        uint maxItem,
        Func<uint, bool>? isHidden,
        Func<uint, double> sizeOf)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0.0;

        var total = 0.0;
        var end = Math.Min(range.End, maxItem);
        for (var value = range.Start; value <= end; value++)
        {
            if (value >= 1 && isHidden?.Invoke(value) != true)
                total += Math.Max(0.0, sizeOf(value));
        }

        return total;
    }

    /// <summary>
    /// Computes extra "manual" break points so that pages break on the real ACCUMULATED size of
    /// visible, non-title rows/columns instead of the fixed count derived from an average size. Walks
    /// [<paramref name="startValue"/>, <paramref name="endValue"/>] in order, skipping title and hidden
    /// values, and records a break before the first value whose addition would push the running total
    /// past <paramref name="availableBodySize"/> -- guaranteeing at least one value per page even when a
    /// single oversized value alone exceeds the budget. Mirrors
    /// <c>PagePaginationPlanner.ComputeAccumulationBreakPoints</c> (R18-print-pagination-exact-3).
    /// </summary>
    private static List<uint> ComputeAccumulationBreakPoints(
        uint startValue,
        uint endValue,
        WorksheetRepeatRange? repeat,
        Func<uint, bool>? isHidden,
        Func<uint, double> sizeOf,
        double availableBodySize)
    {
        var breaks = new List<uint>();
        if (endValue < startValue)
            return breaks;

        var budget = double.IsFinite(availableBodySize) ? Math.Max(1.0, availableBodySize) : double.MaxValue;
        var accumulated = 0.0;
        var pageHasValue = false;
        for (var value = startValue; value <= endValue; value++)
        {
            if (IsWithinRepeatRange(repeat, value) || isHidden?.Invoke(value) == true)
                continue;

            var size = Math.Max(0.0, sizeOf(value));
            if (pageHasValue && accumulated + size > budget)
            {
                breaks.Add(value);
                accumulated = 0.0;
                pageHasValue = false;
            }

            accumulated += size;
            pageHasValue = true;
        }

        return breaks;
    }

    /// <summary>Unions any real manual breaks with the accumulated-size break points.</summary>
    private static List<uint> MergeBreaks(IReadOnlyCollection<uint>? userBreaks, List<uint> computedBreaks)
    {
        if (computedBreaks.Count == 0)
            return userBreaks is null ? [] : new List<uint>(userBreaks);

        var merged = new HashSet<uint>(computedBreaks);
        if (userBreaks is not null)
            merged.UnionWith(userBreaks);

        return [.. merged];
    }

    /// <summary>
    /// An axis capacity large enough that <see cref="PrintLayoutPlanner"/>'s own count-based slicing
    /// never forces a break within a page; used together with <see cref="MergeBreaks"/> so accumulated
    /// (and any real manual) break points are the only thing that decides where pages split. Mirrors
    /// <c>PagePaginationPlanner.UnboundedAxisCapacity</c>.
    /// </summary>
    private static uint UnboundedAxisCapacity(uint start, uint end) =>
        end >= start ? (uint)Math.Min(uint.MaxValue - 1L, (long)(end - start) + 2L) : 1u;

    /// <summary>
    /// The "s" shrink fraction implied by going from <paramref name="naturalItemsPerPage"/> (the
    /// natural, unscaled per-page item count) to <paramref name="constrainedItemsPerPage"/>:
    /// <c>s = natural / constrained</c> -- mirrors <c>PagePaginationPlanner.ComputeScaleFraction</c>.
    /// </summary>
    private static double ComputeScaleFraction(uint naturalItemsPerPage, uint constrainedItemsPerPage) =>
        constrainedItemsPerPage == 0 ? 1.0 : naturalItemsPerPage / (double)constrainedItemsPerPage;

    /// <summary>
    /// Applies the uniform shrink fraction derived from the constrained axis to the free axis's
    /// natural capacity, clamped to the same [10, 400] scale-percent range Excel supports for an
    /// explicit scale -- mirrors <c>PagePaginationPlanner.ApplyUniformScaleToFreeAxis</c>.
    /// </summary>
    private static uint ApplyUniformScaleToFreeAxis(uint naturalItemsPerPage, double scaleFraction)
    {
        if (scaleFraction <= 0 || !double.IsFinite(scaleFraction))
            return naturalItemsPerPage;

        var percent = Math.Clamp(scaleFraction * 100.0, 10.0, 400.0);
        return Math.Max(1u, (uint)Math.Floor(naturalItemsPerPage * (100d / percent)));
    }

    /// <summary>
    /// Counts the rows/columns in a repeat (print titles) range, clipped to <paramref name="maxItem"/>.
    /// Mirrors <c>PagePaginationPlanner.CountRepeatItems</c> so the title count added back onto the
    /// body-only capacity (R76-services-print-pagination-4-1) matches the WPF path exactly.
    /// </summary>
    private static uint CountRepeatItems(WorksheetRepeatRange? repeat, uint maxItem)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0;

        return Math.Min(range.End, maxItem) - range.Start + 1;
    }

    private static uint CountBodyItems(uint start, uint end, WorksheetRepeatRange? repeat)
    {
        if (end < start) return 0;
        var count = end - start + 1;
        if (repeat is not { } range || range.End < start || range.Start > end)
            return count;

        var overlapStart = Math.Max(start, range.Start);
        var overlapEnd   = Math.Min(end,   range.End);
        return overlapEnd >= overlapStart
            ? count - (overlapEnd - overlapStart + 1)
            : count;
    }
}
