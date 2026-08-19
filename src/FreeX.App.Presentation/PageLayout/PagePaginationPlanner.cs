using System.Linq;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// The rows or columns that belong to a single page along one axis: <see cref="Indexes"/> is the
/// explicit, gap-aware list of 1-based sheet indexes actually printed on the page (hidden, filtered,
/// and outline-collapsed rows/columns already excluded by <see cref="PrintLayoutPlanner.BuildRowPlans"/>
/// / <see cref="PrintLayoutPlanner.BuildColumnPlans"/>). <see cref="Start"/>/<see cref="End"/> are the
/// first and last of that list and are a lossy summary -- do not reconstruct the printed set by
/// iterating <see cref="Start"/>..<see cref="End"/>, since a hidden row/column in the interior of the
/// page is absent from <see cref="Indexes"/> but would be wrongly reinstated by that range. Use
/// <see cref="Start"/>/<see cref="End"/> only for bounding-box math (e.g. page-break-preview overlay
/// geometry) where the full extent, not the printed set, is what is needed.
/// </summary>
public readonly record struct PageAxisSegment(uint Start, uint End, IReadOnlyList<uint> Indexes)
{
    /// <summary>
    /// Convenience constructor for callers/tests that only know a contiguous bound (no hidden gaps),
    /// e.g. fixed test fixtures with nothing hidden. Builds <see cref="Indexes"/> as the full
    /// <paramref name="start"/>..<paramref name="end"/> range. Real pagination plans should go through
    /// the 3-arg constructor with the plan's actual (possibly gap-filtered) index list instead.
    /// </summary>
    public PageAxisSegment(uint start, uint end)
        : this(start, end, BuildContiguousRange(start, end))
    {
    }

    private static IReadOnlyList<uint> BuildContiguousRange(uint start, uint end)
    {
        if (end < start)
            return [];

        var range = new List<uint>((int)(end - start + 1));
        for (var value = start; value <= end; value++)
            range.Add(value);

        return range;
    }

    /// <summary>
    /// Value equality treats <see cref="Indexes"/> by content (sequence), not by list reference/instance
    /// identity -- the default record-struct equality would otherwise compare object references and
    /// spuriously report two segments with identical printed rows/columns as unequal.
    /// </summary>
    public bool Equals(PageAxisSegment other) =>
        Start == other.Start &&
        End == other.End &&
        (Indexes ?? []).SequenceEqual(other.Indexes ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Start);
        hash.Add(End);
        foreach (var index in Indexes ?? [])
            hash.Add(index);

        return hash.ToHashCode();
    }
}

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
    /// on one page. The printable body height is the paper height minus the top/bottom margins, where
    /// each margin further expands to the header/footer margin only when that margin is larger than the
    /// corresponding top/bottom margin (R88-services-page-setup-margins-5-1: the header/footer band sits
    /// within the top/bottom margin, not in addition to it); the printable body width is the paper width
    /// minus page margins. Falls back to <see cref="NominalRowHeight"/> or <see cref="MinimumPrintColumnWidth"/>
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
    /// <param name="headerMarginInches">Distance from page top to header, in inches (shrinks the body height only when it exceeds the top margin).</param>
    /// <param name="footerMarginInches">Distance from page bottom to footer, in inches (shrinks the body height only when it exceeds the bottom margin).</param>
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
        return CalculatePageCapacityDetail(
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
            footerMarginInches).Capacity;
    }

    /// <summary>
    /// Internal detail behind <see cref="CalculatePageCapacity"/>: also exposes the printable body
    /// size and the pre-scale-to-fit ("base") per-page item counts, so <see cref="BuildPlan"/> can
    /// derive the real uniform shrink factor implied by the resolved capacity and accumulate real
    /// row/column sizes against it (R18-print-pagination-exact-3) instead of re-deriving the scale
    /// from scratch.
    /// </summary>
    private readonly record struct PageCapacityDetail(
        PageCapacity Capacity,
        double PrintableWidth,
        double PrintableHeight,
        uint BaseRowsPerPage,
        uint BaseColumnsPerPage);

    private static PageCapacityDetail CalculatePageCapacityDetail(
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
        Func<uint, bool>? isRowHidden = null,
        Func<uint, bool>? isColumnHidden = null)
    {
        var pageSize = WorksheetPageLayout.GetPageSizeInches(paperSize, orientation);

        // R88-services-page-setup-margins-5-1: the header/footer margin is the distance from the
        // page edge to the header/footer band, which sits WITHIN the top/bottom margin band as long
        // as it doesn't exceed it -- Excel's own guide-line model -- so the body only shrinks further
        // when a header/footer margin is larger than its corresponding top/bottom margin. Treating the
        // header/footer margins as an ADDITIONAL reservation on top of the top/bottom margins (the old
        // PR5 formula) silently lost real body height even with the universal defaults (0.3in
        // header/footer margin under a 0.75in top/bottom margin), where Excel reserves nothing extra.
        var bodyTopInches = PageGeometryRules.ResolveBodyEdge(margins.Top, headerMarginInches);
        var bodyBottomInches = PageGeometryRules.ResolveBodyEdge(margins.Bottom, footerMarginInches);
        var printableWidth = Math.Max(1.0, (pageSize.Width - margins.Left - margins.Right) * Dpi);
        var printableHeight = Math.Max(1.0, (pageSize.Height - bodyTopInches - bodyBottomInches) * Dpi);

        // PR1: compute average row height from actual per-row sizes across the print range.
        // R107: exclude hidden rows the same way CountBodyItems/CountRepeatItems already do below,
        // so the "base" per-page count and the hidden-aware "target" per-page count are derived from
        // the same (visible-only) population -- see AverageRowHeightPixels's doc comment.
        var effectiveRowHeight = AverageRowHeightPixels(
            printRange.Start.Row, printRange.End.Row,
            rowHeights, defaultRowHeight, isRowHidden);

        // PR1: compute average column width in pixels from actual per-column character widths.
        // R107: exclude hidden columns for the same reason as the row average above.
        var effectiveColWidth = AverageColumnWidthPixels(
            printRange.Start.Col, printRange.End.Col,
            columnWidths, defaultColumnWidth, isColumnHidden);

        var baseRowsPerPage = Math.Max(1u, (uint)Math.Floor(printableHeight / effectiveRowHeight));
        var baseColumnsPerPage = Math.Max(1u, (uint)Math.Floor(printableWidth / effectiveColWidth));

        var rowsPerPage = baseRowsPerPage;
        var columnsPerPage = baseColumnsPerPage;

        // R18-print-pagination-exact-1: Excel derives ONE uniform scale from whichever axis carries
        // an explicit fit-to-pages request and applies that SAME scale to the other (free) axis. When
        // only the column axis is constrained (e.g. "Fit to 1 page wide by [auto] tall"), the row
        // axis must shrink by the same ratio too, instead of staying at 100% capacity -- which used to
        // over-paginate the free axis (e.g. 1x2 pages instead of Excel's uniformly-shrunk 1x1).
        var explicitPercentSet = scaleToFit.ScalePercent is not null;
        var wideConstrained = !explicitPercentSet && scaleToFit.FitToPagesWide is >= 1;
        var tallConstrained = !explicitPercentSet && scaleToFit.FitToPagesTall is >= 1;

        if (wideConstrained && !tallConstrained)
        {
            // R103-print-pagination-scale-bound-1: resolve the constrained axis's own capacity
            // first (possibly implying an unbounded shrink far outside Excel's 10%-400% scale
            // range), but then re-derive BOTH axes -- including this constrained one -- through
            // ApplyUniformScaleToFreeAxis, which clamps to [MinScalePercent, MaxScalePercent].
            // Applying the raw, unclamped ApplyScaleToFitCapacity result directly to the
            // constrained axis while the free axis gets the clamped percent would bake two
            // different real scales into what is supposed to be one uniform scale -- exactly the
            // divergence the "both axes constrained" branch below avoids by re-deriving both axes
            // from the same uniformScale.
            var unboundedColumnsPerPage = ApplyScaleToFitCapacity(
                columnsPerPage,
                printRange.Start.Col,
                printRange.End.Col,
                printTitleColumns,
                CellAddress.MaxCol,
                scalePercent: null,
                scaleToFit.FitToPagesWide,
                isColumnHidden);
            var uniformScale = ComputeScaleFraction(baseColumnsPerPage, unboundedColumnsPerPage);
            columnsPerPage = ApplyUniformScaleToFreeAxis(baseColumnsPerPage, uniformScale);
            rowsPerPage = ApplyUniformScaleToFreeAxis(rowsPerPage, uniformScale);
        }
        else if (tallConstrained && !wideConstrained)
        {
            // R103-print-pagination-scale-bound-1: see the mirror-image comment above.
            var unboundedRowsPerPage = ApplyScaleToFitCapacity(
                rowsPerPage,
                printRange.Start.Row,
                printRange.End.Row,
                printTitleRows,
                CellAddress.MaxRow,
                scalePercent: null,
                scaleToFit.FitToPagesTall,
                isRowHidden);
            var uniformScale = ComputeScaleFraction(baseRowsPerPage, unboundedRowsPerPage);
            rowsPerPage = ApplyUniformScaleToFreeAxis(baseRowsPerPage, uniformScale);
            columnsPerPage = ApplyUniformScaleToFreeAxis(columnsPerPage, uniformScale);
        }
        else if (wideConstrained && tallConstrained)
        {
            // R20-print-area-page-setup-3: when BOTH FitToPagesWide and FitToPagesTall are
            // explicitly set, Excel derives ONE uniform scale -- the smaller (more aggressive
            // shrink) of the two per-axis scales that would be needed to hit each axis's own
            // requested page count independently -- and applies that SAME scale to both axes.
            // Resolving each axis to its own exact page count independently (the old behavior)
            // produces a non-uniform scale Excel's rendering model can never actually apply,
            // over-paginating the axis that needed less shrink.
            var columnsPerPageIfWideOnly = ApplyScaleToFitCapacity(
                baseColumnsPerPage,
                printRange.Start.Col,
                printRange.End.Col,
                printTitleColumns,
                CellAddress.MaxCol,
                scalePercent: null,
                scaleToFit.FitToPagesWide,
                isColumnHidden);
            var rowsPerPageIfTallOnly = ApplyScaleToFitCapacity(
                baseRowsPerPage,
                printRange.Start.Row,
                printRange.End.Row,
                printTitleRows,
                CellAddress.MaxRow,
                scalePercent: null,
                scaleToFit.FitToPagesTall,
                isRowHidden);

            var widthScale = ComputeScaleFraction(baseColumnsPerPage, columnsPerPageIfWideOnly);
            var heightScale = ComputeScaleFraction(baseRowsPerPage, rowsPerPageIfTallOnly);
            var uniformScale = PageGeometryRules.ResolveUniformScale(widthScale, heightScale);

            columnsPerPage = ApplyUniformScaleToFreeAxis(baseColumnsPerPage, uniformScale);
            rowsPerPage = ApplyUniformScaleToFreeAxis(baseRowsPerPage, uniformScale);
        }
        else
        {
            // Neither axis constrained, or an explicit scale percent is set: resolve each axis
            // independently, as before.
            rowsPerPage = ApplyScaleToFitCapacity(
                rowsPerPage,
                printRange.Start.Row,
                printRange.End.Row,
                printTitleRows,
                CellAddress.MaxRow,
                scaleToFit.ScalePercent,
                scaleToFit.FitToPagesTall,
                isRowHidden);
            columnsPerPage = ApplyScaleToFitCapacity(
                columnsPerPage,
                printRange.Start.Col,
                printRange.End.Col,
                printTitleColumns,
                CellAddress.MaxCol,
                scaleToFit.ScalePercent,
                scaleToFit.FitToPagesWide,
                isColumnHidden);
        }

        return new PageCapacityDetail(
            new PageCapacity(rowsPerPage, columnsPerPage),
            printableWidth,
            printableHeight,
            baseRowsPerPage,
            baseColumnsPerPage);
    }

    /// <summary>
    /// The "s" shrink fraction implied by going from <paramref name="baseItemsPerPage"/> (the natural,
    /// unscaled per-page item count) to <paramref name="resolvedItemsPerPage"/>: <c>s = base / resolved</c>,
    /// i.e. <c>resolved = base / s</c> -- the same relationship <see cref="ApplyScaleToFitCapacity"/>'s
    /// explicit-percent branch uses (<c>s = percent / 100</c>). Public so other page-setup-driven
    /// capacity resolvers (e.g. the PDF export tier's page-setup resolver) share this single
    /// implementation instead of re-deriving an identical copy.
    /// </summary>
    public static double ComputeScaleFraction(uint baseItemsPerPage, uint resolvedItemsPerPage) =>
        resolvedItemsPerPage == 0 ? 1.0 : baseItemsPerPage / (double)resolvedItemsPerPage;

    /// <summary>
    /// Applies the uniform shrink fraction derived from the constrained axis to the free axis's
    /// baseline capacity, clamped to the same [<see cref="MinScalePercent"/>, <see cref="MaxScalePercent"/>]
    /// range as an explicit scale percent. Public for the same sharing reason as
    /// <see cref="ComputeScaleFraction"/>.
    /// </summary>
    public static uint ApplyUniformScaleToFreeAxis(uint baseItemsPerPage, double scaleFraction)
    {
        if (scaleFraction <= 0 || !double.IsFinite(scaleFraction))
            return baseItemsPerPage;

        var percent = Math.Clamp(scaleFraction * 100.0, MinScalePercent, MaxScalePercent);
        return Math.Max(1u, (uint)Math.Floor(baseItemsPerPage * (100d / percent)));
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
        var detail = CalculatePageCapacityDetail(
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
            isRowHidden,
            isColumnHidden);
        var capacity = detail.Capacity;

        // R18-print-pagination-exact-3: slice pages by the real ACCUMULATED row height / column
        // width -- breaking a page once its running total would exceed the printable body size --
        // instead of the fixed count implied by the AVERAGE row height / column width used for
        // `capacity` above. A fixed average*count slice over/under-shoots the real printable area
        // whenever the range has non-uniform row heights or column widths. The accumulated break
        // points are fed into PrintLayoutPlanner as extra manual breaks (merged with any real manual
        // breaks), using a capacity large enough that PrintLayoutPlanner's own count-based slicing
        // never forces an additional break of its own -- the accumulated/manual breaks alone decide
        // where pages split.
        double RowSize(uint row) => ResolveRowHeightPixels(row, rowHeights, defaultRowHeight);
        double ColumnSize(uint col) => ResolveColumnWidthPixels(col, columnWidths, defaultColumnWidth);

        var rowScale = ComputeScaleFraction(detail.BaseRowsPerPage, capacity.RowsPerPage);
        var columnScale = ComputeScaleFraction(detail.BaseColumnsPerPage, capacity.ColumnsPerPage);

        var rowTitleSize = PageAxisPaginationRules.ComputeRepeatRangeSize(
            printTitleRows, CellAddress.MaxRow, isRowHidden, RowSize);
        var columnTitleSize = PageAxisPaginationRules.ComputeRepeatRangeSize(
            printTitleColumns, CellAddress.MaxCol, isColumnHidden, ColumnSize);

        var rowBodyBudget = detail.PrintableHeight / rowScale - rowTitleSize;
        var columnBodyBudget = detail.PrintableWidth / columnScale - columnTitleSize;

        var accumulatedRowBreaks = PageAxisPaginationRules.ComputeAccumulationBreakPoints(
            printRange.Start.Row, printRange.End.Row, printTitleRows, isRowHidden, RowSize, rowBodyBudget);
        var accumulatedColumnBreaks = PageAxisPaginationRules.ComputeAccumulationBreakPoints(
            printRange.Start.Col, printRange.End.Col, printTitleColumns, isColumnHidden, ColumnSize, columnBodyBudget);

        var rowPlans = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            printTitleRows,
            PageAxisPaginationRules.UnboundedAxisCapacity(printRange.Start.Row, printRange.End.Row),
            PageAxisPaginationRules.MergeBreaks(rowPageBreaks, accumulatedRowBreaks),
            isRowHidden);
        var columnPlans = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            printTitleColumns,
            PageAxisPaginationRules.UnboundedAxisCapacity(printRange.Start.Col, printRange.End.Col),
            PageAxisPaginationRules.MergeBreaks(columnPageBreaks, accumulatedColumnBreaks),
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
        IReadOnlyCollection<uint>? columnPageBreaks = null,
        Func<uint, bool>? isRowHidden = null,
        Func<uint, bool>? isColumnHidden = null)
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
            columnPageBreaks,
            isRowHidden,
            isColumnHidden);

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
        int? fitToPages,
        Func<uint, bool>? isHidden = null)
    {
        // R150-presentation-pagination-scale-out-of-range-clamp: an explicit scale percent outside
        // Excel's 10%-400% UI bound (reachable from a non-Excel-authored/hand-edited .xlsx whose
        // <pageSetup scale="..."/> the loader does not range-check) must be clamped here exactly like
        // CalculateEffectiveScalePercent clamps it for the VISUAL scale drawn on the page. Previously
        // an out-of-range percent fell all the way through to the fit-to-pages branch below (a no-op,
        // since FitToPagesWide/Tall are null when an explicit percent is set), which returned the
        // UNSCALED, natural (100%) capacity while the sibling render/PDF-export path still drew every
        // cell at the clamped scale -- e.g. ScalePercent=500 assigned each page the ~100%-scale row
        // count but rendered it at the 400% cap, so roughly 4x too many rows were packed onto (and
        // clipped off the bottom of) every page.
        if (scalePercent is { } percent)
            return Math.Max(1, (uint)Math.Floor(baseItemsPerPage * (100d / Math.Clamp(percent, MinScalePercent, MaxScalePercent))));

        if (fitToPages is not { } pageCount || pageCount < 1)
            return baseItemsPerPage;

        // R102-presentation-pagination-fit-to-pages-hidden-exclusion: the fit-to-N-pages target
        // count must be resolved over VISIBLE rows/columns only, matching what
        // ComputeAccumulationBreakPoints/PrintLayoutPlanner actually pack onto each page (both skip
        // hidden rows/columns via this same isHidden predicate). Counting every row/column in
        // [start,end] -- including hidden ones -- inflates bodyCount, which inflates the resolved
        // bodyItemsPerPage/rowsPerPage target, which in turn produces a tiny uniformScale and a
        // hugely inflated per-page body budget (BuildPlan's rowBodyBudget/columnBodyBudget). That
        // inflated budget then lets the accumulation-break walk pack far more real (visible-only)
        // content onto a single page than Excel would, collapsing the pagination onto too few
        // pages. Real Excel excludes hidden rows/columns entirely from this resolution.
        var titleCount = PageGeometryRules.CountRepeatItems(repeat, maxItem, isHidden);
        var bodyCount = PageGeometryRules.CountBodyItems(start, end, repeat, isHidden);
        if (bodyCount == 0)
        {
            // R102-presentation-pagination-titles-cover-axis: when the print title range on this
            // axis fully covers the print range (no body items left to paginate), there is nothing
            // for the fit-to-N-pages request to shrink on this axis, so leave the natural,
            // avg-width/height-derived capacity untouched -- exactly SheetPdfPageSetupResolver's
            // `if (bodyCols > 0)` no-op guard (SheetPdfPageSetupResolver.cs) for the identical case.
            // Previously this returned Math.Max(1, titleCount), an arithmetic coincidence with no
            // relation to baseItemsPerPage; the caller then diffed that titleCount against
            // baseItemsPerPage via ComputeScaleFraction to derive a uniform scale and applied it to
            // the OTHER, unrelated free axis (ApplyUniformScaleToFreeAxis), spuriously
            // shrinking/growing that axis's page capacity for no real fit-to-page reason.
            // Returning baseItemsPerPage unchanged here makes that derived scale exactly 1.0, so the
            // free axis is left alone too, matching the PDF-export tier's behavior.
            //
            // R105-presentation-pagination-titles-cover-axis-excel-unverified: whether this axis-fully-
            // covered-by-titles state is ever reachable from a real Excel print range (and, if so, what
            // Excel itself produces there) could not be established from this sandbox -- no Excel
            // instance or captured fixture demonstrating it was available. This no-op (leave the free-
            // fit capacity untouched, matching SheetPdfPageSetupResolver) is retained unchanged pending
            // that verification; see PageGeometryRules.CountBodyItems for the shared implementation now
            // used by both call sites.
            return baseItemsPerPage;
        }

        var bodyItemsPerPage = (uint)Math.Ceiling(bodyCount / (double)pageCount);
        return Math.Max(1, bodyItemsPerPage + titleCount);
    }

    // R105-presentation-pagination-counting-helpers-consolidation: CountRepeatItems / CountBodyItems
    // used to be maintained here as private near-duplicates of the identical rules in
    // SheetPdfPageSetupResolver (PDF export path); R102 merged them into a single shared home,
    // PageGeometryRules.CountRepeatItems / PageGeometryRules.CountBodyItems
    // (src/FreeX.App.Presentation/PageLayout/PageGeometryRules.cs), and pointed the PDF-export
    // resolver at it, but this planner's copies were left in place because this file was off-limits
    // that round. Both call sites now share the one implementation; equivalence is proven by every
    // pre-existing test in FreeX.App.Presentation.Tests/FreeX.App.Services.Tests passing unchanged.

    /// <summary>
    /// Builds the row page segments from a print row page plan, keeping each page's explicit,
    /// hidden-row-excluding <see cref="PrintPageRowPlan.BodyRows"/> list (falling back to
    /// <see cref="PrintPageRowPlan.TitleRows"/> for a title-only page). Public so
    /// <c>WorkbookPdfContentBuilder</c>'s equivalent plan-to-segment step shares this single
    /// implementation instead of re-deriving a lossy copy.
    /// </summary>
    public static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageRowPlan> plans) =>
        BuildSegments(plans, static plan => plan.BodyRows, static plan => plan.TitleRows);

    /// <summary>Column-axis counterpart of <see cref="BuildSegments(IReadOnlyList{PrintPageRowPlan})"/>.</summary>
    public static IReadOnlyList<PageAxisSegment> BuildSegments(IReadOnlyList<PrintPageColumnPlan> plans) =>
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

            segments.Add(new PageAxisSegment(indexes[0], indexes[^1], indexes));
        }

        return segments;
    }

    /// <summary>
    /// Returns the average row height in pixels across the rows [startRow, endRow]. Each row's height
    /// is taken from <paramref name="rowHeights"/> when present; rows absent from the dictionary use
    /// <paramref name="defaultRowHeight"/>. Falls back to <see cref="NominalRowHeight"/> when
    /// <paramref name="defaultRowHeight"/> is not positive. When <paramref name="isHidden"/> is
    /// supplied, hidden rows are excluded from both the sum and the divisor -- a hidden row takes no
    /// print space, so folding its real (possibly very tall/short) recorded height into the average
    /// would desync this "base" per-page count from the hidden-aware "target" per-page count that
    /// <see cref="PageGeometryRules.CountBodyItems"/> resolves Fit-to-N-pages against
    /// (R107-presentation-pagination-fit-to-pages-hidden-average-exclusion). Falls back to the
    /// all-rows average when every row in range is hidden, to avoid a division by zero.
    /// </summary>
    public static double AverageRowHeightPixels(
        uint startRow,
        uint endRow,
        IReadOnlyDictionary<uint, double> rowHeights,
        double defaultRowHeight,
        Func<uint, bool>? isHidden = null)
    {
        var fallback = defaultRowHeight > 0 ? defaultRowHeight : NominalRowHeight;
        if (endRow < startRow)
            return fallback;

        var total = 0.0;
        uint count = 0;
        for (var row = startRow; row <= endRow; row++)
        {
            if (isHidden?.Invoke(row) == true)
                continue;

            total += rowHeights.TryGetValue(row, out var h) && h > 0 ? h : fallback;
            count++;
        }

        return count > 0 ? total / count : fallback;
    }

    /// <summary>
    /// Returns the average column width in pixels across the columns [startCol, endCol]. Each column's
    /// character-unit width is taken from <paramref name="columnWidths"/> when present; columns absent
    /// from the dictionary use <paramref name="defaultColumnWidth"/>. The character-unit width is
    /// converted to pixels via <see cref="ColumnWidthPixelMapper.ColumnWidthToPixels"/>. Falls back to
    /// <see cref="MinimumPrintColumnWidth"/> when the computed pixel width would be zero or negative.
    /// When <paramref name="isHidden"/> is supplied, hidden columns are excluded from both the sum and
    /// the divisor, for the same reason as <see cref="AverageRowHeightPixels"/>
    /// (R107-presentation-pagination-fit-to-pages-hidden-average-exclusion). Falls back to the
    /// all-columns average when every column in range is hidden, to avoid a division by zero.
    /// </summary>
    public static double AverageColumnWidthPixels(
        uint startCol,
        uint endCol,
        IReadOnlyDictionary<uint, double> columnWidths,
        double defaultColumnWidth,
        Func<uint, bool>? isHidden = null)
    {
        var fallbackChars = defaultColumnWidth > 0 ? defaultColumnWidth : ColumnWidthPixelMapper.PixelsToColumnWidth(MinimumPrintColumnWidth);
        var fallbackPx = Math.Max(MinimumPrintColumnWidth, ColumnWidthPixelMapper.ColumnWidthToPixels(fallbackChars));
        if (endCol < startCol)
            return fallbackPx;

        var total = 0.0;
        uint count = 0;
        for (var col = startCol; col <= endCol; col++)
        {
            if (isHidden?.Invoke(col) == true)
                continue;

            var chars = columnWidths.TryGetValue(col, out var w) && w > 0 ? w : fallbackChars;
            var px = ColumnWidthPixelMapper.ColumnWidthToPixels(chars);
            total += Math.Max(MinimumPrintColumnWidth, px);
            count++;
        }

        return count > 0 ? total / count : fallbackPx;
    }

    /// <summary>Resolves a single row's real height in pixels, the same way <see cref="AverageRowHeightPixels"/> does per row.</summary>
    private static double ResolveRowHeightPixels(uint row, IReadOnlyDictionary<uint, double> rowHeights, double defaultRowHeight)
    {
        var fallback = defaultRowHeight > 0 ? defaultRowHeight : NominalRowHeight;
        return rowHeights.TryGetValue(row, out var h) && h > 0 ? h : fallback;
    }

    /// <summary>Resolves a single column's real width in pixels, the same way <see cref="AverageColumnWidthPixels"/> does per column.</summary>
    private static double ResolveColumnWidthPixels(uint col, IReadOnlyDictionary<uint, double> columnWidths, double defaultColumnWidth)
    {
        var fallbackChars = defaultColumnWidth > 0 ? defaultColumnWidth : ColumnWidthPixelMapper.PixelsToColumnWidth(MinimumPrintColumnWidth);
        var chars = columnWidths.TryGetValue(col, out var w) && w > 0 ? w : fallbackChars;
        return Math.Max(MinimumPrintColumnWidth, ColumnWidthPixelMapper.ColumnWidthToPixels(chars));
    }

}
